using System.Text.RegularExpressions;
using AdoBye.Core;

namespace AdoBye.Document;

/// <summary>PDF/UA-1 (ISO 14289-1) and PDF/UA-2 (ISO 14289-2) basic checks.</summary>
internal static partial class PdfUAChecks
{
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex LangTag();

    public static void Run(ValidationContext c)
    {
        c.Guard("document", () => Document(c));
        c.Guard("metadata", () => Metadata(c));
        c.Guard("security", () => Security(c));
        c.Guard("fonts", () => Fonts(c));
        c.Guard("pages", () => Pages(c));
        var tree = c.Doc.Structure;
        if (tree is null) return;
        c.Guard("structure types", () => StructureTypes(c, tree));
        c.Guard("language", () => Language(c, tree));
        c.Guard("figures", () => Figures(c, tree));
        c.Guard("headings", () => Headings(c, tree));
        c.Guard("tables", () => Tables(c, tree));
        c.Guard("lists", () => Lists(c, tree));
        c.Guard("annotations", () => Annotations(c, tree));
    }

    static string Clause(ValidationContext c, string a, string b) => c.Part == 1 ? $"ISO 14289-1 {a}" : $"ISO 14289-2 {b}";

    static void Document(ValidationContext c)
    {
        var f = c.F;
        var mi = f.Resolve(f.Catalog.Get("MarkInfo")) as PdfDictionary;
        if (mi is null || f.Resolve(mi.Get("Marked")) is not PdfBoolean { Value: true })
            c.Add("PDFUA-MARKINFO", ValidationSeverity.Error, "catalog /MarkInfo /Marked is not true (document is not declared as tagged)", Clause(c, "7.1", "8.2"));
        if (f.Resolve(f.Catalog.Get("StructTreeRoot")) is not PdfDictionary)
            c.Add("PDFUA-STRUCTTREE", ValidationSeverity.Error, "catalog has no /StructTreeRoot", Clause(c, "7.1", "8.2"));
        else if (c.Doc.Structure is null)
            c.Add("PDFUA-STRUCTTREE", ValidationSeverity.Error, "structure tree could not be parsed", Clause(c, "7.1", "8.2"));
        var lang = f.Resolve(f.Catalog.Get("Lang")) is PdfString l ? l.Text : null;
        if (string.IsNullOrWhiteSpace(lang))
            c.Add("PDFUA-LANG", ValidationSeverity.Error, "catalog /Lang is missing or empty", Clause(c, "7.2", "8.4.2"));
        else if (!LangTag().IsMatch(lang))
            c.Add("PDFUA-LANG-INVALID", ValidationSeverity.Error, $"catalog /Lang '{lang}' is not a well-formed language tag", Clause(c, "7.2", "8.4.2"));
        if (f.Resolve(f.Catalog.Get("ViewerPreferences")) is not PdfDictionary vp || f.Resolve(vp.Get("DisplayDocTitle")) is not PdfBoolean { Value: true })
            c.Add("PDFUA-DISPLAYDOCTITLE", ValidationSeverity.Error, "ViewerPreferences /DisplayDocTitle is not true", Clause(c, "7.1", "8.5"));
    }

    static void Metadata(ValidationContext c)
    {
        var x = c.Xmp;
        if (!x.Present) { c.Add("PDFUA-XMP-MISSING", ValidationSeverity.Error, "catalog has no /Metadata XMP stream", Clause(c, "7.1", "8.1")); return; }
        if (!x.WellFormed) { c.Add("PDFUA-XMP-INVALID", ValidationSeverity.Error, "XMP metadata is not well-formed XML", Clause(c, "7.1", "8.1")); return; }
        if (string.IsNullOrWhiteSpace(x.Get(XmpData.Dc, "title")))
            c.Add("PDFUA-TITLE", ValidationSeverity.Error, "XMP has no dc:title", Clause(c, "7.1", "8.5"));
        var part = x.Get(XmpData.PdfUAId, "part");
        if (part is null) c.Add("PDFUA-XMP-ID-MISSING", ValidationSeverity.Error, "XMP has no pdfuaid:part identification", Clause(c, "5", "5"));
        else if (part != c.Part.ToString()) c.Add("PDFUA-XMP-ID-PART", ValidationSeverity.Error, $"pdfuaid:part is '{part}', expected '{c.Part}'", Clause(c, "5", "5"));
    }

    static void Security(ValidationContext c)
    {
        var f = c.F;
        if (!f.IsEncrypted && f.Trailer.Get("Encrypt") is null) return;
        if (f.Resolve(f.Trailer.Get("Encrypt")) is PdfDictionary enc && f.Resolve(enc.Get("P")).AsNumber() is { } p && ((long)p & 0x200) == 0)
            c.Add("PDFUA-ENCRYPT-ACCESS", ValidationSeverity.Error, "encrypted document does not permit content extraction for accessibility (P bit 10)", Clause(c, "7.16", "8.9"));
    }

    static void Fonts(ValidationContext c)
    {
        c.EnsureWalked();
        foreach (var u in c.Fonts)
            if (!FontChecks.IsEmbedded(c.F, u.Font))
                c.Add("PDFUA-FONT-EMBEDDED", ValidationSeverity.Error, $"font {FontChecks.Name(c.F, u.Font)} is not embedded", Clause(c, "7.21.4.1", "8.8"), u.Page);
    }

    static void Pages(ValidationContext c)
    {
        var f = c.F;
        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            var p = c.Doc.Pages[i];
            if (p.Dict.Get("StructParents") is null && p.GetContents().Length > 0 && c.Doc.Structure is not null && f.Resolve(p.Dict.Get("Contents")) is not null)
                c.Add("PDFUA-PAGE-UNTAGGED", ValidationSeverity.Warning, "page has content but no /StructParents, so its content is probably not tagged", Clause(c, "7.1", "8.2"), i);
            if (f.Resolve(p.Dict.Get("Annots")) is PdfArray { Count: > 0 } && f.Resolve(p.Dict.Get("Tabs")).AsName() != "S")
                c.Add("PDFUA-TABS", ValidationSeverity.Error, "page with annotations must have /Tabs /S", Clause(c, "7.18.3", "8.9"), i);
        }
    }

    static void StructureTypes(ValidationContext c, StructureTree tree)
    {
        var reported = new HashSet<string>();
        foreach (var (k, v) in tree.RoleMap)
            if (StructureTree.IsStandardType(k) && k != v)
                c.Add("PDFUA-ROLEMAP-STANDARD", ValidationSeverity.Error, $"standard type /{k} is remapped to /{v}", Clause(c, "7.1", "8.2"));
        foreach (var e in tree.Elements())
            if (!StructureTree.IsStandardType(e.StandardType) && reported.Add(e.Type))
                c.Add("PDFUA-ROLEMAP-MISSING", ValidationSeverity.Error, $"structure type /{e.Type} does not map to a standard type", Clause(c, "7.1", "8.2"));
        if (c.Part == 2 && (tree.Children.Count != 1 || tree.Children[0].StandardType is not ("Document" or "DocumentFragment")))
            c.Add("PDFUA2-DOCUMENT-ROOT", ValidationSeverity.Warning, "structure tree root should have a single Document child element", "ISO 14289-2 8.2");
        if (tree.Truncated) c.Add("PDFUA-STRUCT-TRUNCATED", ValidationSeverity.Warning, "structure tree exceeded the parser's limits and was only partially checked");
    }

    static void Language(ValidationContext c, StructureTree tree)
    {
        foreach (var e in tree.Elements())
            if (e.Lang is { Length: > 0 } l && !LangTag().IsMatch(l))
                c.Add("PDFUA-LANG-INVALID", ValidationSeverity.Error, $"element /{e.Type} has malformed /Lang '{l}'", Clause(c, "7.2", "8.4.2"), e.PageIndex);
    }

    static void Figures(ValidationContext c, StructureTree tree)
    {
        foreach (var e in tree.Elements())
            if (e.StandardType is "Figure" or "Formula" && string.IsNullOrWhiteSpace(e.Alt) && string.IsNullOrWhiteSpace(e.ActualText) && !InsideArtifact(e))
                c.Add("PDFUA-ALT-MISSING", ValidationSeverity.Error, $"{e.StandardType} element has no /Alt text", Clause(c, "7.3", "8.6"), e.PageIndex);
    }

    static bool InsideArtifact(StructElement e)
    {
        for (var p = e.Parent; p is not null; p = p.Parent) if (p.StandardType == "Artifact") return true;
        return false;
    }

    static void Headings(ValidationContext c, StructureTree tree)
    {
        var hs = tree.Elements().Where(e => e.IsHeading).ToList();
        if (hs.Count == 0) return;
        var numbered = hs.Where(h => StructureTree.HeadingLevel(h.StandardType) > 0).ToList();
        if (hs.Count != numbered.Count && numbered.Count > 0)
            c.Add("PDFUA-HEADING-MIX", ValidationSeverity.Error, "document mixes unnumbered H and numbered Hn headings", Clause(c, "7.4.4", "8.2.5"));
        var sev = c.Part == 1 ? ValidationSeverity.Error : ValidationSeverity.Warning;
        var prev = 0;
        foreach (var h in numbered)
        {
            var level = StructureTree.HeadingLevel(h.StandardType)!.Value;
            if (level > prev + 1)
                c.Add("PDFUA-HEADING-NESTING", sev, prev == 0 ? $"first heading is H{level}, expected H1" : $"heading level jumps from H{prev} to H{level}", Clause(c, "7.4.2", "8.2.5"), h.PageIndex);
            prev = level;
        }
        if (c.Part == 2 && numbered.Count > 0)
            c.Add("PDFUA2-HEADING-HN", ValidationSeverity.Info, "numbered Hn headings are deprecated in PDF 2.0; prefer H nested in Sect elements", "ISO 32000-2 14.8.4.3");
    }

    static void Tables(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        foreach (var t in tree.Elements().Where(e => e.StandardType == "Table"))
        {
            var rows = new List<StructElement>();
            foreach (var ch in t.ChildElements)
            {
                if (ch.StandardType == "TR") rows.Add(ch);
                else if (ch.StandardType is "THead" or "TBody" or "TFoot") rows.AddRange(ch.ChildElements.Where(r => r.StandardType == "TR"));
                else if (ch.StandardType != "Caption")
                    c.Add("PDFUA-TABLE-CHILD", ValidationSeverity.Error, $"Table contains unexpected /{ch.Type}", Clause(c, "7.5", "8.2.5.26"), t.PageIndex);
            }
            if (rows.Count == 0) { c.Add("PDFUA-TABLE-ROWS", ValidationSeverity.Error, "Table has no TR rows", Clause(c, "7.5", "8.2.5.26"), t.PageIndex); continue; }
            var hasTh = false;
            var carry = new Dictionary<int, int>();
            var widths = new List<int>();
            foreach (var r in rows)
            {
                var col = 0;
                foreach (var cell in r.ChildElements)
                {
                    if (cell.StandardType is not ("TH" or "TD"))
                    { c.Add("PDFUA-ROW-CHILD", ValidationSeverity.Error, $"TR contains /{cell.Type}, expected TH or TD", Clause(c, "7.5", "8.2.5.26"), t.PageIndex); continue; }
                    hasTh |= cell.StandardType == "TH";
                    while (carry.TryGetValue(col, out var rem) && rem > 0) col++;
                    var cs = Math.Clamp((int)(cell.Attribute(f, "ColSpan").AsNumber() ?? 1), 1, 1000);
                    var rs = Math.Clamp((int)(cell.Attribute(f, "RowSpan").AsNumber() ?? 1), 1, 1000);
                    for (var k = 0; k < cs; k++) if (rs > 1) carry[col + k] = rs;
                    col += cs;
                }
                widths.Add(Math.Max(col, carry.Where(kv => kv.Value > 0).Select(kv => kv.Key + 1).DefaultIfEmpty(0).Max()));
                foreach (var key in carry.Keys.ToList()) if (--carry[key] <= 0) carry.Remove(key);
            }
            if (!hasTh) c.Add("PDFUA-TABLE-HEADERS", ValidationSeverity.Error, "Table has no TH header cells", Clause(c, "7.5", "8.2.5.26"), t.PageIndex);
            if (widths.Distinct().Count() > 1)
                c.Add("PDFUA-TABLE-IRREGULAR", ValidationSeverity.Warning, "table rows have differing column counts", Clause(c, "7.5", "8.2.5.26"), t.PageIndex);
        }
        foreach (var cell in tree.Elements().Where(e => e.StandardType is "TH" or "TD" or "TR"))
        {
            var expected = cell.StandardType == "TR" ? new[] { "Table", "THead", "TBody", "TFoot" } : ["TR"];
            if (cell.Parent is null || !expected.Contains(cell.Parent.StandardType))
                c.Add("PDFUA-TABLE-PARENT", ValidationSeverity.Error, $"{cell.StandardType} is not inside {string.Join("/", expected)}", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
        }
    }

    static void Lists(ValidationContext c, StructureTree tree)
    {
        foreach (var l in tree.Elements().Where(e => e.StandardType == "L"))
            foreach (var ch in l.ChildElements)
                if (ch.StandardType is not ("LI" or "Caption" or "L"))
                    c.Add("PDFUA-LIST-CHILD", ValidationSeverity.Error, $"L contains /{ch.Type}, expected LI", Clause(c, "7.6", "8.2.5.24"), l.PageIndex);
        foreach (var li in tree.Elements().Where(e => e.StandardType == "LI"))
        {
            if (li.Parent?.StandardType != "L") c.Add("PDFUA-LI-PARENT", ValidationSeverity.Error, "LI is not inside an L", Clause(c, "7.6", "8.2.5.24"), li.PageIndex);
            foreach (var ch in li.ChildElements)
                if (ch.StandardType is not ("Lbl" or "LBody"))
                    c.Add("PDFUA-LI-CHILD", ValidationSeverity.Error, $"LI contains /{ch.Type}, expected Lbl or LBody", Clause(c, "7.6", "8.2.5.24"), li.PageIndex);
        }
    }

    static void Annotations(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var a in annots.Items.Take(10_000))
            {
                if (f.Resolve(a) is not PdfDictionary ad) continue;
                var sub = f.Resolve(ad.Get("Subtype")).AsName();
                if (sub is "Popup" or "PrinterMark" or "TrapNet") continue;
                if (f.Resolve(ad.Get("StructParent")) is null)
                    c.Add("PDFUA-ANNOT-UNTAGGED", ValidationSeverity.Error, $"/{sub} annotation has no /StructParent (not tagged)", Clause(c, "7.18.1", "8.9"), i);
                if (sub == "Link" && !(f.Resolve(ad.Get("Contents")) is PdfString { Bytes.Length: > 0 }) && !LinkHasAlt(f, tree, ad))
                    c.Add("PDFUA-LINK-DESCRIPTION", ValidationSeverity.Error, "Link annotation has no /Contents and its structure element has no /Alt", Clause(c, "7.18.5", "8.9"), i);
            }
        }
    }

    static bool LinkHasAlt(PdfFile f, StructureTree tree, PdfDictionary annot)
    {
        if (f.Resolve(annot.Get("StructParent")).AsNumber() is not { } key || tree.ParentTreeLookup((int)key) is not PdfDictionary el) return false;
        return tree.ElementFor(el) is { Alt: { Length: > 0 } };
    }
}
