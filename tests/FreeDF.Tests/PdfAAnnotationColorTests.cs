using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationColorTests
{
    public static IEnumerable<object[]> Colors()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var space in new[] { "", "RGB ", "CMYK", "GRAY" })
                foreach (var key in new[] { "C", "IC" })
                    foreach (var (value, array) in new (string, bool)[]
                    {
                        ("", false), ("null", false), ("[]", true), ("[1 0 0]", true),
                        ("[0]", true), ("8 0 R", true), ("9 0 R", false), ("1", false),
                    })
                        yield return [profile, space, key, value, array && space != "RGB " && profile == PdfValidationProfile.PdfA1b];
    }

    [Theory]
    [MemberData(nameof(Colors))]
    public void PdfA1AnnotationColorArraysRequireRgbOutputProfile(
        PdfValidationProfile profile, string space, string key, string value, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R {(space.Length > 0 ? "/OutputIntents [<< /S /GTS_PDFA1 /DestOutputProfile 7 0 R >>]" : "")} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] /Resources << /ColorSpace << /DefaultRGB [/CalRGB << /WhitePoint [1 1 1] >>] >> >> >>",
            ObjPdf.Stream(""),
            $"<< /Subtype /Square /F 4 /Rect [10 10 20 20] /AP << /N 6 0 R >> {(value.Length > 0 ? "/" + key + " " + value : "")} >>",
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            ObjPdf.Stream(PdfADeviceColorTests.Icc(space.Length > 0 ? space : "RGB "), "/N 3"),
            "[1 0 0]", "null"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA1-ANNOT-COLORSPACE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA1-ANNOT-COLORSPACE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal("ISO 19005-1 6.5.3", finding.Clause);
        });
    }
}
