using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Parsed XMP packet: element text and attribute values keyed by (namespace, local name).</summary>
internal sealed class XmpData
{
    public bool Present { get; init; }
    public bool WellFormed { get; init; }
    public Dictionary<(string Ns, string Local), string> Values { get; } = [];

    public const string PdfAId = "http://www.aiim.org/pdfa/ns/id/";
    public const string PdfUAId = "http://www.aiim.org/pdfua/ns/id/";
    public const string Dc = "http://purl.org/dc/elements/1.1/";

    public const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public const string PdfAExtension = "http://www.aiim.org/pdfa/ns/extension/";
    public const string PdfASchema = "http://www.aiim.org/pdfa/ns/schema#";
    public const string PdfAProperty = "http://www.aiim.org/pdfa/ns/property#";

    /// <summary>Items of array/lang-alt properties (rdf:li) keyed by property; <c>Lang</c> is xml:lang when present.</summary>
    public Dictionary<(string Ns, string Local), List<(string? Lang, string Value)>> Items { get; } = [];
    /// <summary>Top-level properties (attributes and child elements of rdf:Description) in document order.</summary>
    public List<(string Ns, string Local)> Properties { get; } = [];
    /// <summary>Namespaces and (namespace, name) pairs declared through a pdfaExtension:schemas block.</summary>
    public HashSet<string> DeclaredNamespaces { get; } = new(StringComparer.Ordinal);
    public HashSet<(string Ns, string Name)> DeclaredProperties { get; } = [];
    public List<string> ExtensionProblems { get; } = [];
    /// <summary>The text of the leading xpacket processing instruction, when present.</summary>
    public string? PacketHeader { get; private set; }

    public string? Get(string ns, string local) => Values.GetValueOrDefault((ns, local));

    /// <summary>The x-default item (else the first) of a property, or its plain value when it is not an array.</summary>
    public string? GetDefaultItem(string ns, string local) =>
        Items.TryGetValue((ns, local), out var l) && l.Count > 0 ? (l.Any(i => i.Lang == "x-default") ? l.First(i => i.Lang == "x-default").Value : l[0].Value) : Get(ns, local);

    static void CollectExtras(XDocument doc, XmpData result)
    {
        var rdf = XNamespace.Get(Rdf);
        var ext = XNamespace.Get(PdfAExtension);
        var schema = XNamespace.Get(PdfASchema);
        var prop = XNamespace.Get(PdfAProperty);
        foreach (var desc in doc.Descendants(rdf + "RDF").Take(100).SelectMany(r => r.Elements(rdf + "Description")).Take(1000))
        {
            foreach (var a in desc.Attributes())
                if (!a.IsNamespaceDeclaration && a.Name.NamespaceName.Length > 0 && a.Name.NamespaceName != Rdf && a.Name.NamespaceName != XNamespace.Xml.NamespaceName)
                    result.Properties.Add((a.Name.NamespaceName, a.Name.LocalName));
            foreach (var el in desc.Elements())
            {
                var key = (el.Name.NamespaceName, el.Name.LocalName);
                if (key.NamespaceName.Length == 0) continue;
                result.Properties.Add(key);
                var items = new List<(string?, string)>();
                foreach (var li in el.Elements().SelectMany(c => c.Elements(rdf + "li")).Take(10_000))
                    items.Add(((string?)li.Attribute(XNamespace.Xml + "lang"), li.Value.Trim()));
                if (items.Count > 0) result.Items.TryAdd(key, [.. items]);
            }
        }
        foreach (var s in doc.Descendants(schema + "namespaceURI").Take(10_000))
            result.DeclaredNamespaces.Add(s.Value.Trim());
        foreach (var sch in doc.Descendants(ext + "schemas").SelectMany(s => s.Elements().SelectMany(b => b.Elements(rdf + "li"))).Take(10_000))
        {
            var ns = sch.Element(schema + "namespaceURI")?.Value.Trim() ?? (string?)sch.Attribute(schema + "namespaceURI");
            if (ns is null) { result.ExtensionProblems.Add("schema entry has no pdfaSchema:namespaceURI"); continue; }
            foreach (var req in new[] { "schema", "prefix", "property" })
                if (sch.Element(schema + req) is null && sch.Attribute(schema + req) is null) result.ExtensionProblems.Add($"schema '{ns}' has no pdfaSchema:{req}");
            foreach (var n in sch.Descendants(prop + "name").Take(10_000)) result.DeclaredProperties.Add((ns, n.Value.Trim()));
            foreach (var a in sch.Descendants().Attributes(prop + "name").Take(10_000)) result.DeclaredProperties.Add((ns, a.Value.Trim()));
        }
    }

    public static XmpData Parse(byte[]? bytes)
    {
        var data = new XmpData { Present = bytes is not null, WellFormed = false };
        if (bytes is null) return data;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            var doc = XDocument.Load(reader);
            var result = new XmpData { Present = true, WellFormed = true };
            result.PacketHeader = doc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(p => p.Target == "xpacket" && p.Data.TrimStart().StartsWith("begin", StringComparison.Ordinal))?.Data;
            CollectExtras(doc, result);
            var count = 0;
            foreach (var el in doc.Descendants())
            {
                if (++count > 100_000) break;
                var ns = el.Name.NamespaceName;
                var text = string.Concat(el.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
                if (text.Length == 0) text = string.Concat(el.Descendants().Where(d => !d.HasElements).Select(d => d.Value)).Trim();
                if (ns.Length > 0) result.Values.TryAdd((ns, el.Name.LocalName), text);
                foreach (var a in el.Attributes())
                    if (!a.IsNamespaceDeclaration && a.Name.NamespaceName.Length > 0) result.Values.TryAdd((a.Name.NamespaceName, a.Name.LocalName), a.Value);
            }
            return result;
        }
        catch (XmlException) { return data; }
    }
}

internal sealed class ValidationContext
{
    public PdfDocument Doc { get; }
    public PdfFile F => Doc.File;
    public PdfValidationProfile Profile { get; }
    public List<ValidationFinding> Findings { get; } = [];

    public ValidationContext(PdfDocument doc, PdfValidationProfile profile) { Doc = doc; Profile = profile; }

    public int Part => Profile switch
    {
        PdfValidationProfile.PdfA1b or PdfValidationProfile.PdfUA1 => 1,
        PdfValidationProfile.PdfA2b or PdfValidationProfile.PdfUA2 => 2,
        _ => 3,
    };

    public void Add(string rule, ValidationSeverity sev, string message, string? clause = null, int? page = null) =>
        Findings.Add(new ValidationFinding(Profile, rule, sev, message, page, clause));

    /// <summary>Runs one group of checks; a controlled parse failure inside it becomes a finding instead of aborting the report.</summary>
    public void Guard(string group, Action check)
    {
        try { check(); }
        catch (Exception e) when (e is PdfParseException or InvalidCastException or NullReferenceException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            Add("VALIDATION-INTERNAL", ValidationSeverity.Warning, $"check '{group}' could not complete: {e.Message}");
        }
    }

    XmpData? _xmp;
    public XmpData Xmp => _xmp ??= LoadXmp();

    XmpData LoadXmp()
    {
        try
        {
            return F.Resolve(F.Catalog.Get("Metadata")) is PdfStream s ? XmpData.Parse(F.DecodeStream(s)) : XmpData.Parse(null);
        }
        catch (Exception e) when (e is PdfParseException or InvalidDataException) { return new XmpData { Present = true, WellFormed = false }; }
    }

    public sealed record FontUse(PdfDictionary Font, int Page);

    public List<FontUse> Fonts { get; } = [];
    public List<(PdfDictionary Gs, int Page)> ExtGStates { get; } = [];
    public List<(PdfStream Image, int Page)> Images { get; } = [];
    public List<PdfStream> IccBased { get; } = [];
    public bool UsesDeviceColor { get; private set; }
    bool _walked;
    readonly HashSet<PdfObject> _seen = new(ReferenceEqualityComparer.Instance);

    static readonly Regex DeviceOps = new(@"(?<![\w/])[-+.\d]+(?:\s+[-+.\d]+){0,3}\s+(rg|RG|g|G|k|K)(?![\w])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Collects fonts, graphics states, images and device-colour usage from every page (resources, forms, patterns, annotation appearances).</summary>
    public void EnsureWalked()
    {
        if (_walked) return;
        _walked = true;
        for (var i = 0; i < Doc.Pages.Count; i++)
        {
            var i0 = i;
            Guard($"resources of page {i + 1}", () =>
            {
                var page = Doc.Pages[i0];
                Walk(page.Resources, i0, 0);
                ScanContent(page.GetContents());
                if (F.Resolve(page.Dict.Get("Annots")) is PdfArray annots)
                    foreach (var a in annots.Items.Take(10_000))
                        if (F.Resolve(a) is PdfDictionary ad && F.Resolve(ad.Get("AP")) is PdfDictionary ap)
                            foreach (var (_, v) in ap.Entries)
                            {
                                var n = F.Resolve(v);
                                if (n is PdfStream ns) WalkForm(ns, i0, 1);
                                else if (n is PdfDictionary nd) foreach (var (_, sv) in nd.Entries) if (F.Resolve(sv) is PdfStream ss) WalkForm(ss, i0, 1);
                            }
            });
        }
    }

    void ScanContent(byte[] content)
    {
        if (!UsesDeviceColor && DeviceOps.IsMatch(Encoding.Latin1.GetString(content))) UsesDeviceColor = true;
    }

    void NoteColorSpace(PdfObject? cs)
    {
        var o = F.Resolve(cs);
        if (o is PdfArray { Count: > 0 } a)
        {
            if (a.Count > 1 && F.Resolve(a[0]).AsName() == "ICCBased" && F.Resolve(a[1]) is PdfStream icc && _seen.Add(icc)) IccBased.Add(icc);
            o = F.Resolve(a[0]);
        }
        if (o.AsName() is "DeviceRGB" or "DeviceGray" or "DeviceCMYK" or "RGB" or "G" or "CMYK") UsesDeviceColor = true;
    }

    void WalkForm(PdfStream form, int page, int depth)
    {
        if (depth > 16 || !_seen.Add(form)) return;
        if (F.Resolve(form.Dict.Get("Resources")) is PdfDictionary r) Walk(r, page, depth + 1);
        ScanContent(F.DecodeStream(form));
    }

    void Walk(PdfDictionary? res, int page, int depth)
    {
        if (res is null || depth > 16) return;
        if (F.Resolve(res.Get("Font")) is PdfDictionary fonts)
            foreach (var (_, v) in fonts.Entries)
                if (F.Resolve(v) is PdfDictionary fd && _seen.Add(fd)) Fonts.Add(new FontUse(fd, page));
        if (F.Resolve(res.Get("ExtGState")) is PdfDictionary gss)
            foreach (var (_, v) in gss.Entries)
                if (F.Resolve(v) is PdfDictionary gd && _seen.Add(gd)) ExtGStates.Add((gd, page));
        if (F.Resolve(res.Get("ColorSpace")) is PdfDictionary css)
            foreach (var (_, v) in css.Entries) NoteColorSpace(v);
        if (F.Resolve(res.Get("XObject")) is PdfDictionary xs)
            foreach (var (_, v) in xs.Entries)
            {
                if (F.Resolve(v) is not PdfStream x) continue;
                var sub = F.Resolve(x.Dict.Get("Subtype")).AsName();
                if (sub == "Image") { if (_seen.Add(x)) { Images.Add((x, page)); NoteColorSpace(x.Dict.Get("ColorSpace")); } }
                else WalkForm(x, page, depth + 1);
            }
        if (F.Resolve(res.Get("Pattern")) is PdfDictionary pats)
            foreach (var (_, v) in pats.Entries)
                if (F.Resolve(v) is PdfStream ps) WalkForm(ps, page, depth + 1);
    }

    /// <summary>Every in-use indirect object (cached by the file as it is loaded).</summary>
    public IEnumerable<PdfObject> AllObjects()
    {
        foreach (var n in F.Xref.Keys.ToList())
        {
            PdfObject? o = null;
            try { o = F.GetObject(n); }
            catch (Exception e) when (e is PdfParseException or InvalidDataException) { }
            if (o is not null && o is not PdfNull) yield return o;
        }
    }
}
