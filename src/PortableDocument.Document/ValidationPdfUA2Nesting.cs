using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Unconditional ISO/TS 32005:2023 Table 5 exclusions, incorporated by ISO 14289-2:2024 6.2 and 8.2.4.
/// Factual relationships derived from veraPDF-validation-profiles v1.30.3 (c4f57eff), PDF_UA/PDFUA-2-ISO32005.xml.
/// Grouping-dependent and Ruby/Warichu sequence rules are not evaluated here.</summary>
internal static class PdfUA2NestingChecks
{
    const int MaxFindings = 1000;
    const string Clause = "ISO/TS 32005:2023 Table 5";

    // Missing pairs are not a claim of validity: some require grouping or cardinality checks.
    static readonly Dictionary<string, HashSet<string>> Prohibited = BuildProhibited();

    static Dictionary<string, HashSet<string>> BuildProhibited()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Annot"] = "Document Index LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR WP WT",
            ["Art"] = "Art BibEntry Document Em LBody LI Quote RB RP RT Ruby Span Strong Sub TBody TD TFoot TH THead TOCI TR WP WT Warichu",
            ["Aside"] = "Aside BibEntry Em LBody LI Quote RB RP RT Ruby Span Strong Sub TBody TD TFoot TH THead TOCI TR Title WP WT Warichu",
            ["BibEntry"] = "Art Aside BlockQuote Caption Code Document DocumentFragment H Hn Index L LBody LI RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["BlockQuote"] = "Aside BibEntry Em LBody LI Quote RB RP RT Ruby Span Strong Sub TBody TD TFoot TH THead TOCI TR Title WP WT Warichu",
            ["Caption"] = "Caption Document LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["Code"] = "Document H Hn LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["Document"] = "BibEntry Caption Em LBody LI Lbl Quote RB RP RT Reference Ruby Span Strong Sub TBody TD TFoot TH THead TOCI TR WP WT Warichu",
            ["DocumentFragment"] = "BibEntry Caption Em LBody LI Lbl Quote RB RP RT Reference Ruby Span Strong Sub TBody TD TFoot TH THead TOCI TR WP WT Warichu",
            ["Em"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index L LBody LI P Part RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["FENote"] = "Document H Hn LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["Figure"] = "Document DocumentFragment LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["Form"] = "Annot Art Aside BlockQuote Document DocumentFragment Em Form H Hn Index LBody LI Link P Quote RB RP RT Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Title WP WT Warichu",
            ["Formula"] = "Art Document DocumentFragment LBody LI RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["H"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index L LBody LI P Part RB RP RT TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["Hn"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index L LBody LI P Part RB RP RT TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["Index"] = "Art Aside BibEntry BlockQuote Code Document DocumentFragment Em Form Index LBody LI Lbl Link Quote RB RP RT Ruby Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Title WP WT Warichu",
            ["L"] = "Annot Art Aside BibEntry BlockQuote Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index LBody Lbl Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["LBody"] = "Document DocumentFragment LBody LI Lbl RB RP RT TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["LI"] = "Annot Art Aside BibEntry BlockQuote Caption Code Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LI Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["Lbl"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index L LBody LI Lbl P Part RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["Link"] = "Document Index LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR WP WT",
            ["Note"] = "Document H Hn LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["P"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index LBody LI P Part RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["Quote"] = "Art Aside BlockQuote Caption Document DocumentFragment H Hn Index L LBody LI P Part RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["RB"] = "Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment FENote Figure Formula H Hn Index L LBody LI Lbl Note P Part RB RP RT Ruby Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["RP"] = "Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment FENote Figure Formula H Hn Index L LBody LI Lbl Note P Part RB RP RT Ruby Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["RT"] = "Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment FENote Figure Formula H Hn Index L LBody LI Lbl Note P Part RB RP RT Ruby Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["Reference"] = "Document Index LBody LI RB RP RT TBody TD TFoot TH THead TOC TOCI TR WP WT",
            ["Ruby"] = "Annot Art Artifact Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Part Quote Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["Sect"] = "BibEntry Document Em LBody LI Quote RB RP RT Ruby Span Strong Sub TBody TD TFoot TH THead TOCI TR WP WT Warichu",
            ["Span"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index L LBody LI P Part RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["Strong"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index L LBody LI P Part RB RP RT Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["StructTreeRoot"] = "Annot Art Artifact Aside BibEntry BlockQuote Caption Code Div DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link NonStruct Note P Part Private Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["Sub"] = "Art Aside BlockQuote Caption Div Document DocumentFragment H Hn Index LBody LI P Part RB RP RT Sect Sub TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT",
            ["TBody"] = "Annot Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI Table Title WP WT Warichu",
            ["TD"] = "Aside BlockQuote Caption Document DocumentFragment LBody LI Part RB RP RT Sub TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["TFoot"] = "Annot Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI Table Title WP WT Warichu",
            ["TH"] = "Aside BlockQuote Caption Document DocumentFragment LBody LI Part RB RP RT Sub TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["THead"] = "Annot Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI Table Title WP WT Warichu",
            ["TOC"] = "Annot Art Aside BibEntry BlockQuote Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TR Table Title WP WT Warichu",
            ["TOCI"] = "Art Aside BibEntry BlockQuote Caption Code Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Link Note Part Quote RB RP RT Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOCI TR Table Title WP WT Warichu",
            ["TR"] = "Annot Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TFoot THead TOC TOCI TR Table Title WP WT Warichu",
            ["Table"] = "Annot Art Aside BibEntry BlockQuote Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TD TH TOC TOCI Table Title WP WT Warichu",
            ["Title"] = "Art BlockQuote Document DocumentFragment H Hn Index LBody LI RB RP RT Sect Sub TBody TD TFoot TH THead TOC TOCI TR Title WP WT",
            ["WP"] = "Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment FENote Figure Formula H Hn Index L LBody LI Lbl Note P Part RB RP RT Ruby Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["WT"] = "Art Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment FENote Figure Formula H Hn Index L LBody LI Lbl Note P Part RB RP RT Ruby Sect TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu",
            ["Warichu"] = "Annot Art Artifact Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link Note P Part Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Table Title Warichu",
        };
        return rows.ToDictionary(r => r.Key, r => r.Value.Split(' ').ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    static readonly HashSet<string> NoContent = new(StringComparer.Ordinal)
    {
        "Art", "Document", "DocumentFragment", "Index", "L", "Sect", "TBody", "TFoot", "THead", "TOC", "TOCI", "TR", "Table",
    };

    static readonly Dictionary<string, string[]> AtMostOne = new(StringComparer.Ordinal)
    {
        ["Art"] = ["Caption", "H"],
        ["Artifact"] = ["Caption", "H"],
        ["Aside"] = ["Caption", "H"],
        ["BlockQuote"] = ["Caption", "H"],
        ["Caption"] = ["H"],
        ["Code"] = ["Caption"],
        ["Document"] = ["H"],
        ["DocumentFragment"] = ["H"],
        ["Figure"] = ["Caption", "H"],
        ["Form"] = ["Caption"],
        ["Formula"] = ["Caption", "H"],
        ["H"] = ["Sect"],
        ["Hn"] = ["Sect"],
        ["Index"] = ["Caption"],
        ["L"] = ["Caption"],
        ["LBody"] = ["Caption", "H"],
        ["Note"] = ["Caption"],
        ["Sect"] = ["H"],
        ["TD"] = ["H"],
        ["TH"] = ["H"],
        ["TOC"] = ["Caption"],
        ["Table"] = ["Caption", "TFoot", "THead"],
        ["Title"] = ["Caption"],
    };

    public static void Run(ValidationContext c, StructureTree tree)
    {
        var containers = new Dictionary<StructElement, (string? Type, StructElement? Owner)>();
        var occurrences = new Dictionary<(StructElement? Owner, string Type), int>();
        var rootDocuments = 0;
        var findings = 0;
        var incomplete = tree.Truncated || tree.ParseIssues.Count > 0;
        var rootKids = c.F.Resolve(tree.Root.Get("K"));
        IEnumerable<PdfObject> rootItems = rootKids is PdfArray array ? array.Items : rootKids is null ? [] : [rootKids];
        foreach (var item in rootItems.Take(StructureTree.MaxNodes))
        {
            var value = c.F.Resolve(item);
            if (value is not PdfInteger
                && !(value is PdfDictionary d && c.F.Resolve(d.Get("Type")).AsName() is "MCR" or "OBJR")) continue;
            findings++;
            c.Add("PDFUA2-NESTING-CONTENT", ValidationSeverity.Error, "StructTreeRoot must not contain content items", Clause);
            break;
        }
        if (rootKids is PdfArray { Count: > StructureTree.MaxNodes }) incomplete = true;
        foreach (var e in tree.Elements())
        {
            if (findings >= MaxFindings) { incomplete = true; break; }
            var type = TypeOf(e);
            var (parentType, owner) = e.Parent is null ? ("StructTreeRoot", null) : containers[e.Parent];
            var transparent = type is "Part" or "Div" or "NonStruct";
            if (!transparent && type is not null)
            {
                if (owner is null && parentType == "StructTreeRoot" && type == "Document") rootDocuments++;
                if (parentType is not null && AtMostOne.TryGetValue(parentType, out var limited) && limited.Contains(type))
                {
                    var key = (owner, type);
                    occurrences.TryGetValue(key, out var count);
                    occurrences[key] = count + 1;
                    if (count == 1)
                    {
                        findings++;
                        c.Add("PDFUA2-NESTING-CARDINALITY", ValidationSeverity.Error,
                            $"{parentType} contains more than one {type}, counting through transparent wrappers (ISO/TS 32005 Table 5. {parentType}-{type}; occurrence limit)",
                            Clause, e.PageIndex);
                    }
                }
            }
            if (type is not null && parentType is not null
                && Prohibited.TryGetValue(parentType, out var excluded) && excluded.Contains(type))
            {
                if (findings >= MaxFindings) { incomplete = true; break; }
                findings++;
                c.Add("PDFUA2-NESTING", ValidationSeverity.Error,
                    $"/{e.Type} ({type}) is not permitted inside {parentType} (ISO/TS 32005 Table 5. {parentType}-{type})",
                    Clause, e.PageIndex);
            }
            var contentType = transparent ? parentType : type;
            containers[e] = (contentType, transparent ? owner : e);
            if (contentType is not null && NoContent.Contains(contentType) && e.Children.Any(k => k is not StructElement))
            {
                if (findings >= MaxFindings) { incomplete = true; break; }
                findings++;
                c.Add("PDFUA2-NESTING-CONTENT", ValidationSeverity.Error,
                    $"/{e.Type} inherits {contentType} containment and must not contain content items", Clause, e.PageIndex);
            }
        }
        if (rootDocuments > 1 || rootDocuments == 0 && !incomplete)
        {
            if (findings >= MaxFindings) incomplete = true;
            else
                c.Add("PDFUA2-NESTING-CARDINALITY", ValidationSeverity.Error,
                    "StructTreeRoot must contain exactly one Document (ISO/TS 32005 Table 5. StructTreeRoot-Document; occurrence limit)", Clause);
        }
        if (incomplete)
            c.Add("PDFUA2-NESTING-INCOMPLETE", ValidationSeverity.Error,
                "structure parse issues or truncation, an unresolved namespace or role mapping, or the containment finding limit prevented a complete nesting check", Clause);

        string? TypeOf(StructElement e)
        {
            var rawNs = e.Dict.Get("NS");
            var seen = new HashSet<int>();
            while (rawNs is PdfRef reference)
            {
                if (seen.Count >= 32 || !seen.Add(reference.Number)
                    || !c.F.Xref.TryGetValue(reference.Number, out var entry) || !entry.InUse
                    || reference.Generation != (entry.Compressed ? 0 : entry.GenOrIndex))
                {
                    incomplete = true;
                    return null;
                }
                rawNs = c.F.GetObject(reference);
            }
            if (rawNs is not (null or PdfNull) && (e.NamespaceDict is null || e.Namespace is null))
            {
                incomplete = true;
                return null;
            }
            var type = PdfUA2Checks.StandardType(c, tree, e, out var roleResolved);
            if (!roleResolved) incomplete = true;
            return StructureTree.HeadingLevel(type ?? "") is > 0 ? "Hn" : type;
        }
    }
}
