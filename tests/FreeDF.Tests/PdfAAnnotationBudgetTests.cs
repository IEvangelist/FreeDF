using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationBudgetTests
{
    public static IEnumerable<object[]> Counts()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var count in new[] { 0, 9999, 10000, 10001 })
                foreach (var lastInvalid in new[] { false, true })
                    yield return [profile, count, lastInvalid];
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void ReachingAnnotationBudgetCannotSilentlySkipRemainingEntries(
        PdfValidationProfile profile, int count, bool lastInvalid)
    {
        var annotations = string.Concat(Enumerable.Repeat("5 0 R ", Math.Max(0, count - 1)))
            + (count > 0 ? lastInvalid ? "6 0 R" : "5 0 R" : "");
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [{annotations}] >>",
            ObjPdf.Stream(""),
            "<< /Type /Annot /Subtype /Text /Rect [10 10 10 10] /F 4 >>",
            "<< /Type /Annot /Subtype /Unknown /Rect [10 10 10 10] /F 4 >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(count > 10000, report.Has("PDFA-ANNOT-INCOMPLETE"));
        Assert.Equal(lastInvalid && count is > 0 and <= 10000, report.Has("PDFA-ANNOT-TYPE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ANNOT-INCOMPLETE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Contains("10000", finding.Message);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b, 10000, false)]
    [InlineData(PdfValidationProfile.PdfA1b, 10001, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 10000, false)]
    [InlineData(PdfValidationProfile.PdfA2b, 10001, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 10000, false)]
    [InlineData(PdfValidationProfile.PdfA3b, 10001, true)]
    public void BudgetIsPerPageAndReportsTheAffectedPage(PdfValidationProfile profile,
        int secondPageCount, bool incomplete)
    {
        var first = string.Concat(Enumerable.Repeat("7 0 R ", 10000));
        var second = string.Concat(Enumerable.Repeat("7 0 R ", secondPageCount));
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 5 0 R /Annots [{first}] >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 6 0 R /Annots [{second}] >>",
            ObjPdf.Stream(""),
            ObjPdf.Stream(""),
            "<< /Type /Annot /Subtype /Text /Rect [10 10 10 10] /F 4 >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        var findings = report.Findings.Where(f => f.RuleId == "PDFA-ANNOT-INCOMPLETE").ToArray();
        Assert.Equal(incomplete ? 1 : 0, findings.Length);
        Assert.All(findings, finding => Assert.Equal(1, finding.PageIndex));
    }
}
