using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAActionEntryTests
{
    public static IEnumerable<object[]> Entries()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var scope in new[] { "Catalog", "Page", "Widget", "Link" })
                foreach (var key in new[] { "A", "AA" })
                    foreach (var (value, present) in new (string, bool)[]
                    {
                        ("", false), ("null", false), ("<< >>", true),
                        ("<< /S /GoTo /D [3 0 R /Fit] >>", true), ("1", true),
                        ("7 0 R", false), ("8 0 R", true),
                    })
                    {
                        var forbidden = scope switch
                        {
                            "Catalog" => key == "AA",
                            "Page" => key == "AA" && profile != PdfValidationProfile.PdfA1b,
                            "Widget" => true,
                            _ => false,
                        };
                        yield return [profile, scope, key, value, present && forbidden];
                    }
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void ActionEntryPolicyIsContextAndPartSpecific(
        PdfValidationProfile profile, string scope, string key, string value, bool invalid)
    {
        var entry = value.Length > 0 ? $"/{key} {value}" : "";
        var doc = PdfDocument.Load(ObjPdf.Build(
            $"<< /Type /Catalog /Pages 2 0 R {(scope == "Catalog" ? entry : "")} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] {(scope == "Page" ? entry : "")} >>",
            ObjPdf.Stream(""),
            $"<< /Subtype /{(scope == "Widget" ? "Widget" : "Link")} /FT /Tx /F 4 /Rect [10 10 20 20] /AP << /N 6 0 R >> {(scope is "Widget" or "Link" ? entry : "")} >>",
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            "null", "<< >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        var rule = scope switch { "Catalog" => "PDFA-CATALOG-AA", "Page" => "PDFA-PAGE-AA", _ => "PDFA-WIDGET-ACTIONS" };
        Assert.Equal(invalid, report.Has(rule));
        Assert.All(report.Findings.Where(f => f.RuleId == rule), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(scope == "Catalog" ? (int?)null : 0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => scope == "Widget" && key == "A" ? "ISO 19005-1 6.6.1" : "ISO 19005-1 6.6.2",
                PdfValidationProfile.PdfA2b => scope == "Widget" ? "ISO 19005-2 6.4.1" : "ISO 19005-2 6.5.2",
                _ => scope == "Widget" ? "ISO 19005-3 6.4.1" : "ISO 19005-3 6.5.2",
            }, finding.Clause);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b, false)]
    [InlineData(PdfValidationProfile.PdfA2b, true)]
    [InlineData(PdfValidationProfile.PdfA3b, true)]
    public void PagesWithoutAnnotationsStillReceiveActionChecks(PdfValidationProfile profile, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /AA << >> >>",
            ObjPdf.Stream("")));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA-PAGE-AA"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void BothWidgetEntriesRetainTheirOwnFindings(PdfValidationProfile profile)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            "<< /Subtype /Widget /FT /Tx /F 4 /Rect [10 10 20 20] /AP << /N 6 0 R >> /A << >> /AA << >> >>",
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]")));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        var findings = report.Findings.Where(f => f.RuleId == "PDFA-WIDGET-ACTIONS").ToArray();
        Assert.Equal(2, findings.Length);
        if (profile == PdfValidationProfile.PdfA1b)
            Assert.Equal(new[] { "ISO 19005-1 6.6.1", "ISO 19005-1 6.6.2" }, findings.Select(f => f.Clause).Order().ToArray());
    }
}
