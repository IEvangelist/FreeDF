using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaMediaClipTests
{
    static PdfDocument Load(string clip, string root = "7 0 R", string[]? extraObjects = null, int part = 1)
    {
        return PdfDocument.Load(ObjPdf.Build([
            $"<< /Type /Catalog /Version /2.0 /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 6 0 R /OpenAction << /S /Rendition /OP 0 /R {root} >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            ObjPdf.Stream(""),
            "<< /Type /StructTreeRoot /K 9 0 R >>",
            ObjPdf.Xmp($"<rdf:Description rdf:about='' xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/' xmlns:dc='http://purl.org/dc/elements/1.1/' pdfuaid:part='{part}' pdfuaid:rev='2024'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Doc</rdf:li></rdf:Alt></dc:title></rdf:Description>"),
            "<< /Type /Rendition /S /MR /C 8 0 R >>",
            $"<< /S /MCD /D << /Type /Filespec /F (https://never.invalid/media.mp4) >> {clip} >>",
            "<< /Type /StructElem /S /Document /NS 10 0 R /P 5 0 R >>",
            "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>",
            .. extraObjects ?? [],
        ]));
    }

    static ValidationReport Validate(PdfDocument doc, int part = 1)
    {
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"), string.Join("\n", report.Findings));
        return report;
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("/CT null", true)]
    [InlineData("/CT /video", true)]
    [InlineData("/CT 42", true)]
    [InlineData("/CT (video/mp4)", false)]
    [InlineData("/CT ()", false)]
    public void Ua1RequiresAStringContentType(string contentType, bool invalid)
    {
        var report = Validate(Load($"{contentType} /Alt [() (Video)]"));

        Assert.Equal(invalid, report.Has("PDFUA-MEDIA-CT"));
        Assert.False(report.Has("PDFUA-MEDIA-ALT"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("/Alt null", true)]
    [InlineData("/Alt (Video)", true)]
    [InlineData("/Alt [(en)]", true)]
    [InlineData("/Alt [(en) ()]", true)]
    [InlineData("/Alt [(en) 42]", true)]
    [InlineData("/Alt [/en (Video)]", true)]
    [InlineData("/Alt [(en) (Video) (fr)]", true)]
    [InlineData("/Alt [(en) (Video) (fr) ()]", true)]
    [InlineData("/Alt [() (Video)]", false)]
    [InlineData("/Alt [(en) (Video) (fr) (Film)]", false)]
    [InlineData("/Alt []", false)]
    public void AlternateDescriptionsAreMultilingualStringPairs(string alt, bool invalid)
    {
        var report = Validate(Load($"/CT (video/mp4) {alt}"));

        Assert.Equal(invalid, report.Has("PDFUA-MEDIA-ALT"));
        Assert.False(report.Has("PDFUA-MEDIA-CT"));
    }

    [Fact]
    public void IndirectContentTypeAndAlternateStringsAreResolvedLocally()
    {
        var report = Validate(Load("/CT 11 0 R /Alt 12 0 R", extraObjects: [
            "(video/mp4)", "[13 0 R 14 0 R]", "()", "(Video)",
        ]));

        Assert.True(report.IsCompliant);
    }

    [Fact]
    public void SelectorsAndClipSectionsReachTheDataWithoutRequiringSectionMetadata()
    {
        var report = Validate(Load("", root: "11 0 R", extraObjects: [
            "<< /S /SR /R [12 0 R 12 0 R] >>",
            "<< /S /MR /C 13 0 R >>",
            "<< /S /MCS /D 8 0 R >>",
        ]));

        Assert.Single(report.Findings, f => f.RuleId == "PDFUA-MEDIA-CT");
        Assert.Single(report.Findings, f => f.RuleId == "PDFUA-MEDIA-ALT");
        Assert.False(report.Has("PDFUA-MEDIA-INCOMPLETE"));
    }

    [Theory]
    [InlineData("<< /S /SR /R [11 0 R] >>", "11 0 R")]
    [InlineData("<< /S /MR /C 12 0 R >>", "<< /S /MCS /D 12 0 R >>")]
    [InlineData("12 0 R", "11 0 R")]
    public void CyclicRenditionsClipsAndReferencesReportIncomplete(string first, string second)
    {
        var report = Validate(Load("/CT (video/mp4) /Alt [() (Video)]", root: "11 0 R", extraObjects: [first, second]));

        Assert.Single(report.Findings, f => f.RuleId == "PDFUA-MEDIA-INCOMPLETE");
    }

    [Theory]
    [InlineData("<< /S 11 0 R /C 8 0 R >>", "11 0 R")]
    [InlineData("<< /S /MR /C << /S 11 0 R >> >>", "11 0 R")]
    public void CyclicSubtypeReferencesReportIncomplete(string root, string reference)
    {
        var report = Validate(Load("", root: root, extraObjects: [reference]));

        Assert.True(report.Has("PDFUA-MEDIA-INCOMPLETE"));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void SubtypeReferenceDepthBoundaryIsExplicit(int length, bool incomplete)
    {
        var objects = Enumerable.Range(11, length).Select(n => n == 10 + length ? "/MR" : $"{n + 1} 0 R").ToArray();
        var report = Validate(Load("/CT (video/mp4) /Alt [() (Video)]", root: "<< /S 11 0 R /C 8 0 R >>", extraObjects: objects));

        Assert.Equal(incomplete, report.Has("PDFUA-MEDIA-INCOMPLETE"));
    }

    [Fact]
    public void ExcessiveAlternateArrayReportsIncompleteRatherThanSuccess()
    {
        var pairs = string.Join(" ", Enumerable.Repeat("() (Video)", 4097));
        var report = Validate(Load($"/CT (video/mp4) /Alt [{pairs}]"));

        Assert.True(report.Has("PDFUA-MEDIA-INCOMPLETE"));
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void ClipDepthBoundaryIsExplicit(int length, bool incomplete)
    {
        var objects = Enumerable.Range(11, length).Select(n => n == 10 + length
            ? "<< /S /MCD /CT (video/mp4) /Alt [() (Video)] >>"
            : $"<< /S /MCS /D {n + 1} 0 R >>").ToArray();
        var report = Validate(Load("", root: "<< /S /MR /C 11 0 R >>", extraObjects: objects));

        Assert.Equal(incomplete, report.Has("PDFUA-MEDIA-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-MEDIA-CT"));
        Assert.False(report.Has("PDFUA-MEDIA-ALT"));
    }

    [Fact]
    public void SelectorWorkBoundaryIsExplicit()
    {
        var alternatives = string.Join(" ", Enumerable.Repeat("7 0 R", 4096));
        var report = Validate(Load("/CT (video/mp4) /Alt [() (Video)]", root: $"<< /S /SR /R [{alternatives}] >>"));

        Assert.True(report.Has("PDFUA-MEDIA-INCOMPLETE"));
    }

    [Fact]
    public void WrongContextDoesNotHideAReusedClipDictionary()
    {
        var report = Validate(Load("", root: "<< /S /SR /R [8 0 R 7 0 R] >>"));

        Assert.True(report.Has("PDFUA-MEDIA-CT"));
        Assert.True(report.Has("PDFUA-MEDIA-ALT"));
    }

    [Fact]
    public void Ua1MediaRulesDoNotLeakIntoUa2()
    {
        var report = Validate(Load("", part: 2), 2);

        Assert.DoesNotContain(report.Findings, f => f.RuleId.StartsWith("PDFUA-MEDIA-", StringComparison.Ordinal));
    }
}
