using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationFlagTests
{
    public static IEnumerable<object[]> Flags()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var (indirect, popup) in new[] { (false, false), (false, true), (true, false), (true, true) })
            {
                foreach (var extra in new[] { 0, 8 | 16 | 64 | 128 | 512 })
                    for (var mask = 0; mask < 32; mask++)
                    {
                        var flags = extra | (mask & 7) | ((mask & 8) << 2) | ((mask & 16) << 4);
                        yield return [profile, indirect, popup, flags.ToString(), (flags & 4) == 0,
                            (flags & (1 | 2 | 32 | (profile == PdfValidationProfile.PdfA1b ? 0 : 256))) != 0];
                    }
                foreach (var value in new[] { "", "null" })
                    yield return [profile, indirect, popup, value, profile == PdfValidationProfile.PdfA1b || !popup, false];
            }
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void FlagsFollowProfileSpecificPresenceAndVisibilityRequirements(PdfValidationProfile profile,
        bool indirect, bool popup, string value, bool printError, bool visibilityError)
    {
        var entry = value.Length == 0 ? "" : "/F " + (indirect ? "6 0 R" : value);
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Type /Annot /Subtype /{(popup ? "Popup" : "Text")} /Rect [10 10 20 20] /Contents (note) {entry} >>",
            value.Length == 0 ? "null" : value));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(printError, report.Has("PDFA-ANNOT-PRINT"));
        Assert.Equal(visibilityError, report.Has("PDFA-ANNOT-HIDDEN"));
        Assert.All(report.Findings.Where(f => f.RuleId is "PDFA-ANNOT-PRINT" or "PDFA-ANNOT-HIDDEN"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.5.3",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.3.2",
                _ => "ISO 19005-3 6.3.2",
            }, finding.Clause);
        });
    }
}
