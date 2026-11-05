using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaCidToGidTests
{
    public static IEnumerable<object[]> Maps()
    {
        foreach (var part in new[] { 1, 2 })
            foreach (var embedded in new[] { false, true })
                foreach (var subtype in new[] { "CIDFontType0", "CIDFontType2" })
                    foreach (var (value, target, valid) in new (string, string, bool)[]
                    {
                        ("", "null", false),
                        ("null", "null", false),
                        ("/Identity", "null", true),
                        ("/NoIdentity", "null", false),
                        ("()", "null", false),
                        ("42", "null", false),
                        ("<< >>", "null", false),
                        ("[]", "null", false),
                        ("true", "null", false),
                        ("9 0 R", "/Identity", true),
                        ("9 0 R", "/NoIdentity", false),
                        ("9 0 R", "null", false),
                        ("9 0 R", ObjPdf.Stream("\0\0\0\u0001"), true),
                        ("9 0 R", "10 0 R", false),
                    })
                        yield return [part, embedded, subtype, value, target, embedded && subtype == "CIDFontType2" && !valid];
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void EmbeddedType2RequiresIdentityOrStream(int part, bool embedded, string subtype, string value, string target, bool invalid)
    {
        var pdf = ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            ObjPdf.Stream("BT /F1 12 Tf <0001> Tj ET"),
            "<< /Type /Font /Subtype /Type0 /BaseFont /Foo /Encoding /Identity-H /DescendantFonts [6 0 R] >>",
            $"<< /Type /Font /Subtype /{subtype} /BaseFont /Foo /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 7 0 R "
                + (value.Length == 0 ? "" : "/CIDToGIDMap " + value) + " >>",
            "<< /Type /FontDescriptor /FontName /Foo /Flags 4 " + (embedded ? "/FontFile2 8 0 R" : "") + " >>",
            ObjPdf.Stream("synthetic font program"),
            target,
            "9 0 R");
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFUA-FONT-CIDTOGID"));
        foreach (var finding in report.Findings.Where(f => f.RuleId == "PDFUA-FONT-CIDTOGID"))
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(part == 1 ? "ISO 14289-1 7.21.3.2" : "ISO 14289-2 8.4.5.3.2", finding.Clause);
        }
    }
}
