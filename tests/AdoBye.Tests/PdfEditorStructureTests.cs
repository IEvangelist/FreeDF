using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class PdfEditorStructureTests
{
    const string Content = "/P <</MCID 0>> BDC BT (a) Tj ET EMC /Span <</MCID 1>> BDC BT (b) Tj ET EMC";

    static PdfEditor Open(string pageExtra = "", string annot = "")
    {
        var bodies = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /Lang (en) >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] {pageExtra} >>",
            ObjPdf.Stream(Content),
            $"<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Contents (go) {annot} >>",
        };
        return PdfEditor.Open(PdfDocument.Load(ObjPdf.Build(bodies)));
    }

    static void NoIssues(StructureTree tree)
    {
        var issues = tree.CheckConsistency();
        Assert.True(issues.Count == 0, string.Join("; ", issues.Select(i => i.Code + ": " + i.Message)));
    }

    static PdfDocument Reload(PdfEditor editor) => PdfDocument.Load(editor.Save());

    static PdfDictionary FirstAnnotation(PdfDocument doc) =>
        Assert.IsType<PdfDictionary>(doc.File.Resolve(Assert.IsType<PdfArray>(doc.File.Resolve(doc.Pages[0].Dict.Get("Annots"))).Items[0]));

    static (PdfEditor Editor, PdfRef Doc, PdfRef P1, PdfRef P2, PdfRef Link) Tagged()
    {
        var e = Open();
        var root = e.EnsureStructureRoot();
        var doc = e.AddStructureElement(root, "Document");
        var p1 = e.AddStructureElement(doc, "P");
        e.AddStructureMarkedContent(p1, 0, 0);
        var p2 = e.AddStructureElement(doc, "P");
        e.AddStructureMarkedContent(p2, 0, 1);
        var link = e.AddStructureElement(doc, "Link");
        e.AddStructureObjectReference(link, 0, new PdfRef(5, 0));
        return (e, doc, p1, p2, link);
    }

    [Fact]
    public void BuildsAConsistentTreeAndParentTree()
    {
        var (e, _, _, _, _) = Tagged();
        var result = e.RebuildParentTree();
        Assert.Equal(4, result.Elements);
        Assert.Equal(2, result.ParentTreeEntries);
        Assert.Equal(0, result.Skipped);

        var doc = Reload(e);
        Assert.True(doc.IsTagged());
        var tree = doc.Structure!;
        NoIssues(tree);
        var owner0 = tree.ElementFromParentTree(0, 0);
        var owner1 = tree.ElementFromParentTree(0, 1);
        Assert.Equal("P", owner0!.Type);
        Assert.NotSame(owner0, owner1);
        Assert.Equal(new[] { 0, 1 }, tree.Children[0].ChildElements.Where(c => c.Type == "P").Select(c => c.MarkedContent.Single().Mcid));
        var annotKey = (int)doc.File.Resolve(FirstAnnotation(doc).Get("StructParent")).AsNumber()!;
        Assert.Equal("Link", Assert.IsType<StructElement>(tree.ElementFor(Assert.IsType<PdfDictionary>(tree.ParentTreeLookup(annotKey)))).Type);
        Assert.NotNull(tree.ParentTreeLookup((int)doc.Pages[0].Dict.Get("StructParents").AsNumber()!) as PdfArray);
    }

    [Fact]
    public void MoveAndRemoveKeepTheParentTreeInSync()
    {
        var (e, doc, p1, p2, _) = Tagged();
        e.MoveStructureElement(p2, doc, 0);
        Assert.Equal(1, e.RemoveStructureElement(p1));
        e.RebuildParentTree();

        var tree = Reload(e).Structure!;
        NoIssues(tree);
        var kids = tree.Children[0].ChildElements.ToList();
        Assert.Equal(["P", "Link"], kids.Select(k => k.Type));
        Assert.Equal(1, kids[0].MarkedContent.Single().Mcid);
        Assert.Null(tree.ElementFromParentTree(0, 0));
        Assert.Equal("P", tree.ElementFromParentTree(0, 1)!.Type);
    }

    [Fact]
    public void RemoveCanKeepChildrenAndMoveRejectsCycles()
    {
        var e = Open();
        var root = e.EnsureStructureRoot();
        var doc = e.AddStructureElement(root, "Document");
        var sect = e.AddStructureElement(doc, "Sect");
        var p = e.AddStructureElement(sect, "P");
        Assert.Throws<ArgumentException>(() => e.MoveStructureElement(sect, p));
        Assert.Throws<ArgumentException>(() => e.MoveStructureElement(sect, sect));
        Assert.Equal(1, e.RemoveStructureElement(sect, keepChildren: true));
        e.AddStructureMarkedContent(p, 0, 0);
        e.RebuildParentTree();
        var tree = Reload(e).Structure!;
        Assert.Equal(["Document", "P"], tree.Elements().Select(x => x.Type));
        Assert.Equal("Document", tree.Elements().Last().Parent!.Type);
    }

    [Fact]
    public void ExistingUniqueKeysAreKeptAndClashesReassigned()
    {
        var e = Open(pageExtra: "/StructParents 7", annot: "/StructParent 7");
        var root = e.EnsureStructureRoot();
        var doc = e.AddStructureElement(root, "Document");
        var p = e.AddStructureElement(doc, "P");
        e.AddStructureMarkedContent(p, 0, 0);
        var link = e.AddStructureElement(doc, "Link");
        e.AddStructureObjectReference(link, 0, new PdfRef(5, 0));
        var result = e.RebuildParentTree();
        Assert.Equal(9, result.NextKey);

        var loaded = Reload(e);
        var pageKey = (int)loaded.Pages[0].Dict.Get("StructParents").AsNumber()!;
        var annotKey = (int)loaded.File.Resolve(FirstAnnotation(loaded).Get("StructParent")).AsNumber()!;
        Assert.Equal(7, pageKey);
        Assert.NotEqual(pageKey, annotKey);
        NoIssues(loaded.Structure!);
    }

    [Fact]
    public void ReportsNoTreeAndSkipsDirectElements()
    {
        Assert.Throws<InvalidOperationException>(() => Open().RebuildParentTree());
        var e = Open();
        var root = e.EnsureStructureRoot();
        var doc = e.AddStructureElement(root, "Document");
        e.SetStructureText(doc, "Lang", "en-US");
        Assert.Equal("en-US", Reload(e).Structure!.Children[0].Lang);
        e.SetStructureText(doc, "Lang", null);
        Assert.Null(Reload(e).Structure!.Children[0].Lang);
        Assert.Equal(0, e.RebuildParentTree().Skipped);
    }

    [Fact]
    public void EditedDocumentPassesThePdfUaContentTaggingChecks()
    {
        var (e, _, _, _, _) = Tagged();
        e.RebuildParentTree();
        var report = PdfValidator.ValidatePdfUA(Reload(e), 1);
        Assert.False(report.Has("PDFUA-MCID-UNMAPPED"));
        Assert.False(report.Has("PDFUA-CONTENT-UNTAGGED"));
        Assert.False(report.Has("PDFUA-ANNOT-PARENTTREE"));
        Assert.False(report.Has("PDFUA-ANNOT-OBJR"));
    }

    const string Untagged = "q 1 0 0 1 0 0 cm 0 0 40 40 re f Q BT /F1 12 Tf 10 50 Td (hello) Tj ET 0 80 m 100 80 l S BT 10 20 Td (again) Tj ET /Fm0 Do BI /W 1 /H 1 /CS /G /BPC 8 ID \0 EI";

    static PdfEditor OpenUntagged(string content)
    {
        var bodies = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /Lang (en) >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> /XObject << /Fm0 6 0 R >> >> >>",
            ObjPdf.Stream(content),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            ObjPdf.Stream("0 0 1 rg 60 60 20 20 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100]"),
        };
        return PdfEditor.Open(PdfDocument.Load(ObjPdf.Build(bodies)));
    }

    [Fact]
    public void AutoTagMarksTextAsParagraphsAndEverythingElseAsArtifacts()
    {
        var e = OpenUntagged(Untagged);
        var before = PortableDocument.Render.PortableDocument.RenderPage(PdfDocument.Load(e.Save()).Pages[0], 72).ToPng();
        var result = e.AutoTagUntagged();
        Assert.Equal(new PdfEditor.AutoTagResult(1, 0, 2, 4), result);

        var doc = Reload(e);
        // Tagging must not change what is drawn.
        Assert.Equal(before, PortableDocument.Render.PortableDocument.RenderPage(doc.Pages[0], 72).ToPng());
        var tree = doc.Structure!;
        NoIssues(tree);
        Assert.Equal(["Document", "P", "P"], tree.Elements().Select(x => x.Type));
        Assert.Equal(new[] { 0, 1 }, tree.Elements().Skip(1).Select(x => x.MarkedContent.Single().Mcid));
        var report = PdfValidator.ValidatePdfUA(doc, 1);
        Assert.False(report.Has("PDFUA-CONTENT-UNTAGGED"));
        Assert.False(report.Has("PDFUA-MCID-UNMAPPED"));
        Assert.False(report.Has("PDFUA-MCID-MISSING"));
        Assert.True(report.Has("PDFUA-TITLE") || report.Has("PDFUA-XMP-MISSING"), "auto-tagging does not claim PDF/UA conformance");
    }

    [Fact]
    public void AutoTagLeavesMarkedPagesAndTaggedDocumentsAlone()
    {
        var marked = OpenUntagged("/Span <</Lang (en)>> BDC BT 10 50 Td (x) Tj ET EMC");
        Assert.Equal(new PdfEditor.AutoTagResult(0, 1, 0, 0), marked.AutoTagUntagged());
        Assert.Null(marked.StructureRoot);

        var tagged = OpenUntagged(Untagged);
        tagged.AutoTagUntagged();
        Assert.Throws<InvalidOperationException>(() => tagged.AutoTagUntagged());
    }
}
