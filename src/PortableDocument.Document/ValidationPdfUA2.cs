using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// PDF/UA-2 (ISO 14289-2) structure rules that depend on PDF 2.0 structure namespaces (ISO 32000-2 14.7.4, 14.8.6).
/// Rule choices were reconciled against veraPDF 1.30.3 over the veraPDF-corpus PDF_UA-2 files; they are not a certification.
/// </summary>
internal static class PdfUA2Checks
{
    public const string Pdf17Namespace = "http://iso.org/pdf/ssn";
    public const string Pdf2Namespace = "http://iso.org/pdf2/ssn";
    public const string MathMlNamespace = "http://www.w3.org/1998/Math/MathML";
    const string Clause = "ISO 14289-2 8.2";
    const int MaxRoleDepth = 32;

    public static void Run(ValidationContext c, StructureTree tree)
    {
        c.Guard("PDF/UA-2 version", () => Version(c));
        c.Guard("PDF/UA-2 root", () => Root(c, tree));
        c.Guard("PDF/UA-2 roles", () => Roles(c, tree));
        c.Guard("PDF/UA-2 element rules", () => Elements(c, tree));
        c.Guard("PDF/UA-2 destinations", () => Destinations(c));
        c.Guard("PDF/UA-2 file specifications", () => Files(c));
    }

    /// <summary>8.14.1-1 (Desc on embedded-file specifications) and 8.9.2.4.10-1 (AFRelationship on file-attachment targets). Specifications are found by scanning all objects, a superset of the EmbeddedFiles name tree.</summary>
    static void Files(ValidationContext c)
    {
        var f = c.F;
        bool desc = false, rel = false;
        foreach (var o in c.AllObjects())
        {
            if (o is not PdfDictionary d) continue;
            if (!desc && d.Get("EF") is not null && f.Resolve(d.Get("Type")).AsName() is "Filespec" or null && f.Resolve(d.Get("Desc")) is not PdfString)
            { desc = true; c.Add("PDFUA2-FILESPEC-DESC", ValidationSeverity.Error, "an embedded-file specification has no /Desc (veraPDF PDFUA-2 8.14.1-1)", "ISO 14289-2 8.14.1"); }
            if (!rel && f.Resolve(d.Get("Subtype")).AsName() == "FileAttachment" && f.Resolve(d.Get("FS")) is PdfDictionary fs && fs.Get("AFRelationship") is null)
            { rel = true; c.Add("PDFUA2-FILEATTACH-AFRELATIONSHIP", ValidationSeverity.Error, "a file-attachment annotation's file specification has no /AFRelationship (veraPDF PDFUA-2 8.9.2.4.10-1)", "ISO 14289-2 8.9.2.4.10"); }
        }
    }

    static void Version(ValidationContext c)
    {
        var f = c.F;
        var catalogVersion = f.Resolve(f.Catalog.Get("Version")).AsName();
        if (f.Version != "2.0" && catalogVersion != "2.0")
            c.Add("PDFUA2-VERSION", ValidationSeverity.Error, $"file version is not PDF 2.0 (header '{f.Version}', catalog /Version '{catalogVersion}')", "ISO 14289-2 6.1");
    }

    static void Root(ValidationContext c, StructureTree tree)
    {
        if (tree.Children.Count != 1 || tree.Children[0].StandardType != "Document")
            c.Add("PDFUA2-DOCUMENT-ROOT", ValidationSeverity.Error, "structure tree root must contain a single Document structure element as its only child", "ISO 14289-2 8.2.5.2");
        else if (tree.Children[0].Namespace != Pdf2Namespace)
            c.Add("PDFUA2-DOCUMENT-NS", ValidationSeverity.Error, "the Document structure element must be in the PDF 2.0 namespace (http://iso.org/pdf2/ssn)", "ISO 14289-2 8.2.5.2");
    }

    /// <summary>Where a (type, namespace) pair ends after role mapping: a PDF 1.7 or PDF 2.0 standard type, a circular chain, or nowhere.</summary>
    enum RoleOutcome { Standard, Circular, Unmapped }

    static RoleOutcome Resolve(PdfFile f, StructureTree tree, string type, PdfDictionary? nsDict, string? ns)
    {
        var seen = new HashSet<(string, string?)>();
        for (var depth = 0; depth < MaxRoleDepth; depth++)
        {
            if ((ns is null || ns == Pdf17Namespace) && StructureTree.IsPdf17StandardType(type) && !tree.RoleMap.ContainsKey(type)) return RoleOutcome.Standard;
            if (ns == Pdf2Namespace && StructureTree.IsStandardType(type)) return RoleOutcome.Standard;
            if (ns == MathMlNamespace) return RoleOutcome.Standard;
            if (!seen.Add((type, ns))) return RoleOutcome.Circular;
            if (nsDict is not null && ns is not null && ns != Pdf17Namespace && ns != Pdf2Namespace)
            {
                if (f.Resolve(nsDict.Get("RoleMapNS")) is not PdfDictionary map || map.Get(type) is not { } entry) return RoleOutcome.Unmapped;
                var target = f.Resolve(entry);
                if (target is PdfName n) { type = n.Value; continue; }
                if (target is PdfArray { Count: >= 1 } a && f.Resolve(a[0]).AsName() is { } tn)
                {
                    type = tn;
                    nsDict = a.Count > 1 ? f.Resolve(a[1]) as PdfDictionary : null;
                    ns = nsDict is not null && f.Resolve(nsDict.Get("NS")) is PdfString s ? s.Text : null;
                    continue;
                }
                return RoleOutcome.Unmapped;
            }
            if (tree.RoleMap.TryGetValue(type, out var next)) { type = next; ns = null; nsDict = null; continue; }
            return RoleOutcome.Unmapped;
        }
        return RoleOutcome.Circular;
    }

    static void Roles(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        var reported = new HashSet<(string, string?)>();
        var namespaces = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        foreach (var e in tree.Elements())
        {
            if (e.Namespace is { } uri && e.NamespaceDict is { } nd) namespaces.TryAdd(uri, nd);
            if (!reported.Add((e.Type, e.Namespace))) continue;
            switch (Resolve(f, tree, e.Type, e.NamespaceDict, e.Namespace))
            {
                case RoleOutcome.Circular:
                    c.Add("PDFUA2-ROLEMAP-CIRCULAR", ValidationSeverity.Error, $"structure type /{e.Type} has a circular role mapping", "ISO 14289-2 8.2.4", e.PageIndex);
                    break;
                case RoleOutcome.Unmapped:
                    c.Add("PDFUA2-ROLEMAP-MISSING", ValidationSeverity.Error, $"structure type /{e.Type} does not belong to or map to the PDF 1.7 or PDF 2.0 namespace", "ISO 14289-2 8.2.4", e.PageIndex);
                    break;
            }
        }
        foreach (var (uri, nd) in namespaces)
        {
            if (uri is Pdf17Namespace or Pdf2Namespace || f.Resolve(nd.Get("RoleMapNS")) is not PdfDictionary map) continue;
            foreach (var (name, raw) in map.Entries.Take(10_000))
            {
                var target = f.Resolve(raw);
                var sameNs = target is PdfName
                    || target is PdfArray { Count: >= 2 } a && f.Resolve(a[1]) is PdfDictionary td && f.Resolve(td.Get("NS")) is PdfString ts && ts.Text == uri;
                if (sameNs)
                    c.Add("PDFUA2-ROLEMAP-SAME-NS", ValidationSeverity.Error, $"/{name} is role mapped to another type in its own namespace '{uri}'", "ISO 14289-2 8.2.4");
            }
        }
    }

    static void Elements(ValidationContext c, StructureTree tree)
    {
        var f = c.F;
        foreach (var e in tree.Elements())
        {
            var inPdf17 = e.Namespace is null or Pdf17Namespace;
            if (e.Type == "Math" && e.Parent?.StandardType != "Formula")
                c.Add("PDFUA2-MATH-PARENT", ValidationSeverity.Error, "a Math element may only occur as a child of Formula", "ISO 14289-2 8.2.5.29", e.PageIndex);
            if (e.StandardType == "H")
                c.Add("PDFUA2-HEADING-H", ValidationSeverity.Error, "the unnumbered H structure type must not be used (use Hn or H within Sect)", "ISO 14289-2 8.2.5.12", e.PageIndex);
            if (inPdf17 && e.Type == "Note" && !tree.RoleMap.ContainsKey("Note"))
                c.Add("PDFUA2-NOTE", ValidationSeverity.Error, "the Note structure type is only permitted when role mapped to a PDF 2.0 namespace element", "ISO 14289-2 8.2.5.14", e.PageIndex);
            switch (e.StandardType)
            {
                case "L":
                    if (e.ChildElements.Any(li => li.StandardType == "LI" && li.ChildElements.Any(l => l.StandardType == "Lbl"))
                        && (e.Attribute(f, "ListNumbering").AsName() is null or "None"))
                        c.Add("PDFUA2-LIST-NUMBERING", ValidationSeverity.Error, "L contains Lbl elements but has no ListNumbering attribute other than None", "ISO 14289-2 8.2.5.25", e.PageIndex);
                    break;
                case "LI":
                    if (e.MarkedContent.Any())
                        c.Add("PDFUA2-LI-CONTENT", ValidationSeverity.Error, "content directly inside LI must be enclosed in Lbl or LBody", "ISO 14289-2 8.2.5.25", e.PageIndex);
                    break;
                case "Form":
                    if (e.Children.OfType<StructObjectRef>().Count(o => o.Object is PdfDictionary d && f.Resolve(d.Get("Subtype")).AsName() == "Widget") > 1)
                        c.Add("PDFUA2-FORM-WIDGETS", ValidationSeverity.Error, "a Form structure element must contain at most one widget annotation", "ISO 14289-2 8.10.1", e.PageIndex);
                    break;
            }
            if (e.StandardType is not ("Table" or "L" or "TOC"))
            {
                var kids = e.ChildElements.ToList();
                for (var i = 0; i < kids.Count; i++)
                    if (kids[i].StandardType == "Caption" && i != 0 && i != kids.Count - 1)
                    { c.Add("PDFUA2-CAPTION-POSITION", ValidationSeverity.Error, $"Caption must be the first or last child of /{e.Type}", "ISO 14289-2 8.2.5.27", e.PageIndex); break; }
            }
        }
    }

    /// <summary>8.8: destinations that target this document must be structure destinations. Only direct destination arrays are evaluated; named destinations are not resolved.</summary>
    static void Destinations(ValidationContext c)
    {
        var f = c.F;
        bool PageTarget(PdfObject? dest) =>
            f.Resolve(dest) is PdfArray { Count: > 0 } a && f.Resolve(a[0]) is PdfDictionary d && f.Resolve(d.Get("Type")).AsName() == "Page";
        bool ActionTargetsPage(PdfObject? action) =>
            f.Resolve(action) is PdfDictionary ad && f.Resolve(ad.Get("S")).AsName() == "GoTo" && ad.Get("SD") is null && PageTarget(ad.Get("D"));

        for (var i = 0; i < c.Doc.Pages.Count; i++)
        {
            if (f.Resolve(c.Doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var a in annots.Items.Take(10_000))
                if (f.Resolve(a) is PdfDictionary ad && (PageTarget(ad.Get("Dest")) || ActionTargetsPage(ad.Get("A"))))
                {
                    c.Add("PDFUA2-DEST-NOT-STRUCTURE", ValidationSeverity.Error, "an annotation destination targets a page instead of a structure element (veraPDF PDFUA-2 8.8-1/2)", "ISO 14289-2 8.8", i);
                    return;
                }
        }
        if (f.Resolve(f.Catalog.Get("Outlines")) is not PdfDictionary root) return;
        var stack = new Stack<PdfDictionary>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (f.Resolve(root.Get("First")) is PdfDictionary first) stack.Push(first);
        while (stack.Count > 0 && seen.Count < 10_000)
        {
            var item = stack.Pop();
            if (!seen.Add(item)) continue;
            if (PageTarget(item.Get("Dest")) || ActionTargetsPage(item.Get("A")))
            {
                c.Add("PDFUA2-DEST-NOT-STRUCTURE", ValidationSeverity.Error, "an outline destination targets a page instead of a structure element (veraPDF PDFUA-2 8.8-1/2)", "ISO 14289-2 8.8");
                return;
            }
            if (f.Resolve(item.Get("Next")) is PdfDictionary next) stack.Push(next);
            if (f.Resolve(item.Get("First")) is PdfDictionary child) stack.Push(child);
        }
    }
}
