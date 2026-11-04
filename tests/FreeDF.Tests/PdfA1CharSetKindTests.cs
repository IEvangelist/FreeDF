using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfA1CharSetKindTests
{
    public static IEnumerable<object[]> Values()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subset in new[] { false, true })
                foreach (var embedded in new[] { false, true })
                    foreach (var value in new[] { "", "null", "0", "/CharSet", "[]", "<< >>", "8 0 R", "9 0 R", "()", "(/A)", "10 0 R" })
                        yield return [profile, subset, embedded, value,
                            profile == PdfValidationProfile.PdfA1b && subset && embedded && value is not ("()" or "(/A)" or "10 0 R")];
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void EmbeddedSubsetCharSetPresenceRequiresAStringButDoesNotVerifyItsListing(
        PdfValidationProfile profile, bool subset, bool embedded, string value, bool rejected)
    {
        var name = (subset ? "ABCDEF+" : "") + "TestFont";
        var entry = value.Length == 0 ? "" : "/CharSet " + value;
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F 5 0 R >> >> /Contents 4 0 R >>",
            ObjPdf.Stream("BT /F 12 Tf (A) Tj ET"),
            $"<< /Type /Font /Subtype /Type1 /BaseFont /{name} /Encoding /WinAnsiEncoding /FirstChar 65 /LastChar 65 /Widths [500] /FontDescriptor 6 0 R >>",
            $"<< /Type /FontDescriptor /FontName /{name} /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 {(embedded ? "/FontFile3 7 0 R" : "")} {entry} >>",
            ObjPdf.Stream("", "/Subtype /Type1C"),
            "null",
            ObjPdf.Stream("/A"),
            "(/A)"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(rejected, report.Has("PDFA1-FONT-CHARSET"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA1-FONT-CHARSET"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal("ISO 19005-1 6.3.5", finding.Clause);
        });
    }
}
