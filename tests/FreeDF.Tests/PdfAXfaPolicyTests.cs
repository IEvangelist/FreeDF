using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAXfaPolicyTests
{
    public static IEnumerable<object[]> Policy()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var key in new[] { "XFA", "NeedsRendering" })
                foreach (var indirectForm in new[] { false, true })
                    foreach (var (value, xfa, rendering) in new (string, bool, bool)[]
                    {
                        ("", false, false), ("null", false, false), ("false", true, false), ("true", true, true),
                        ("1", true, true), ("(inert)", true, true), ("[]", true, true), ("<< >>", true, true),
                        ("6 0 R", true, true), ("7 0 R", false, false), ("8 0 R", true, true),
                        ("9 0 R", true, false), ("10 0 R", true, true),
                    })
                        yield return [profile, key, indirectForm, value,
                            profile != PdfValidationProfile.PdfA1b && (key == "XFA" ? xfa : rendering)];
    }

    [Theory]
    [MemberData(nameof(Policy))]
    public void PdfA2And3RejectXfaAndRenderingRequirements(
        PdfValidationProfile profile, string key, bool indirectForm, string value, bool invalid)
    {
        var entry = value.Length > 0 ? $"/{key} {value}" : "";
        var form = $"<< /Fields [] {(key == "XFA" ? entry : "")} >>";
        var doc = PdfDocument.Load(ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm {(indirectForm ? "5 0 R" : form)} {(key == "NeedsRendering" ? entry : "")} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
            ObjPdf.Stream(""), form,
            ObjPdf.Stream("% inert"), "null", "true", "false", "[]"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        var rule = key == "XFA" ? "PDFA-FORM-XFA" : "PDFA-NEEDSRENDERING";
        Assert.Equal(invalid, report.Has(rule));
        Assert.All(report.Findings.Where(f => f.RuleId == rule), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile == PdfValidationProfile.PdfA2b ? "ISO 19005-2 6.4.2" : "ISO 19005-3 6.4.2", finding.Clause);
        });
    }
}
