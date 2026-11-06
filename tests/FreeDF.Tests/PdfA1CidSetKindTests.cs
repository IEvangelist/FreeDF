using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfA1CidSetKindTests
{
    public static IEnumerable<object[]> Values()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subset in new[] { false, true })
                foreach (var embedded in new[] { false, true })
                    foreach (var value in new[] { "", "null", "0", "(cidset)", "/CIDSet", "[]", "<< >>", "9 0 R", "10 0 R" })
                        yield return [profile, subset, embedded, value,
                            profile == PdfValidationProfile.PdfA1b && subset && embedded && value != "10 0 R"];
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void EmbeddedSubsetCidSetPresenceRequiresAStreamButDoesNotVerifyItsListing(
        PdfValidationProfile profile, bool subset, bool embedded, string value, bool rejected)
    {
        var name = (subset ? "ABCDEF+" : "") + "TestFont";
        var entry = value.Length == 0 ? "" : "/CIDSet " + value;
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F 5 0 R >> >> /Contents 4 0 R >>",
            ObjPdf.Stream("BT /F 12 Tf <0001> Tj ET"),
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding /Identity-H /DescendantFonts [6 0 R] >>",
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /CIDToGIDMap /Identity /FontDescriptor 7 0 R >>",
            $"<< /Type /FontDescriptor /FontName /{name} /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 {(embedded ? "/FontFile2 8 0 R" : "")} {entry} >>",
            ObjPdf.Stream(""),
            "null",
            ObjPdf.Stream("")));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(rejected, report.Has("PDFA1-FONT-CIDSET"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA1-FONT-CIDSET"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal("ISO 19005-1 6.3.5", finding.Clause);
        });
    }
}
