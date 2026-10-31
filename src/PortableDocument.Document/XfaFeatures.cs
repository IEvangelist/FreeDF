using System.Text;
using System.Xml;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Read-only XFA/XDP XML element. Attribute order is document order; text is the concatenated direct character data (bounded).</summary>
public sealed record XfaElement(
    string Name,
    string? Namespace,
    IReadOnlyList<KeyValuePair<string, string>> Attributes,
    IReadOnlyList<XfaElement> Children,
    string? Text)
{
    public string? Attribute(string name) => Attributes.FirstOrDefault(a => a.Key == name).Value;
    public XfaElement? Child(string name) => Children.FirstOrDefault(c => c.Name == name);
}

/// <summary>Parse outcome for one XML document. <see cref="Error"/> is set (and <see cref="Root"/> null or partial) for DTDs, malformed XML and budget violations.</summary>
public sealed record XfaXmlDocument(XfaElement? Root, bool Truncated, string? Error, int ElementCount, int MaxDepth);

/// <summary>Packet kinds from the XFA 3.3 specification section 2 (packets: xdp, template, datasets, config, localeSet, stylesheet, connectionSet, sourceSet, xfdf).</summary>
public sealed record XfaPacketModel(string? Name, string Kind, XfaXmlDocument? Xml, int RawLength);

public sealed record XfaField(string Path, string? Name, string Kind, string? Ui, string? Access, string? Presence, string? DefaultValue, string? BindMatch);

public sealed record XfaScript(string Source, string? ContentType, string? RunAt, string? Activity, string Text, bool TextTruncated)
{
    /// <summary>True for FormCalc (the XFA default scripting language is FormCalc when no contentType is given).</summary>
    public bool IsFormCalc => ContentType is null || ContentType.Contains("formcalc", StringComparison.OrdinalIgnoreCase);
}

public sealed record XfaTemplateSummary(
    string? Namespace,
    int Subforms,
    int Fields,
    int ExclGroups,
    int Draws,
    int PageAreas,
    int Scripts,
    int MaxDepth,
    IReadOnlyList<string> Layouts,
    IReadOnlyList<XfaField> FieldList,
    bool HasFlowedOrRepeatingLayout);

public sealed record XfaDataValue(string Path, string Value);

/// <summary>
/// Inert XFA document model. IsDynamic reflects /NeedsRendering true (ISO 32000-2 Table 29 and Table 226 /XFA); Static XFA forms
/// carry a page-based PDF alongside the XFA. No script is executed, nothing is rendered and no external resource is loaded.
/// </summary>
public sealed record XfaReport(
    bool Present,
    bool NeedsRendering,
    bool IsDynamic,
    IReadOnlyList<XfaPacketModel> Packets,
    XfaTemplateSummary? Template,
    IReadOnlyList<XfaDataValue> Data,
    IReadOnlyList<XfaScript> Scripts,
    bool Truncated);

public static class XfaReader
{
    public const int MaxBytes = 32 * 1024 * 1024;
    public const int MaxElements = 200_000;
    public const int MaxDepth = 64;
    public const int MaxTextPerElement = 64 * 1024;
    public const int MaxDataValues = 10_000;
    public const int MaxFields = 10_000;
    public const int MaxScripts = 2_000;

    /// <summary>Safe XML parse: DTDs prohibited (no entity expansion, no XXE), no resolver, size/element/depth bounded.</summary>
    public static XfaXmlDocument ParseXml(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxBytes) return new XfaXmlDocument(null, true, "XML exceeds size budget", 0, 0);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = true,
        };
        var stack = new Stack<(string Name, string? Ns, List<KeyValuePair<string, string>> Attrs, List<XfaElement> Kids, StringBuilder Text)>();
        XfaElement? root = null;
        var count = 0;
        var maxDepth = 0;
        var truncated = false;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(data.ToArray()), settings);
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (++count > MaxElements) return new XfaXmlDocument(root, true, "element budget exceeded", count, maxDepth);
                        if (stack.Count >= MaxDepth) return new XfaXmlDocument(root, true, "depth budget exceeded", count, maxDepth);
                        var attrs = new List<KeyValuePair<string, string>>();
                        var empty = reader.IsEmptyElement;
                        var name = reader.LocalName;
                        var ns = string.IsNullOrEmpty(reader.NamespaceURI) ? null : reader.NamespaceURI;
                        if (reader.HasAttributes)
                            while (reader.MoveToNextAttribute())
                                if (attrs.Count < 256 && reader.Prefix != "xmlns" && reader.Name != "xmlns")
                                    attrs.Add(new(reader.LocalName, reader.Value));
                        reader.MoveToElement();
                        stack.Push((name, ns, attrs, [], new StringBuilder()));
                        maxDepth = Math.Max(maxDepth, stack.Count);
                        if (empty) Close();
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA:
                        if (stack.Count > 0)
                        {
                            var t = stack.Peek().Text;
                            if (t.Length < MaxTextPerElement) t.Append(reader.Value, 0, Math.Min(reader.Value.Length, MaxTextPerElement - t.Length));
                            else truncated = true;
                        }
                        break;
                    case XmlNodeType.EndElement:
                        Close();
                        break;
                }
            }
        }
        catch (XmlException e)
        {
            return new XfaXmlDocument(root, truncated, e.Message, count, maxDepth);
        }
        return new XfaXmlDocument(root, truncated, root is null ? "no root element" : null, count, maxDepth);

        void Close()
        {
            var (n, ns, attrs, kids, text) = stack.Pop();
            var el = new XfaElement(n, ns, attrs, kids, text.Length == 0 ? null : text.ToString());
            if (stack.Count == 0) root = el;
            else stack.Peek().Kids.Add(el);
        }
    }

    public static XfaReport Read(PdfDocument doc)
    {
        var f = doc.File;
        var needs = f.Resolve(f.Catalog.Get("NeedsRendering")) is PdfBoolean { Value: true };
        if (f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary form && f.Resolve(form.Get("NeedsRendering")) is PdfBoolean { Value: true })
            needs = true;
        var raw = doc.GetXfaPackets();
        if (raw.Count == 0) return new XfaReport(false, needs, needs, [], null, [], [], false);

        var packets = new List<XfaPacketModel>();
        var truncated = false;
        foreach (var p in raw)
        {
            byte[] bytes;
            try { bytes = f.DecodeStream(p.Stream); }
            catch (Exception e) when (e is PdfParseException or InvalidDataException or InvalidOperationException)
            {
                packets.Add(new XfaPacketModel(p.Name, KindFromName(p.Name), new XfaXmlDocument(null, false, "stream could not be decoded", 0, 0), 0));
                continue;
            }
            if (p.Name is "preamble" or "postamble")
            {
                packets.Add(new XfaPacketModel(p.Name, p.Name, null, bytes.Length));
                continue;
            }
            var xml = ParseXml(bytes);
            truncated |= xml.Truncated;
            packets.Add(new XfaPacketModel(p.Name, KindFromRoot(xml.Root) ?? KindFromName(p.Name), xml, bytes.Length));
        }

        var logical = new List<XfaElement>();
        foreach (var p in packets)
        {
            if (p.Xml?.Root is not { } root) continue;
            if (root.Name == "xdp") logical.AddRange(root.Children);
            else logical.Add(root);
        }
        var template = logical.FirstOrDefault(e => e.Name == "template");
        var datasets = logical.FirstOrDefault(e => e.Name == "datasets");
        var scripts = new List<XfaScript>();
        var summary = template is null ? null : Summarize(template, scripts, ref truncated);
        var data = new List<XfaDataValue>();
        if (datasets is not null) CollectData(datasets, data, ref truncated);
        return new XfaReport(true, needs, needs, packets, summary, data, scripts, truncated);
    }

    static string? KindFromRoot(XfaElement? root) => root?.Name switch
    {
        "xdp" or "template" or "datasets" or "config" or "localeSet" or "stylesheet" or "connectionSet" or "sourceSet" or "xfdf" => root.Name,
        _ => null,
    };

    static string KindFromName(string? name) => name switch
    {
        "template" or "datasets" or "config" or "localeSet" or "stylesheet" or "connectionSet" or "sourceSet" or "xdp" or "xfdf" => name,
        null => "xdp",
        _ => "other",
    };

    static XfaTemplateSummary Summarize(XfaElement template, List<XfaScript> scripts, ref bool truncated)
    {
        int subforms = 0, fields = 0, excl = 0, draws = 0, pageAreas = 0, scriptCount = 0, depthMax = 0;
        var layouts = new SortedSet<string>(StringComparer.Ordinal);
        var list = new List<XfaField>();
        var flowed = false;
        var activities = new Dictionary<XfaElement, string>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(XfaElement E, string Path, int Depth)>();
        stack.Push((template, "", 0));
        while (stack.Count > 0)
        {
            var (e, path, depth) = stack.Pop();
            depthMax = Math.Max(depthMax, depth);
            var named = e.Attribute("name");
            var childPath = e.Name is "subform" or "field" or "exclGroup" or "draw" or "pageSet" or "pageArea"
                ? (named is null ? path : path.Length == 0 ? named : path + "." + named) : path;
            switch (e.Name)
            {
                case "subform":
                    subforms++;
                    if (e.Attribute("layout") is { } l) { layouts.Add(l); if (l != "position") flowed = true; }
                    break;
                case "event":
                    if (e.Attribute("activity") is { } act)
                        foreach (var c in e.Children) if (c.Name == "script") activities[c] = act;
                    break;
                case "exclGroup": excl++; break;
                case "draw": draws++; break;
                case "pageArea": pageAreas++; break;
                case "occur":
                    if (e.Attribute("max") is { } max && max != "1") flowed = true;
                    break;
                case "field":
                    fields++;
                    if (list.Count < MaxFields)
                        list.Add(new XfaField(childPath, named, "field", UiKind(e), e.Attribute("access"), e.Attribute("presence"), DefaultValue(e),
                            e.Child("bind")?.Attribute("match")));
                    else truncated = true;
                    break;
                case "script":
                    scriptCount++;
                    if (scripts.Count < MaxScripts)
                        scripts.Add(new XfaScript(path.Length == 0 ? "template" : path, e.Attribute("contentType"), e.Attribute("runAt"),
                            activities.GetValueOrDefault(e), e.Text ?? "", e.Text is { Length: >= MaxTextPerElement }));
                    else truncated = true;
                    break;
            }
            for (var i = e.Children.Count - 1; i >= 0; i--) stack.Push((e.Children[i], childPath, depth + 1));
        }
        return new XfaTemplateSummary(template.Namespace, subforms, fields, excl, draws, pageAreas, scriptCount, depthMax, layouts.ToList(), list, flowed);
    }

    static string? UiKind(XfaElement field) => field.Child("ui")?.Children.FirstOrDefault(c => c.Name != "extras" && c.Name != "picture" && c.Name != "margin")?.Name;

    static string? DefaultValue(XfaElement field)
    {
        var v = field.Child("value")?.Children.FirstOrDefault();
        return v?.Text;
    }

    static void CollectData(XfaElement datasets, List<XfaDataValue> data, ref bool truncated)
    {
        var stack = new Stack<(XfaElement E, string Path)>();
        foreach (var c in datasets.Children.Reverse<XfaElement>()) stack.Push((c, ""));
        while (stack.Count > 0)
        {
            var (e, path) = stack.Pop();
            if (e.Name is "dataGroup" or "data" && e.Namespace?.Contains("xfa-data") == true)
            {
                foreach (var c in e.Children.Reverse<XfaElement>()) stack.Push((c, path));
                continue;
            }
            var self = path.Length == 0 ? e.Name : path + "/" + e.Name;
            if (e.Children.Count == 0)
            {
                if (e.Text is not null)
                {
                    if (data.Count >= MaxDataValues) { truncated = true; return; }
                    data.Add(new XfaDataValue(self, e.Text));
                }
                continue;
            }
            for (var i = e.Children.Count - 1; i >= 0; i--) stack.Push((e.Children[i], self));
        }
    }
}

public static class XfaFeatures
{
    /// <summary>Returns the inert XFA model; scripts are collected as text only and never run.</summary>
    public static XfaReport GetXfaReport(this PdfDocument doc) => XfaReader.Read(doc);
}
