using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAFieldAdditionalActionTests
{
    public static IEnumerable<object[]> Fields()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var position in new[] { 0, 1, 2 })
                foreach (var (value, invalid) in new (string, bool)[]
                {
                    ("", false), ("null", false), ("<< >>", true), ("1", true),
                    ("8 0 R", false), ("9 0 R", true), ("<< /K << /S /GoTo /D [3 0 R /Fit] >> >>", true),
                })
                    yield return [profile, position, value, invalid];
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void FieldAncestorsAndTerminalDictionariesCannotHaveAdditionalActions(
        PdfValidationProfile profile, int position, string value, bool invalid)
    {
        string Entry(int index) => position == index && value.Length > 0 ? $"/AA {value}" : "";
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
            ObjPdf.Stream(""),
            $"<< /T (root) /Kids [6 0 R] {Entry(0)} >>",
            $"<< /T (parent) /Parent 5 0 R /Kids [7 0 R] {Entry(1)} >>",
            $"<< /T (leaf) /Parent 6 0 R /FT /Tx {Entry(2)} >>",
            "null", "<< >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FIELD-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FIELD-AA"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FIELD-AA"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.6.2",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.4.1",
                _ => "ISO 19005-3 6.4.1",
            }, finding.Clause);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void DepthCyclesAndMalformedChildrenDoNotSilentlyPass(PdfValidationProfile profile)
    {
        foreach (var (depth, cycle, malformed, incomplete) in new[]
        {
            (64, false, false, false), (65, false, false, true),
            (1, true, false, true), (1, false, true, true),
        })
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
                ObjPdf.Stream(""),
            };
            for (var n = 0; n < depth; n++)
                objects.Add(n < depth - 1 ? $"<< /Kids [{n + 6} 0 R] >>"
                    : cycle ? "<< /Kids [5 0 R] >>" : malformed ? "<< /Kids [1] >>" : "<< /FT /Tx /AA << >> >>");
            var report = PdfValidator.Validate(PdfDocument.Load(ObjPdf.Build(objects.ToArray())), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.Equal(incomplete, report.Has("PDFA-FIELD-INCOMPLETE"));
            Assert.Equal(!incomplete, report.Has("PDFA-FIELD-AA"));
        }
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void WorkBoundIncludesRootEntries(PdfValidationProfile profile)
    {
        foreach (var count in new[] { 100000, 100001 })
        {
            var fields = string.Join(" ", Enumerable.Range(0, count).Select(_ => "<< /FT /Tx >>"));
            var doc = PdfDocument.Load(ObjPdf.Build(
                $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{fields}] >> >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
                ObjPdf.Stream("")));
            var report = PdfValidator.Validate(doc, profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.Equal(count > 100000, report.Has("PDFA-FIELD-INCOMPLETE"));
        }
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void FindingBoundIsReportedSeparatelyFromCompleteTraversal(PdfValidationProfile profile)
    {
        foreach (var count in new[] { 1000, 1001 })
        {
            var fields = string.Join(" ", Enumerable.Range(0, count).Select(_ => "<< /FT /Tx /AA << >> >>"));
            var doc = PdfDocument.Load(ObjPdf.Build(
                $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{fields}] >> >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
                ObjPdf.Stream("")));
            var report = PdfValidator.Validate(doc, profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.Equal(1000, report.Findings.Count(f => f.RuleId == "PDFA-FIELD-AA"));
            Assert.Equal(count > 1000, report.Has("PDFA-FIELD-INCOMPLETE"));
        }
    }
}
