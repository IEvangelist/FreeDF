using System.Globalization;
using System.Text.RegularExpressions;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Deeper PDF/A checks: ICC profile consistency, Info/XMP equivalence, XMP extension schemas, font dictionary completeness and form/action restrictions.
/// Rule semantics follow the published veraPDF validation profiles (ISO 19005-1/-2/-3); rules without a profile counterpart are marked in their messages.
/// </summary>
internal static partial class PdfADeepChecks
{
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
            c.Add("PDFA-XMP-EXTENSION-INVALID", ValidationSeverity.Error, $"PDF/A extension schema is not well formed: {problem}", Clause(c, "6.7.8", "6.6.2.3.2", "6.6.2.3.2"));

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (ns, local) in x.Properties)
        {
            if (PredefinedNamespaces.Contains(ns) || (c.Part >= 2 && ns == "http://ns.adobe.com/xmp/1.0/DynamicMedia/")) continue;
            if (x.DeclaredProperties.Contains((ns, local))) continue;
            if (reported.Add(ns + local))
                c.Add("PDFA-XMP-EXTENSION-UNDECLARED", ValidationSeverity.Error, $"XMP property {{{ns}}}{local} is not predefined and is not described by a PDF/A extension schema", Clause(c, "6.7.9", "6.6.2.3.1", "6.6.2.3.1"));
        }

        if (f.Resolve(f.Trailer.Get("Info")) is not PdfDictionary info) return;
        var clause = Clause(c, "6.7.3", "6.6.2.3", "6.6.2.3");
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
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;

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
                && !(f.Resolve(font.Get("FontDescriptor")) is PdfDictionary fd && fd.Get("CharSet") is not null))
                c.Add("PDFA1-FONT-CHARSET", ValidationSeverity.Error, $"subset font {name} has no /CharSet in its descriptor (listing completeness is not verified)", "ISO 19005-1 6.3.5", u.Page);
            if (subtype == "Type0" && f.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } df && f.Resolve(df[0]) is PdfDictionary cid)
            {
                if (c.Part == 1 && SubsetPrefix().IsMatch(FontChecks.Name(f, cid)) && FontChecks.IsEmbedded(f, font)
                    && !(f.Resolve(cid.Get("FontDescriptor")) is PdfDictionary cd && cd.Get("CIDSet") is not null))
                    c.Add("PDFA1-FONT-CIDSET", ValidationSeverity.Error, $"subset CIDFont {FontChecks.Name(f, cid)} has no /CIDSet in its descriptor (listing completeness is not verified)", "ISO 19005-1 6.3.5", u.Page);
                if (f.Resolve(font.Get("Encoding")) is PdfStream cmap && SystemInfo(f, cmap.Dict) is { } a && SystemInfo(f, cid) is { } b && a != b)
                    c.Add("PDFA-FONT-CMAP-SYSTEMINFO", ValidationSeverity.Error, $"embedded CMap of {name} has CIDSystemInfo {a} but the CIDFont has {b}", Clause(c, "6.3.3.1", "6.2.11.3.3", "6.2.11.3.3"), u.Page);
            }
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
        if (symbolic && enc is not null and not PdfNull)
            c.Add("PDFA-FONT-TRUETYPE-ENCODING", ValidationSeverity.Error, $"symbolic TrueType font {name} must not have an /Encoding entry", clause, page);
        else if (!symbolic)
        {
            var baseOk = enc.AsName() is "MacRomanEncoding" or "WinAnsiEncoding"
                || (enc is PdfDictionary ed && f.Resolve(ed.Get("BaseEncoding")).AsName() is "MacRomanEncoding" or "WinAnsiEncoding" && ed.Get("Differences") is null);
            if (!baseOk)
                c.Add("PDFA-FONT-TRUETYPE-ENCODING", ValidationSeverity.Error, $"non-symbolic TrueType font {name} needs MacRomanEncoding or WinAnsiEncoding without /Differences", clause, page);
        }
    }

    public static void ActionsAndForms(ValidationContext c)
    {
        var f = c.F;
        if (f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary form && f.Resolve(form.Get("NeedAppearances")) is PdfBoolean { Value: true })
            c.Add("PDFA-FORM-NEEDAPPEARANCES", ValidationSeverity.Error, "AcroForm /NeedAppearances must be absent or false", Clause(c, "6.9", "6.4.1", "6.4.1"));
        var reported = false;
        foreach (var o in c.AllObjects())
        {
            if (reported) break;
            if (o is not PdfDictionary d || f.Resolve(d.Get("S")).AsName() != "Named") continue;
            if (f.Resolve(d.Get("N")).AsName() is not ("NextPage" or "PrevPage" or "FirstPage" or "LastPage"))
            {
                reported = true;
                c.Add("PDFA-ACTION-NAMED", ValidationSeverity.Error, $"named action /{f.Resolve(d.Get("N")).AsName() ?? "(missing)"} is not permitted (only NextPage, PrevPage, FirstPage, LastPage)", Clause(c, "6.6.1", "6.5.1", "6.5.1"));
            }
        }
    }
}
