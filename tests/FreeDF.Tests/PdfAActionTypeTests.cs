using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAActionTypeTests
{
    public static IEnumerable<object[]> Types()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var root in new[] { "Link", "OpenAction", "Next" })
                foreach (var (subtype, permitted) in new (string, bool)[]
                {
                    ("", false), ("/S null", false), ("/S 1", false), ("/S /Unknown", false),
                    ("/S /SetState", false), ("/S /NOP", false), ("/S /GoToDp", false),
                    ("/S /GoTo", true), ("/S /GoToR", true), ("/S /Thread", true),
                    ("/S /URI", true), ("/S /Named", true), ("/S /SubmitForm", true),
                    ("/S /GoToE", profile != PdfValidationProfile.PdfA1b), ("/S /Hide", false),
                    ("/S 8 0 R", true), ("/S 9 0 R", false),
                })
                    yield return [profile, root, subtype, !permitted];
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void KnownActionContextsRequirePermittedSubtype(PdfValidationProfile profile, string root, string subtype, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R {(root == "OpenAction" ? "/OpenAction 6 0 R" : "")} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Subtype /Link /F 4 /Rect [10 10 20 20] {(root == "OpenAction" ? "" : "/A 6 0 R")} >>",
            root == "Next" ? "<< /S /GoTo /D [3 0 R /Fit] /Next 7 0 R >>" : $"<< {subtype} /D [3 0 R /Fit] /N /NextPage >>",
            $"<< {subtype} /D [3 0 R /Fit] /N /NextPage >>",
            "/URI", "null"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-ACTION-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-ACTION-TYPE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ACTION-TYPE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.6.1",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.5.1",
                _ => "ISO 19005-3 6.5.1",
            }, finding.Clause);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void DestinationArraysAndNonActionSNamesAreNotMisclassified(PdfValidationProfile profile)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /OpenAction [3 0 R /Fit] >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /AA << /D [3 0 R /Fit] >> /Annots [6 0 R] >>",
            ObjPdf.Stream(""),
            "<< /S /Unknown /Type /StructElem >>",
            "<< /Subtype /Link /F 4 /Rect [10 10 20 20] /AA << /D [3 0 R /Fit] >> >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("PDFA-ACTION-TYPE"));
        Assert.False(report.Has("PDFA-ACTION-INCOMPLETE"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void RecognizedAnnotationEventsStillValidateTheirActionTypes(PdfValidationProfile profile)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            "<< /Subtype /Link /F 4 /Rect [10 10 20 20] /AA << /E << >> >> >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.True(report.Has("PDFA-ACTION-TYPE"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void CyclicNextIsExplicitlyIncomplete(PdfValidationProfile profile)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /OpenAction 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
            ObjPdf.Stream(""),
            "<< /S /GoTo /D [3 0 R /Fit] /Next 5 0 R >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.True(report.Has("PDFA-ACTION-INCOMPLETE"));
        Assert.False(report.Has("PDFA-ACTION-TYPE"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void FindingBudgetReportsOverflowWithoutClaimingCompleteInspection(PdfValidationProfile profile)
    {
        foreach (var count in new[] { 1000, 1001 })
        {
            var children = string.Join(" ", Enumerable.Range(0, count).Select(_ => "<< /S /Unknown >>"));
            var doc = PdfDocument.Load(ObjPdf.Build(
                "<< /Type /Catalog /Pages 2 0 R /OpenAction 5 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
                ObjPdf.Stream(""),
                $"<< /S /GoTo /D [3 0 R /Fit] /Next [{children}] >>"));
            var report = PdfValidator.Validate(doc, profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.Equal(1000, report.Findings.Count(f => f.RuleId == "PDFA-ACTION-TYPE"));
            Assert.Equal(count > 1000, report.Has("PDFA-ACTION-INCOMPLETE"));
        }
    }
}
