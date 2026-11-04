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
}

/// <summary>PDF/A-1b/2b/3b basic checks (ISO 19005-1/2/3).</summary>
internal static class PdfAChecks
{
    static readonly HashSet<string> ForbiddenActions = new(StringComparer.Ordinal)
        { "Launch", "Sound", "Movie", "ResetForm", "ImportData", "JavaScript", "SetOCGState", "Rendition", "Trans", "GoTo3DView" };

    public static void Run(ValidationContext c)
    {
        c.Guard("file structure", () => FileStructure(c));
        c.Guard("metadata", () => Metadata(c));
        c.Guard("fonts", () => Fonts(c));
        c.Guard("output intent", () => OutputIntent(c));
        c.Guard("transparency", () => Transparency(c));
        c.Guard("annotations", () => Annotations(c));
        c.Guard("object scan", () => ObjectScan(c));
        c.Guard("metadata consistency", () => PdfADeepChecks.Metadata(c));
        c.Guard("icc profiles", () => PdfADeepChecks.IccProfiles(c));
        c.Guard("font dictionaries", () => PdfADeepChecks.FontDictionaries(c));
        c.Guard("actions and forms", () => PdfADeepChecks.ActionsAndForms(c));
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
        if (f.Resolve(f.Catalog.Get("AA")) is PdfDictionary)
            c.Add("PDFA-CATALOG-AA", ValidationSeverity.Error, "catalog additional-actions dictionary is not permitted", Clause(c, "6.6.2", "6.6.1", "6.6.1"));
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
        foreach (var u in c.Fonts)
        {
            if (!FontChecks.IsEmbedded(c.F, u.Font))
                c.Add("PDFA-FONT-EMBEDDED", ValidationSeverity.Error, $"font {FontChecks.Name(c.F, u.Font)} is not embedded", Clause(c, "6.3.4", "6.2.11.4.1", "6.2.11.4.1"), u.Page);
            else if (c.F.Resolve(u.Font.Get("Subtype")).AsName() == "Type0" && c.F.Resolve(u.Font.Get("DescendantFonts")) is PdfArray { Count: > 0 } df
                && c.F.Resolve(df[0]) is PdfDictionary cid && c.F.Resolve(cid.Get("Subtype")).AsName() == "CIDFontType2" && cid.Get("CIDToGIDMap") is null)
                c.Add("PDFA-FONT-CIDTOGID", ValidationSeverity.Error, $"CIDFontType2 {FontChecks.Name(c.F, cid)} has no /CIDToGIDMap", Clause(c, "6.3.5", "6.2.11.3.2", "6.2.11.3.2"), u.Page);
        }
    }

    static void OutputIntent(ValidationContext c)
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
        var outputSpaces = new HashSet<string>(StringComparer.Ordinal);
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

    static void Annotations(ValidationContext c)
    {
        var f = c.F;
        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var a in annots.Items.Take(10_000))
            {
                if (f.Resolve(a) is not PdfDictionary ad) continue;
                var sub = f.Resolve(ad.Get("Subtype")).AsName();
                if (sub is "Sound" or "Movie" or "Screen" or "3D" && c.Part == 1 || sub is "Sound" or "Movie")
                    c.Add("PDFA-ANNOT-TYPE", ValidationSeverity.Error, $"annotation subtype /{sub} is not permitted", Clause(c, "6.5.2", "6.3.1", "6.3.1"), i);
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
        var reportedLzw = false; var reportedJpx = false; var reportedAction = false; var reportedObjStm = false; var reportedFile = false; var reportedAf = false;
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
                if (st.Dict.Get("F") is not null || st.Dict.Get("FFilter") is not null || st.Dict.Get("FDecodeParms") is not null)
                    c.Add("PDFA-STREAM-EXTERNAL", ValidationSeverity.Error, "stream refers to an external file (/F, /FFilter or /FDecodeParms)", Clause(c, "6.1.7", "6.1.7", "6.1.7"));
                if (c.Part == 1 && !reportedObjStm && f.Resolve(st.Dict.Get("Type")).AsName() is "ObjStm" or "XRef")
                { reportedObjStm = true; c.Add("PDFA1-OBJSTM", ValidationSeverity.Error, "object streams and cross-reference streams are not permitted in PDF/A-1", "ISO 19005-1 6.1.4"); }
            }
            if (!reportedAction && f.Resolve(dict.Get("S")).AsName() is { } actionType && ForbiddenActions.Contains(actionType) && (dict.Get("Type") is null || f.Resolve(dict.Get("Type")).AsName() == "Action"))
            { reportedAction = true; c.Add("PDFA-ACTION", ValidationSeverity.Error, $"action /{actionType} is not permitted", Clause(c, "6.6.1", "6.5.1", "6.5.1")); }
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
