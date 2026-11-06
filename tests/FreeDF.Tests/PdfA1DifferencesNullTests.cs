using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfA1DifferencesNullTests
{
    public static IEnumerable<object[]> Values()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var encoding in new[] { "WinAnsiEncoding", "MacRomanEncoding", "StandardEncoding" })
                foreach (var indirectEncoding in new[] { false, true })
                    foreach (var value in new[] { "", "null", "8 0 R", "[]", "[65 /A]" })
                        yield return [profile, encoding, indirectEncoding, value,
                            encoding == "StandardEncoding" || profile == PdfValidationProfile.PdfA1b && value is ("[]" or "[65 /A]")];
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void NullDifferencesAreOmittedWithoutWaivingBaseEncodingOrNonNullA1Restrictions(
        PdfValidationProfile profile, string encoding, bool indirectEncoding, string value, bool rejected)
    {
        var differences = value.Length == 0 ? "" : "/Differences " + value;
        var dictionary = $"<< /BaseEncoding /{encoding} {differences} >>";
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F 5 0 R >> >> /Contents 4 0 R >>",
            ObjPdf.Stream("BT /F 12 Tf (A) Tj ET"),
            $"<< /Type /Font /Subtype /TrueType /BaseFont /TestFont /FirstChar 65 /LastChar 65 /Widths [500] /Encoding {(indirectEncoding ? "9 0 R" : dictionary)} /FontDescriptor 6 0 R >>",
            "<< /Type /FontDescriptor /FontName /TestFont /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 7 0 R >>",
            ObjPdf.Stream(""),
            "null", dictionary));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(rejected, report.Has("PDFA-FONT-TRUETYPE-ENCODING"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FONT-TRUETYPE-ENCODING"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.3.7",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.2.11.6",
                _ => "ISO 19005-3 6.2.11.6",
            }, finding.Clause);
        });
    }
}
