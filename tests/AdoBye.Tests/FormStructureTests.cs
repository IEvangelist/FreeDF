using PortableDocument.Core;
using PortableDocument.Content;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

public class FormStructureTests
{
    const string Font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";
    const string Form = "/Type /XObject /Subtype /Form /BBox [0 0 200 200]";

    static string Text(string value) => $"BT /F1 12 Tf 10 100 Td ({value}) Tj ET";
    static string Marked(string value, int mcid = 0) => $"/Span <</MCID {mcid}>> BDC {Text(value)} EMC";
    static string Mcr(int stream, string mcid = "0") => $"<< /Type /MCR /Stm {stream} 0 R /MCID {mcid} >>";

    static PdfDocument Load(params (int Number, string Body)[] replacements)
    {
        var bodies = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 16 0 R] /Count 2 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /StructParents 0 /Resources << {Font} /XObject << /Outer 11 0 R /Alias 11 0 R /Figure 14 0 R >> >> >>",
            ObjPdf.Stream(Marked("Page") + " /Outer Do /Alias Do /Figure Do"),
            "<< /Type /StructTreeRoot /K 6 0 R /ParentTree 13 0 R >>",
            "<< /S /Document /Pg 3 0 R /K [9 0 R 8 0 R 7 0 R 10 0 R] >>",
            "<< /S /P /P 6 0 R /K 0 >>",
            $"<< /S /P /P 6 0 R /K {Mcr(11)} >>",
            $"<< /S /P /P 6 0 R /K {Mcr(12)} >>",
            $"<< /S /Figure /P 6 0 R /Alt (Diagram) /K {Mcr(14)} >>",
            ObjPdf.Stream(Marked("Outer") + " /Inner Do /Inner Do", $"{Form} /StructParents 1 /Resources << {Font} /XObject << /Inner 12 0 R /Self 11 0 R >> >>"),
            ObjPdf.Stream(Marked("Inner"), $"{Form} /StructParents 2 /Resources << {Font} >>"),
            "<< /Nums [0 [7 0 R] 1 [8 0 R] 2 [9 0 R] 3 [10 0 R]] >>",
            ObjPdf.Stream("/Figure <</MCID 0>> BDC 0 0 10 10 re f EMC", $"{Form} /StructParents 3"),
            "<< /Nums [] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 17 0 R /Resources << /XObject << /Outer 11 0 R >> >> >>",
            ObjPdf.Stream("/Outer Do"),
        };
        foreach (var (number, body) in replacements) bodies[number - 1] = body;
        return PdfDocument.Load(ObjPdf.Build(bodies.ToArray()));
    }

    static PdfStream Stream(PdfDocument doc, int number) => Assert.IsType<PdfStream>(doc.File.GetObject(number));

    [Fact]
    public void PageAndNestedFormMcidsHaveIndependentStructureAndParentTreeMappings()
    {
        var doc = Load();
        var tree = doc.Structure!;
        var page = tree.FindMarkedContent(0, 0)!;
        var outer = tree.FindMarkedContent(Stream(doc, 11), 0)!;
        var inner = tree.FindMarkedContent(Stream(doc, 12), 0)!;
        Assert.Null(page.Stream);
        Assert.Same(Stream(doc, 11), outer.Stream);
        Assert.Same(Stream(doc, 12), inner.Stream);
        Assert.Equal(0, inner.PageIndex);
        Assert.Same(page.Parent, tree.ElementFromParentTree(0, 0));
        Assert.Same(outer.Parent, tree.ElementFromParentTree(outer.Stream!, 0));
        Assert.Same(inner.Parent, tree.ElementFromParentTree(inner.Stream!, 0));
        Assert.NotSame(page.Parent, inner.Parent);
        Assert.Null(tree.FindMarkedContent(1, 0));
        Assert.Null(tree.FindMarkedContent(inner.Stream!, 1));
        Assert.Null(tree.ElementFromParentTree(inner.Stream!, -1));
    }

    [Fact]
    public void StructureOrderIsStreamScopedAndReusedFormsContributeOnce()
    {
        var doc = Load();
        Assert.Equal("Inner\nOuter\nPage", StructuredTextExtractor.Extract(doc));
        var extractor = new StructuredTextExtractor(doc);
        Assert.Equal("Page", extractor.GetMarkedContentText(0, 0));
        Assert.Equal("Outer", extractor.GetMarkedContentText(0, Stream(doc, 11), 0));
        Assert.Equal("Inner", extractor.GetMarkedContentText(0, Stream(doc, 12), 0));
        Assert.Equal("Inner", extractor.GetMarkedContentText(1, Stream(doc, 12), 0));
        Assert.Null(extractor.GetMarkedContentText(1, 0));
        Assert.Null(extractor.GetMarkedContentText(-1, Stream(doc, 12), 0));
        Assert.Equal(4, DocumentRenderer.ExtractText(doc.Pages[0]).Count(s => s.Text == "Inner"));
    }

    [Fact]
    public void NonAppearanceFormOwnerDoesNotSuppressStreamText()
    {
        var doc = Load(
            (8, "<< /S /P /K << /Type /MCR /Stm 11 0 R /StmOwn 3 0 R /MCID 0 >> >>"),
            (9, "<< /S /P /K << /Type /MCR /Stm 12 0 R /StmOwn 11 0 R /MCID 0 >> >>"));
        Assert.Equal("Inner\nOuter\nPage", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void MultipleTextOperatorsInOneFormSequenceAreNotDeduplicated()
    {
        var doc = Load((12, ObjPdf.Stream(
            $"/Span <</MCID 0>> BDC {Text("Same")} {Text("Same")} EMC",
            $"{Form} /StructParents 2 /Resources << {Font} >>")));
        Assert.Equal("SameSame\nOuter\nPage", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void AltAndActualTextApplyToFormElementsWithoutDuplicatingPlacements()
    {
        var doc = Load();
        Assert.Equal("Inner\nOuter\nPage\nDiagram", StructuredTextExtractor.Extract(doc, includeAlt: true));
        Assert.Equal("Diagram", new StructuredTextExtractor(doc) { IncludeAlt = true }.GetText(doc.Structure!.ElementFromParentTree(Stream(doc, 14), 0)!));

        doc = Load((9, $"<< /S /P /ActualText (Replacement) /Alt (Not used) /K {Mcr(12)} >>"));
        Assert.Equal("Replacement\nOuter\nPage\nDiagram", StructuredTextExtractor.Extract(doc, includeAlt: true));
    }

    [Fact]
    public void McrPageOverridesInheritedPageForReusedForm()
    {
        var doc = Load((9, "<< /S /P /K << /Type /MCR /Stm 12 0 R /MCID 0 /Pg 16 0 R >> >>"));
        Assert.Equal(1, doc.Structure!.FindMarkedContent(Stream(doc, 12), 0)!.PageIndex);
        Assert.Equal("Inner\nOuter\nPage", StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Stm 12 0 R")]
    [InlineData("/MCID -1")]
    [InlineData("/MCID 0.5")]
    [InlineData("/MCID 2147483648")]
    [InlineData("/MCID /Zero")]
    [InlineData("/MCID 0 /Stm 15 0 R")]
    [InlineData("/MCID 0 /Stm null")]
    [InlineData("/MCID 0 /Stm 99 0 R")]
    public void MalformedFormReferencesDoNotBecomePageMcidZero(string entries)
    {
        var doc = Load((9, $"<< /S /P /K << /Type /MCR {entries} >> >>"));
        var element = doc.Structure!.Elements().First(e => ReferenceEquals(e.Dict, doc.File.GetObject(9)));
        Assert.Empty(element.MarkedContent);
        Assert.Equal("Outer\nPage", StructuredTextExtractor.Extract(doc));
        Assert.Equal("Page", new StructuredTextExtractor(doc).GetMarkedContentText(0, 0));
    }

    [Fact]
    public void ParentTreeNullHolesAndInvalidKeysDoNotAliasZero()
    {
        var doc = Load(
            (12, ObjPdf.Stream(Marked("Inner", 2), $"{Form} /StructParents 2 /Resources << {Font} >>")),
            (9, $"<< /S /P /K {Mcr(12, "2")} >>"),
            (13, "<< /Nums [0 [7 0 R] 1 [8 0 R] 2 [null null 9 0 R]] >>"));
        Assert.Null(doc.Structure!.ElementFromParentTree(Stream(doc, 12), 0));
        Assert.Null(doc.Structure.ElementFromParentTree(Stream(doc, 12), 1));
        Assert.Same(doc.Structure.FindMarkedContent(Stream(doc, 12), 2)!.Parent, doc.Structure.ElementFromParentTree(Stream(doc, 12), 2));
        Assert.Equal("Inner\nOuter\nPage", StructuredTextExtractor.Extract(doc));

        doc = Load((12, ObjPdf.Stream(Marked("Inner"), $"{Form} /StructParents 0.5 /Resources << {Font} >>")));
        Assert.Null(doc.Structure!.ElementFromParentTree(Stream(doc, 12), 0));
    }

    [Fact]
    public void CyclicStructureParentTreeAndFormResourcesTerminate()
    {
        var doc = Load(
            (8, $"<< /S /P /K [{Mcr(11)} 6 0 R 8 0 R] >>"),
            (11, ObjPdf.Stream(Marked("Outer") + " /Self Do /Inner Do", $"{Form} /StructParents 1 /Resources << {Font} /XObject << /Inner 12 0 R /Self 11 0 R >> >>")),
            (13, "<< /Kids [13 0 R 15 0 R] >>"),
            (15, "<< /Nums [1 [8 0 R]] >>"));
        Assert.Equal(5, doc.Structure!.Elements().Count());
        Assert.False(doc.Structure.Truncated);
        Assert.Same(doc.Structure.FindMarkedContent(Stream(doc, 11), 0)!.Parent, doc.Structure.ElementFromParentTree(Stream(doc, 11), 0));
        Assert.Null(doc.Structure.ParentTreeLookup(999));
        Assert.Equal("Inner\nOuter\nPage", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void UntaggedFormInsidePageSequenceRetainsExistingPageContentBehavior()
    {
        var doc = Load(
            (4, ObjPdf.Stream($"/P <</MCID 0>> BDC {Text("Page")} /Figure Do EMC")),
            (10, "<< /S /Figure /Alt (Diagram) >>"),
            (14, ObjPdf.Stream(Text("Untagged form"), $"{Form} /Resources << {Font} >>")));
        Assert.Equal("PageUntagged form", new StructuredTextExtractor(doc).GetMarkedContentText(0, 0));
        Assert.Equal("PageUntagged form", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void StreamIdentityKeepsEqualMcidValuesAndTextSeparate()
    {
        var doc = Load(
            (12, ObjPdf.Stream(Marked("Outer"), $"{Form} /StructParents 2 /Resources << {Font} >>")));
        var tree = doc.Structure!;
        Assert.NotSame(tree.FindMarkedContent(Stream(doc, 11), 0), tree.FindMarkedContent(Stream(doc, 12), 0));
        Assert.Equal("Outer\nOuter\nPage", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void ExcessiveStructureDepthTruncatesSafely()
    {
        var bodies = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            "<< /Type /StructTreeRoot /K 5 0 R >>",
        };
        for (var i = 0; i < StructureTree.MaxDepth + 10; i++)
            bodies.Add($"<< /S /Div /K {i + 6} 0 R >>");
        var doc = PdfDocument.Load(ObjPdf.Build(bodies.ToArray()));
        Assert.True(doc.Structure!.Truncated);
        Assert.Equal(StructureTree.MaxDepth + 1, doc.Structure.Elements().Count());
        Assert.Equal("", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void UntaggedDescendantDoesNotInheritAncestorFormMcidOrAliasPage()
    {
        var doc = Load(
            (4, ObjPdf.Stream($"/P <</MCID 0>> BDC {Text("Page")} /Outer Do EMC")),
            (11, ObjPdf.Stream($"/Span <</MCID 0>> BDC {Text("Outer")} /Inner Do EMC",
                $"{Form} /StructParents 1 /Resources << {Font} /XObject << /Inner 12 0 R >> >>")),
            (12, ObjPdf.Stream(Text("Untagged"), $"{Form} /Resources << {Font} >>")));
        var spans = DocumentRenderer.ExtractText(doc.Pages[0]);
        Assert.All(spans.Where(s => s.Text == "Untagged"), s =>
        {
            Assert.Equal(12, s.Form);
            Assert.Equal(-1, s.Mcid);
            Assert.Equal(-1, s.EnclosingPageMcid);
        });
        var extractor = new StructuredTextExtractor(doc);
        Assert.Equal("Page", extractor.GetMarkedContentText(0, 0));
        Assert.Equal("Outer", extractor.GetMarkedContentText(0, Stream(doc, 11), 0));
        Assert.Null(extractor.GetMarkedContentText(0, Stream(doc, 12), 0));
        Assert.Equal("Outer\nPage", extractor.Extract());
    }

    [Theory]
    [InlineData("EMC")]
    [InlineData("/Span BMC")]
    [InlineData("/Span <</MCID 0>> BDC")]
    [InlineData("EMC /Span <</MCID 0>> BDC")]
    public void MalformedFormMarkedContentCannotConsumeOrLeakIntoCallerAndSiblings(string malformed)
    {
        var doc = Load(
            (4, ObjPdf.Stream($"/P <</MCID 0>> BDC {Text("Before")} /Figure Do {Text("After")} EMC /Outer Do {Text("Tail")}")),
            (14, ObjPdf.Stream(malformed + " " + Text("Form"), $"{Form} /Resources << {Font} >>")));
        var spans = DocumentRenderer.ExtractText(doc.Pages[0]);
        Assert.Equal(0, Assert.Single(spans, s => s.Text == "Before").Mcid);
        Assert.Equal(0, Assert.Single(spans, s => s.Text == "After").Mcid);
        Assert.Equal(-1, Assert.Single(spans, s => s.Text == "Tail").Mcid);
        Assert.All(spans.Where(s => s.Text == "Outer"), s => Assert.Equal(0, s.Mcid));
        Assert.All(spans.Where(s => s.Text == "Inner"), s => Assert.Equal(0, s.Mcid));
        Assert.Equal("Outer", new StructuredTextExtractor(doc).GetMarkedContentText(0, Stream(doc, 11), 0));
    }

    [Fact]
    public void FormMcidWithoutStructParentsDoesNotAliasPageMcid()
    {
        var doc = Load(
            (4, ObjPdf.Stream($"/P <</MCID 0>> BDC {Text("Page")} /Figure Do EMC")),
            (10, "<< /S /Figure /Alt (Diagram) >>"),
            (14, ObjPdf.Stream(Marked("Local form"), $"{Form} /Resources << {Font} >>")));
        var extractor = new StructuredTextExtractor(doc);
        Assert.Equal("Page", extractor.GetMarkedContentText(0, 0));
        Assert.Equal("Local form", extractor.GetMarkedContentText(0, Stream(doc, 14), 0));
        Assert.Equal("Page", extractor.Extract());
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public void InvalidFormMcidIsNotCoercedToPageOrFormZero(string mcid)
    {
        var doc = Load((12, ObjPdf.Stream(
            $"/Span <</MCID {mcid}>> BDC {Text("Invalid")} EMC",
            $"{Form} /StructParents 2 /Resources << {Font} >>")));
        Assert.All(DocumentRenderer.ExtractText(doc.Pages[0]).Where(s => s.Text == "Invalid"), s => Assert.Equal(-1, s.Mcid));
        Assert.Equal("Outer\nPage", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void UnclosedHiddenFormSequenceDoesNotHideCallerOrSibling()
    {
        var doc = Load(
            (1, "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /OCProperties << /D << /BaseState /OFF >> >> >>"),
            (4, ObjPdf.Stream($"/Figure Do {Marked("Page")} /Outer Do")),
            (14, ObjPdf.Stream("/OC << /Type /OCG /Name (Hidden) >> BDC " + Text("Hidden"), $"{Form} /Resources << {Font} >>")));
        var spans = DocumentRenderer.ExtractText(doc.Pages[0]);
        Assert.DoesNotContain(spans, s => s.Text == "Hidden");
        Assert.Contains(spans, s => s.Text == "Page");
        Assert.Contains(spans, s => s.Text == "Outer");
        Assert.Contains(spans, s => s.Text == "Inner");
        Assert.Equal("Inner\nOuter\nPage", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void DecodeFailureRestoresCallerStateAndAllowsSameFormToBeTriedAgain()
    {
        var doc = Load(
            (4, ObjPdf.Stream($"/P <</MCID 0>> BDC /Figure Do {Text("After")} /Figure Do {Text("Again")} EMC {Text("Tail")} /Outer Do")),
            (14, ObjPdf.Stream(Text("Recovered"), $"{Form} /Resources << {Font} >> /Filter /Unsupported")));
        var spans = DocumentRenderer.ExtractText(doc.Pages[0]);
        Assert.Equal(0, Assert.Single(spans, s => s.Text == "After").Mcid);
        Assert.Equal(0, Assert.Single(spans, s => s.Text == "Again").Mcid);
        Assert.Equal(-1, Assert.Single(spans, s => s.Text == "Tail").Mcid);
        Assert.Contains(spans, s => s.Form == 11 && s.Text == "Outer");
        Assert.Equal("AfterAgain", new StructuredTextExtractor(doc).GetMarkedContentText(0, 0));
        using var raster = new SoftwareRasterizer(1, 1);
        var interpreter = new global::PortableDocument.Content.ContentInterpreter(doc.File, raster)
            { TextSpans = [], DrawGraphics = false, DrawAnnotations = false };
        interpreter.Render(doc.Pages[0], Matrix.Identity);
        Assert.Contains("operator Do: NotSupportedException", interpreter.Warnings);
        Stream(doc, 14).Dict.Entries.Remove("Filter");
        interpreter.Render(doc.Pages[0], Matrix.Identity);
        Assert.Equal(2, interpreter.TextSpans.Count(s => s.Form == 14 && s.Text == "Recovered"));
    }

    [Theory]
    [InlineData("EMC")]
    [InlineData("/Span BMC")]
    [InlineData("/Span <</MCID 99>> BDC")]
    public void MalformedNestedFormDoesNotChangeParentFormSequence(string malformed)
    {
        var doc = Load(
            (11, ObjPdf.Stream($"/Span <</MCID 0>> BDC {Text("Before")} /Inner Do {Text("After")} EMC {Text("Tail")}",
                $"{Form} /StructParents 1 /Resources << {Font} /XObject << /Inner 12 0 R >> >>")),
            (12, ObjPdf.Stream(malformed + " " + Text("Child"), $"{Form} /StructParents 2 /Resources << {Font} >>")));
        var spans = DocumentRenderer.ExtractText(doc.Pages[0]);
        Assert.All(spans.Where(s => s.Form == 11 && s.Text is "Before" or "After"), s => Assert.Equal(0, s.Mcid));
        Assert.All(spans.Where(s => s.Form == 11 && s.Text == "Tail"), s => Assert.Equal(-1, s.Mcid));
        Assert.Equal("BeforeAfter", new StructuredTextExtractor(doc).GetMarkedContentText(0, Stream(doc, 11), 0));
        var warnings = DocumentRenderer.RenderPage(doc.Pages[0], 10).Warnings;
        Assert.Contains(malformed == "EMC" ? "unmatched EMC" : "unclosed marked content in form", warnings);
    }
}
