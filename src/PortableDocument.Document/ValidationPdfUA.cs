using System.Text.RegularExpressions;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>PDF/UA-1 (ISO 14289-1) and PDF/UA-2 (ISO 14289-2) basic checks.</summary>
internal static partial class PdfUAChecks
{
    // RFC 3066 shape, as ISO 14289 requires of ISO 32000 14.9.2 language identifiers (veraPDF PDFUA-1 7.2-29 accepts 1-8 letter primary subtags).
    [GeneratedRegex(@"\A[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})*\z")]
    internal static partial Regex LangTag();

    public static void Run(ValidationContext c)
    {
        c.Guard("document", () => Document(c));
        c.Guard("metadata", () => Metadata(c));
        c.Guard("language sources", () => LanguageSources(c));
        c.Guard("security", () => Security(c));
        c.Guard("fonts", () => Fonts(c));
        c.Guard("document flags", () => PdfUADeepChecks.DocumentFlags(c));
        c.Guard("font mapping", () => PdfUADeepChecks.Fonts(c));
        c.Guard("pages", () => Pages(c));
        if (c.Part == 1) c.Guard("media clip data", () => MediaClipData(c));
        var tree = c.Doc.Structure;
        if (tree is null) return;
        c.Guard("content tagging", () => PdfUADeepChecks.ContentTagging(c, tree));
        c.Guard("structure rules", () => PdfUADeepChecks.StructureRules(c, tree));
        c.Guard("structure types", () => StructureTypes(c, tree));
        if (c.Part == 2) PdfUA2Checks.Run(c, tree);
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
        {
            // UA-1 only requires that the language of text be determinable (checked per structure element); UA-2 requires the catalog entry.
            if (c.Part != 1 || c.Doc.Structure is null)
                c.Add("PDFUA-LANG", ValidationSeverity.Error, "catalog /Lang is missing or empty", Clause(c, "7.2", "8.4.4"));
        }
        if (lang is not null && !LangTag().IsMatch(lang))
            c.Add("PDFUA-LANG-INVALID", ValidationSeverity.Error, $"catalog /Lang '{lang}' is not a well-formed language tag", Clause(c, "7.2", "8.4.4"));
        if (lang is not null) PdfUA2Checks.LanguagePrivateUse(c, lang);
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
        foreach (var (name, prefix) in x.PdfUAPrefixes)
            if (name is "part" or "amd" or "corr" or "rev" && prefix != "pdfuaid")
                c.Add("PDFUA-XMP-ID-PREFIX", ValidationSeverity.Error, $"pdfuaid property '{name}' uses namespace prefix '{prefix}', expected 'pdfuaid'", Clause(c, "5", "5"));
        if (c.Part == 2 && x.Get(XmpData.PdfUAId, "rev") != "2024")
            c.Add("PDFUA2-XMP-ID-REV", ValidationSeverity.Error, "pdfuaid:rev must be 2024 for PDF/UA-2", "ISO 14289-2 5");
    }

    static void Security(ValidationContext c)
    {
        var f = c.F;
        if (!f.IsEncrypted && f.Trailer.Get("Encrypt") is null) return;
        if (f.Resolve(f.Trailer.Get("Encrypt")) is PdfDictionary enc)
        {
            var permissions = f.Resolve(enc.Get("P"));
            if (c.Part == 1 && permissions is (null or PdfNull))
                c.Add("PDFUA-ENCRYPT-ACCESS", ValidationSeverity.Error, "encrypted document has no /P accessibility permissions entry", "ISO 14289-1 7.16");
            else if (permissions.AsNumber() is { } p && ((long)p & 0x200) == 0)
                c.Add("PDFUA-ENCRYPT-ACCESS", ValidationSeverity.Error, "encrypted document does not permit content extraction for accessibility (P bit 10)", Clause(c, "7.16", "8.9"));
        }
    }

    static void Fonts(ValidationContext c)
    {
        c.EnsureWalked();
        var usageReported = false;
        foreach (var u in c.Fonts)
            if (!FontChecks.IsEmbedded(c.F, u.Font))
            {
                if (u.RenderingPage is { } page)
                    c.Add("PDFUA-FONT-EMBEDDED", ValidationSeverity.Error, $"font {FontChecks.Name(c.F, u.Font)} used for rendering is not embedded", Clause(c, "7.21.4.1", "8.4.5.5.1"), page);
                else if (!c.FontUsageComplete && !usageReported)
                {
                    usageReported = true;
                    c.Add("PDFUA-FONT-USAGE-INCOMPLETE", ValidationSeverity.Error,
                        "Font use could not be completely inspected; the unused or mode-3-only exemption for unembedded fonts cannot be established",
                        Clause(c, "7.21.4.1", "8.4.5.5.1"));
                }
            }
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

    static void MediaClipData(ValidationContext c)
    {
        const string clause = "ISO 14289-1 7.18.6.2";
        const int maxNodes = 4096, maxDepth = 64, maxAltEntries = 8192;
        var f = c.F;
        var actions = c.Doc.GetActionReport();
        var active = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var completedRenditions = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var completedClips = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var work = 0;
        var incomplete = false;
        if (actions.Truncated) Incomplete();
        foreach (var action in actions.Actions)
        {
            if (action.ActionType == "Rendition") Walk(action.Dictionary.Get("R"), false, 0, action.PageIndex);
            if (work > maxNodes) break;
        }

        void Walk(PdfObject? raw, bool clip, int depth, int? page)
        {
            if (raw is null) return;
            if (++work > maxNodes || depth > maxDepth) { Incomplete(); return; }
            if (Resolve(raw) is not PdfDictionary dictionary) return;
            var completed = clip ? completedClips : completedRenditions;
            if (completed.Contains(dictionary)) return;
            if (!active.Add(dictionary)) { Incomplete(); return; }
            var subtype = Resolve(dictionary.Get("S")).AsName();
            if (clip)
            {
                if (subtype == "MCD")
                {
                    if (Resolve(dictionary.Get("CT")) is not PdfString)
                        c.Add("PDFUA-MEDIA-CT", ValidationSeverity.Error, "media clip data has no string /CT content type", clause, page);
                    var alt = Resolve(dictionary.Get("Alt"));
                    if (alt is PdfArray large && large.Count > maxAltEntries) Incomplete();
                    else if (!ValidAlt(alt))
                        c.Add("PDFUA-MEDIA-ALT", ValidationSeverity.Error, "media clip data /Alt must be an even-length multilingual string array with nonempty descriptions", clause, page);
                }
                else if (subtype == "MCS") Walk(dictionary.Get("D"), true, depth + 1, page);
            }
            else if (subtype == "MR") Walk(dictionary.Get("C"), true, depth + 1, page);
            else if (subtype == "SR" && Resolve(dictionary.Get("R")) is PdfArray alternatives)
                foreach (var alternative in alternatives.Items)
                {
                    Walk(alternative, false, depth + 1, page);
                    if (work > maxNodes) break;
                }
            active.Remove(dictionary);
            completed.Add(dictionary);
        }

        bool ValidAlt(PdfObject? value)
        {
            if (value is not PdfArray array || array.Count % 2 != 0) return false;
            for (var i = 0; i < array.Count; i++)
                if (Resolve(array[i]) is not PdfString text || (i % 2 == 1 && text.Text.Length == 0)) return false;
            return true;
        }

        PdfObject? Resolve(PdfObject? value)
        {
            var references = new HashSet<int>();
            while (value is PdfRef reference)
            {
                if (references.Count >= 32 || !references.Add(reference.Number)) { Incomplete(); return null; }
                value = f.GetObject(reference.Number);
            }
            return value;
        }

        void Incomplete()
        {
            if (incomplete) return;
            incomplete = true;
            c.Add("PDFUA-MEDIA-INCOMPLETE", ValidationSeverity.Error,
                "media clip data validation could not complete because action traversal or a rendition, clip, reference or alternate-text boundary was reached", clause);
        }
    }

    static void LanguageSources(ValidationContext c)
    {
        var f = c.F;
        if (c.Part != 1 || f.Resolve(f.Catalog.Get("Lang")) is PdfString { Bytes.Length: > 0 }) return;
        const string clause = "ISO 14289-1 7.2";
        if (f.Resolve(f.Catalog.Get("Outlines")) is PdfDictionary outlines && outlines.Get("First") is not null)
            c.Add("PDFUA-LANG-OUTLINE", ValidationSeverity.Error, "outline entries exist but the document has no catalog /Lang, so their natural language is not determinable (veraPDF PDFUA-1 7.2-2)", clause);
        if (c.Xmp.Present && c.Xmp.Items.Values.Any(v => v.Any(i => i.Lang == "x-default") && v.All(i => i.Lang is "x-default" or null)))
            c.Add("PDFUA-LANG-XMP", ValidationSeverity.Error, "XMP language alternative has only x-default and the document has no catalog /Lang, so its language is not determinable (veraPDF PDFUA-1 7.2-33)", clause);
        bool contents = false, tooltip = false;
        var tree = c.Doc.Structure;
        string? ParentLang(PdfDictionary ad) =>
            tree is not null && c.F.Resolve(ad.Get("StructParent")) is PdfInteger { Value: >= 0 and <= int.MaxValue } k
            && AnnotationParentTreeValue(c.F, tree, (int)k.Value) is { } pv && c.F.Resolve(pv) is PdfDictionary pd && tree.ElementFor(pd) is { } pe ? pe.EffectiveLang : null;
        for (var i = 0; i < c.Doc.Pages.Count && !(contents && tooltip); i++)
        {
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var a in annots.Items.Take(10_000))
            {
                if (f.Resolve(a) is not PdfDictionary ad) continue;
                if (!contents && f.Resolve(ad.Get("Contents")) is PdfString { Bytes.Length: > 0 } && ad.Get("Lang") is null && string.IsNullOrEmpty(ParentLang(ad)))
                { contents = true; c.Add("PDFUA-LANG-ANNOT", ValidationSeverity.Error, "an annotation /Contents has no determinable natural language without a catalog /Lang (veraPDF PDFUA-1 7.2-24)", clause, i); }
                if (!tooltip && HasText(f, ad, "TU") && !HasLang(f, ad) && string.IsNullOrEmpty(ParentLang(ad)))
                { tooltip = true; c.Add("PDFUA-LANG-TU", ValidationSeverity.Error, "a form field /TU has no determinable natural language without a catalog /Lang (veraPDF PDFUA-1 7.2-25)", clause, i); }
            }
        }
    }

    static bool HasLang(PdfFile f, PdfDictionary? d)
    {
        for (var n = 0; d is not null && n < 64; n++, d = f.Resolve(d.Get("Parent")) as PdfDictionary)
            if (d.Get("Lang") is not null) return true;
        return false;
    }

    static void StructureTypes(ValidationContext c, StructureTree tree)
    {
        var reported = new HashSet<string>();
        if (c.Part == 1)
        {
            // ISO 14289-1 7.1: only types actually used by structure elements matter, judged against the ISO 32000-1 vocabulary (veraPDF 7.1-5/6/7).
            foreach (var e in tree.Elements())
            {
                if (!reported.Add(e.Type)) continue;
                if (StructureTree.IsPdf17StandardType(e.Type))
                {
                    if (tree.RoleMap.TryGetValue(e.Type, out var v) && v != e.Type)
                        c.Add("PDFUA-ROLEMAP-STANDARD", ValidationSeverity.Error, $"standard type /{e.Type} is remapped to /{v}", "ISO 14289-1 7.1");
                    else if (v == e.Type)
                        c.Add("PDFUA-ROLEMAP-CIRCULAR", ValidationSeverity.Error, $"standard type /{e.Type} is mapped to itself in the RoleMap (circular mapping)", "ISO 14289-1 7.1");
                }
                else if (tree.ResolvePdf17Role(e.Type, out var circular) is null)
                    c.Add(circular ? "PDFUA-ROLEMAP-CIRCULAR" : "PDFUA-ROLEMAP-MISSING", ValidationSeverity.Error, circular ? $"structure type /{e.Type} has a circular RoleMap mapping" : $"structure type /{e.Type} does not map to a standard type", "ISO 14289-1 7.1");
            }
        }
        // UA-2 role mapping is evaluated per namespace by PdfUA2Checks.
        if (tree.Truncated) c.Add("PDFUA-STRUCT-TRUNCATED", ValidationSeverity.Warning, "structure tree exceeded the parser's limits and was only partially checked");
    }

    static void Language(ValidationContext c, StructureTree tree)
    {
        var catalogLang = c.F.Resolve(c.F.Catalog.Get("Lang")) is PdfString { Bytes.Length: > 0 };
        var reported = catalogLang || c.Part != 1;
        foreach (var e in tree.Elements())
        {
            if (e.Lang is { } l && !LangTag().IsMatch(l))
                c.Add("PDFUA-LANG-INVALID", ValidationSeverity.Error, $"element /{e.Type} has malformed /Lang '{l}'", Clause(c, "7.2", "8.4.4"), e.PageIndex);
            if (e.Lang is { } language) PdfUA2Checks.LanguagePrivateUse(c, language, e.PageIndex);
            if (!reported && string.IsNullOrEmpty(e.EffectiveLang) && e.MarkedContent.Any(m => c.TextMcids.Contains((m.PageIndex ?? e.PageIndex ?? -1, m.Mcid))))
            {
                reported = true;
                c.Add("PDFUA-LANG", ValidationSeverity.Error, $"no catalog /Lang and /{e.Type} content has no inherited /Lang, so its natural language is not determinable (veraPDF PDFUA-1 7.2-34 considers text items)", Clause(c, "7.2", "8.4.4"), e.PageIndex);
            }
        }
    }

    static void Figures(ValidationContext c, StructureTree tree)
    {
        foreach (var e in tree.Elements())
        {
            if (e.StandardType is not ("Figure" or "Formula") || InsideArtifact(e)) continue;
            // UA-1 (7.3-1, 7.7-1): Alt non-empty or ActualText present. UA-2 (8.2.5.28.2-1): Alt or ActualText present; a Formula may use MathML (Math child) instead.
            var described = c.Part == 1 ? !string.IsNullOrEmpty(e.Alt) || e.ActualText is not null : e.Alt is not null || e.ActualText is not null;
            if (c.Part == 2 && e.StandardType == "Formula" && e.ChildElements.Any(k => k.StandardType == "Math" || k.Type == "Math")) described = true;
            if (!described)
                c.Add("PDFUA-ALT-MISSING", ValidationSeverity.Error, $"{e.StandardType} element has no /Alt text", Clause(c, "7.3", "8.6"), e.PageIndex);
        }
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

    static string? TableType(ValidationContext c, StructureTree tree, StructElement element) =>
        c.Part == 2 ? PdfUA2Checks.StandardType(c, tree, element) : element.StandardType;

    static void Tables(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        var idCounts = tree.Elements()
            .Where(e => !string.IsNullOrEmpty(e.Id))
            .GroupBy(e => e.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var headerTargets = tree.Elements()
            .Where(e => TableType(c, tree, e) == "TH" && !string.IsNullOrEmpty(e.Id))
            .GroupBy(e => e.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        foreach (var t in tree.Elements().Where(e => TableType(c, tree, e) == "Table"))
        {
            var rows = new List<StructElement>();
            foreach (var ch in t.ChildElements)
            {
                var type = TableType(c, tree, ch);
                if (type == "TR") rows.Add(ch);
                else if (type is "THead" or "TBody" or "TFoot") rows.AddRange(ch.ChildElements.Where(r => TableType(c, tree, r) == "TR"));
                else if (type != "Caption" && (c.Part != 2 || type is not null))
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
                    var type = TableType(c, tree, cell);
                    if (type is not ("TH" or "TD"))
                    { c.Add("PDFUA-ROW-CHILD", ValidationSeverity.Error, $"TR contains /{cell.Type}, expected TH or TD", Clause(c, "7.5", "8.2.5.26"), t.PageIndex); continue; }
                    hasTh |= type == "TH";
                    while (carry.TryGetValue(col, out var rem) && rem > 0) col++;
                    var cs = Math.Clamp((int)((c.Part == 2 ? TableAttribute(c, tree, cell, "ColSpan") : cell.Attribute(f, "ColSpan")).AsNumber() ?? 1), 1, 1000);
                    var rs = Math.Clamp((int)((c.Part == 2 ? TableAttribute(c, tree, cell, "RowSpan") : cell.Attribute(f, "RowSpan")).AsNumber() ?? 1), 1, 1000);
                    for (var k = 0; k < cs; k++) if (rs > 1) carry[col + k] = rs;
                    col += cs;
                }
                widths.Add(Math.Max(col, carry.Where(kv => kv.Value > 0).Select(kv => kv.Key + 1).DefaultIfEmpty(0).Max()));
                foreach (var key in carry.Keys.ToList()) if (--carry[key] <= 0) carry.Remove(key);
            }
            if (!hasTh) c.Add("PDFUA-TABLE-HEADERS", ValidationSeverity.Error, "Table has no TH header cells", Clause(c, "7.5", "8.2.5.26"), t.PageIndex);
            if (c.Part == 2 && carry.Values.Any(remaining => remaining > 0))
                c.Add("PDFUA2-TABLE-ROWSPAN-END", ValidationSeverity.Error,
                    "table cell RowSpan extends beyond the final table row",
                    "ISO 14289-2 8.2.5.26", t.PageIndex);
            if (widths.Distinct().Count() > 1)
                c.Add("PDFUA-TABLE-IRREGULAR", ValidationSeverity.Error, "table rows have differing column counts (veraPDF PDFUA-1 7.2-42/43, PDFUA-2 8.2.5.26-3/4)", Clause(c, "7.2", "8.2.5.26"), t.PageIndex);
            TableHeaderAssociations(c, tree, rows, idCounts, headerTargets);
        }
        foreach (var cell in tree.Elements().Where(e => TableType(c, tree, e) is "TH" or "TD" or "TR"))
        {
            var type = TableType(c, tree, cell);
            var expected = type == "TR" ? new[] { "Table", "THead", "TBody", "TFoot" } : ["TR"];
            var parentType = cell.Parent is null ? null : TableType(c, tree, cell.Parent);
            if ((cell.Parent is null || parentType is not null || c.Part != 2) && !expected.Contains(parentType))
                c.Add("PDFUA-TABLE-PARENT", ValidationSeverity.Error, $"{type} is not inside {string.Join("/", expected)}", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
        }
    }

    static PdfObject? TableAttribute(ValidationContext c, StructureTree tree, StructElement element, string key)
    {
        if (c.Part == 2)
            return PdfUA2Checks.OwnedAttribute(c, tree, element, "Table", key, () =>
                c.Add("PDFUA2-TABLE-ATTRIBUTE-INCOMPLETE", ValidationSeverity.Error,
                    $"Table /{key} attribute inspection is malformed or exceeds its reference/work bound",
                    "ISO 14289-2 8.2.5.26", element.PageIndex));
        var f = c.F;

        PdfObject? Find(PdfObject? raw)
        {
            var value = f.Resolve(raw);
            if (value is PdfDictionary dict)
                return f.Resolve(dict.Get("O")).AsName() == "Table" ? f.Resolve(dict.Get(key)) : null;
            if (value is PdfArray array)
                foreach (var item in array.Items)
                    if (Find(item) is { } attribute) return attribute;
            return null;
        }

        if (Find(element.Dict.Get("A")) is { } direct) return direct;
        if (f.Resolve(tree.Root.Get("ClassMap")) is not PdfDictionary classMap) return null;

        var classes = f.Resolve(element.Dict.Get("C")) switch
        {
            PdfName name => new[] { name.Value },
            PdfArray array => array.Items.Select(item => f.Resolve(item).AsName()).OfType<string>(),
            _ => [],
        };
        foreach (var name in classes)
            if (Find(classMap.Get(name)) is { } attribute) return attribute;
        return null;
    }

    static void TableHeaderAssociations(ValidationContext c, StructureTree tree, List<StructElement> rows, Dictionary<string, int> idCounts, Dictionary<string, StructElement[]> headerTargets)
    {
        var f = c.F;
        var cells = rows.SelectMany(row => row.ChildElements)
            .Where(cell => TableType(c, tree, cell) is "TH" or "TD")
            .ToArray();
        var headerAttributes = new Dictionary<StructElement, PdfObject?>();

        foreach (var cell in cells)
        {
            var type = TableType(c, tree, cell);
            var scope = TableAttribute(c, tree, cell, "Scope");
            if (scope is not null)
            {
                if (type != "TH" || f.Resolve(scope).AsName() is not ("Row" or "Column" or "Both"))
                    c.Add("PDFUA-TABLE-SCOPE", ValidationSeverity.Error, $"/Scope on {type} must be a name with value /Row, /Column, or /Both", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
            }
            else if (c.Part == 1 && type == "TH")
                c.Add("PDFUA-TABLE-SCOPE-RECOMMENDED", ValidationSeverity.Info, "TH should have a /Scope attribute; whether the table's header relationships can be determined without it requires semantic review", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);

            var cellHeaders = TableAttribute(c, tree, cell, "Headers");
            headerAttributes[cell] = cellHeaders;
            if (cell.Id is { Length: > 0 } id && type == "TH" && idCounts.GetValueOrDefault(id) > 1)
                c.Add("PDFUA-TABLE-HEADER-ID-DUPLICATE", ValidationSeverity.Error, $"TH /ID '{id}' is not unique in the structure tree", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
            if (cellHeaders is null) continue;
            // veraPDF (PDFUA-1 7.5-1/2) only evaluates /Headers on TD cells; dangling or empty references on TH cells are reported as warnings.
            var headerSeverity = type == "TD" ? ValidationSeverity.Error : ValidationSeverity.Warning;

            if (f.Resolve(cellHeaders) is not PdfArray idArray)
            {
                c.Add("PDFUA-TABLE-HEADERS-FORMAT", headerSeverity, "/Headers must be an array of header-cell ID strings", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
                continue;
            }

            foreach (var rawId in idArray.Items)
            {
                if (f.Resolve(rawId) is not PdfString headerId || string.IsNullOrWhiteSpace(headerId.Text))
                {
                    c.Add("PDFUA-TABLE-HEADERS-FORMAT", headerSeverity, "/Headers entries must be non-empty text strings", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
                    continue;
                }

                if (!headerTargets.TryGetValue(headerId.Text, out var targets) || targets.Length != 1 || idCounts.GetValueOrDefault(headerId.Text) != 1)
                    c.Add("PDFUA-TABLE-HEADERS-REFERENCE", headerSeverity, $"/Headers references '{headerId.Text}', which does not identify a unique TH structure element", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
            }
        }

        if (c.Part == 1)
            TableScopeConnections(c, tree, rows, headerAttributes);
        if (c.Part == 2 && headerAttributes.Values.Any(value => value is not null))
            foreach (var cell in cells.Where(cell => TableType(c, tree, cell) == "TD" && headerAttributes[cell] is null))
                c.Add("PDFUA-TABLE-HEADERS-CONSISTENCY", ValidationSeverity.Error, "TD has no /Headers attribute although /Headers is used in this table", Clause(c, "7.5", "8.2.5.26"), cell.PageIndex);
    }

    /// <summary>ISO 14289-1 7.5 / veraPDF 7.5-1: a TD whose headers are not given by /Headers needs a TH with a /Scope sharing its row or column (the scope direction is not evaluated).</summary>
    static void TableScopeConnections(ValidationContext c, StructureTree tree, List<StructElement> rows, Dictionary<StructElement, PdfObject?> headerAttributes)
    {
        var f = c.F;
        var cells = rows.SelectMany(row => row.ChildElements).ToList();
        var dataCells = cells.Where(cell => cell.StandardType == "TD").ToList();
        if (dataCells.Count > 0 && dataCells.All(cell => headerAttributes.GetValueOrDefault(cell) is null)
            && cells.FirstOrDefault(cell => cell.StandardType == "TH"
                && f.Resolve(TableAttribute(c, tree, cell, "Scope")).AsName() is not ("Row" or "Column" or "Both")) is { } missingScope)
        {
            c.Add("PDFUA-TABLE-HEADER-CONNECTION", ValidationSeverity.Error,
                "no TD supplies /Headers, so every TH requires a valid /Scope to describe header associations",
                "ISO 14289-1 7.5", missingScope.PageIndex);
            return;
        }
        var carry = new Dictionary<int, int>();
        var placed = new List<(StructElement Cell, int Row, int Col, int ColSpan, int RowSpan)>();
        for (var r = 0; r < rows.Count; r++)
        {
            var col = 0;
            foreach (var cell in rows[r].ChildElements.Where(e => e.StandardType is "TH" or "TD"))
            {
                while (carry.TryGetValue(col, out var rem) && rem > 0) col++;
                var cs = Math.Clamp((int)(cell.Attribute(f, "ColSpan").AsNumber() ?? 1), 1, 1000);
                var rs = Math.Clamp((int)(cell.Attribute(f, "RowSpan").AsNumber() ?? 1), 1, 1000);
                placed.Add((cell, r, col, cs, rs));
                for (var k = 0; k < cs; k++) if (rs > 1) carry[col + k] = rs;
                col += cs;
            }
            foreach (var key in carry.Keys.ToList()) if (--carry[key] <= 0) carry.Remove(key);
        }

        var headers = placed.Where(p => p.Cell.StandardType == "TH")
            .Select(p => (p, Scope: f.Resolve(TableAttribute(c, tree, p.Cell, "Scope")).AsName()))
            .Where(h => h.Scope is "Row" or "Column" or "Both")
            .ToList();
        var sharing = placed.Where(p => p.Cell.StandardType == "TH").ToList();
        var anyScope = headers.Count > 0;
        foreach (var p in placed.Where(p => p.Cell.StandardType == "TD"))
        {
            if (headerAttributes.GetValueOrDefault(p.Cell) is not null) continue;
            var related = sharing.Where(h => (h.Col < p.Col + p.ColSpan && p.Col < h.Col + h.ColSpan) || (h.Row < p.Row + p.RowSpan && p.Row < h.Row + h.RowSpan)).ToList();
            var allScoped = related.Count > 0 && related.All(h => headers.Any(s => ReferenceEquals(s.p.Cell, h.Cell)));
            if (!anyScope)
            {
                c.Add("PDFUA-TABLE-HEADER-CONNECTION", ValidationSeverity.Error, "no TD has /Headers and no TH has a valid /Scope, so the table's header cells are not determinable (ISO 14289-1 7.5; veraPDF PDFUA-1 7.5-1)", "ISO 14289-1 7.5", p.Cell.PageIndex);
                break;
            }
            else if (!allScoped)
                c.Add("PDFUA-TABLE-HEADER-SCOPE-PARTIAL", ValidationSeverity.Warning, "a TD shares a row or column with a TH that has no valid /Scope (veraPDF rejects this in its corpus; the exact veraPDF semantics, including Scope direction, are not reproduced, so this is advisory)", "ISO 14289-1 7.5", p.Cell.PageIndex);
        }
    }

    static void Lists(ValidationContext c, StructureTree tree)
    {
        string? Type(StructElement? element) => element is null ? null
            : c.Part == 2 ? PdfUA2Checks.StandardType(c, tree, element) : element.StandardType;
        foreach (var l in tree.Elements().Where(e => Type(e) == "L"))
            foreach (var ch in l.ChildElements)
            {
                var childType = Type(ch);
                if (c.Part == 2 && childType is null) continue;
                if (childType is not ("LI" or "Caption" or "L"))
                    c.Add("PDFUA-LIST-CHILD", ValidationSeverity.Error, $"L contains /{ch.Type}, expected LI", Clause(c, "7.6", "8.2.5.24"), l.PageIndex);
            }
        foreach (var li in tree.Elements().Where(e => Type(e) == "LI"))
        {
            var parentType = Type(li.Parent);
            if (parentType != "L" && (c.Part != 2 || li.Parent is null || parentType is not null))
                c.Add("PDFUA-LI-PARENT", ValidationSeverity.Error, "LI is not inside an L", Clause(c, "7.6", "8.2.5.24"), li.PageIndex);
            foreach (var ch in li.ChildElements)
            {
                var childType = Type(ch);
                if (c.Part == 2 && childType is null) continue;
                if (childType is not ("Lbl" or "LBody"))
                    c.Add("PDFUA-LI-CHILD", ValidationSeverity.Error, $"LI contains /{ch.Type}, expected Lbl or LBody", Clause(c, "7.6", "8.2.5.24"), li.PageIndex);
            }
        }
    }

    static void Annotations(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        var objectRefs = new Dictionary<StructElement, HashSet<PdfDictionary>>();
        var widgetFields = new Dictionary<PdfDictionary, PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (c.Part == 1)
        {
            var fields = c.Doc.GetFormFieldDetails();
            var incomplete = fields.Truncated;
            foreach (var field in fields.Fields)
                foreach (var widget in field.Widgets)
                    if (!widgetFields.TryAdd(widget, field.Dictionary) && !ReferenceEquals(widgetFields[widget], field.Dictionary))
                        incomplete = true;
            if (incomplete)
                c.Add("PDFUA-FORM-INCOMPLETE", ValidationSeverity.Error, "terminal field/widget description ownership is truncated or ambiguous", "ISO 14289-1 7.18.1");
        }
        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var a in annots.Items.Take(10_000))
            {
                if (f.Resolve(a) is not PdfDictionary ad) continue;
                var sub = f.Resolve(ad.Get("Subtype")).AsName();
                var tagIncomplete = false;
                var key = PdfUA2Checks.ResolveBounded(f, ad.Get("StructParent"), () => tagIncomplete = true);
                if (key is PdfNull) key = null;
                if (tagIncomplete)
                    c.Add("PDFUA-ANNOT-TAG-INCOMPLETE", ValidationSeverity.Error,
                        $"/{sub} annotation /StructParent resolution is cyclic or exceeds the reference bound",
                        "ISO 32000-2 14.7.5.4", i);
                var parent = key is PdfInteger { Value: >= 0 and <= int.MaxValue } number
                    && AnnotationParentTreeValue(f, tree, (int)number.Value) is PdfRef reference
                    && f.Xref.TryGetValue(reference.Number, out var entry) && entry.InUse
                    && reference.Generation == (entry.Compressed ? 0 : entry.GenOrIndex)
                    && f.Resolve(reference) is PdfDictionary dict ? tree.ElementFor(dict) : null;
                var inArtifact = c.Part == 2 && parent is not null && PdfUA2Checks.InsideArtifact(c, tree, parent);
                var exempt = (((int?)f.Resolve(ad.Get("F")).AsNumber() ?? 0) & 2) != 0 || OutsideCropBox(f, ad, c.Doc.Pages[i].CropBox);
                if (sub is "Popup")
                {
                    if (c.Part == 2 && key is not null)
                        c.Add("PDFUA2-ANNOT-POPUP", ValidationSeverity.Error, "Popup annotations must not be present in the structure tree (veraPDF PDFUA-2 8.9.2.4.9-1)", "ISO 14289-2 8.9.2.4.9", i);
                    continue;
                }
                if (c.Part == 2 && sub is "Sound" or "Movie")
                    c.Add("PDFUA2-ANNOT-DEPRECATED", ValidationSeverity.Error, $"/{sub} annotations cannot be used with PDF/UA-2 (veraPDF PDFUA-2 8.9.2.4.11)", "ISO 14289-2 8.9.2.4.11", i);
                if (c.Part == 2 && !exempt && key is not null && !inArtifact && ((int?)f.Resolve(ad.Get("F")).AsNumber() ?? 0) is var flags2 && ((flags2 & 1) != 0 || (flags2 & 32) != 0 && (flags2 & 256) == 0))
                    c.Add("PDFUA2-ANNOT-ARTIFACT", ValidationSeverity.Error, $"/{sub} annotation has the Invisible or NoView flag but is in the structure tree; it must be an artifact (veraPDF PDFUA-2 8.9.2.2)", "ISO 14289-2 8.9.2.2", i);
                if (sub is "TrapNet" or "PrinterMark")
                {
                    if (sub == "TrapNet" ? c.Part == 2 || !exempt : key is not null && !inArtifact && (c.Part == 2 || !exempt))
                        c.Add(sub == "TrapNet" ? "PDFUA-ANNOT-TRAPNET" : "PDFUA-ANNOT-PRINTERMARK", ValidationSeverity.Error, sub == "TrapNet" ? "TrapNet annotations are not permitted (veraPDF PDFUA-1 7.18.2-1, PDFUA-2 8.9.2.4.15-1)" : "PrinterMark annotation is in the structure tree but must be an artifact (veraPDF PDFUA-1 7.18.8-1, PDFUA-2 8.9.2.4.14-1)", Clause(c, "7.18", "8.9"), i);
                    if (sub == "TrapNet" || !inArtifact) continue;
                }
                if (!tagIncomplete && key is null)
                {
                    if (!exempt) c.Add("PDFUA-ANNOT-UNTAGGED", ValidationSeverity.Error, $"/{sub} annotation has no /StructParent (not tagged)", Clause(c, "7.18.1", "8.9"), i);
                }
                else if (!tagIncomplete && parent is null)
                    c.Add("PDFUA-ANNOT-PARENTTREE", ValidationSeverity.Error, $"/{sub} annotation /StructParent must be a non-negative integer key mapping to a parsed structure element in /ParentTree", "ISO 32000-2 14.7.5.4", i);
                else if (parent is not null)
                {
                    if (!objectRefs.TryGetValue(parent, out var objects))
                        objectRefs[parent] = objects = AnnotationObjectReferences(f, parent);
                    if (!objects.Contains(ad))
                        c.Add("PDFUA-ANNOT-OBJR", ValidationSeverity.Error, $"/{sub} annotation's parent structure element has no direct /OBJR child with an indirect /Obj identifying this annotation", "ISO 32000-2 14.7.5.3", i);
                }
                if (sub == "PrinterMark") continue;
                if (c.Part == 2 && sub == "Widget" && parent is not null)
                    PdfUA2Checks.WidgetAdditionalActions(c, tree, ad, parent, i);
                if (exempt) continue;
                if (sub == "Link")
                {
                    // UA-1 7.18.5-2 accepts only /Contents; the structure /Alt fallback is kept for UA-2.
                    var hasContents = f.Resolve(ad.Get("Contents")) is PdfString { Bytes.Length: > 0 };
                    if (!hasContents && (c.Part == 1 || parent?.Alt is not { Length: > 0 }))
                        c.Add("PDFUA-LINK-DESCRIPTION", c.Part == 1 ? ValidationSeverity.Error : ValidationSeverity.Warning, c.Part == 1 ? "Link annotation has no /Contents" : "Link annotation has no /Contents and its structure element has no /Alt (advisory: veraPDF's PDFUA-2 profile has no counterpart)", Clause(c, "7.18.5", "8.9"), i);
                    var parentType = parent is null ? null : c.Part == 2 ? PdfUA2Checks.StandardType(c, tree, parent) : parent.StandardType;
                    if (parent is not null && parentType != "Link" && !(c.Part == 2 && parentType == "Reference"))
                        c.Add("PDFUA-LINK-STRUCT", ValidationSeverity.Error, $"Link annotation is nested in /{parent.Type}, expected {(c.Part == 2 ? "Link or Reference" : "Link")}", Clause(c, "7.18.5", "8.2.5.20"), i);
                }
                else if (sub == "Widget")
                {
                    var parentType = parent is null ? null : c.Part == 2 ? PdfUA2Checks.StandardType(c, tree, parent) : parent.StandardType;
                    if (parent is not null && parentType != "Form"
                        && !(c.Part == 2 && PdfUA2Checks.InsideArtifact(c, tree, parent)))
                        c.Add("PDFUA-FORM-STRUCT", ValidationSeverity.Error, $"Widget annotation is nested in /{parent.Type}, expected Form", Clause(c, "7.18.4", "8.10.1"), i);
                    var hasTooltip = c.Part == 1 && widgetFields.TryGetValue(ad, out var field)
                        ? f.Resolve(field.Get("TU")) is PdfString { Bytes.Length: > 0 }
                        : HasText(f, ad, "TU");
                    if (HasFieldType(f, ad) && !hasTooltip && (c.Part == 1 || !HasText(f, ad, "Contents")) && parent?.Alt is not { Length: > 0 })
                        c.Add("PDFUA-FORM-TU", ValidationSeverity.Error, "form field has no /TU tooltip (nor structure /Alt)", Clause(c, "7.18.1", "8.9"), i);
                }
                else
                {
                    if (c.Part == 1 && parent is not null && parent.StandardType != "Annot")
                        c.Add("PDFUA-ANNOT-STRUCT", ValidationSeverity.Error, $"/{sub} annotation is nested in /{parent.Type}, expected Annot", "ISO 14289-1 7.18.1", i);
                    if (!HasText(f, ad, "Contents") && parent?.Alt is not { Length: > 0 })
                        c.Add("PDFUA-ANNOT-CONTENTS", ValidationSeverity.Error, $"/{sub} annotation has no /Contents and its structure element has no /Alt", Clause(c, "7.18.1", "8.9"), i);
                }
            }
        }
    }

    static bool OutsideCropBox(PdfFile f, PdfDictionary annot, PdfRect crop)
    {
        if (f.Resolve(annot.Get("Rect")) is not PdfArray { Count: 4 } r) return false;
        var v = new double[4];
        for (var k = 0; k < 4; k++) if (f.Resolve(r[k]).AsNumber() is { } n) v[k] = n; else return false;
        return Math.Max(v[0], v[2]) < crop.Left || Math.Min(v[0], v[2]) > crop.Right || Math.Max(v[1], v[3]) < crop.Bottom || Math.Min(v[1], v[3]) > crop.Top;
    }

    static bool HasFieldType(PdfFile f, PdfDictionary? d)
    {
        for (var n = 0; d is not null && n < 64; n++, d = f.Resolve(d.Get("Parent")) as PdfDictionary)
            if (f.Resolve(d.Get("FT")) is not (null or PdfNull)) return true;
        return false;
    }

    static bool HasText(PdfFile f, PdfDictionary? d, string key)
    {
        for (var n = 0; d is not null && n < 64; n++, d = f.Resolve(d.Get("Parent")) as PdfDictionary)
            if (f.Resolve(d.Get(key)) is PdfString { Bytes.Length: > 0 }) return true;
        return false;
    }

    static PdfObject? AnnotationParentTreeValue(PdfFile f, StructureTree tree, int key)
    {
        if (f.Resolve(tree.Root.Get("ParentTree")) is not PdfDictionary root) return null;
        // Match ParentTreeLookup's traversal bounds, but retain the raw value required by Table 354.
        var pending = new Stack<(PdfDictionary Node, int Depth)>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        pending.Push((root, 0));
        var visited = 0;
        while (pending.TryPop(out var entry) && visited++ < StructureTree.MaxNodes)
        {
            var (node, depth) = entry;
            if (depth >= 32 || !seen.Add(node)) continue;
            if (f.Resolve(node.Get("Nums")) is PdfArray nums)
            {
                for (var i = 0; i + 1 < nums.Count; i += 2)
                    if (f.Resolve(nums[i]) is PdfInteger { Value: >= 0 and <= int.MaxValue } number
                        && number.Value == key) return nums[i + 1];
                continue;
            }
            if (f.Resolve(node.Get("Kids")) is not PdfArray kids) continue;
            for (var i = kids.Count - 1; i >= 0; i--)
            {
                if (f.Resolve(kids[i]) is not PdfDictionary kd) continue;
                if (f.Resolve(kd.Get("Limits")) is PdfArray { Count: >= 2 } lim
                    && f.Resolve(lim[0]).AsNumber() is { } lo && f.Resolve(lim[1]).AsNumber() is { } hi)
                {
                    if (key < lo || key > hi) continue;
                }
                if (pending.Count < StructureTree.MaxNodes) pending.Push((kd, depth + 1));
            }
        }
        return null;
    }

    static HashSet<PdfDictionary> AnnotationObjectReferences(PdfFile f, StructElement element)
    {
        var objects = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var child in element.Children)
            if (child is StructObjectRef { Object: PdfDictionary obj }) objects.Add(obj);

        // Parsed OBJR nodes retain only the resolved object; inspect raw K to verify /Obj indirection.
        var indirectObjects = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var kids = f.Resolve(element.Dict.Get("K"));
        if (kids is PdfArray array)
        {
            foreach (var kid in array.Items.Take(StructureTree.MaxNodes)) AddIndirectObject(kid);
        }
        else AddIndirectObject(kids);
        objects.IntersectWith(indirectObjects);
        return objects;

        void AddIndirectObject(PdfObject? kid)
        {
            if (f.Resolve(kid) is PdfDictionary dict && f.Resolve(dict.Get("Type")).AsName() == "OBJR"
                && dict.Get("Obj") is PdfRef reference && f.Resolve(reference) is PdfDictionary obj)
                indirectObjects.Add(obj);
        }
    }
}
