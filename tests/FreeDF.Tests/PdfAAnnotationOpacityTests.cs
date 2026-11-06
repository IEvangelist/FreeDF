using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationOpacityTests
{
    public static IEnumerable<object[]> Opacity()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subtype in new[] { "Square", "Link", "Popup", "Widget" })
                foreach (var (value, restricted) in new (string, bool)[]
                {
                    ("", false), ("null", false), ("1", false), ("1.0", false),
                    ("0", true), ("0.5", true), ("-1", true), ("2", true),
                    ("7 0 R", false), ("8 0 R", false), ("9 0 R", true),
                    ("/Opaque", false), ("(1)", false), ("[1]", false), ("<< >>", false), ("true", false),
                })
                    yield return [profile, subtype, value, restricted && profile == PdfValidationProfile.PdfA1b];
    }

    [Theory]
    [MemberData(nameof(Opacity))]
    public void PdfA1OptionalNumericOpacityMustBeOne(PdfValidationProfile profile, string subtype, string value, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Type /Annot /Subtype /{subtype} /Rect [10 10 20 20] /F 4 /FT /Tx /AP << /N 6 0 R >> {(value.Length > 0 ? "/CA " + value : "")} >>",
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            "null", "1", "0.5"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA1-ANNOT-OPACITY"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA1-ANNOT-OPACITY"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal("ISO 19005-1 6.5.3", finding.Clause);
        });
    }
}
