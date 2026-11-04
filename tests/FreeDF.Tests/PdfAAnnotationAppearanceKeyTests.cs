using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationAppearanceKeyTests
{
    public static IEnumerable<object[]> Keys()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subtype in new[] { "Square", "Link", "Popup" })
                foreach (var indirect in new[] { false, true })
                    foreach (var (value, invalid) in new (string, bool)[]
                    {
                        ("null", false), ("(N)", false), ("7 0 R", false), ("<< >>", true),
                        ("<< /N 7 0 R >>", false), ("<< /R 7 0 R >>", true), ("<< /D 7 0 R >>", true),
                        ("<< /N 7 0 R /R 7 0 R >>", true), ("<< /N 7 0 R /D 7 0 R >>", true),
                        ("<< /N 7 0 R /Custom 1 >>", true), ("<< /N 7 0 R /R null >>", false),
                        ("<< /N 7 0 R /Custom null >>", false), ("<< /N 7 0 R /R 8 0 R /Custom 8 0 R >>", false),
                        ("<< /N null >>", true), ("<< /N 8 0 R >>", true), ("<< /R null >>", true),
                        ("<< /N 1 >>", false), ("<< /N << >> >>", false), ("<< /N 7 0 R /Type /Appearance >>", true),
                    })
                        yield return [profile, subtype, indirect, value, invalid];
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void AppearanceDictionariesContainExactlyOneEffectiveNormalKey(
        PdfValidationProfile profile, string subtype, bool indirect, string value, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Type /Annot /Subtype /{subtype} /Rect [10 10 20 20] /F 4 /AP {(indirect ? "6 0 R" : value)} >>",
            value,
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            "null"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA-ANNOT-APPEARANCE-KEYS"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ANNOT-APPEARANCE-KEYS"), finding =>
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
