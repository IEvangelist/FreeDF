using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfANeedAppearancesKindTests
{
    public static IEnumerable<object[]> Values()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
        {
            yield return [profile, "", false, false];
            foreach (var indirect in new[] { false, true })
                foreach (var (value, rejected) in new (string, bool)[]
                {
                    ("null", false), ("false", false), ("true", true),
                    ("0", true), ("1", true), ("/false", true),
                    ("(false)", true), ("[]", true), ("<< >>", true),
                })
                    yield return [profile, value, indirect, rejected];
        }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void NeedAppearancesMustResolveToAbsentNullOrBooleanFalse(
        PdfValidationProfile profile, string value, bool indirect, bool rejected)
    {
        var entry = value.Length == 0 ? "" : "/NeedAppearances " + (indirect ? "5 0 R" : value);
        var doc = PdfDocument.Load(ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] {entry} >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
            ObjPdf.Stream(""),
            value.Length == 0 ? "null" : value));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(rejected, report.Has("PDFA-FORM-NEEDAPPEARANCES"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FORM-NEEDAPPEARANCES"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.9",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.4.1",
                _ => "ISO 19005-3 6.4.1",
            }, finding.Clause);
        });
    }
}
