using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfA1WidgetAppearancePresenceTests
{
    public static IEnumerable<object[]> Appearances()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var point in new[] { false, true })
                foreach (var inForm in new[] { false, true })
                    foreach (var (value, missing) in new (string, bool)[]
                    {
                        ("", true), ("null", true), ("1", true), ("7 0 R", true),
                        ("<< >>", false), ("<< /N 6 0 R >>", false), ("8 0 R", false),
                    })
                        yield return [profile, point, inForm, value, missing && profile == PdfValidationProfile.PdfA1b];
    }

    [Theory]
    [MemberData(nameof(Appearances))]
    public void PdfA1PageWidgetsRequireAppearancesEvenAtPointRectangles(
        PdfValidationProfile profile, bool point, bool inForm, string value, bool missing)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R {(inForm ? "/AcroForm << /Fields [5 0 R] >>" : "")} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Subtype /Widget /FT /Tx /T (field) /F 4 /Rect {(point ? "[10 10 10 10]" : "[10 10 20 20]")} {(value.Length > 0 ? "/AP " + value : "")} >>",
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            "null", "<< /N 6 0 R >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(missing, report.Has("PDFA1-FIELD-APPEARANCE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA1-FIELD-APPEARANCE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal("ISO 19005-1 6.9", finding.Clause);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void NonPageWidgetsDoNotExtendThisPageScopedPredicate(PdfValidationProfile profile)
    {
        foreach (var subtype in new[] { "Widget", "Square", "Link", "Popup" })
        {
            var doc = PdfDocument.Load(ObjPdf.Build(
                "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
                ObjPdf.Stream(""),
                $"<< /Subtype /{subtype} /FT /Tx /F 4 /Rect [10 10 20 20] >>"));
            var report = PdfValidator.Validate(doc, profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.False(report.Has("PDFA1-FIELD-APPEARANCE"));
        }
    }
}
