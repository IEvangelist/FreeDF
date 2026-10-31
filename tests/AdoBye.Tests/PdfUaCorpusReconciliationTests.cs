using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

/// <summary>
/// Self-derived minimal fixtures for rules reconciled against veraPDF 1.30.3 over the veraPDF-corpus (PDF_UA-1). The corpus itself is not
/// committed; each test restates the behaviour veraPDF showed (rule ids in comments) with a hand-built file.
/// </summary>
public class PdfUaCorpusReconciliationTests
{
    const string Catalog = "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 6 0 R >>";

    static PdfDocument Load(int part, string catalog = Catalog, string page = "", string content = "", string document = "<< /Type /StructElem /S /Document /P 5 0 R /K 8 0 R >>",
        string element = "<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /K 0 >>", string root = "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R >>", string extra = "")
    {
        var bodies = new[]
        {
            catalog,
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /StructParents 0 /Resources << /XObject << /Fm0 11 0 R >> >> {page} >>",
            ObjPdf.Stream(content),
            root,
            ObjPdf.Xmp($"<rdf:Description rdf:about='' xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/' xmlns:dc='http://purl.org/dc/elements/1.1/' pdfuaid:part='{part}'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Doc</rdf:li></rdf:Alt></dc:title></rdf:Description>"),
            document,
            element,
            "<< /Type /Annot /Subtype /Stamp /Rect [0 0 10 10] >>",
            "<< /Nums [0 [8 0 R]] >>",
            ObjPdf.Stream("0 0 5 5 re f", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]" + extra),
        };
        return PdfDocument.Load(ObjPdf.Build(bodies));
    }

    static ValidationReport Validate(PdfDocument doc, int part = 1)
    {
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"), string.Join("\n", report.Findings));
        return report;
    }

    const string TaggedText = "/P <</MCID 0>> BDC BT 0 0 Td (x) Tj ET EMC";

    [Fact]
    public void BaselineFixtureIsErrorFree()
    {
        var report = Validate(Load(1, content: TaggedText));
        Assert.DoesNotContain(report.Errors, e => e.RuleId != "PDFUA-FONT-EMBEDDED");
    }

    [Theory]
    [InlineData("portugue-pt", false)]
    [InlineData("en-US", false)]
    [InlineData("toolongsubtag", true)]
    [InlineData("1x", true)]
    public void LanguageIdentifiersFollowRfc3066Shape(string lang, bool invalid)
    {
        var doc = Load(1, content: TaggedText, element: $"<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /K 0 /Lang ({lang}) >>");
        Assert.Equal(invalid, Validate(doc).Has("PDFUA-LANG-INVALID"));
    }

    [Fact]
    public void MissingCatalogLangOnlyFailsWhenTextLanguageIsNotDeterminable()
    {
        var noLang = Catalog.Replace("/Lang (en) ", "");
        // veraPDF PDFUA-1 7.2-34: text without an inherited /Lang fails; a /Lang on the structure element or the marked-content property list is enough.
        Assert.True(Validate(Load(1, noLang, content: TaggedText)).Has("PDFUA-LANG"));
        Assert.False(Validate(Load(1, noLang, content: TaggedText, element: "<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /K 0 /Lang (en) >>")).Has("PDFUA-LANG"));
        Assert.False(Validate(Load(1, noLang, content: "/P <</MCID 0>> BDC /Span <</Lang (en)>> BDC BT (x) Tj ET EMC EMC")).Has("PDFUA-LANG"));
        Assert.False(Validate(Load(1, noLang, content: "/P <</MCID 0>> BDC 0 0 5 5 re f EMC")).Has("PDFUA-LANG"));
        // UA-2 always requires the catalog entry.
        Assert.True(Validate(Load(2, noLang, content: TaggedText), 2).Has("PDFUA-LANG"));
    }

    [Fact]
    public void EmptyActualTextSatisfiesFigureAlternative()
    {
        // veraPDF PDFUA-1 7.3-1 tests ActualText != null, so an empty string counts.
        var figure = "<< /Type /StructElem /S /Figure /P 7 0 R /Pg 3 0 R /K 0 /ActualText () >>";
        Assert.False(Validate(Load(1, content: TaggedText, element: figure)).Has("PDFUA-ALT-MISSING"));
        Assert.True(Validate(Load(1, content: TaggedText, element: figure.Replace(" /ActualText ()", ""))).Has("PDFUA-ALT-MISSING"));
    }

    [Fact]
    public void RoleMapRulesOnlyApplyToUsedTypesAndUsePdf17Vocabulary()
    {
        // 7.1-7: only a used standard type that is remapped fails; unused remaps (here Artifact) pass.
        var unused = Load(1, content: TaggedText, root: "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R /RoleMap << /Artifact /P /Title /P >> >>");
        Assert.False(Validate(unused).Has("PDFUA-ROLEMAP-STANDARD"));
        var remapped = Load(1, content: TaggedText, root: "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R /RoleMap << /P /Span >> >>");
        Assert.True(Validate(remapped).Has("PDFUA-ROLEMAP-STANDARD"));
        // 7.1-6: a self mapping is circular. Title is not an ISO 32000-1 type, so it needs (and has) a mapping.
        var circular = Load(1, content: TaggedText, root: "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R /RoleMap << /P /P >> >>");
        Assert.True(Validate(circular).Has("PDFUA-ROLEMAP-CIRCULAR"));
        var title = "<< /Type /StructElem /S /Title /P 7 0 R /Pg 3 0 R /K 0 >>";
        Assert.True(Validate(Load(1, content: TaggedText, element: title)).Has("PDFUA-ROLEMAP-MISSING"));
        Assert.False(Validate(Load(1, content: TaggedText, element: title, root: "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R /RoleMap << /Title /P >> >>")).Has("PDFUA-ROLEMAP-MISSING"));
    }

    [Fact]
    public void ArtifactAndTaggedContentMustNotNest()
    {
        Assert.True(Validate(Load(1, content: "/P <</MCID 0>> BDC /Artifact BMC 0 0 5 5 re f EMC EMC")).Has("PDFUA-ARTIFACT-IN-TAGGED"));
        Assert.True(Validate(Load(1, content: "/Artifact BMC /P <</MCID 0>> BDC 0 0 5 5 re f EMC EMC")).Has("PDFUA-TAGGED-IN-ARTIFACT"));
    }

    [Fact]
    public void FormXObjectContentIsJudgedInItsInvocationContext()
    {
        // 7.20-t02 pass: invoking a Form is not itself untagged content; its operators inherit the invocation's tagging.
        Assert.False(Validate(Load(1, content: "/P <</MCID 0>> BDC /Fm0 Do EMC")).Has("PDFUA-CONTENT-UNTAGGED"));
        Assert.False(Validate(Load(1, content: "/Artifact BMC /Fm0 Do EMC /P <</MCID 0>> BDC EMC")).Has("PDFUA-CONTENT-UNTAGGED"));
        Assert.True(Validate(Load(1, content: "/Fm0 Do /P <</MCID 0>> BDC EMC")).Has("PDFUA-CONTENT-UNTAGGED"));
    }

    [Fact]
    public void ReferenceXObjectsAreRejectedInUa1()
    {
        Assert.True(Validate(Load(1, content: TaggedText, extra: " /Ref << /F (x.pdf) /Page 0 >>")).Has("PDFUA-XOBJECT-REF"));
    }
}
