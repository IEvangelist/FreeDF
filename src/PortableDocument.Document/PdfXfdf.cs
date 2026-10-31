using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PortableDocument.Document;

/// <summary>
/// XFDF (ISO 19444-1) import and export of authored annotations. Content is data only: rich text is carried as an inert string and never
/// interpreted, DTDs are rejected, and nothing in the XML can trigger an action, URI or script.
/// </summary>
public static class PdfXfdf
{
    static readonly XNamespace Ns = "http://ns.adobe.com/xfdf/";
    const int MaxAnnotations = 20000;

    static readonly Dictionary<string, string> ToElement = new()
    {
        ["Text"] = "text", ["Highlight"] = "highlight", ["Underline"] = "underline", ["StrikeOut"] = "strikeout", ["Squiggly"] = "squiggly",
        ["Square"] = "square", ["Circle"] = "circle", ["Line"] = "line", ["Polygon"] = "polygon", ["PolyLine"] = "polyline", ["Ink"] = "ink",
        ["FreeText"] = "freetext", ["Caret"] = "caret", ["Stamp"] = "stamp", ["Redact"] = "redact",
    };

    static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    static string Join(IEnumerable<double> v) => string.Join(',', v.Select(F));
    static string Hex(double[] c) => c.Length == 3 ? "#" + string.Concat(c.Select(v => ((int)Math.Round(Math.Clamp(v, 0, 1) * 255)).ToString("X2", CultureInfo.InvariantCulture))) : c.Length == 1
        ? "#" + string.Concat(Enumerable.Repeat(((int)Math.Round(Math.Clamp(c[0], 0, 1) * 255)).ToString("X2", CultureInfo.InvariantCulture), 3)) : "";

    static double[]? ParseColor(string? s) => s is { Length: 7 } && s[0] == '#' && int.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
        ? [(v >> 16 & 255) / 255.0, (v >> 8 & 255) / 255.0, (v & 255) / 255.0] : null;

    static double[]? ParseNumbers(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        var list = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++) if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out list[i]) || !double.IsFinite(list[i])) return null;
        return list;
    }

    /// <summary>Exports every authored annotation of the document.</summary>
    public static string Export(PdfEditor editor)
    {
        var annots = new XElement(Ns + "annots");
        for (var page = 0; page < editor.PageCount; page++)
        {
            var infos = editor.ListAnnotations(page);
            var names = infos.ToDictionary(i => i.Number, i => i.Spec.Name);
            foreach (var info in infos)
            {
                var s = info.Spec;
                if (s.Subtype is null || !ToElement.TryGetValue(s.Subtype, out var tag)) continue;
                var e = new XElement(Ns + tag, new XAttribute("page", page));
                void A(string n, object? v) { if (v is not null) e.SetAttributeValue(n, v); }
                A("rect", s.Rect is null ? null : Join(s.Rect));
                A("name", s.Name); A("title", s.Author); A("subject", s.Subject);
                A("creationdate", s.Created is { } c ? "D:" + c.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z" : null);
                A("date", s.Modified is { } m ? "D:" + m.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z" : null);
                A("flags", info.ParentNumber is null ? "print" : "noview");
                if (s.Color is { Length: > 0 } col) A("color", Hex(col));
                if (s.InteriorColor is { Length: > 0 } ic) A("interior-color", Hex(ic));
                if (s.Opacity is { } op && op != 1) A("opacity", F(op));
                if (s.BorderWidth is { } bw) A("width", F(bw));
                if (s.BorderStyle is "D") A("style", "dash");
                if (s.QuadPoints is { } q) A("coords", Join(q));
                if (s.Icon is { } icon && s.Subtype == "Text") A("icon", icon);
                if (s.StampName is { } sn) A("icon", sn);
                if (s.Subtype == "Line" && s.Line is { Length: >= 4 } l) { A("start", Join(l.Take(2))); A("end", Join(l.Skip(2).Take(2))); A("head", s.LineStart); A("tail", s.LineEnd); }
                if (s.State is { } st) { A("state", st); A("statemodel", s.StateModel); }
                if (info.ParentNumber is { } pn && names.TryGetValue(pn, out var parentName)) { A("inreplyto", parentName); A("replyto", parentName); A("replytype", s.ReplyType ?? "R"); }
                if (s.Subtype == "Caret") A("caret-symbol", s.Symbol);
                if (s.Cloud == true) A("intensity", "1");
                if (s.Vertices is { } vs) e.Add(new XElement(Ns + "vertices", string.Join(';', Pairs(vs))));
                if (s.InkList is { } ink) e.Add(new XElement(Ns + "inklist", ink.Select(g => new XElement(Ns + "gesture", string.Join(';', Pairs(g))))));
                if (s.Contents is { Length: > 0 } text) e.Add(new XElement(Ns + "contents", text));
                if (s.RichContents is { Length: > 0 } rc) e.Add(new XElement(Ns + "contents-richtext", rc));
                if (s.Subtype == "FreeText") { A("fontsize", s.FontSize is { } fs ? F(fs) : null); A("justification", s.Quadding is 1 ? "center" : s.Quadding is 2 ? "right" : "left"); }
                annots.Add(e);
            }
        }
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(Ns + "xfdf", new XAttribute(XNamespace.Xml + "space", "preserve"), annots));
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = new UTF8Encoding(false) })) doc.Save(w);
        return sb.ToString();
    }

    static IEnumerable<string> Pairs(double[] v)
    {
        for (var i = 0; i + 1 < v.Length; i += 2) yield return F(v[i]) + "," + F(v[i + 1]);
    }

    /// <summary>Imports annotations into the editor, generating appearances. Unknown elements and out-of-range pages are skipped. Returns the number created.</summary>
    public static int Import(PdfEditor editor, string xml, string? defaultAuthor = null, DateTimeOffset? now = null)
    {
        XDocument doc;
        using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64_000_000 }))
            doc = XDocument.Load(reader);
        var annots = doc.Root?.Element(Ns + "annots") ?? doc.Root?.Element("annots");
        if (annots is null) return 0;
        var byName = new Dictionary<string, (int Page, int Number)>();
        var elements = annots.Elements().Take(MaxAnnotations).ToList();
        var created = 0;
        using var batch = editor.BeginAnnotationBatch();
        foreach (var pass in new[] { false, true })
            foreach (var e in elements)
            {
                var replyTo = (string?)e.Attribute("inreplyto");
                if ((replyTo is not null) != pass) continue;
                var tag = e.Name.LocalName;
                var sub = ToElement.FirstOrDefault(kv => kv.Value == tag).Key;
                if (sub is null || !int.TryParse((string?)e.Attribute("page"), NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page >= editor.PageCount) continue;
                string? At(string n) => (string?)e.Attribute(n);
                var vertices = ParseNumbers(e.Elements().FirstOrDefault(x => x.Name.LocalName == "vertices")?.Value);
                var ink = e.Elements().FirstOrDefault(x => x.Name.LocalName == "inklist")?.Elements().Select(g => ParseNumbers(g.Value)).Where(g => g is { Length: >= 2 }).Select(g => g!).ToList();
                var start = ParseNumbers(At("start")); var end = ParseNumbers(At("end"));
                var rect = ParseNumbers(At("rect"));
                var contents = e.Elements().FirstOrDefault(x => x.Name.LocalName == "contents")?.Value;
                var spec = new PdfAnnotationSpec
                {
                    Subtype = sub, Rect = rect is { Length: 4 } ? rect : null, Name = At("name"), Author = At("title") ?? defaultAuthor, Subject = At("subject"),
                    Color = ParseColor(At("color")), InteriorColor = ParseColor(At("interior-color")),
                    Opacity = double.TryParse(At("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var op) ? op : null,
                    BorderWidth = double.TryParse(At("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var bw) && bw >= 0 ? bw : null,
                    BorderStyle = At("style") == "dash" ? "D" : null,
                    QuadPoints = ParseNumbers(At("coords")) is { Length: >= 8 } cq && cq.Length % 8 == 0 ? cq : null,
                    Vertices = vertices is { Length: >= 4 } && vertices.Length % 2 == 0 ? vertices : null,
                    InkList = ink is { Count: > 0 } ? ink : null,
                    Line = start is { Length: 2 } && end is { Length: 2 } ? [start[0], start[1], end[0], end[1]] : null,
                    LineStart = At("head"), LineEnd = At("tail"), Cloud = At("intensity") is { } it && it != "0" ? true : null,
                    Icon = sub == "Text" ? At("icon") : null, StampName = sub == "Stamp" ? At("icon") ?? "Draft" : null,
                    Contents = contents, RichContents = e.Elements().FirstOrDefault(x => x.Name.LocalName == "contents-richtext")?.Value,
                    State = At("state"), StateModel = At("statemodel"), Symbol = At("caret-symbol"),
                    FontSize = double.TryParse(At("fontsize"), NumberStyles.Float, CultureInfo.InvariantCulture, out var fs) && fs > 0 ? fs : null,
                    Quadding = At("justification") switch { "center" => 1, "right" => 2, _ => null },
                    Created = ParseDate(At("creationdate")), Modified = ParseDate(At("date")),
                };
                if (replyTo is not null)
                {
                    if (!byName.TryGetValue(replyTo, out var parent) || parent.Page != page) continue;
                    spec = spec with { InReplyTo = parent.Number, ReplyType = At("replytype") == "Group" ? "Group" : "R", Subtype = "Text", Rect = null };
                }
                var needsGeometry = sub is "Highlight" or "Underline" or "StrikeOut" or "Squiggly" or "Redact" ? spec.QuadPoints is null
                    : sub is "Polygon" or "PolyLine" ? spec.Vertices is null : sub == "Line" ? spec.Line is null : sub == "Ink" ? spec.InkList is null
                    : spec.Rect is null && replyTo is null;
                if (needsGeometry) continue;
                var number = editor.AddAnnotation(page, spec, now);
                created++;
                if (spec.Name is { Length: > 0 }) byName[spec.Name] = (page, number.Number);
            }
        return created;
    }

    static DateTimeOffset? ParseDate(string? s)
    {
        if (s is null || !s.StartsWith("D:", StringComparison.Ordinal) || s.Length < 16) return null;
        return DateTimeOffset.TryParseExact(s.AsSpan(2, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
    }
}
