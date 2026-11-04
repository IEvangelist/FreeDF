using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAExternalStreamTests
{
    public static IEnumerable<object[]> Entries()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var key in new[] { "F", "FFilter", "FDecodeParms" })
                foreach (var indirect in new[] { false, true })
                    foreach (var value in new[] { "null", "(external-stream-control.bin)", "/FlateDecode", "<< >>", "[]", "false", "0" })
                        yield return [profile, key, indirect, value];
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void OnlyNonNullExternalStreamEntriesAreForbidden(PdfValidationProfile profile,
        string key, bool indirect, string value)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
            ObjPdf.Stream("q Q", $"/{key} {(indirect ? "5 0 R" : value)}"),
            value));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(value != "null", report.Has("PDFA-STREAM-EXTERNAL"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-STREAM-EXTERNAL"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.1.7",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.1.7.1",
                _ => "ISO 19005-3 6.1.7.1",
            }, finding.Clause);
        });
    }
}
