using PortableDocument.Document;
using static FreeDF.Tests.PdfASymbolicCmapTests;

namespace FreeDF.Tests;

public class NonSymbolicCmapTests
{
    public static IEnumerable<object[]> Encodings()
    {
        foreach (var profile in new[] { "A2", "A3", "UA1", "UA2" })
            foreach (var openType in new[] { false, true })
                foreach (var records in new[]
                {
                    Array.Empty<(ushort, ushort)>(), new (ushort, ushort)[] { (3, 0) },
                    [(1, 0)], [(3, 1)], [(3, 10)], [(3, 1), (3, 10)],
                    [(1, 0), (3, 1)], [(3, 0), (3, 1)],
                })
                    yield return [profile, openType, records, profile == "UA2"
                        ? !records.Contains(((ushort)3, (ushort)1)) && !records.Contains(((ushort)1, (ushort)0))
                        : records.Length == 0 || records.All(record => record == ((ushort)3, (ushort)0))];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void EmbeddedNonSymbolicProgramsRequireAppropriateRecords(
        string profile, bool openType, (ushort, ushort)[] records, bool invalid)
    {
        var report = Validate(profile, Program(records), openType);
        var prefix = Prefix(profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(prefix + "-FONT-CMAP-INCOMPLETE"));
        Assert.False(report.Has(prefix + "-FONT-SYMBOLIC-CMAP"));
        Assert.Equal(invalid, report.Has(prefix + "-FONT-NONSYMBOLIC-CMAP"));
        Assert.All(report.Findings.Where(f => f.RuleId == prefix + "-FONT-NONSYMBOLIC-CMAP"), finding =>
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
    public void UninspectableMetadataAndFiltersAreExplicitlyIncomplete(string profile, bool openType)
    {
        var valid = Program([(3, 1)]);
        foreach (var length in Enumerable.Range(0, valid.Length))
            AssertIncomplete(Validate(profile, valid[..length], openType), profile);
        AssertIncomplete(Validate(profile, valid, openType, filter: "UnknownFilter"), profile);
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
    public void UnembeddedFontsHaveNoInventedCmapVerdict(string profile, bool openType)
    {
        var report = Validate(profile, null, openType);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-CMAP-INCOMPLETE"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-NONSYMBOLIC-CMAP"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PdfA1HasNoNewNonSymbolicRecordRequirement(bool openType)
    {
        var report = PdfASymbolicCmapTests.Validate(PdfValidationProfile.PdfA1b, [], symbolic: false, openType: openType);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CMAP-INCOMPLETE"));
        Assert.False(report.Has("PDFA-FONT-NONSYMBOLIC-CMAP"));
    }

    static void AssertIncomplete(ValidationReport report, string profile)
    {
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-NONSYMBOLIC-CMAP"));
        Assert.True(report.Has(Prefix(profile) + "-FONT-CMAP-INCOMPLETE"));
    }

    static string Prefix(string profile) => profile.StartsWith("UA", StringComparison.Ordinal) ? "PDFUA" : "PDFA";

    static ValidationReport Validate(string profile, byte[]? program, bool openType, string? filter = null) => profile switch
    {
        "A2" => PdfASymbolicCmapTests.Validate(PdfValidationProfile.PdfA2b, program, symbolic: false, filter: filter, openType: openType),
        "A3" => PdfASymbolicCmapTests.Validate(PdfValidationProfile.PdfA3b, program, symbolic: false, filter: filter, openType: openType),
        "UA1" => PdfUaSymbolicCmapTests.Validate(1, program, symbolic: false, filter: filter, openType: openType),
        "UA2" => PdfUaSymbolicCmapTests.Validate(2, program, symbolic: false, filter: filter, openType: openType),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
