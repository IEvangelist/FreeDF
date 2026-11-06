using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfACidToGidTests
{
    public static IEnumerable<object[]> Maps()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var embedded in new[] { false, true })
                foreach (var subtype in new[] { "CIDFontType0", "CIDFontType2" })
                    foreach (var mode in new[] { 0, 3 })
                        foreach (var (value, target, valid) in new (string, string, bool)[]
                        {
                            ("", "null", false),
                            ("null", "null", false),
                            ("/Identity", "null", true),
                            ("/Other", "null", false),
                            ("()", "null", false),
                            ("42", "null", false),
                            ("<< >>", "null", false),
                            ("[]", "null", false),
                            ("true", "null", false),
                            ("9 0 R", "/Identity", true),
                            ("9 0 R", "/Other", false),
                            ("9 0 R", "null", false),
                            ("9 0 R", ObjPdf.Stream("\0\0\0\u0001"), true),
                            ("9 0 R", "10 0 R", false),
                        })
                            yield return [profile, embedded, subtype, mode, value, target,
                                embedded && subtype == "CIDFontType2" && !valid];
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void EmbeddedType2RequiresIdentityOrStreamEvenForInvisibleText(PdfValidationProfile profile,
        bool embedded, string subtype, int mode, string value, string target, bool invalid)
    {
        var report = PdfValidator.Validate(Document(embedded, subtype, mode, value, target), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-CIDTOGID"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FONT-CIDTOGID"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile == PdfValidationProfile.PdfA2b
                ? "ISO 19005-2 6.2.11.3.2" : "ISO 19005-3 6.2.11.3.2", finding.Clause);
        });
    }

    internal static PdfDocument Document(bool embedded, string subtype, int mode, string value, string target) =>
        PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            ObjPdf.Stream($"BT /F1 12 Tf {mode} Tr <0001> Tj ET"),
            "<< /Type /Font /Subtype /Type0 /BaseFont /Foo /Encoding /Identity-H /DescendantFonts [6 0 R] >>",
            $"<< /Type /Font /Subtype /{subtype} /BaseFont /Foo /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 7 0 R "
                + (value.Length == 0 ? "" : "/CIDToGIDMap " + value) + " >>",
            "<< /Type /FontDescriptor /FontName /Foo /Flags 4 " + (embedded ? "/FontFile2 8 0 R" : "") + " >>",
            ObjPdf.Stream("synthetic font program"),
            target,
            "9 0 R"));
}
