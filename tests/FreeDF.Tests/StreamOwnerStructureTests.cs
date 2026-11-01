using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

namespace FreeDF.Tests;

public class StreamOwnerStructureTests
{
    const string Appearance = "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /StructParents 2";
    const string Reference = "<< /Type /MCR /Stm 9 0 R /StmOwn 10 0 R /MCID 0 >>";

    static PdfDocument Load(params (int Number, string Body)[] replacements)
    {
        var bodies = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /StructParents 0 /Annots [10 0 R] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>",
            ObjPdf.Stream("/P <</MCID 0>> BDC BT /F1 12 Tf (Page) Tj ET EMC"),
            "<< /Type /StructTreeRoot /K 6 0 R /ParentTree 11 0 R >>",
            "<< /S /Document /Pg 3 0 R /K [7 0 R 8 0 R 12 0 R] >>",
            "<< /S /P /K 0 >>",
            $"<< /S /Figure /Alt (Appearance description) /K {Reference} >>",
            ObjPdf.Stream("/Span <</MCID 0>> BDC EMC", Appearance),
            "<< /Type /Annot /Subtype /Stamp /Rect [0 0 100 100] /StructParent 1 /AP << /N 9 0 R >> >>",
            "<< /Nums [0 [7 0 R] 1 12 0 R 2 [8 0 R]] >>",
            "<< /S /Annot /K << /Type /OBJR /Obj 10 0 R >> >>",
            "<< /N 9 0 R >>",
            "14 0 R",
        };
        foreach (var (number, body) in replacements) bodies[number - 1] = body;
        return PdfDocument.Load(ObjPdf.Build(bodies));
    }

    static PdfStream Stream(PdfDocument doc) => Assert.IsType<PdfStream>(doc.File.GetObject(9));
    static StructMarkedContent Content(PdfDocument doc) => Assert.Single(doc.Structure!.ElementFor(Assert.IsType<PdfDictionary>(doc.File.GetObject(8)))!.MarkedContent);

    [Fact]
    public void AppearanceOwnerIsNotTheParentTreeContainer()
    {
        var doc = Load();
        var tree = doc.Structure!;
        var content = Content(doc);
        Assert.Same(Stream(doc), content.Stream);
        Assert.Same(doc.File.GetObject(10), content.StreamOwner);
        Assert.Equal(0, content.PageIndex);
        Assert.Same(content, tree.FindMarkedContent(Stream(doc), 0));
        Assert.Same(content.Parent, tree.ElementFromParentTree(content));
        Assert.Same(content.Parent, tree.ElementFromParentTree(Stream(doc), 0));
        Assert.NotSame(tree.ElementFromParentTree(0, 0), tree.ElementFromParentTree(content));
        var pageContent = tree.FindMarkedContent(0, 0)!;
        Assert.Null(pageContent.StreamOwner);
        Assert.Same(pageContent.Parent, tree.ElementFromParentTree(pageContent));
        Assert.Equal("Page", StructuredTextExtractor.Extract(doc));
        Assert.Equal("Page\nAppearance description", StructuredTextExtractor.Extract(doc, includeAlt: true));
    }

    [Fact]
    public void TaggedAppearanceTextUsesItsStreamIdentityAndStructureOrder()
    {
        var doc = Load(
            (6, "<< /S /Document /Pg 3 0 R /K [8 0 R 7 0 R] >>"),
            (9, ObjPdf.Stream("/Span <</MCID 0>> BDC BT /FA 12 Tf 10 80 Td (Appearance) Tj ET EMC",
                Appearance + " /Resources << /Font << /FA << /Type /Font /Subtype /Type1 /BaseFont /Courier >> >> >>")));
        var extractor = new StructuredTextExtractor(doc);

        Assert.Equal("Page", string.Concat(global::PortableDocument.Render.PortableDocument.ExtractText(doc.Pages[0]).Select(s => s.Text)));
        Assert.DoesNotContain(global::PortableDocument.Render.PortableDocument.Analyze(doc.Pages[0]).Spans, s => s.Text == "Appearance");
        Assert.Equal("Appearance\nPage", extractor.Extract());
        Assert.Equal("Appearance", extractor.GetMarkedContentText(0, Stream(doc), 0));
    }

    [Fact]
    public void ActualTextTakesPrecedenceOverAppearanceTextAndAlt()
    {
        var doc = Load(
            (8, "<< /S /Figure /ActualText (Replacement) /Alt (Description) /K << /Type /MCR /Stm 9 0 R /StmOwn 10 0 R /MCID 0 >> >>"),
            (9, ObjPdf.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 10 80 Td (Appearance) Tj ET EMC", Appearance)));

        Assert.Equal("Page\nReplacement", StructuredTextExtractor.Extract(doc, includeAlt: true));
    }

    [Fact]
    public void SelectedNormalAppearanceStateIsExtractedButInactiveStateIsNot()
    {
        var doc = Load(
            (6, "<< /S /Document /Pg 3 0 R /K [8 0 R 12 0 R 7 0 R] >>"),
            (10, "<< /Type /Annot /Subtype /Stamp /Rect [0 0 100 100] /StructParent 1 /AP << /N << /On 9 0 R /Off 13 0 R >> >> /AS /On >>"),
            (9, ObjPdf.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 10 80 Td (Selected) Tj ET EMC", Appearance)),
            (12, "<< /S /Figure /Pg 3 0 R /K << /Type /MCR /Stm 13 0 R /StmOwn 10 0 R /MCID 0 >> >>"),
            (13, ObjPdf.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 10 80 Td (Inactive) Tj ET EMC", Appearance)));

        Assert.Equal("Selected\nPage", new StructuredTextExtractor(doc).Extract());
    }

    [Fact]
    public void SharedAppearanceStreamContributesOnceAndRequiresMatchingLocalOwner()
    {
        var doc = Load(
            (3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /StructParents 0 /Annots [10 0 R 13 0 R] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>"),
            (9, ObjPdf.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 10 80 Td (Shared) Tj ET EMC", Appearance)),
            (13, "<< /Type /Annot /Subtype /Stamp /Rect [0 0 100 100] /StructParent 2 /AP << /N 9 0 R >> >>"));
        Assert.Equal("Page\nShared", new StructuredTextExtractor(doc).Extract());

        doc = Load(
            (8, "<< /S /Figure /Alt (Appearance description) /K << /Type /MCR /Stm 9 0 R /StmOwn 13 0 R /MCID 0 >> >>"),
            (13, "<< /Type /Annot /Subtype /Stamp /Rect [0 0 100 100] /AP << /N 9 0 R >> >>"));
        Assert.Equal("Page\nAppearance description", StructuredTextExtractor.Extract(doc, includeAlt: true));
    }

    [Fact]
    public void MissingOwnerIsAllowedForLocalAppearanceAndAnnotationContentsAreNotText()
    {
        var doc = Load(
            (8, "<< /S /Figure /K << /Type /MCR /Stm 9 0 R /MCID 0 >> >>"),
            (10, "<< /Type /Annot /Subtype /Stamp /Rect [0 0 100 100] /Contents (Annotation contents) /AP << /N 9 0 R >> >>"),
            (9, ObjPdf.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 10 80 Td (Appearance) Tj ET EMC", Appearance)));

        Assert.Equal("Page\nAppearance", new StructuredTextExtractor(doc).Extract());
        Assert.DoesNotContain("Annotation contents", new StructuredTextExtractor(doc).Extract());
    }

    [Fact]
    public void StructuredExtractionDoesNotSynthesizeMissingAppearances()
    {
        var doc = Load(
            (10, "<< /Type /Annot /Subtype /FreeText /Rect [0 0 100 100] /Contents (Synthetic only) /DA (/F1 12 Tf) >>"));
        Assert.Equal("Page", new StructuredTextExtractor(doc).Extract());
    }

    [Fact]
    public void StmWithoutOwnerStillUsesTheNonPageStreamKey()
    {
        var doc = Load((8, "<< /S /Figure /K << /Type /MCR /Stm 9 0 R /MCID 0 >> >>"));
        var content = Content(doc);
        Assert.Null(content.StreamOwner);
        Assert.Same(content.Parent, doc.Structure!.ElementFromParentTree(content));
    }

    [Fact]
    public void OwnerStructParentsCannotReplaceMissingStreamStructParents()
    {
        var doc = Load(
            (9, ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 100 100]")),
            (10, "<< /StructParents 0 /AP << /N 9 0 R >> >>"));
        Assert.NotNull(Content(doc).StreamOwner);
        Assert.Null(doc.Structure!.ElementFromParentTree(Content(doc)));
    }

    [Fact]
    public void OwnerMayReferenceTheAppearanceThroughANamedStateDictionary()
    {
        var doc = Load((10, "<< /Type /Annot /Subtype /Stamp /StructParent 1 /AP << /N << /On 9 0 R >> >> /AS /On >>"));
        Assert.Same(doc.File.GetObject(10), Content(doc).StreamOwner);
        Assert.Same(Content(doc).Parent, doc.Structure!.ElementFromParentTree(Content(doc)));
    }

    [Fact]
    public void OwnerIsNotRequiredToBeAnAnnotationAndIsNotRecursivelyWalked()
    {
        var doc = Load((10, "[9 0 R 10 0 R]"));
        Assert.Same(doc.File.GetObject(10), Content(doc).StreamOwner);
        Assert.Same(Content(doc).Parent, doc.Structure!.ElementFromParentTree(Content(doc)));
    }

    [Fact]
    public void ReusedStreamOwnersDoNotCreateDifferentMcidNamespaces()
    {
        var doc = Load(
            (8, $"<< /S /Figure /K [{Reference} << /Type /MCR /Stm 9 0 R /StmOwn 13 0 R /MCID 0 >>] >>"));
        var contents = doc.Structure!.ElementFor(Assert.IsType<PdfDictionary>(doc.File.GetObject(8)))!.MarkedContent.ToArray();
        Assert.Equal(2, contents.Length);
        Assert.Same(contents[0].Stream, contents[1].Stream);
        Assert.NotSame(contents[0].StreamOwner, contents[1].StreamOwner);
        Assert.Same(contents[0], doc.Structure.FindMarkedContent(Stream(doc), 0));
        Assert.All(contents, content => Assert.Same(content.Parent, doc.Structure.ElementFromParentTree(content)));
    }

    [Fact]
    public void StreamMappingDoesNotRequirePageDiscoveryOrDecodeStreamPayload()
    {
        var doc = Load(
            (6, "<< /S /Document /K [7 0 R 8 0 R 12 0 R] >>"),
            (9, ObjPdf.Stream("", $"{Appearance} /Filter /Unsupported /F (https://invalid.example/never-load)")));
        var content = Content(doc);
        Assert.Null(content.PageIndex);
        Assert.Same(content.Parent, doc.Structure!.ElementFromParentTree(content));
        Assert.Same(content, doc.Structure.FindMarkedContent(Stream(doc), 0));
        Assert.Null(content.Parent!.PageIndex);
    }

    [Theory]
    [InlineData("/StmOwn 10 0 R")]
    [InlineData("/Stm 9 0 R /StmOwn null")]
    [InlineData("/Stm 9 0 R /StmOwn << /N 9 0 R >>")]
    [InlineData("/Stm 9 0 R /StmOwn 99 0 R")]
    [InlineData("/Stm 9 0 R /StmOwn 14 0 R")]
    [InlineData("/Stm 99 0 R /StmOwn 10 0 R")]
    [InlineData("/Stm 10 0 R /StmOwn 9 0 R")]
    [InlineData("/Stm << /Type /XObject >> /StmOwn 10 0 R")]
    public void MalformedOwnerOrStreamCannotAliasPageMcidZero(string entries)
    {
        var doc = Load((8, $"<< /S /Figure /K << /Type /MCR /MCID 0 {entries} >> >>"));
        var tree = doc.Structure!;
        Assert.Empty(tree.ElementFor(Assert.IsType<PdfDictionary>(doc.File.GetObject(8)))!.MarkedContent);
        Assert.Null(tree.FindMarkedContent(Stream(doc), 0));
        Assert.Equal("Page", StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("/StructParents 0.5")]
    [InlineData("/StructParents -1")]
    [InlineData("/StructParents 2147483648")]
    [InlineData("/StructParents 1")]
    public void InvalidOrNonArrayStreamParentTreeDoesNotFallBackToPageOrOwner(string key)
    {
        var doc = Load((9, ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] " + key)));
        Assert.NotNull(Content(doc).StreamOwner);
        Assert.Null(doc.Structure!.ElementFromParentTree(Content(doc)));
    }
}
