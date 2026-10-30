using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

namespace AdoBye.Tests;

public class ObjectReferenceTextTests
{
    const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [9 0 R] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>";
    const string Annotation = "<< /Type /Annot /Subtype /Stamp /Rect [0 0 100 100] /StructParent 1 /Contents (Not appearance text) /AP << /N 10 0 R >> >>";
    const string Appearance = "/Type /XObject /Subtype /Form /BBox [0 0 100 100]";
    const string Text = "BT /F1 12 Tf 10 80 Td (Appearance) Tj ET";
    const string Objr = "<< /Type /OBJR /Obj 9 0 R >>";

    static PdfDocument Load(params (int Number, string Body)[] replacements)
    {
        var bodies = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            Page,
            ObjPdf.Stream("BT /F1 12 Tf (Untagged page) Tj ET"),
            "<< /Type /StructTreeRoot /K 6 0 R /ParentTree 11 0 R >>",
            "<< /S /Document /Pg 3 0 R /K 7 0 R >>",
            $"<< /S /Annot /Alt (Description) /K {Objr} >>",
            "<< /S /P /Pg 3 0 R /K << /Type /MCR /Stm 10 0 R /MCID 0 >> >>",
            Annotation,
            ObjPdf.Stream(Text, Appearance),
            "<< /Nums [1 7 0 R 2 [8 0 R]] >>",
            ObjPdf.Stream("BT /F1 12 Tf (Inactive) Tj ET", Appearance),
        };
        foreach (var (number, body) in replacements)
        {
            while (bodies.Count < number) bodies.Add("null");
            bodies[number - 1] = body;
        }
        return PdfDocument.Load(ObjPdf.Build(bodies.ToArray()));
    }

    const string FormFont = "/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>";
    const string WholePage = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /StructParents 0 /Resources << /XObject << /Fm 13 0 R >> /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>";
    const string FormObjr = "<< /S /Figure /K << /Type /OBJR /Pg 3 0 R /Obj 13 0 R >> >>";

    static PdfDocument WholeForm(string formKeys = "/StructParent 3", params (int Number, string Body)[] more) => Load(
        [
            (3, WholePage),
            (4, ObjPdf.Stream("/Fm Do")),
            (7, FormObjr),
            (11, "<< /Nums [1 7 0 R 3 7 0 R] >>"),
            (13, ObjPdf.Stream("BT /F1 12 Tf 10 50 Td (Form text) Tj ET", Appearance + " " + formKeys + " " + FormFont)),
            .. more,
        ]);

    [Fact]
    public void NestedAppearanceFormsContributeThroughTheOuterAppearance()
    {
        var doc = Load(
            (10, ObjPdf.Stream(Text + " /Inner Do", Appearance + " /Resources << /XObject << /Inner 12 0 R >> /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>")),
            (12, ObjPdf.Stream("BT /F1 12 Tf 10 40 Td (Nested) Tj ET", Appearance + " /Resources << /XObject << /Outer 10 0 R >> /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>")));
        var extractor = new StructuredTextExtractor(doc);
        Assert.Equal("Appearance Nested", extractor.Extract());
        Assert.False(extractor.NestedFormsTruncated);
    }

    [Fact]
    public void NestedFormsStayUnattributedWhenExplicitlyTaggedByMcr()
    {
        var doc = Load(
            (6, "<< /S /Document /Pg 3 0 R /K [7 0 R 8 0 R] >>"),
            (8, "<< /S /P /Pg 3 0 R /K << /Type /MCR /Stm 12 0 R /MCID 0 >> >>"),
            (10, ObjPdf.Stream(Text + " /Inner Do", Appearance + " /Resources << /XObject << /Inner 12 0 R >> /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>")),
            (11, "<< /Nums [1 7 0 R 2 [8 0 R]] >>"),
            (12, ObjPdf.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 10 40 Td (Nested) Tj ET EMC", Appearance + " /StructParents 2 " + FormFont)));
        Assert.Equal("Appearance\nNested", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void WholeObjectFormXObjectContributesTextWhenStructParentMapsBack()
    {
        var doc = WholeForm();
        Assert.Equal("Form text", StructuredTextExtractor.Extract(doc));
        Assert.Equal("Form text", StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/StructParent 4")]
    [InlineData("/StructParent -3")]
    [InlineData("/StructParent 1")]
    public void WholeObjectFormXObjectNeedsAMatchingStructParent(string keys)
    {
        var doc = WholeForm(keys, (11, "<< /Nums [1 6 0 R 3 7 0 R] >>"));
        Assert.Equal("", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void WholeObjectFormInsideAnEnclosingPageMcrIsNotDuplicated()
    {
        var doc = WholeForm(more:
        [
            (4, ObjPdf.Stream("/Span <</MCID 0>> BDC /Fm Do EMC")),
            (6, "<< /S /Document /Pg 3 0 R /K [7 0 R 14 0 R] >>"),
            (11, "<< /Nums [0 [14 0 R] 3 7 0 R] >>"),
            (14, "<< /S /P /Pg 3 0 R /K 0 >>"),
        ]);
        Assert.Equal("Form text", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void ImageXObjectObjrNeverInfersText()
    {
        var doc = WholeForm(more: (13, ObjPdf.Stream("x", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /StructParent 3")));
        Assert.Equal("", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void WholeObjectUsesSelectedAppearanceWithoutChangingPlainExtraction()
    {
        var doc = Load();
        var extractor = new StructuredTextExtractor(doc);
        Assert.Equal("Appearance", extractor.Extract());
        Assert.Equal("Appearance", extractor.Extract());
        Assert.Equal("Appearance", extractor.GetText(doc.Structure!.ElementFor((PdfDictionary)doc.File.GetObject(7)!)!));
        Assert.Equal("Untagged page", string.Concat(global::PortableDocument.Render.PortableDocument.ExtractText(doc.Pages[0]).Select(span => span.Text)));
        Assert.DoesNotContain(global::PortableDocument.Render.PortableDocument.Analyze(doc.Pages[0]).Spans, span => span.Text == "Appearance");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/StructParents 2")]
    public void WholeObjectCanContainMcidTextWithoutAnExplicitMcr(string keys)
    {
        var doc = Load((10, ObjPdf.Stream("/Span <</MCID 0>> BDC " + Text + " EMC", Appearance + " " + keys)));
        Assert.Equal("Appearance", StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("[7 0 R 8 0 R]", "Whole\nAppearance")]
    [InlineData("[8 0 R 7 0 R]", "Appearance\nWhole")]
    public void ExplicitMcrRetainsItsTextWithoutWholeObjectDuplication(string kids, string expected)
    {
        var doc = Load(
            (6, $"<< /S /Document /Pg 3 0 R /K {kids} >>"),
            (10, ObjPdf.Stream("BT /F1 12 Tf (Whole) Tj ET /Span <</MCID 0>> BDC " + Text + " EMC", Appearance + " /StructParents 2")));
        Assert.Equal(expected, StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("/Artifact BMC BT /F1 12 Tf (Artifact) Tj ET EMC")]
    [InlineData("/Artifact << /Type /Pagination >> BDC /Span BMC BT /F1 12 Tf (Artifact) Tj ET EMC EMC")]
    public void ArtifactTextDoesNotBecomeWholeObjectText(string artifact)
    {
        Assert.Equal("Appearance", StructuredTextExtractor.Extract(Load((10, ObjPdf.Stream(artifact + " " + Text, Appearance)))));
    }

    [Fact]
    public void ActualTextAndOptInAltRetainPrecedence()
    {
        Assert.Equal("Replacement", StructuredTextExtractor.Extract(Load((7, $"<< /S /Annot /ActualText (Replacement) /Alt (Description) /K {Objr} >>")), includeAlt: true));
        var doc = Load((10, ObjPdf.Stream("", Appearance)));
        Assert.Equal("", StructuredTextExtractor.Extract(doc));
        Assert.Equal("Description", StructuredTextExtractor.Extract(doc, includeAlt: true));
    }

    [Fact]
    public void NamedStateUsesOnlyTheSelectedNormalAppearance()
    {
        var doc = Load((9, Annotation.Replace("/N 10 0 R", "/N << /On 10 0 R /Off 12 0 R >>").Replace("/Contents", "/AS /On /Contents")));
        Assert.Equal("Appearance", StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("/F 2")]
    [InlineData("/F 32")]
    [InlineData("/Subtype /Popup")]
    [InlineData("/StructParent -1")]
    [InlineData("/StructParent 1.5")]
    public void HiddenOrUnmappedAnnotationsCannotSupplyText(string entry)
    {
        var annotation = Annotation;
        if (entry.StartsWith("/Subtype")) annotation = annotation.Replace("/Subtype /Stamp", entry);
        else if (entry.StartsWith("/StructParent")) annotation = annotation.Replace("/StructParent 1", entry);
        else annotation = annotation.Replace("/Type /Annot", "/Type /Annot " + entry);
        Assert.Equal("", StructuredTextExtractor.Extract(Load((9, annotation))));
    }

    [Theory]
    [InlineData(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>")]
    [InlineData(9, "<< /Type /Annot /Subtype /FreeText /Rect [0 0 100 100] /StructParent 1 /Contents (Not synthesized) /DA (/F1 12 Tf) >>")]
    [InlineData(11, "<< /Nums [1 8 0 R] >>")]
    [InlineData(7, "<< /S /Annot /K << /Type /OBJR /Obj 12 0 R >> >>")]
    public void NonlocalMissingOrMismatchedReferencesDoNotInferText(int number, string body)
    {
        Assert.Equal("", StructuredTextExtractor.Extract(Load((number, body))));
    }

    [Fact]
    public void RepeatedReferencesContributeOncePerPage()
    {
        Assert.Equal("Appearance", StructuredTextExtractor.Extract(Load((7, $"<< /S /Annot /K [{Objr} {Objr}] >>"))));
    }

    [Fact]
    public void ExplicitObjrPageOverridesTheContainingElement()
    {
        var doc = Load(
            (6, "<< /S /Document /K 7 0 R >>"),
            (7, $"<< /S /Annot /K << /Type /OBJR /Pg 3 0 R /Obj 9 0 R >> >>"));
        Assert.Equal("Appearance", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void SeparatePageReferencesContributeOnEachRenderedPage()
    {
        var doc = Load(
            (2, "<< /Type /Pages /Kids [3 0 R 12 0 R] /Count 2 >>"),
            (7, "<< /S /Annot /K [<< /Type /OBJR /Pg 3 0 R /Obj 9 0 R >> << /Type /OBJR /Pg 12 0 R /Obj 9 0 R >>] >>"),
            (12, Page));
        Assert.Equal("Appearance Appearance", StructuredTextExtractor.Extract(doc));
    }
}
