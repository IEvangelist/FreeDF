using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationAppearanceStreamTests
{
    public static IEnumerable<object[]> Streams()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subtype in new[] { "Square", "Link", "Popup", "Widget" })
                foreach (var indirect in new[] { false, true })
                    foreach (var (value, invalid) in new (string, bool)[]
                    {
                        ("<< /N 7 0 R >>", false), ("<< /N << /Off 7 0 R >> >>", true),
                        ("<< /N << >> >>", true), ("<< /N 1 >>", true), ("<< /N /Off >>", true),
                        ("<< /N [] >>", true), ("<< /N true >>", true), ("<< /N (normal) >>", true),
                        ("<< /N null >>", false), ("<< /N 8 0 R >>", false),
                        ("<< /N 7 0 R /R null >>", false), ("<< /N 1 /R null >>", true),
                        ("<< /N 1 /R 7 0 R >>", false), ("<< >>", false), ("null", false), ("7 0 R", false),
                        ("<< /N 9 0 R >>", true), ("<< /N 10 0 R >>", true), ("<< /N 11 0 R >>", true),
                        ("<< /N 12 0 R >>", true), ("<< /N 13 0 R >>", true), ("<< /N 14 0 R >>", true),
                    })
                        yield return [profile, subtype, indirect, value, invalid && subtype != "Widget"];
    }

    [Theory]
    [MemberData(nameof(Streams))]
    public void NonWidgetNormalAppearancesRequireStreams(
        PdfValidationProfile profile, string subtype, bool indirect, string value, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Type /Annot /Subtype /{subtype} /Rect [10 10 10 10] /F 4 /FT /Btn /AP {(indirect ? "6 0 R" : value)} >>",
            value,
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            "null", "1", "<< /Off 7 0 R >>", "/Off", "[]", "true", "(normal)"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA-ANNOT-APPEARANCE-STREAM"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ANNOT-APPEARANCE-STREAM"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.5.3",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.3.3",
                _ => "ISO 19005-3 6.3.3",
            }, finding.Clause);
        });
    }
}
