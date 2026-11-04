using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Deeper PDF/A checks: ICC profile consistency, Info/XMP equivalence, XMP extension schemas, font dictionary completeness and form/action restrictions.
/// Rules were compared with the published veraPDF profiles PDFA-1B/2B/3B (rule ids cited per check); rules without a profile counterpart say so in their messages. This is not a certification.
/// </summary>
internal static partial class PdfADeepChecks
{
    static readonly HashSet<string> ForbiddenActions = new(StringComparer.Ordinal)
        { "Launch", "Sound", "Movie", "ResetForm", "ImportData", "Hide", "JavaScript", "SetOCGState", "Rendition", "Trans", "GoTo3DView" };

    [GeneratedRegex("^[A-Z]{6}\\+")]
    private static partial Regex SubsetPrefix();

    static string Clause(ValidationContext c, string a1, string a2, string a3) => PdfAChecks.Clause(c, a1, a2, a3);

    static readonly HashSet<string> Standard14 = new(StringComparer.Ordinal)
    {
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic", "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Symbol", "ZapfDingbats",
    };

    // Namespaces predefined by XMP (2004/2005) and the PDF/A schemas themselves; anything else needs a pdfaExtension declaration.
    static readonly HashSet<string> PredefinedNamespaces = new(StringComparer.Ordinal)
    {
        XmpData.Dc, "http://ns.adobe.com/xap/1.0/", "http://ns.adobe.com/pdf/1.3/", "http://ns.adobe.com/photoshop/1.0/", "http://ns.adobe.com/tiff/1.0/",
        "http://ns.adobe.com/exif/1.0/", "http://ns.adobe.com/xap/1.0/rights/", "http://ns.adobe.com/xap/1.0/mm/", "http://ns.adobe.com/xap/1.0/bj/",
        "http://ns.adobe.com/xap/1.0/t/pg/", "http://ns.adobe.com/xmp/Identifier/qual/1.0/",
        XmpData.PdfAId, XmpData.PdfAExtension, XmpData.PdfASchema, XmpData.PdfAProperty, "http://www.aiim.org/pdfa/ns/type#", "http://www.aiim.org/pdfa/ns/field#",
        XmpData.Rdf, "http://www.w3.org/XML/1998/namespace", "adobe:ns:meta/",
    };

    internal sealed record IccHeader(long Size, int Major, int Minor, string Class, string Space);

    internal static IccHeader? ReadIcc(byte[] d)
    {
        if (d.Length < 128 || d[36] != 'a' || d[37] != 'c' || d[38] != 's' || d[39] != 'p') return null;
        var size = ((long)d[0] << 24) | ((long)d[1] << 16) | ((long)d[2] << 8) | d[3];
        return new IccHeader(size, d[8], d[9] >> 4, System.Text.Encoding.Latin1.GetString(d, 12, 4), System.Text.Encoding.Latin1.GetString(d, 16, 4));
    }

    static int ComponentsOf(string space) => space switch { "GRAY" => 1, "RGB " or "Lab " => 3, "CMYK" => 4, _ => 0 };

    public static void Metadata(ValidationContext c)
    {
        var x = c.Xmp;
        if (!x.Present || !x.WellFormed) return;
        var f = c.F;

        if (x.PacketHeader is { } header && (Regex.IsMatch(header, @"\bbytes\s*=") || Regex.IsMatch(header, @"\bencoding\s*=")))
            c.Add("PDFA-XMP-PACKET-HEADER", ValidationSeverity.Error, "XMP packet header must not use the bytes or encoding attributes", Clause(c, "6.7.5", "6.6.2.1", "6.6.2.1"));

        foreach (var problem in x.ExtensionProblems.Take(50))
            c.Add("PDFA-XMP-EXTENSION-INVALID", ValidationSeverity.Error, $"PDF/A extension schema is not well formed: {problem}", Clause(c, "6.7.8", "6.6.2.3.3", "6.6.2.3.3"));

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (ns, local) in x.Properties)
        {
            if (PredefinedNamespaces.Contains(ns) || (c.Part >= 2 && ns == "http://ns.adobe.com/xmp/1.0/DynamicMedia/")) continue;
            if (x.DeclaredProperties.Contains((ns, local))) continue;
            if (reported.Add(ns + local))
                c.Add("PDFA-XMP-EXTENSION-UNDECLARED", ValidationSeverity.Error, $"XMP property {{{ns}}}{local} is not predefined and is not described by a PDF/A extension schema", Clause(c, "6.7.9", "6.6.2.3.1", "6.6.2.3.1"));
        }

        // veraPDF PDF/A-1b has CosInfo rules 6.7.3-1..8; the PDF/A-2b and -3b profiles contain no Info/XMP equivalence rules, so this check is part 1 only.
        if (c.Part != 1 || f.Resolve(f.Trailer.Get("Info")) is not PdfDictionary info) return;
        var clause = "ISO 19005-1 6.7.3"; // veraPDF PDFA-1B CosInfo 6.7.3-1..8
        const string pdfNs = "http://ns.adobe.com/pdf/1.3/";
        const string xmpNs = "http://ns.adobe.com/xap/1.0/";

        void Text(string key, string? xmpValue, string xmpName)
        {
            if (f.Resolve(info.Get(key)) is not PdfString s) return;
            if (!string.Equals(s.Text, xmpValue, StringComparison.Ordinal))
                c.Add("PDFA-XMP-INFO-MISMATCH", ValidationSeverity.Error, $"Info /{key} does not match XMP {xmpName}", clause);
        }

        Text("Title", x.GetDefaultItem(XmpData.Dc, "title"), "dc:title");
        Text("Subject", x.GetDefaultItem(XmpData.Dc, "description"), "dc:description");
        Text("Keywords", x.Get(pdfNs, "Keywords"), "pdf:Keywords");
        Text("Creator", x.Get(xmpNs, "CreatorTool"), "xmp:CreatorTool");
        Text("Producer", x.Get(pdfNs, "Producer"), "pdf:Producer");
        if (f.Resolve(info.Get("Author")) is PdfString author)
        {
            var creators = x.Items.GetValueOrDefault((XmpData.Dc, "creator"));
            if (creators is not { Count: 1 } || !string.Equals(author.Text, creators[0].Value, StringComparison.Ordinal))
                c.Add("PDFA-XMP-INFO-MISMATCH", ValidationSeverity.Error, "Info /Author does not match a single dc:creator entry in XMP", clause);
        }
        Date("CreationDate", x.Get(xmpNs, "CreateDate"), "xmp:CreateDate");
        Date("ModDate", x.Get(xmpNs, "ModifyDate"), "xmp:ModifyDate");

        void Date(string key, string? xmpValue, string xmpName)
        {
            if (f.Resolve(info.Get(key)) is not PdfString s) return;
            var a = ParsePdfDate(s.Text);
            var b = xmpValue is null ? null : ParseXmpDate(xmpValue);
            if (a is null) return;
            if (b is null || a.Value != b.Value)
                c.Add("PDFA-XMP-INFO-MISMATCH", ValidationSeverity.Error, $"Info /{key} does not match XMP {xmpName}", clause);
        }
    }

    /// <summary>Parses D:YYYYMMDDHHmmSSOHH'mm' to a UTC instant (missing time zone is taken as UTC); null when malformed.</summary>
    internal static DateTimeOffset? ParsePdfDate(string s)
    {
        var m = Regex.Match(s, @"^D:(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?(?:(Z)|([+-])(\d{2})(?:'?(\d{2})'?)?)?$");
        if (!m.Success) return null;
        int G(int i, int dflt) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : dflt;
        try
        {
            var offset = TimeSpan.Zero;
            if (m.Groups[8].Success) offset = new TimeSpan(G(9, 0), G(10, 0), 0) * (m.Groups[8].Value == "-" ? -1 : 1);
            return new DateTimeOffset(G(1, 1), G(2, 1), G(3, 1), G(4, 0), G(5, 0), G(6, 0), offset).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    internal static DateTimeOffset? ParseXmpDate(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? new DateTimeOffset(d.UtcTicks - d.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero) : null;

    public static void IccProfiles(ValidationContext c)
    {
        var f = c.F;
        c.EnsureWalked();
        var seen = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        if (f.Resolve(f.Catalog.Get("OutputIntents")) is PdfArray intents)
            foreach (var item in intents.Items.Take(100))
                if (f.Resolve(item) is PdfDictionary oi && f.Resolve(oi.Get("DestOutputProfile")) is PdfStream p && seen.Add(p))
                    CheckProfile(c, p, output: true);
        foreach (var icc in c.IccBased.Take(1000))
            if (seen.Add(icc)) CheckProfile(c, icc, output: false);
    }

    static void CheckProfile(ValidationContext c, PdfStream stream, bool output)
    {
        var d = c.F.DecodeStream(stream);
        if (ReadIcc(d) is not { } h) return; // PDFA-OUTPUTINTENT-ICC already reports an unreadable output-intent profile.
        var where = output ? "output intent profile" : "ICCBased profile";
        var clause = output ? Clause(c, "6.2.2", "6.2.3", "6.2.3") : Clause(c, "6.2.3.2", "6.2.4.2", "6.2.4.2");
        if (h.Size != d.Length)
            c.Add("PDFA-ICC-SIZE", ValidationSeverity.Warning, $"{where} header size {h.Size} differs from the stream length {d.Length} (ICC.1 header check; no veraPDF counterpart)", clause);
        var limit = c.Part == 1 ? 3 : 5;
        if (h.Major >= limit)
            c.Add("PDFA-ICC-VERSION", ValidationSeverity.Error, $"{where} version {h.Major}.{h.Minor} is not permitted (must be below {limit}.0)", clause);
        var classes = output ? new[] { "prtr", "mntr" } : ["prtr", "mntr", "scnr", "spac"];
        if (!classes.Contains(h.Class))
            c.Add("PDFA-ICC-CLASS", ValidationSeverity.Error, $"{where} device class '{h.Class}' is not one of {string.Join("/", classes)}", clause);
        var n = ComponentsOf(h.Space);
        if (n == 0 || (output && h.Space == "Lab "))
            c.Add("PDFA-ICC-COLORSPACE", ValidationSeverity.Error, $"{where} colour space '{h.Space.Trim()}' is not permitted", clause);
        else if (c.F.Resolve(stream.Dict.Get("N")).AsNumber() is not { } nv || (int)nv != n)
            c.Add("PDFA-ICC-N", ValidationSeverity.Error, $"{where} /N {(c.F.Resolve(stream.Dict.Get("N")).AsNumber()?.ToString(CultureInfo.InvariantCulture) ?? "(missing)")} does not match the profile's {n} colour components", clause);
    }

    public static void FontDictionaries(ValidationContext c)
    {
        var f = c.F;
        c.EnsureWalked();
        foreach (var u in c.Fonts)
        {
            var font = u.Font;
            var subtype = f.Resolve(font.Get("Subtype")).AsName();
            var name = FontChecks.Name(f, font);
            if (subtype is "Type1" or "MMType1" or "TrueType" or "Type3")
            {
                var embedded = FontChecks.IsEmbedded(f, font);
                var standardUnembedded = subtype != "Type3" && !embedded && font.Get("Widths") is null && Standard14.Contains(name);
                if (!standardUnembedded)
                {
                    var first = f.Resolve(font.Get("FirstChar")).AsNumber();
                    var last = f.Resolve(font.Get("LastChar")).AsNumber();
                    var widths = f.Resolve(font.Get("Widths")) as PdfArray;
                    if (first is null || last is null || widths is null || widths.Count != (int)last.Value - (int)first.Value + 1)
                        c.Add("PDFA-FONT-WIDTHS", ValidationSeverity.Error, $"font {name} lacks consistent /FirstChar, /LastChar and /Widths (Widths length must equal LastChar-FirstChar+1)", Clause(c, "6.3.2", "6.2.11.2", "6.2.11.2"), u.Page);
                }
                if (subtype == "TrueType") TrueTypeEncoding(c, font, name, u.Page);
            }
            if (c.Part == 1 && subtype == "Type1" && SubsetPrefix().IsMatch(name) && FontChecks.IsEmbedded(f, font)
                && !(f.Resolve(font.Get("FontDescriptor")) is PdfDictionary fd && f.Resolve(fd.Get("CharSet")) is PdfString))
                c.Add("PDFA1-FONT-CHARSET", ValidationSeverity.Error, $"subset font {name} has no /CharSet string in its descriptor", "ISO 19005-1 6.3.5", u.Page);
            if (subtype == "Type1" && SubsetPrefix().IsMatch(name)
                && f.Resolve(font.Get("FontDescriptor")) is PdfDictionary type1Descriptor
                && f.Resolve(type1Descriptor.Get("CharSet")) is PdfString charset
                && f.Resolve(type1Descriptor.Get("FontFile3")) is PdfStream type1Program
                && f.Resolve(type1Program.Dict.Get("Subtype")).AsName() == "Type1C")
                Type1CCharSet(c, type1Program, charset, name, u.Page);
            if (subtype == "Type0" && f.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } df && f.Resolve(df[0]) is PdfDictionary cid)
            {
                if (c.Part == 1 && SubsetPrefix().IsMatch(FontChecks.Name(f, cid)) && FontChecks.IsEmbedded(f, font)
                    && !(f.Resolve(cid.Get("FontDescriptor")) is PdfDictionary cd && f.Resolve(cd.Get("CIDSet")) is PdfStream))
                    c.Add("PDFA1-FONT-CIDSET", ValidationSeverity.Error, $"subset CIDFont {FontChecks.Name(f, cid)} has no /CIDSet stream in its descriptor", "ISO 19005-1 6.3.5", u.Page);
                if (f.Resolve(cid.Get("Subtype")).AsName() == "CIDFontType2"
                    && SubsetPrefix().IsMatch(FontChecks.Name(f, cid))
                    && f.Resolve(cid.Get("CIDToGIDMap")) is { } cidMapping
                    && cidMapping is (PdfStream or PdfName { Value: "Identity" })
                    && f.Resolve(cid.Get("FontDescriptor")) is PdfDictionary cidDescriptor
                    && f.Resolve(cidDescriptor.Get("CIDSet")) is PdfStream cidSet
                    && f.Resolve(cidDescriptor.Get("FontFile2")) is PdfStream cidProgram)
                    TrueTypeCidSet(c, cidProgram, cidSet, cidMapping, FontChecks.Name(f, cid), u.Page);
                if (f.Resolve(font.Get("Encoding")) is PdfStream cmap && SystemInfo(f, cmap.Dict) is { } a && SystemInfo(f, cid) is { } b && a != b)
                    c.Add("PDFA-FONT-CMAP-SYSTEMINFO", ValidationSeverity.Error, $"embedded CMap of {name} has CIDSystemInfo {a} but the CIDFont has {b}", Clause(c, "6.3.3.1", "6.2.11.3.3", "6.2.11.3.3"), u.Page);
            }
        }
    }

    static void TrueTypeCidSet(ValidationContext c, PdfStream program, PdfStream cidSet, PdfObject mapping, string name, int page)
    {
        var clause = Clause(c, "6.3.5", "6.2.11.4.2", "6.2.11.4.2");
        void Incomplete(string reason) =>
            c.Add("PDFA-FONT-CIDSET-INCOMPLETE", ValidationSeverity.Error, $"cannot completely inspect TrueType CIDSet of {name}: {reason}", clause, page);
        try
        {
            var data = c.F.DecodeStream(program);
            if (data.Length > 16_777_216) { Incomplete("decoded font program exceeds 16777216 bytes"); return; }
            if (FontChecks.TrueTypeGlyphCount(data) is not { } glyphCount)
            { Incomplete("malformed or unsupported TrueType maxp metadata"); return; }
            var count = glyphCount;
            if (mapping is PdfStream map)
            {
                var mapData = c.F.DecodeStream(map);
                if (mapData.Length > 131_072 || mapData.Length % 2 != 0)
                { Incomplete("CIDToGIDMap has an odd byte count or exceeds 131072 bytes"); return; }
                count = Math.Max(1, mapData.Length / 2);
                for (var cid = 0; cid < mapData.Length / 2; cid++)
                {
                    if (BinaryPrimitives.ReadUInt16BigEndian(mapData.AsSpan(cid * 2)) < glyphCount) continue;
                    Incomplete("CIDToGIDMap refers to a GID outside the font program");
                    return;
                }
            }
            var bitmap = c.F.DecodeStream(cidSet);
            if (bitmap.Length > 1_048_576) { Incomplete("decoded CIDSet exceeds 1048576 bytes"); return; }
            for (var i = 0; i < Math.Max(bitmap.Length, (count + 7) / 8); i++)
            {
                var remaining = count - i * 8;
                var expected = remaining >= 8 ? 255 : remaining > 0 ? 255 << (8 - remaining) & 255 : 0;
                var actual = i < bitmap.Length ? bitmap[i] : 0;
                if (actual == expected) continue;
                c.Add("PDFA-FONT-CIDSET-LIST", ValidationSeverity.Error,
                    $"TrueType CIDSet of {name} must identify exactly CIDs 0 through {count - 1} defined by the font program and CIDToGIDMap", clause, page);
                return;
            }
        }
        catch (Exception e) when (e is PdfParseException or InvalidDataException or NotSupportedException
            or InvalidOperationException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException or OverflowException)
        {
            Incomplete(e.Message);
        }
    }

    static void Type1CCharSet(ValidationContext c, PdfStream program, PdfString charset, string name, int page)
    {
        var clause = Clause(c, "6.3.5", "6.2.11.4.2", "6.2.11.4.2");
        void Incomplete(string reason) =>
            c.Add("PDFA-FONT-CHARSET-INCOMPLETE", ValidationSeverity.Error, $"cannot completely inspect Type1C CharSet of {name}: {reason}", clause, page);
        void Invalid(string reason) =>
            c.Add("PDFA-FONT-CHARSET-LIST", ValidationSeverity.Error, $"Type1C CharSet of {name} {reason}", clause, page);
        try
        {
            if (charset.Bytes.Length > 1_048_576) { Incomplete("CharSet exceeds 1048576 bytes"); return; }
            var data = c.F.DecodeStream(program);
            if (data.Length > 16_777_216) { Incomplete("decoded font program exceeds 16777216 bytes"); return; }
            var names = CffGlyphNames.Read(data);
            var expected = new HashSet<string>(StringComparer.Ordinal);
            for (var gid = 1; gid < names.Length; gid++)
            {
                var glyph = names[gid];
                if (glyph.Length == 0 || glyph == ".notdef" || !expected.Add(glyph))
                { Incomplete("program glyph names are missing or duplicated"); return; }
            }
            var actual = new HashSet<string>(StringComparer.Ordinal);
            var lexer = new PdfLexer(charset.Bytes);
            var entries = 0;
            while (true)
            {
                lexer.SkipWhite();
                if (lexer.Pos == lexer.Length) break;
                if (entries++ >= 65_535) { Incomplete("CharSet exceeds 65535 name entries"); return; }
                var start = lexer.Pos;
                if (lexer.Data[lexer.Pos] != '/' || lexer.ReadObject(false) is not PdfName glyph)
                { Invalid("does not contain only names in PDF syntax"); return; }
                for (var i = start + 1; i < lexer.Pos; i++)
                {
                    if (lexer.Data[i] != '#') continue;
                    if (i + 2 >= lexer.Pos || !char.IsAsciiHexDigit((char)lexer.Data[i + 1])
                        || !char.IsAsciiHexDigit((char)lexer.Data[i + 2]))
                    { Invalid("contains an invalid PDF name escape"); return; }
                    i += 2;
                }
                actual.Add(glyph.Value);
            }
            if (!expected.SetEquals(actual))
                Invalid("must list exactly the program's defined glyph names, excluding .notdef");
        }
        catch (Exception e) when (e is PdfParseException or InvalidDataException or NotSupportedException
            or InvalidOperationException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException or OverflowException)
        {
            Incomplete(e.Message);
        }
    }

    static string? SystemInfo(PdfFile f, PdfDictionary d) =>
        f.Resolve(d.Get("CIDSystemInfo")) is PdfDictionary si && f.Resolve(si.Get("Registry")) is PdfString r && f.Resolve(si.Get("Ordering")) is PdfString o ? $"{r.Text}-{o.Text}" : null;

    static void TrueTypeEncoding(ValidationContext c, PdfDictionary font, string name, int page)
    {
        var f = c.F;
        if (f.Resolve(font.Get("FontDescriptor")) is not PdfDictionary desc || f.Resolve(desc.Get("Flags")).AsNumber() is not { } flags) return;
        var symbolic = ((int)flags & 4) != 0;
        var enc = f.Resolve(font.Get("Encoding"));
        var clause = Clause(c, "6.3.7", "6.2.11.6", "6.2.11.6");
        if (symbolic) SymbolicTrueTypeCmap(c, desc, name, page, clause);
        if (symbolic && enc is not null and not PdfNull)
            c.Add("PDFA-FONT-TRUETYPE-ENCODING", ValidationSeverity.Error, $"symbolic TrueType font {name} must not have an /Encoding entry", clause, page);
        else if (!symbolic)
        {
            var baseOk = enc.AsName() is "MacRomanEncoding" or "WinAnsiEncoding"
                || (enc is PdfDictionary ed && f.Resolve(ed.Get("BaseEncoding")).AsName() is "MacRomanEncoding" or "WinAnsiEncoding"
                    && (c.Part != 1 || f.Resolve(ed.Get("Differences")) is null or PdfNull));
            if (!baseOk)
                c.Add("PDFA-FONT-TRUETYPE-ENCODING", ValidationSeverity.Error,
                    c.Part == 1 ? $"non-symbolic TrueType font {name} needs MacRomanEncoding or WinAnsiEncoding without /Differences"
                        : $"non-symbolic TrueType font {name} needs MacRomanEncoding or WinAnsiEncoding", clause, page);
            if (c.Part != 1 && enc is PdfDictionary differencesEncoding)
            {
                var result = FontChecks.InspectDifferences(f, differencesEncoding);
                if (result.InvalidName is { } glyph)
                    c.Add("PDFA-FONT-TRUETYPE-ENCODING", ValidationSeverity.Error, $"non-symbolic TrueType font {name} has a /Differences name /{glyph} not listed in the Adobe Glyph List", clause, page);
                else if (result.IncompleteReason is { } reason)
                    c.Add("PDFA-FONT-ENCODING-INCOMPLETE", ValidationSeverity.Error, $"cannot completely inspect /Differences of {name}: {reason}", clause, page);
            }
        }
    }

    static void SymbolicTrueTypeCmap(ValidationContext c, PdfDictionary descriptor, string name, int page, string clause)
    {
        if (c.F.Resolve(descriptor.Get("FontFile2")) is not PdfStream program) return;
        (int Count, bool Symbol, bool Roman)? records;
        try { records = FontChecks.TrueTypeCmapRecords(c.F.DecodeStream(program)); }
        catch (Exception e) when (e is PdfParseException or InvalidDataException or NotSupportedException)
        {
            c.Add("PDFA-FONT-CMAP-INCOMPLETE", ValidationSeverity.Error, $"cannot inspect embedded TrueType cmap records of {name}: {e.Message}", clause, page);
            return;
        }
        if (records is not { } found)
            c.Add("PDFA-FONT-CMAP-INCOMPLETE", ValidationSeverity.Error, $"embedded TrueType cmap headers of {name} are truncated, malformed or unsupported; symbolic encoding validation is incomplete", clause, page);
        else if (found.Count != 1 && (c.Part == 1 || !found.Symbol))
            c.Add("PDFA-FONT-SYMBOLIC-CMAP", ValidationSeverity.Error,
                c.Part == 1 ? $"symbolic TrueType font {name} needs exactly one cmap encoding"
                    : $"symbolic TrueType font {name} needs exactly one cmap encoding or Microsoft Symbol (3,0)",
                clause, page);
    }

    public static void FieldAdditionalActions(ValidationContext c)
    {
        var f = c.F;
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary form
            || f.Resolve(form.Get("Fields")) is not PdfArray fields) return;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var work = 0;
        var findings = 0;
        var incomplete = false;
        var stopped = false;
        void Walk(PdfObject? value, int depth)
        {
            if (stopped) return;
            if (++work > FormFieldDetailFeatures.MaxFields) { incomplete = stopped = true; return; }
            if (depth >= FormFieldDetailFeatures.MaxDepth
                || f.Resolve(value) is not PdfDictionary field || !seen.Add(field))
            { incomplete = true; return; }
            if (f.Resolve(field.Get("AA")) is not null and not PdfNull)
            {
                if (findings++ >= 1000) { incomplete = stopped = true; return; }
                c.Add("PDFA-FIELD-AA", ValidationSeverity.Error, "form field additional-actions entry is not permitted", Clause(c, "6.6.2", "6.4.1", "6.4.1"));
            }
            var kids = f.Resolve(field.Get("Kids"));
            if (kids is null or PdfNull) return;
            if (kids is not PdfArray children) { incomplete = true; return; }
            foreach (var child in children.Items)
            {
                Walk(child, depth + 1);
                if (stopped) break;
            }
        }
        foreach (var field in fields.Items)
        {
            Walk(field, 0);
            if (stopped) break;
        }
        if (incomplete)
            c.Add("PDFA-FIELD-INCOMPLETE", ValidationSeverity.Error, "field additional-action inspection is incomplete due to malformed/repeated/cyclic field nodes or the 64-depth, 100000-node or 1000-finding bound", Clause(c, "6.6.2", "6.4.1", "6.4.1"));
    }

    public static void ActionsAndForms(ValidationContext c)
    {
        var f = c.F;
        if (f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary form
            && f.Resolve(form.Get("NeedAppearances")) is { } needAppearances and not PdfNull
            && needAppearances is not PdfBoolean { Value: false })
            c.Add("PDFA-FORM-NEEDAPPEARANCES", ValidationSeverity.Error, "AcroForm /NeedAppearances must be absent or false", Clause(c, "6.9", "6.4.1", "6.4.1"));
        if (c.Part != 1)
        {
            if (f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary acroForm && f.Resolve(acroForm.Get("XFA")) is not null and not PdfNull)
                c.Add("PDFA-FORM-XFA", ValidationSeverity.Error, "AcroForm /XFA is not permitted", Clause(c, "6.9", "6.4.2", "6.4.2"));
            if (f.Resolve(f.Catalog.Get("NeedsRendering")) is { } rendering and not PdfNull && rendering is not PdfBoolean { Value: false })
                c.Add("PDFA-NEEDSRENDERING", ValidationSeverity.Error, "catalog /NeedsRendering must be absent or false", Clause(c, "6.9", "6.4.2", "6.4.2"));
        }
        var actions = c.Doc.GetActionReport();
        var actionFindings = 0;
        var actionIncomplete = actions.Truncated;
        var namedReported = false;
        var prohibitedReported = false;
        foreach (var action in actions.Actions)
        {
            if (action.EventKey is not null && action.Event is null) continue;
            var rootArray = action.Trigger.IndexOf('[');
            var nextHop = action.Trigger.IndexOf("/Next", StringComparison.Ordinal);
            if (rootArray >= 0 && (nextHop < 0 || rootArray < nextHop)) continue;
            if (!prohibitedReported && action.ActionType is { } actionType
                && (ForbiddenActions.Contains(actionType) || c.Part == 1 && actionType == "GoToE")
                && f.Resolve(action.Dictionary.Get("Type")) is null or PdfNull or PdfName { Value: "Action" })
            {
                prohibitedReported = true;
                c.Add("PDFA-ACTION", ValidationSeverity.Error, $"action /{actionType} is not permitted", Clause(c, "6.6.1", "6.5.1", "6.5.1"), action.PageIndex);
            }
            if (!namedReported && action.ActionType == "Named"
                && f.Resolve(action.Dictionary.Get("N")).AsName() is not ("NextPage" or "PrevPage" or "FirstPage" or "LastPage"))
            {
                namedReported = true;
                c.Add("PDFA-ACTION-NAMED", ValidationSeverity.Error, $"named action /{f.Resolve(action.Dictionary.Get("N")).AsName() ?? "(missing)"} is not permitted (only NextPage, PrevPage, FirstPage, LastPage)", Clause(c, "6.6.1", "6.5.1", "6.5.1"), action.PageIndex);
            }
            if (action.ActionType is "GoTo" or "GoToR" or "Thread" or "URI" or "Named" or "SubmitForm"
                || c.Part != 1 && action.ActionType == "GoToE") continue;
            if (actionFindings++ >= 1000) { actionIncomplete = true; break; }
            c.Add("PDFA-ACTION-TYPE", ValidationSeverity.Error, $"action subtype /{action.ActionType ?? "(missing or non-name)"} is not permitted", Clause(c, "6.6.1", "6.5.1", "6.5.1"), action.PageIndex);
        }
        if (actionIncomplete)
            c.Add("PDFA-ACTION-INCOMPLETE", ValidationSeverity.Error, "action inspection was truncated by traversal limits, a cycle or the 1000 subtype-finding bound", Clause(c, "6.6.1", "6.5.1", "6.5.1"));
    }
}
