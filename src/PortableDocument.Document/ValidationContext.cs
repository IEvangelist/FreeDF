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
    /// <summary>Namespace prefix used for each pdfuaid property (part, amd, corr).</summary>
    public Dictionary<string, string?> PdfUAPrefixes { get; } = new(StringComparer.Ordinal);
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
            CollectPdfUaPrefixes(bytes, result);
            return result;
        }
        catch (XmlException) { return data; }
    }

    /// <summary>XLinq discards prefixes, so the pdfuaid property prefixes (ISO 14289 clause 5) are read with an XmlReader.</summary>
    static void CollectPdfUaPrefixes(byte[] bytes, XmpData result)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
        using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
        var nodes = 0;
        while (reader.Read() && nodes++ < 100_000)
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.NamespaceURI == PdfUAId) result.PdfUAPrefixes.TryAdd(reader.LocalName, reader.Prefix);
            for (var ok = reader.MoveToFirstAttribute(); ok; ok = reader.MoveToNextAttribute())
                if (reader.NamespaceURI == PdfUAId) result.PdfUAPrefixes.TryAdd(reader.LocalName, reader.Prefix);
            reader.MoveToElement();
        }
    }
}

internal sealed class ValidationContext
{
    public PdfDocument Doc { get; }
    public PdfFile F => Doc.File;
    public PdfValidationProfile Profile { get; }
    public List<ValidationFinding> Findings { get; } = [];

    /// <summary>(page index, MCID) pairs whose marked content shows text, collected by the PDF/UA content scan.</summary>
    public HashSet<(int Page, int Mcid)> TextMcids { get; } = [];

    readonly int _contentScanTokenLimit;

    public ValidationContext(PdfDocument doc, PdfValidationProfile profile, int contentScanTokenLimit = MaxContentTokens)
    {
        Doc = doc;
        Profile = profile;
        _contentScanTokenLimit = contentScanTokenLimit;
    }

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

    public sealed record FontUse(PdfDictionary Font, int Page)
    {
        public int? RenderingPage { get; set; }
        public int? ZeroTwoByteCodePage { get; private set; }
        public Dictionary<byte, int> RenderingAsciiCodes { get; } = [];

        public void NoteRenderingAscii(PdfString text, int page)
        {
            foreach (var code in text.Bytes)
                if (code is >= 32 and <= 126) RenderingAsciiCodes.TryAdd(code, page);
        }

        public void NoteTwoByteCodes(PdfString text, int page)
        {
            if (ZeroTwoByteCodePage is not null) return;
            for (var i = 0; i + 1 < text.Bytes.Length; i += 2)
                if (text.Bytes[i] == 0 && text.Bytes[i + 1] == 0) { ZeroTwoByteCodePage = page; return; }
        }
    }

    public List<FontUse> Fonts { get; } = [];
    public bool FontUsageComplete { get; private set; } = true;
    public List<(PdfDictionary Gs, int Page)> ExtGStates { get; } = [];
    public List<(PdfStream Image, int Page)> Images { get; } = [];
    public List<PdfStream> IccBased { get; } = [];
    public bool UsesDeviceColor { get; private set; }
    public bool UsesUnmappedDeviceGray { get; private set; }
    public bool UsesUnmappedDeviceRgb { get; private set; }
    public bool UsesUnmappedDeviceCmyk { get; private set; }
    bool _walked;
    readonly HashSet<PdfObject> _seen = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<PdfDictionary, FontUse> _fontUses = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<PdfDictionary, Dictionary<string, bool>> _defaultColorValidity = new(ReferenceEqualityComparer.Instance);

    static readonly Regex DeviceOps = new(@"(?<![\w/])[-+.\d]+(?:\s+[-+.\d]+){0,3}\s+(rg|RG|g|G|k|K)(?![\w])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly HashSet<string> PathOps = new(StringComparer.Ordinal) { "m", "l", "c", "v", "y", "re" };
    const int MaxContentTokens = 20_000_000;
    const int MaxFormDepth = 16;

    enum DeviceColor { None, Gray, Rgb, Cmyk }

    sealed class ContentScanBudget
    {
        public int Tokens;
        public bool LimitReported;
        public HashSet<PdfStream> ActiveForms { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<PdfStream, HashSet<(PdfDictionary Resources, DeviceColor Fill, DeviceColor Stroke, int RenderMode, PdfDictionary? Font)>> FormStates { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>Collects fonts, graphics states, images and device-colour usage from every page (resources, forms, patterns, annotation appearances).</summary>
    public void EnsureWalked()
    {
        if (_walked) return;
        _walked = true;
        for (var i = 0; i < Doc.Pages.Count; i++)
        {
            var i0 = i;
            var completed = false;
            Guard($"resources of page {i + 1}", () =>
            {
                var page = Doc.Pages[i0];
                Walk(page.Resources, i0, 0);
                ScanContent(PageContents(page), page.Resources, page: i0);
                if (F.Resolve(page.Dict.Get("Annots")) is PdfArray annots)
                {
                    if (annots.Count > 10_000) FontUsageComplete = false;
                    foreach (var a in annots.Items.Take(10_000))
                        if (F.Resolve(a) is PdfDictionary ad && F.Resolve(ad.Get("AP")) is PdfDictionary ap)
                            foreach (var (_, v) in ap.Entries)
                            {
                                var n = F.Resolve(v);
                                if (n is PdfStream ns) ScanAppearance(ns, i0, page.Resources);
                                else if (n is PdfDictionary nd) foreach (var (_, sv) in nd.Entries) if (F.Resolve(sv) is PdfStream ss) ScanAppearance(ss, i0, page.Resources);
                            }
                }
                completed = true;
            });
            if (!completed) FontUsageComplete = false;
        }
    }

    FontUse FontFor(PdfDictionary font, int page)
    {
        if (_fontUses.TryGetValue(font, out var use)) return use;
        use = new FontUse(font, page);
        _fontUses.Add(font, use);
        _seen.Add(font);
        Fonts.Add(use);
        return use;
    }

    byte[] PageContents(PdfPage page)
    {
        if (Profile != PdfValidationProfile.PdfA1b) return page.GetContents();
        var contents = F.Resolve(page.Dict.Get("Contents"));
        using var output = new MemoryStream();
        if (contents is null or PdfNull) return [];
        foreach (var item in contents is PdfArray array ? array.Items : [contents])
        {
            if (F.Resolve(item) is not PdfStream stream) { FontUsageComplete = false; continue; }
            output.Write(ContentBytes(stream));
            output.WriteByte((byte)'\n');
        }
        return output.ToArray();
    }

    byte[] ContentBytes(PdfStream stream)
    {
        if (Profile != PdfValidationProfile.PdfA1b) return F.DecodeStream(stream);
        var filters = PdfFilters.GetFilters(stream.Dict, F.Resolve);
        var filter = F.Resolve(stream.Dict.Get("Filter") ?? stream.Dict.Get("F"));
        var parms = F.Resolve(stream.Dict.Get("DecodeParms") ?? stream.Dict.Get("DP"));
        if (filter is not (null or PdfNull or PdfName or PdfArray)
            || filter is PdfArray array && array.Count != filters.Count
            || parms is not (null or PdfNull or PdfDictionary or PdfArray)
            || filter is PdfArray && parms is PdfDictionary
            || parms is PdfArray parameters && (parameters.Count != filters.Count
                || parameters.Items.Any(x => F.Resolve(x) is not (null or PdfNull or PdfDictionary)))
            || filters.Any(x => x.Name is not ("FlateDecode" or "ASCIIHexDecode" or "ASCII85Decode" or "RunLengthDecode")
                || x.Name != "FlateDecode" && F.Resolve(x.Parms?.Get("Predictor")) is { } predictor and not PdfNull
                    && predictor.AsNumber() != 1
                || F.Resolve(x.Parms?.Get("Predictor")).AsNumber() > 1
                    && F.Resolve(x.Parms?.Get("BitsPerComponent")).AsNumber() == 16))
            FontUsageComplete = false;
        try { return PdfFilters.Decode(stream.RawData, filters, F.Resolve, strictContentFilters: true).Data; }
        catch (Exception e) when (e is InvalidDataException or IOException or PdfParseException)
        {
            FontUsageComplete = false;
            Add("PDFA-CONTENT-SCAN-INCOMPLETE", ValidationSeverity.Error,
                $"Content decoding did not complete: {e.Message}");
            return [];
        }
    }

    DeviceColor UnmappedDeviceColor(PdfObject? colorSpace, PdfDictionary resources)
    {
        HashSet<string>? aliases = null;
        while (true)
        {
            var resolved = F.Resolve(colorSpace);
            if (resolved is PdfArray { Count: > 0 } array)
            {
                if (F.Resolve(array[0]).AsName() is not ("DeviceGray" or "G" or "DeviceRGB" or "RGB" or "DeviceCMYK" or "CMYK"))
                    return DeviceColor.None;
                resolved = F.Resolve(array[0]);
            }
            if (resolved is not PdfName name) return DeviceColor.None;
            var (color, space) = name.Value switch
            {
                "DeviceGray" or "G" => (DeviceColor.Gray, "Gray"),
                "DeviceRGB" or "RGB" => (DeviceColor.Rgb, "RGB"),
                "DeviceCMYK" or "CMYK" => (DeviceColor.Cmyk, "CMYK"),
                _ => (DeviceColor.None, ""),
            };
            if (color != DeviceColor.None)
                return HasDeviceIndependentDefault(resources, space) ? DeviceColor.None : color;
            aliases ??= new HashSet<string>(StringComparer.Ordinal);
            if (!aliases.Add(name.Value)) return DeviceColor.None;
            var spaces = F.Resolve(resources.Get("ColorSpace")) as PdfDictionary;
            if (spaces?.Get(name.Value) is not { } alias) return DeviceColor.None;
            colorSpace = alias;
        }
    }

    void NotePaintedDeviceColor(DeviceColor color)
    {
        if (color == DeviceColor.None) return;
        UsesDeviceColor = true;
        if (color == DeviceColor.Gray) UsesUnmappedDeviceGray = true;
        else if (color == DeviceColor.Rgb) UsesUnmappedDeviceRgb = true;
        else if (color == DeviceColor.Cmyk) UsesUnmappedDeviceCmyk = true;
    }

    bool HasDeviceIndependentDefault(PdfDictionary resources, string space)
    {
        if (!_defaultColorValidity.TryGetValue(resources, out var defaults))
            _defaultColorValidity.Add(resources, defaults = new(StringComparer.Ordinal));
        if (defaults.TryGetValue(space, out var valid)) return valid;
        valid = F.Resolve(resources.Get("ColorSpace")) is PdfDictionary spaces
            && spaces.Get("Default" + space) is { } replacement
            && IsDeviceIndependentColor(replacement, spaces, new HashSet<string>(StringComparer.Ordinal), 0, space);
        defaults.Add(space, valid);
        return valid;
    }

    bool IsDeviceIndependentColor(PdfObject? colorSpace, PdfDictionary spaces, HashSet<string> aliases, int depth, string space)
    {
        if (depth > 16) return false;
        var resolved = F.Resolve(colorSpace);
        if (resolved is PdfName name)
        {
            if (name.Value is "DeviceGray" or "G" or "DeviceRGB" or "RGB" or "DeviceCMYK" or "CMYK") return false;
            return aliases.Add(name.Value) && spaces.Get(name.Value) is { } alias
                && IsDeviceIndependentColor(alias, spaces, aliases, depth + 1, space);
        }
        if (resolved is not PdfArray { Count: > 0 } array) return false;
        switch (F.Resolve(array[0]).AsName())
        {
            case "CalGray" when space == "Gray":
            case "CalRGB" when space == "RGB":
                if (array.Count != 2 || F.Resolve(array[1]) is not PdfDictionary parameters) return false;
                if (F.Resolve(parameters.Get("WhitePoint")) is not PdfArray { Count: 3 } whitePoint) return false;
                var white = whitePoint.Items.Select(x => F.Resolve(x).AsNumber()).ToArray();
                if (!white.All(x => x is { } value && double.IsFinite(value) && value > 0)
                    || white[1] != 1)
                    return false;
                if (parameters.Get("BlackPoint") is { } blackPointObject)
                {
                    if (F.Resolve(blackPointObject) is not PdfArray { Count: 3 } blackPoint) return false;
                    var black = blackPoint.Items.Select(x => F.Resolve(x).AsNumber()).ToArray();
                    if (!black.All(x => x is { } value && double.IsFinite(value) && value >= 0)) return false;
                }
                if (space == "Gray")
                    return parameters.Get("Gamma") is null
                        || F.Resolve(parameters.Get("Gamma")).AsNumber() is { } gamma && double.IsFinite(gamma) && gamma > 0;
                if (parameters.Get("Gamma") is { } rgbGamma)
                {
                    if (F.Resolve(rgbGamma) is not PdfArray { Count: 3 } gammaArray
                        || !gammaArray.Items.Select(x => F.Resolve(x).AsNumber())
                            .All(x => x is { } value && double.IsFinite(value) && value > 0))
                        return false;
                }
                return parameters.Get("Matrix") is null
                    || F.Resolve(parameters.Get("Matrix")) is PdfArray { Count: 9 } matrix
                        && matrix.Items.Select(x => F.Resolve(x).AsNumber()).All(x => x is { } value && double.IsFinite(value));
            case "ICCBased" when array.Count == 2 && F.Resolve(array[1]) is PdfStream profile:
                var components = space == "Gray" ? 1 : space == "RGB" ? 3 : 4;
                if (F.Resolve(profile.Dict.Get("N")).AsNumber() != components) return false;
                return PdfADeepChecks.ReadIcc(F.DecodeStream(profile))?.Space == (space == "Gray" ? "GRAY" : space.PadRight(4));
            default:
                return false;
        }
    }

    void ScanAppearance(PdfStream appearance, int page, PdfDictionary resources)
    {
        WalkForm(appearance, page, 1);
        ScanContent(ContentBytes(appearance), FormResources(appearance, resources), page: page);
    }

    PdfDictionary FormResources(PdfStream form, PdfDictionary? inheritedResources) =>
        F.Resolve(form.Dict.Get("Resources")) as PdfDictionary ?? inheritedResources ?? new PdfDictionary();

    void ScanContent(byte[] content, PdfDictionary resources, DeviceColor? initialFill = null, DeviceColor? initialStroke = null,
        int initialRenderMode = 0, int depth = 0, ContentScanBudget? budget = null, int page = 0, PdfDictionary? initialFont = null)
    {
        budget ??= new ContentScanBudget();
        foreach (Match match in DeviceOps.Matches(Encoding.Latin1.GetString(content)))
        {
            var space = match.Groups[1].Value switch { "g" or "G" => "Gray", "rg" or "RG" => "RGB", _ => "CMYK" };
            if (!HasDeviceIndependentDefault(resources, space))
                UsesDeviceColor = true;
        }

        var fill = initialFill ?? UnmappedDeviceColor(new PdfName("DeviceGray"), resources);
        var stroke = initialStroke ?? fill;
        var renderMode = initialRenderMode;
        var font = initialFont;
        var path = false;
        var states = new Stack<(DeviceColor Fill, DeviceColor Stroke, int RenderMode, PdfDictionary? Font)>();
        var operands = new List<PdfObject>();
        var lexer = new PdfLexer(content);
        while (budget.Tokens < _contentScanTokenLimit)
        {
            budget.Tokens++;
            PdfObject? item;
            try { item = lexer.ReadObject(false); }
            catch (PdfParseException) { FontUsageComplete = false; break; }
            if (item is null)
            {
                if (operands.Count != 0) FontUsageComplete = false;
                break;
            }
            if (item is not PdfKeyword keyword)
            {
                operands.Add(item);
                if (operands.Count > 512) operands.RemoveAt(0);
                continue;
            }

            var op = keyword.Value;
            switch (op)
            {
                case "BI":
                    // The inline-image skipper is heuristic, so it cannot prove that no text was skipped.
                    FontUsageComplete = false;
                    PdfUADeepChecks.SkipInlineImage(lexer);
                    break;
                case "q":
                    states.Push((fill, stroke, renderMode, font));
                    break;
                case "Q":
                    if (states.TryPop(out var saved)) (fill, stroke, renderMode, font) = saved;
                    else FontUsageComplete = false;
                    break;
                case "Tf":
                    font = null;
                    if (operands.Count == 2 && operands[0] is PdfName fontName
                        && operands[1].AsNumber() is { } fontSize && double.IsFinite(fontSize)
                        && F.Resolve(resources.Get("Font")) is PdfDictionary fonts
                        && F.Resolve(fonts.Get(fontName.Value)) is PdfDictionary selectedFont)
                        font = selectedFont;
                    else FontUsageComplete = false;
                    break;
                case "gs":
                    if (operands.Count != 1 || operands[0] is not PdfName gsName
                        || F.Resolve(resources.Get("ExtGState")) is not PdfDictionary graphicsStates
                        || F.Resolve(graphicsStates.Get(gsName.Value)) is not PdfDictionary graphicsState)
                    {
                        FontUsageComplete = false;
                        font = null;
                    }
                    else if (F.Resolve(graphicsState.Get("Font")) is { } stateFont and not PdfNull)
                    {
                        if (stateFont is PdfArray { Count: 2 } fontArray
                            && F.Resolve(fontArray[0]) is PdfDictionary gsFont
                            && F.Resolve(fontArray[1]).AsNumber() is { } size && double.IsFinite(size))
                            font = gsFont;
                        else { FontUsageComplete = false; font = null; }
                    }
                    break;
                case "scn" or "SCN" when Profile == PdfValidationProfile.PdfA1b:
                    if (operands.LastOrDefault() is PdfName patternName
                        && F.Resolve(resources.Get("Pattern")) is PdfDictionary patterns
                        && F.Resolve(patterns.Get(patternName.Value)) is PdfStream)
                        FontUsageComplete = false;
                    break;
                case "g":
                    fill = UnmappedDeviceColor(new PdfName("DeviceGray"), resources);
                    break;
                case "G":
                    stroke = UnmappedDeviceColor(new PdfName("DeviceGray"), resources);
                    break;
                case "rg":
                    fill = UnmappedDeviceColor(new PdfName("DeviceRGB"), resources);
                    break;
                case "k":
                    fill = UnmappedDeviceColor(new PdfName("DeviceCMYK"), resources);
                    break;
                case "RG":
                    stroke = UnmappedDeviceColor(new PdfName("DeviceRGB"), resources);
                    break;
                case "K":
                    stroke = UnmappedDeviceColor(new PdfName("DeviceCMYK"), resources);
                    break;
                case "cs":
                    if (operands.LastOrDefault() is PdfName fillName)
                    {
                        fill = UnmappedDeviceColor(fillName, resources);
                        if (fillName.Value is "DeviceRGB" or "DeviceCMYK")
                            NoteColorSpace(fillName, resources);
                    }
                    break;
                case "CS":
                    if (operands.LastOrDefault() is PdfName strokeName)
                    {
                        stroke = UnmappedDeviceColor(strokeName, resources);
                        if (strokeName.Value is "DeviceRGB" or "DeviceCMYK")
                            NoteColorSpace(strokeName, resources);
                    }
                    break;
                case "Do":
                    if (operands.LastOrDefault() is PdfName xObjectName
                        && F.Resolve(resources.Get("XObject")) is PdfDictionary xObjects
                        && F.Resolve(xObjects.Get(xObjectName.Value)) is PdfStream xObject)
                    {
                        var subtype = F.Resolve(xObject.Dict.Get("Subtype")).AsName();
                        if (subtype == "Image")
                            NotePaintedDeviceColor(F.Resolve(xObject.Dict.Get("ImageMask")) is PdfBoolean { Value: true }
                                ? fill : UnmappedDeviceColor(xObject.Dict.Get("ColorSpace"), resources));
                        else if (subtype == "Form" && depth < MaxFormDepth)
                        {
                            if (!budget.FormStates.TryGetValue(xObject, out var formStates))
                                budget.FormStates.Add(xObject, formStates = []);
                            var formResources = FormResources(xObject, resources);
                            if (budget.ActiveForms.Contains(xObject)) FontUsageComplete = false;
                            else if (formStates.Add((formResources, fill, stroke, renderMode, font)))
                            {
                                budget.ActiveForms.Add(xObject);
                                ScanContent(ContentBytes(xObject), formResources, fill, stroke, renderMode, depth + 1, budget, page, font);
                                budget.ActiveForms.Remove(xObject);
                            }
                        }
                        else if (subtype == "Form") FontUsageComplete = false;
                    }
                    else FontUsageComplete = false;
                    break;
                case "Tr":
                    var mode = operands.LastOrDefault()?.AsNumber();
                    if (operands.Count != 1 || mode is not { } m || !double.IsFinite(m)
                        || m < 0 || m > 7 || m != Math.Truncate(m))
                        FontUsageComplete = false;
                    renderMode = (int)(operands.LastOrDefault()?.AsNumber() ?? 0);
                    break;
                default:
                    if (PathOps.Contains(op))
                        path = true;
                    else if (op is "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*")
                    {
                        if (path)
                        {
                            var paintsFill = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
                            var paintsStroke = op is "S" or "s" or "B" or "B*" or "b" or "b*";
                            if (paintsFill) NotePaintedDeviceColor(fill);
                            if (paintsStroke) NotePaintedDeviceColor(stroke);
                        }
                        path = false;
                    }
                    else if (op == "n")
                        path = false;
                    else if (op is "Tj" or "TJ" or "'" or "\"")
                    {
                        var text = operands.LastOrDefault();
                        var valid = operands.Count == (op == "\"" ? 3 : 1)
                            && (op == "TJ" ? text is PdfArray array
                                && array.Items.All(x => x is PdfString || x.AsNumber() is { } n && double.IsFinite(n))
                                : text is PdfString);
                        if (op == "\"")
                            valid &= operands.Take(2).All(x => x.AsNumber() is { } n && double.IsFinite(n));
                        if (!valid) FontUsageComplete = false;
                        if (valid && (Profile != PdfValidationProfile.PdfA1b || renderMode != 3))
                        {
                            if (font is null) FontUsageComplete = false;
                            else
                            {
                                var use = FontFor(font, page);
                                if (Profile is PdfValidationProfile.PdfUA1 or PdfValidationProfile.PdfUA2)
                                {
                                    if (text is PdfString shown)
                                    {
                                        use.NoteTwoByteCodes(shown, page);
                                        if (renderMode != 3) use.NoteRenderingAscii(shown, page);
                                    }
                                    else if (text is PdfArray shownArray)
                                        foreach (var value in shownArray.Items)
                                            if (value is PdfString shownItem)
                                            {
                                                use.NoteTwoByteCodes(shownItem, page);
                                                if (renderMode != 3) use.NoteRenderingAscii(shownItem, page);
                                            }
                                }
                                if (Profile == PdfValidationProfile.PdfA1b)
                                {
                                    use.RenderingPage ??= page;
                                    if (F.Resolve(font.Get("Subtype")).AsName() == "Type3") FontUsageComplete = false;
                                }
                            }
                        }
                        var paintsFill = renderMode is 0 or 2 or 4 or 6;
                        var paintsStroke = renderMode is 1 or 2 or 5 or 6;
                        if (paintsFill) NotePaintedDeviceColor(fill);
                        if (paintsStroke) NotePaintedDeviceColor(stroke);
                    }
                    break;
            }
            operands.Clear();
        }
        if (budget.Tokens >= _contentScanTokenLimit && !budget.LimitReported
            && Profile is PdfValidationProfile.PdfA1b or PdfValidationProfile.PdfA2b or PdfValidationProfile.PdfA3b)
        {
            var contentRemains = false;
            try
            {
                contentRemains = lexer.ReadObject(false) is not null;
            }
            catch (PdfParseException) { contentRemains = true; }
            if (contentRemains)
            {
                budget.LimitReported = true;
                FontUsageComplete = false;
                Add("PDFA-CONTENT-SCAN-INCOMPLETE", ValidationSeverity.Error,
                    $"Content scanning stopped after {_contentScanTokenLimit} tokens before all content was examined", page: page);
            }
        }
    }

    void NoteColorSpace(PdfObject? cs, PdfDictionary resources)
    {
        var o = F.Resolve(cs);
        string? name = o.AsName();
        if (o is PdfArray { Count: > 0 } a)
        {
            if (a.Count > 1 && F.Resolve(a[0]).AsName() == "ICCBased" && F.Resolve(a[1]) is PdfStream icc && _seen.Add(icc)) IccBased.Add(icc);
            name = F.Resolve(a[0]).AsName();
        }
        var space = name switch { "DeviceGray" or "G" => "Gray", "DeviceRGB" or "RGB" => "RGB", "DeviceCMYK" or "CMYK" => "CMYK", _ => null };
        if (space is not null && !HasDeviceIndependentDefault(resources, space))
            UsesDeviceColor = true;
    }

    void WalkForm(PdfStream form, int page, int depth)
    {
        if (depth > 16 || !_seen.Add(form)) return;
        if (F.Resolve(form.Dict.Get("Resources")) is PdfDictionary resources)
            Walk(resources, page, depth + 1);
    }

    void Walk(PdfDictionary? res, int page, int depth)
    {
        if (res is null || depth > 16) return;
        if (F.Resolve(res.Get("Font")) is PdfDictionary fonts)
            foreach (var (_, v) in fonts.Entries)
                if (F.Resolve(v) is PdfDictionary fd && _seen.Add(fd)) FontFor(fd, page);
        if (F.Resolve(res.Get("ExtGState")) is PdfDictionary gss)
            foreach (var (_, v) in gss.Entries)
                if (F.Resolve(v) is PdfDictionary gd && _seen.Add(gd)) ExtGStates.Add((gd, page));
        if (F.Resolve(res.Get("ColorSpace")) is PdfDictionary css)
            foreach (var (_, v) in css.Entries) NoteColorSpace(v, res);
        if (F.Resolve(res.Get("XObject")) is PdfDictionary xs)
            foreach (var (_, v) in xs.Entries)
            {
                if (F.Resolve(v) is not PdfStream x) continue;
                var sub = F.Resolve(x.Dict.Get("Subtype")).AsName();
                if (sub == "Image")
                {
                    if (_seen.Add(x)) Images.Add((x, page));
                    NoteColorSpace(x.Dict.Get("ColorSpace"), res);
                }
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
