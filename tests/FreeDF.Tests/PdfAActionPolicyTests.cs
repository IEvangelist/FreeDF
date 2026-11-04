using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAActionPolicyTests
{
    public static IEnumerable<object[]> Actions()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var type in new[] { "", "/Action", "null", "6 0 R", "/Other" })
                foreach (var indirect in new[] { false, true })
                    foreach (var (action, forbidden) in new[]
                    {
                        ("Hide", true), ("GoToE", profile == PdfValidationProfile.PdfA1b),
                        ("Launch", true), ("Sound", true), ("Movie", true), ("ResetForm", true),
                        ("ImportData", true), ("JavaScript", true), ("SetOCGState", true),
                        ("Rendition", true), ("Trans", true), ("GoTo3DView", true),
                        ("GoTo", false), ("GoToR", false), ("Thread", false), ("URI", false),
                        ("Named", false), ("SubmitForm", false),
                    })
                        yield return [profile, type, indirect, action, forbidden && type != "/Other"];
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void KnownActionKindsRespectPdfAPartAndOptionalType(PdfValidationProfile profile,
        string type, bool indirect, string action, bool forbidden)
    {
        var typeEntry = type.Length == 0 ? "" : "/Type " + type;
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /OpenAction 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
            ObjPdf.Stream(""),
            $"<< {typeEntry} /S {(indirect ? "7 0 R" : "/" + action)} >>",
            "null",
            "/" + action));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(forbidden, report.Has("PDFA-ACTION"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ACTION"), finding =>
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
}
