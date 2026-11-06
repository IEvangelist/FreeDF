using System.Text;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class DifferencesCmapTests
{
    public static IEnumerable<object[]> Encodings()
    {
        foreach (var profile in new[] { "A2", "A3", "UA1", "UA2" })
            foreach (var openType in new[] { false, true })
                foreach (var records in new[]
                {
                    new (ushort, ushort)[] { (1, 0) },
                    [(3, 1)], [(1, 0), (3, 1)],
                })
                    foreach (var differences in new[] { "absent", "null", "empty", "valid", "indirect-empty", "indirect-valid" })
                        yield return [profile, openType, records, differences,
                            differences is not ("absent" or "null") && !records.Contains(((ushort)3, (ushort)1))];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void DifferencesArraysRequireUnicode31EvenWhenEmpty(
        string profile, bool openType, (ushort, ushort)[] records, string differences, bool invalid)
    {
        var report = Validate(profile, Document(PdfASymbolicCmapTests.Program(records), openType, differences));
        var prefix = Prefix(profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(prefix + "-FONT-CMAP-INCOMPLETE"));
        Assert.False(report.Has(prefix + "-FONT-NONSYMBOLIC-CMAP"));
        Assert.False(report.Has(prefix + "-FONT-ENCODING-INCOMPLETE"));
        Assert.Equal(invalid, report.Has(prefix + "-FONT-DIFFERENCES-CMAP"));
        Assert.All(report.Findings.Where(f => f.RuleId == prefix + "-FONT-DIFFERENCES-CMAP"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                "A2" => "ISO 19005-2 6.2.11.6",
                "A3" => "ISO 19005-3 6.2.11.6",
                "UA1" => "ISO 14289-1 7.21.6",
                _ => "ISO 14289-2 8.4.5.7",
            }, finding.Clause);
        });
    }

    [Theory]
    [InlineData("A2", false)]
    [InlineData("A2", true)]
    [InlineData("A3", false)]
    [InlineData("A3", true)]
    [InlineData("UA1", false)]
    [InlineData("UA1", true)]
    [InlineData("UA2", false)]
    [InlineData("UA2", true)]
    public void UninspectableProgramsCannotInventMissingEncodingVerdicts(string profile, bool openType)
    {
        var report = Validate(profile, Document([], openType, "valid"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.True(report.Has(Prefix(profile) + "-FONT-CMAP-INCOMPLETE"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-DIFFERENCES-CMAP"));
    }

    [Theory]
    [InlineData("A2", false)]
    [InlineData("A2", true)]
    [InlineData("A3", false)]
    [InlineData("A3", true)]
    [InlineData("UA1", false)]
    [InlineData("UA1", true)]
    [InlineData("UA2", false)]
    [InlineData("UA2", true)]
    public void UnembeddedFontsHaveNoInventedProgramVerdicts(string profile, bool openType)
    {
        var report = Validate(profile, Document(null, openType, "valid"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-CMAP-INCOMPLETE"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-DIFFERENCES-CMAP"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PdfA1RetainsItsDifferencesBanRatherThanAddingNewCmapRequirements(bool openType)
    {
        var report = PdfValidator.Validate(Document([], openType, "empty"), PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.True(report.Has("PDFA-FONT-TRUETYPE-ENCODING"));
        Assert.False(report.Has("PDFA-FONT-DIFFERENCES-CMAP"));
        Assert.False(report.Has("PDFA-FONT-CMAP-INCOMPLETE"));
    }

    static PdfDocument Document(byte[]? program, bool openType, string differences)
    {
        var encoding = differences == "absent" ? "/WinAnsiEncoding" :
            "<< /BaseEncoding /WinAnsiEncoding /Differences " + (differences switch
            {
                "null" => "null",
                "empty" => "[]",
                "valid" => "[65 /A]",
                _ => "7 0 R",
            }) + " >>";
        return PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F 4 0 R >> >> /Contents 8 0 R >>",
            $"<< /Type /Font /Subtype /TrueType /BaseFont /Test /FirstChar 65 /LastChar 65 /Widths [600] /Encoding {encoding} /FontDescriptor 5 0 R >>",
            "<< /Type /FontDescriptor /FontName /Test /Flags 32 " + (program is null ? "" : $"/{(openType ? "FontFile3" : "FontFile2")} 6 0 R") + " >>",
            ObjPdf.Stream(Encoding.Latin1.GetString(program ?? []), openType ? "/Subtype /OpenType" : ""),
            differences == "indirect-empty" ? "[]" : "[65 /A]",
            ObjPdf.Stream("BT /F 12 Tf (A) Tj ET")));
    }

    static string Prefix(string profile) => profile.StartsWith("UA", StringComparison.Ordinal) ? "PDFUA" : "PDFA";

    static ValidationReport Validate(string profile, PdfDocument document) => profile switch
    {
        "A2" => PdfValidator.Validate(document, PdfValidationProfile.PdfA2b),
        "A3" => PdfValidator.Validate(document, PdfValidationProfile.PdfA3b),
        "UA1" => PdfValidator.ValidatePdfUA(document, 1),
        "UA2" => PdfValidator.ValidatePdfUA(document, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
