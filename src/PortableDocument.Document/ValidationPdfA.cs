using System.Buffers.Binary;
using PortableDocument.Core;

namespace PortableDocument.Document;

internal static class FontChecks
{
    /// <summary>True when the font program is embedded (Type 3 fonts carry their glyphs in the font dictionary and are exempt).</summary>
    public static bool IsEmbedded(PdfFile f, PdfDictionary font)
    {
        var subtype = f.Resolve(font.Get("Subtype")).AsName();
        if (subtype == "Type3") return true;
        var target = font;
        if (subtype == "Type0")
        {
            if (f.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } df || f.Resolve(df[0]) is not PdfDictionary cid) return false;
            target = cid;
        }
        if (f.Resolve(target.Get("FontDescriptor")) is not PdfDictionary desc) return false;
        return new[] { "FontFile", "FontFile2", "FontFile3" }.Any(k => f.Resolve(desc.Get(k)) is PdfStream);
    }

    public static string Name(PdfFile f, PdfDictionary font) => f.Resolve(font.Get("BaseFont")).AsName() ?? "(unnamed)";

    public static PdfStream? TrueTypeProgram(PdfFile f, PdfDictionary descriptor)
    {
        if (f.Resolve(descriptor.Get("FontFile2")) is PdfStream raw) return raw;
        return f.Resolve(descriptor.Get("FontFile3")) is PdfStream openType
            && f.Resolve(openType.Dict.Get("Subtype")).AsName() == "OpenType" ? openType : null;
    }

    static bool TrySfntTable(ReadOnlySpan<byte> data, ReadOnlySpan<byte> tag, out (int Offset, int Length)? result, bool cff = false)
    {
        result = null;
        if (data.Length < 12) return false;
        var signature = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (cff ? signature != 0x4F54544F : signature is not (0x00010000 or 0x74727565)) return false;
        var tables = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        var directoryEnd = 12 + tables * 16;
        if (directoryEnd > data.Length) return false;
        for (var i = 0; i < tables; i++)
        {
            var entry = data.Slice(12 + i * 16, 16);
            if (!entry[..4].SequenceEqual(tag)) continue;
            if (result is not null) return false;
            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            if (offset < directoryEnd || offset > data.Length || length > data.Length - offset) return false;
            result = ((int)offset, (int)length);
        }
        return true;
    }

    public static int? TrueTypeGlyphCount(ReadOnlySpan<byte> data)
    {
        if (!TrySfntTable(data, "maxp"u8, out var table) || table is not { Length: >= 32 } t) return null;
        var maxp = data.Slice(t.Offset, t.Length);
        if (BinaryPrimitives.ReadUInt32BigEndian(maxp) != 0x00010000) return null;
        var count = BinaryPrimitives.ReadUInt16BigEndian(maxp[4..]);
        return count == 0 ? null : count;
    }

    public static byte[] OpenTypeCff(ReadOnlySpan<byte> data)
    {
        if (!TrySfntTable(data, "CFF "u8, out var table, cff: true) || table is not { Length: > 0 } t)
            throw new InvalidDataException("missing, duplicate or malformed OpenType CFF table");
        return data.Slice(t.Offset, t.Length).ToArray();
    }

    public readonly record struct CmapRecords(int Count, bool Symbol, bool Roman, bool Unicode, bool NonSymbolic);

    public static CmapRecords? TrueTypeCmapRecords(ReadOnlySpan<byte> data)
    {
        if (!TrySfntTable(data, "cmap"u8, out var table)) return null;
        if (table is not { } t) return new(0, false, false, false, false);
        if (t.Length < 4) return null;
        var cmap = data.Slice(t.Offset, t.Length);
        if (BinaryPrimitives.ReadUInt16BigEndian(cmap) != 0) return null;
        var count = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);
        var recordsEnd = 4 + count * 8;
        if (recordsEnd > cmap.Length) return null;
        var symbol = false;
        var roman = false;
        var unicode = false;
        var nonSymbolic = false;
        for (var j = 0; j < count; j++)
        {
            var record = cmap.Slice(4 + j * 8, 8);
            var subtable = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
            if (subtable < recordsEnd || subtable > cmap.Length - 2) return null;
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
            symbol |= platform == 3 && encoding == 0;
            roman |= platform == 1 && encoding == 0;
            unicode |= platform == 3 && encoding == 1;
            nonSymbolic |= platform != 3 || encoding != 0;
        }
        return new(count, symbol, roman, unicode, nonSymbolic);
    }

    public static (string? InvalidName, string? IncompleteReason) InspectDifferences(PdfFile f, PdfDictionary encoding)
    {
        var raw = encoding.Get("Differences");
        if (raw is null || f.Resolve(raw) is PdfNull) return (null, null);
        if (f.Resolve(raw) is not PdfArray { Count: <= 10_000 } differences)
            return (null, "non-array value or more than 10000 entries");
        long? code = null;
        foreach (var item in differences.Items)
        {
            switch (f.Resolve(item))
            {
                case PdfInteger { Value: >= 0 and <= 255 } next:
                    code = next.Value;
                    break;
                case PdfName glyph when code is >= 0 and <= 255:
                    if (glyph.Value != ".notdef" && !GlyphList.ContainsName(glyph.Value))
                        return (glyph.Value, null);
                    code++;
                    break;
                default:
                    return (null, "expected a byte character code followed by glyph names");
            }
        }
        return (null, null);
    }
}

/// <summary>PDF/A-1b/2b/3b basic checks (ISO 19005-1/2/3).</summary>
internal static class PdfAChecks
{
    public static void Run(ValidationContext c)
    {
        var outputSpaces = new HashSet<string>(StringComparer.Ordinal);
        c.Guard("file structure", () => FileStructure(c));
        c.Guard("metadata", () => Metadata(c));
        c.Guard("fonts", () => Fonts(c));
        c.Guard("output intent", () => OutputIntent(c, outputSpaces));
        c.Guard("transparency", () => Transparency(c));
        c.Guard("annotations", () => Annotations(c, outputSpaces));
        c.Guard("object scan", () => ObjectScan(c));
        c.Guard("metadata consistency", () => PdfADeepChecks.Metadata(c));
        c.Guard("icc profiles", () => PdfADeepChecks.IccProfiles(c));
        c.Guard("font dictionaries", () => PdfADeepChecks.FontDictionaries(c));
        c.Guard("actions and forms", () => PdfADeepChecks.ActionsAndForms(c));
        c.Guard("field additional actions", () => PdfADeepChecks.FieldAdditionalActions(c));
    }

    internal static string Clause(ValidationContext c, string a1, string a2, string a3) => c.Part switch { 1 => $"ISO 19005-1 {a1}", 2 => $"ISO 19005-2 {a2}", _ => $"ISO 19005-3 {a3}" };

    static void Header(ValidationContext c)
    {
        var d = c.F.Data;
        var p = 0;
        while (p + 5 <= d.Length && !(d[p] == '%' && d[p + 1] == 'P' && d[p + 2] == 'D' && d[p + 3] == 'F' && d[p + 4] == '-')) p++;
        if (p != 0)
            c.Add("PDFA-HEADER-OFFSET", ValidationSeverity.Error, "the %PDF- header does not start at byte 0", Clause(c, "6.1.2", "6.1.2", "6.1.2"));
        while (p < d.Length && d[p] != '\r' && d[p] != '\n') p++;
        if (p < d.Length && d[p] == '\r') p++;
        if (p < d.Length && d[p] == '\n') p++;
        var binary = p + 5 <= d.Length && d[p] == '%' && d[p + 1] > 127 && d[p + 2] > 127 && d[p + 3] > 127 && d[p + 4] > 127;
        if (!binary)
            c.Add("PDFA-HEADER-BINARY", ValidationSeverity.Error, "the header line must be followed by a comment of at least four bytes with values above 127", Clause(c, "6.1.2", "6.1.2", "6.1.2"));
    }

    static void FileStructure(ValidationContext c)
    {
        var f = c.F;
        Header(c);
        if (f.Trailer.Get("Encrypt") is not null || f.IsEncrypted)
            c.Add("PDFA-ENCRYPT", ValidationSeverity.Error, "encryption is not permitted", Clause(c, "6.1.3", "6.1.4", "6.1.4"));
        if (f.Trailer.Get("ID") is null)
            c.Add("PDFA-TRAILER-ID", ValidationSeverity.Error, "trailer has no /ID", Clause(c, "6.1.3", "6.1.3", "6.1.3"));
        if (f.Repaired)
            c.Add("PDFA-STRUCTURE-REPAIRED", ValidationSeverity.Error, "file structure is damaged (cross-reference data had to be rebuilt)", Clause(c, "6.1.4", "6.1.4", "6.1.4"));
        var version = f.Resolve(f.Catalog.Get("Version")).AsName() ?? f.Version;
        if (double.TryParse(version, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 2.0)
            c.Add("PDFA-VERSION", ValidationSeverity.Error, $"PDF version {version} is not allowed (PDF/A-1/2/3 are based on PDF 1.x)", Clause(c, "6.1.2", "6.1.2", "6.1.2"));
        if (c.Part == 1 && f.Resolve(f.Catalog.Get("OCProperties")) is not null and not PdfNull)
            c.Add("PDFA-OPTIONAL-CONTENT", ValidationSeverity.Error, "optional content is not permitted in PDF/A-1", "ISO 19005-1 6.1.13");
        if (f.Resolve(f.Catalog.Get("AA")) is not null and not PdfNull)
            c.Add("PDFA-CATALOG-AA", ValidationSeverity.Error, "catalog additional-actions entry is not permitted", Clause(c, "6.6.2", "6.5.2", "6.5.2"));
    }

    static void Metadata(ValidationContext c)
    {
        var f = c.F;
        var x = c.Xmp;
        if (!x.Present) { c.Add("PDFA-XMP-MISSING", ValidationSeverity.Error, "catalog has no /Metadata XMP stream", Clause(c, "6.7.2", "6.6.2.1", "6.6.2.1")); return; }
        if (!x.WellFormed) { c.Add("PDFA-XMP-INVALID", ValidationSeverity.Error, "XMP metadata is not well-formed XML", Clause(c, "6.7.2", "6.6.2.1", "6.6.2.1")); return; }
        if (f.Resolve(f.Catalog.Get("Metadata")) is PdfStream ms)
        {
            if (f.Resolve(ms.Dict.Get("Subtype")).AsName() != "XML" || f.Resolve(ms.Dict.Get("Type")).AsName() != "Metadata")
                c.Add("PDFA-XMP-STREAM", ValidationSeverity.Error, "metadata stream must have /Type /Metadata and /Subtype /XML", Clause(c, "6.7.2", "6.6.2.1", "6.6.2.1"));
        }
        var part = x.Get(XmpData.PdfAId, "part");
        var conf = x.Get(XmpData.PdfAId, "conformance");
        if (part is null) c.Add("PDFA-XMP-ID-MISSING", ValidationSeverity.Error, "XMP has no pdfaid:part identification", Clause(c, "6.7.11", "6.6.4", "6.6.4"));
        else if (part != c.Part.ToString()) c.Add("PDFA-XMP-ID-PART", ValidationSeverity.Error, $"pdfaid:part is '{part}', expected '{c.Part}'", Clause(c, "6.7.11", "6.6.4", "6.6.4"));
        if (part is not null && conf is null) c.Add("PDFA-XMP-ID-CONFORMANCE", ValidationSeverity.Error, "XMP has no pdfaid:conformance", Clause(c, "6.7.11", "6.6.4", "6.6.4"));
        else if (conf is not null && conf != "B") c.Add("PDFA-XMP-ID-CONFORMANCE", ValidationSeverity.Error, $"pdfaid:conformance is '{conf}'; level B was requested", Clause(c, "6.7.11", "6.6.4", "6.6.4"));
    }


    static void Fonts(ValidationContext c)
    {
        c.EnsureWalked();
        var usageReported = false;
        foreach (var u in c.Fonts)
        {
            if (!FontChecks.IsEmbedded(c.F, u.Font))
                c.Add("PDFA-FONT-EMBEDDED", ValidationSeverity.Error, $"font {FontChecks.Name(c.F, u.Font)} is not embedded", Clause(c, "6.3.4", "6.2.11.4.1", "6.2.11.4.1"), u.Page);
            else if (c.F.Resolve(u.Font.Get("Subtype")).AsName() == "Type0" && c.F.Resolve(u.Font.Get("DescendantFonts")) is PdfArray { Count: > 0 } df
                && c.F.Resolve(df[0]) is PdfDictionary cid && c.F.Resolve(cid.Get("Subtype")).AsName() == "CIDFontType2"
                && c.F.Resolve(cid.Get("CIDToGIDMap")) is not (PdfStream or PdfName { Value: "Identity" }))
            {
                if (c.Part == 1)
                {
                    if (!c.FontUsageComplete && !usageReported)
                    {
                        usageReported = true;
                        c.Add("PDFA-FONT-USAGE-INCOMPLETE", ValidationSeverity.Error,
                            "Font rendering use could not be completely inspected; the mode-3 exemption for invalid CIDToGIDMap entries cannot be established",
                            "ISO 19005-1 6.3.3.2");
                    }
                    if (u.RenderingPage is null) continue;
                }
                c.Add("PDFA-FONT-CIDTOGID", ValidationSeverity.Error,
                    $"embedded CIDFontType2 {FontChecks.Name(c.F, cid)}{(c.Part == 1 ? " used for rendering" : "")} requires /CIDToGIDMap to be a stream or /Identity",
                    Clause(c, "6.3.3.2", "6.2.11.3.2", "6.2.11.3.2"), u.RenderingPage ?? u.Page);
            }
        }
    }

    static void OutputIntent(ValidationContext c, HashSet<string> outputSpaces)
    {
        var f = c.F;
        c.EnsureWalked();
        var intents = f.Resolve(f.Catalog.Get("OutputIntents")) as PdfArray;
        if (intents is null || intents.Count == 0)
        {
            if (c.UsesDeviceColor || c.UsesUnmappedDeviceGray)
                c.Add("PDFA-OUTPUTINTENT-MISSING", ValidationSeverity.Error, "device-dependent colour is used but the catalog has no /OutputIntents (device colour use is detected heuristically)", Clause(c, "6.2.3.3", "6.2.4.3", "6.2.4.3"));
            return;
        }
        PdfStream? profile = null;
        foreach (var item in intents.Items)
        {
            if (f.Resolve(item) is not PdfDictionary oi) continue;
            var isPdfAOutputIntent = f.Resolve(oi.Get("S")).AsName() == "GTS_PDFA1";
            if (!isPdfAOutputIntent)
                c.Add("PDFA-OUTPUTINTENT-SUBTYPE", ValidationSeverity.Error, "output intent /S must be /GTS_PDFA1", Clause(c, "6.2.2", "6.2.3", "6.2.3"));
            if (f.Resolve(oi.Get("DestOutputProfile")) is PdfStream p)
            {
                if (profile is not null && !ReferenceEquals(profile, p))
                    c.Add("PDFA-OUTPUTINTENT-PROFILES", ValidationSeverity.Error, "multiple different DestOutputProfile streams", Clause(c, "6.2.2", "6.2.3", "6.2.3"));
                profile = p;
                var d = f.DecodeStream(p);
                if (d.Length < 128 || d[36] != 'a' || d[37] != 'c' || d[38] != 's' || d[39] != 'p')
                    c.Add("PDFA-OUTPUTINTENT-ICC", ValidationSeverity.Error, "DestOutputProfile is not a valid ICC profile", Clause(c, "6.2.2", "6.2.3", "6.2.3"));
                else if (isPdfAOutputIntent && PdfADeepChecks.ReadIcc(d) is { } header)
                    outputSpaces.Add(header.Space);
            }
        }
        if (profile is null)
            c.Add("PDFA-OUTPUTINTENT-PROFILE", ValidationSeverity.Error, "output intent has no /DestOutputProfile", Clause(c, "6.2.2", "6.2.3", "6.2.3"));
        if (c.UsesUnmappedDeviceGray && outputSpaces.Count == 0)
            c.Add("PDFA-OUTPUTINTENT-MISSING", ValidationSeverity.Error, "DeviceGray is used without a device-independent /DefaultGray colour space or a PDF/A OutputIntent", Clause(c, "6.2.3.3", "6.2.4.3", "6.2.4.3"));
        if (c.UsesUnmappedDeviceRgb && !outputSpaces.Contains("RGB "))
            c.Add("PDFA-OUTPUTINTENT-COLORSPACE", ValidationSeverity.Error, "DeviceRGB is painted without a device-independent /DefaultRGB or a PDF/A OutputIntent with an RGB profile", Clause(c, "6.2.3.3", "6.2.4.3", "6.2.4.3"));
        if (c.UsesUnmappedDeviceCmyk && !outputSpaces.Contains("CMYK"))
            c.Add("PDFA-OUTPUTINTENT-COLORSPACE", ValidationSeverity.Error, "DeviceCMYK is painted without a device-independent /DefaultCMYK or a PDF/A OutputIntent with a CMYK profile", Clause(c, "6.2.3.3", "6.2.4.3", "6.2.4.3"));
    }

    static void Transparency(ValidationContext c)
    {
        if (c.Part != 1) return;
        var f = c.F;
        c.EnsureWalked();
        const string clause = "ISO 19005-1 6.4";
        foreach (var (gs, page) in c.ExtGStates)
        {
            if (f.Resolve(gs.Get("SMask")) is { } sm && sm is not PdfNull && sm.AsName() != "None")
                c.Add("PDFA1-TRANSPARENCY-SMASK", ValidationSeverity.Error, "ExtGState soft mask is not permitted", clause, page);
            foreach (var k in new[] { "CA", "ca" })
                if (f.Resolve(gs.Get(k)).AsNumber() is { } a && a != 1)
                    c.Add("PDFA1-TRANSPARENCY-ALPHA", ValidationSeverity.Error, $"/{k} {a} (non-opaque) is not permitted", clause, page);
            var bm = f.Resolve(gs.Get("BM"));
            if (bm is PdfArray { Count: > 0 } ba) bm = f.Resolve(ba[0]);
            if (bm.AsName() is { } bn && bn is not ("Normal" or "Compatible"))
                c.Add("PDFA1-TRANSPARENCY-BLEND", ValidationSeverity.Error, $"blend mode /{bn} is not permitted", clause, page);
        }
        foreach (var (img, page) in c.Images)
            if (f.Resolve(img.Dict.Get("SMask")) is PdfStream || (f.Resolve(img.Dict.Get("SMaskInData")).AsNumber() ?? 0) > 0)
                c.Add("PDFA1-TRANSPARENCY-IMAGE", ValidationSeverity.Error, "image soft mask is not permitted", clause, page);
        for (var i = 0; i < c.Doc.Pages.Count; i++)
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Group")) is PdfDictionary g && f.Resolve(g.Get("S")).AsName() == "Transparency")
                c.Add("PDFA1-TRANSPARENCY-GROUP", ValidationSeverity.Error, "page transparency group is not permitted", clause, i);
    }

    static bool AnnotationFieldType(PdfFile f, PdfDictionary annotation, out string? fieldType)
    {
        fieldType = null;
        var current = annotation;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (var depth = 0; depth < 64; depth++)
        {
            if (!seen.Add(current)) return false;
            var value = f.Resolve(current.Get("FT"));
            if (value is not null and not PdfNull)
            {
                fieldType = value.AsName();
                return true;
            }
            var parent = f.Resolve(current.Get("Parent"));
            if (parent is null or PdfNull) return true;
            if (parent is not PdfDictionary dictionary) return false;
            current = dictionary;
        }
        return false;
    }

    static void Annotations(ValidationContext c, HashSet<string> outputSpaces)
    {
        const int annotationLimit = 10_000;
        var f = c.F;
        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            if (c.Part != 1 && f.Resolve(c.Doc.Pages[i].Dict.Get("AA")) is not null and not PdfNull)
                c.Add("PDFA-PAGE-AA", ValidationSeverity.Error, "page additional-actions entry is not permitted", Clause(c, "6.6.2", "6.5.2", "6.5.2"), i);
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            if (annots.Count > annotationLimit)
                c.Add("PDFA-ANNOT-INCOMPLETE", ValidationSeverity.Error, $"cannot completely inspect {annots.Count} page annotation entries: limit is {annotationLimit}", Clause(c, "6.5", "6.3", "6.3"), i);
            foreach (var a in annots.Items.Take(annotationLimit))
            {
                if (f.Resolve(a) is not PdfDictionary ad) continue;
                var sub = f.Resolve(ad.Get("Subtype")).AsName();
                var allowed = sub is "Text" or "Link" or "FreeText" or "Line" or "Square" or "Circle" or "Highlight" or "Underline"
                    or "Squiggly" or "StrikeOut" or "Stamp" or "Ink" or "Popup" or "Widget" or "PrinterMark" or "TrapNet"
                    || c.Part != 1 && sub is "Polygon" or "PolyLine" or "Caret" or "FileAttachment" or "Watermark" or "Redact";
                if (!allowed)
                    c.Add("PDFA-ANNOT-TYPE", ValidationSeverity.Error, $"annotation subtype /{sub ?? "(missing or non-name)"} is not permitted", Clause(c, "6.5.2", "6.3.1", "6.3.1"), i);
                if (sub == "Widget")
                {
                    if (f.Resolve(ad.Get("A")) is not null and not PdfNull)
                        c.Add("PDFA-WIDGET-ACTIONS", ValidationSeverity.Error, "Widget /A action entry is not permitted", Clause(c, "6.6.1", "6.4.1", "6.4.1"), i);
                    if (f.Resolve(ad.Get("AA")) is not null and not PdfNull)
                        c.Add("PDFA-WIDGET-ACTIONS", ValidationSeverity.Error, "Widget /AA additional-actions entry is not permitted", Clause(c, "6.6.2", "6.4.1", "6.4.1"), i);
                }
                if (c.Part == 1 && f.Resolve(ad.Get("CA")).AsNumber() is { } opacity && opacity != 1)
                    c.Add("PDFA1-ANNOT-OPACITY", ValidationSeverity.Error, "annotation opacity must be 1 in PDF/A-1", "ISO 19005-1 6.5.3", i);
                if (c.Part == 1 && !outputSpaces.Contains("RGB ")
                    && (f.Resolve(ad.Get("C")) is PdfArray || f.Resolve(ad.Get("IC")) is PdfArray))
                    c.Add("PDFA1-ANNOT-COLORSPACE", ValidationSeverity.Error, "annotation /C or /IC array requires an RGB PDF/A output profile", "ISO 19005-1 6.5.3", i);
                var appearance = f.Resolve(ad.Get("AP")) as PdfDictionary;
                if (c.Part == 1 && sub == "Widget" && appearance is null)
                    c.Add("PDFA1-FIELD-APPEARANCE", ValidationSeverity.Error, "page Widget field has no appearance dictionary", "ISO 19005-1 6.9", i);
                var normal = f.Resolve(appearance?.Get("N"));
                var normalOnly = appearance is not null && normal is not null and not PdfNull
                    && !appearance.Entries.Any(entry => entry.Key != "N" && f.Resolve(entry.Value) is not null and not PdfNull);
                if (appearance is not null && !normalOnly)
                    c.Add("PDFA-ANNOT-APPEARANCE-KEYS", ValidationSeverity.Error, "annotation appearance dictionary must contain only a non-null /N entry", Clause(c, "6.5.3", "6.3.3", "6.3.3"), i);
                if (normalOnly)
                {
                    string? fieldType = null;
                    if (sub == "Widget" && !AnnotationFieldType(f, ad, out fieldType))
                        c.Add("PDFA-ANNOT-APPEARANCE-INCOMPLETE", ValidationSeverity.Error, "cannot resolve Widget field type within the 64-node parent traversal bound or through a cyclic/malformed parent chain", Clause(c, "6.5.3", "6.3.3", "6.3.3"), i);
                    else if (sub == "Widget" && fieldType == "Btn")
                    {
                        const int stateLimit = 10_000;
                        var states = normal as PdfDictionary;
                        if (states?.Entries.Take(stateLimit).Any(entry => f.Resolve(entry.Value) is PdfStream) != true)
                        {
                            if (states?.Entries.Count > stateLimit)
                                c.Add("PDFA-ANNOT-APPEARANCE-INCOMPLETE", ValidationSeverity.Error, $"cannot determine whether button Widget has an appearance stream within the {stateLimit}-entry state search bound", Clause(c, "6.5.3", "6.3.3", "6.3.3"), i);
                            else
                                c.Add("PDFA-ANNOT-APPEARANCE-BUTTON", ValidationSeverity.Error, "button Widget normal appearance must be a dictionary containing at least one stream", Clause(c, "6.5.3", "6.3.3", "6.3.3"), i);
                        }
                    }
                    else if (normal is not PdfStream)
                        c.Add("PDFA-ANNOT-APPEARANCE-STREAM", ValidationSeverity.Error, "non-button annotation normal appearance must be a stream", Clause(c, "6.5.3", "6.3.3", "6.3.3"), i);
                }
                if (c.Part != 1 && sub is not ("Popup" or "Link") && appearance is null)
                {
                    var point = f.Resolve(ad.Get("Rect")) is PdfArray { Count: 4 } rect
                        && f.Resolve(rect[0]).AsNumber() is { } x0 && f.Resolve(rect[1]).AsNumber() is { } y0
                        && f.Resolve(rect[2]).AsNumber() is { } x1 && f.Resolve(rect[3]).AsNumber() is { } y1
                        && x0 == x1 && y0 == y1;
                    if (!point)
                        c.Add("PDFA-ANNOT-APPEARANCE-MISSING", ValidationSeverity.Error, "non-exempt annotation has no appearance dictionary", Clause(c, "6.5.3", "6.3.3", "6.3.3"), i);
                }
                var flagValue = f.Resolve(ad.Get("F"));
                if (c.Part != 1 && sub == "Popup" && flagValue is null or PdfNull) continue;
                var flags = (int)(flagValue.AsNumber() ?? 0);
                if ((flags & 4) == 0) c.Add("PDFA-ANNOT-PRINT", ValidationSeverity.Error, "annotation lacks the Print flag", Clause(c, "6.5.3", "6.3.2", "6.3.2"), i);
                if ((flags & (1 | 2 | 32 | (c.Part == 1 ? 0 : 256))) != 0)
                    c.Add("PDFA-ANNOT-HIDDEN", ValidationSeverity.Error, $"annotation visibility flags are not permitted in this PDF/A part (/F {flags})", Clause(c, "6.5.3", "6.3.2", "6.3.2"), i);
            }
        }
    }

    static void ObjectScan(ValidationContext c)
    {
        var f = c.F;
        var reportedLzw = false; var reportedJpx = false; var reportedObjStm = false; var reportedFile = false; var reportedAf = false;
        var reportedXObjects = new HashSet<string>(StringComparer.Ordinal);
        void XObjectFinding(string rule, string message, string part1, string part23)
        {
            if (reportedXObjects.Add(rule))
                c.Add(rule, ValidationSeverity.Error, message, Clause(c, part1, part23, part23));
        }
        foreach (var o in c.AllObjects())
        {
            var dict = o switch { PdfStream s => s.Dict, PdfDictionary d => d, _ => null };
            if (dict is null) continue;
            if (o is PdfStream st)
            {
                var subtype = f.Resolve(dict.Get("Subtype")).AsName();
                if (subtype == "PS" && f.Resolve(dict.Get("Type")).AsName() == "XObject")
                    XObjectFinding("PDFA-XOBJECT-POSTSCRIPT", "a standalone PostScript XObject is not permitted", "6.2.7", "6.2.9");
                if (subtype == "Image")
                {
                    if (f.Resolve(dict.Get("Interpolate")) is PdfBoolean { Value: true })
                        XObjectFinding("PDFA-IMAGE-INTERPOLATE", "an image XObject requests interpolation; PDF/A requires /Interpolate to be false", "6.2.4", "6.2.8");
                    if (f.Resolve(dict.Get("Alternates")) is not null and not PdfNull)
                        XObjectFinding("PDFA-IMAGE-ALTERNATES", "an image XObject contains /Alternates", "6.2.4", "6.2.8");
                    if (f.Resolve(dict.Get("OPI")) is not null and not PdfNull)
                        XObjectFinding("PDFA-IMAGE-OPI", "an image XObject contains /OPI", "6.2.4", "6.2.8");
                    var bits = f.Resolve(dict.Get("BitsPerComponent"));
                    if (bits is not null and not PdfNull)
                    {
                        if (bits is not PdfInteger bpc || !(bpc.Value is 1 or 2 or 4 or 8 || c.Part > 1 && bpc.Value == 16))
                            XObjectFinding("PDFA-IMAGE-BPC", "an image XObject has a BitsPerComponent value not permitted by this PDF/A profile", "6.2.4", "6.2.8");
                        if (f.Resolve(dict.Get("ImageMask")) is PdfBoolean { Value: true } && bits is not PdfInteger { Value: 1 })
                            XObjectFinding("PDFA-IMAGE-MASK-BPC", "an image mask's /BitsPerComponent must be 1 when present", "6.2.4", "6.2.8");
                    }
                }
                else if (subtype == "Form")
                {
                    if (f.Resolve(dict.Get("OPI")) is not null and not PdfNull)
                        XObjectFinding("PDFA-FORM-OPI", "a Form XObject contains /OPI", "6.2.4", "6.2.9");
                    if (f.Resolve(dict.Get("PS")) is not null and not PdfNull || f.Resolve(dict.Get("Subtype2")).AsName() == "PS")
                        XObjectFinding("PDFA-FORM-POSTSCRIPT", "a Form XObject contains /PS or /Subtype2 /PS", "6.2.5", "6.2.9");
                }
                foreach (var name in FilterNames(f, st.Dict))
                {
                    if (name is "LZWDecode" or "LZW" && !reportedLzw) { reportedLzw = true; c.Add("PDFA-FILTER-LZW", ValidationSeverity.Error, "LZWDecode filter is not permitted", Clause(c, "6.1.10", "6.1.10", "6.1.10")); }
                    if (name == "JPXDecode" && c.Part == 1 && !reportedJpx) { reportedJpx = true; c.Add("PDFA1-FILTER-JPX", ValidationSeverity.Error, "JPXDecode is not permitted in PDF/A-1", "ISO 19005-1 6.2.8"); }
                }
                if (f.Resolve(st.Dict.Get("F")) is not null and not PdfNull
                    || f.Resolve(st.Dict.Get("FFilter")) is not null and not PdfNull
                    || f.Resolve(st.Dict.Get("FDecodeParms")) is not null and not PdfNull)
                    c.Add("PDFA-STREAM-EXTERNAL", ValidationSeverity.Error, "stream refers to an external file (/F, /FFilter or /FDecodeParms)", Clause(c, "6.1.7", "6.1.7.1", "6.1.7.1"));
                if (c.Part == 1 && !reportedObjStm && f.Resolve(st.Dict.Get("Type")).AsName() is "ObjStm" or "XRef")
                { reportedObjStm = true; c.Add("PDFA1-OBJSTM", ValidationSeverity.Error, "object streams and cross-reference streams are not permitted in PDF/A-1", "ISO 19005-1 6.1.4"); }
            }
            if (dict.Get("EF") is not null && f.Resolve(dict.Get("Type")).AsName() is "Filespec" or null)
            {
                if (c.Part < 3 && !reportedFile)
                {
                    reportedFile = true;
                    c.Add("PDFA-EMBEDDED-FILE", c.Part == 1 ? ValidationSeverity.Error : ValidationSeverity.Warning,
                        c.Part == 1 ? "embedded files are not permitted in PDF/A-1" : "embedded files must themselves conform to PDF/A-1 or PDF/A-2", Clause(c, "6.1.11", "6.8", "6.8"));
                }
                if (c.Part == 3 && !reportedAf && f.Resolve(dict.Get("AFRelationship")) is null)
                { reportedAf = true; c.Add("PDFA3-AFRELATIONSHIP", ValidationSeverity.Error, "embedded file specification has no /AFRelationship", "ISO 19005-3 6.8"); }
            }
        }
        if (f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names && f.Resolve(names.Get("JavaScript")) is PdfDictionary)
            c.Add("PDFA-JAVASCRIPT", ValidationSeverity.Error, "JavaScript name tree is not permitted", Clause(c, "6.6.1", "6.5.1", "6.5.1"));
    }

    internal static IEnumerable<string> FilterNames(PdfFile f, PdfDictionary d)
    {
        var fl = f.Resolve(d.Get("Filter"));
        if (fl is PdfName n) yield return n.Value;
        else if (fl is PdfArray a) foreach (var i in a.Items) if (f.Resolve(i) is PdfName nn) yield return nn.Value;
    }
}
