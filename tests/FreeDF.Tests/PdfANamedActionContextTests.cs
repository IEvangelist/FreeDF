using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfANamedActionContextTests
{
    public static IEnumerable<object[]> Contexts()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var context in new[] { "open", "link", "next", "unused", "extension", "unknown-event", "array-root" })
                foreach (var (value, rejected) in new (string, bool)[]
                {
                    ("", true), ("null", true), ("0", true), ("(NextPage)", true), ("/Print", true),
                    ("/NextPage", false), ("/PrevPage", false), ("/FirstPage", false), ("/LastPage", false),
                    ("7 0 R", false), ("8 0 R", true),
                })
                    yield return [profile, context, value, rejected && context is ("open" or "link" or "next")];
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void NamedRestrictionsApplyToActionsNotArbitraryDictionaries(
        PdfValidationProfile profile, string context, string value, bool rejected)
    {
        var catalog = context switch
        {
            "open" => "/OpenAction 5 0 R",
            "next" => "/OpenAction << /S /Named /N /NextPage /Next [5 0 R] >>",
            "extension" => "/FreeDFExtension 5 0 R",
            "array-root" => "/OpenAction [5 0 R]",
            _ => "",
        };
        var annotations = context is "link" or "unknown-event" ? "/Annots [6 0 R]" : "";
        var trigger = context == "unknown-event" ? "/AA << /Unknown 5 0 R >>" : "/A 5 0 R";
        var doc = PdfDocument.Load(ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R {annotations} >>",
            ObjPdf.Stream(""),
            $"<< /Type /Action /S /Named {(value.Length > 0 ? "/N " + value : "")} >>",
            $"<< /Subtype /Link /F 4 /Rect [10 10 20 20] {trigger} >>",
            "/NextPage", "null"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(rejected, report.Has("PDFA-ACTION-NAMED"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ACTION-NAMED"), finding =>
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
