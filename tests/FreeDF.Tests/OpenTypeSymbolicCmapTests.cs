using PortableDocument.Core;
using PortableDocument.Document;
using static FreeDF.Tests.PdfASymbolicCmapTests;

namespace FreeDF.Tests;

public class OpenTypeSymbolicCmapTests
{
    [Fact]
    public void SharedProgramSelectionPreservesRawPriorityAndRequiresOpenTypeSubtype()
    {
        var doc = PdfDocument.Load(TestPdf.Build(""));
        var descriptor = new PdfDictionary();
        var raw = new PdfStream(new PdfDictionary(), []);
        var wrapper = new PdfStream(new PdfDictionary(), []);
        descriptor.Entries["FontFile3"] = wrapper;
        Assert.Null(FontChecks.TrueTypeProgram(doc.File, descriptor));
        wrapper.Dict.Entries["Subtype"] = new PdfName("Type1C");
        Assert.Null(FontChecks.TrueTypeProgram(doc.File, descriptor));
        wrapper.Dict.Entries["Subtype"] = new PdfName("OpenType");
        Assert.Same(wrapper, FontChecks.TrueTypeProgram(doc.File, descriptor));
        descriptor.Entries["FontFile2"] = raw;
        Assert.Same(raw, FontChecks.TrueTypeProgram(doc.File, descriptor));
        descriptor.Entries["FontFile2"] = PdfNull.Instance;
        Assert.Same(wrapper, FontChecks.TrueTypeProgram(doc.File, descriptor));
    }

    public static IEnumerable<object[]> Encodings()
    {
        foreach (var row in PdfASymbolicCmapTests.Encodings())
            if ((PdfValidationProfile)row[0] != PdfValidationProfile.PdfA1b)
                yield return [(PdfValidationProfile)row[0] == PdfValidationProfile.PdfA2b ? "A2" : "A3", row[1], row[2]];
        foreach (var row in PdfUaSymbolicCmapTests.Encodings())
            yield return [(int)row[0] == 1 ? "UA1" : "UA2", row[1], row[2]];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void OpenTypeSymbolicRecordsRespectProfileSpecificRequirements(
        string profile, (ushort, ushort)[] records, bool invalid)
    {
        var report = Validate(profile, Program(records));
        var prefix = Prefix(profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(prefix + "-FONT-CMAP-INCOMPLETE"));
        Assert.Equal(invalid, report.Has(prefix + "-FONT-SYMBOLIC-CMAP"));
        Assert.All(report.Findings.Where(f => f.RuleId == prefix + "-FONT-SYMBOLIC-CMAP"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
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
    [InlineData("A2")]
    [InlineData("A3")]
    [InlineData("UA1")]
    [InlineData("UA2")]
    public void MalformedHeadersAndFiltersAreExplicitlyIncomplete(string profile)
    {
        var valid = Program([(3, 0)]);
        var invalid = Enumerable.Range(0, valid.Length).Select(length => valid[..length]).ToList();
        foreach (var offset in new[] { 0, 4, 20, 24, 28, 30, 36 })
        {
            var data = valid.ToArray();
            data[offset] = 255;
            invalid.Add(data);
        }
        invalid.Add(PdfAOpenTypeCharSetTests.Wrap(CffGlyphNamesTests.Build(4, null)));
        foreach (var data in invalid) AssertIncomplete(Validate(profile, data), profile);
        AssertIncomplete(Validate(profile, valid, filter: "UnknownFilter"), profile);
    }

    [Theory]
    [InlineData("A2")]
    [InlineData("A3")]
    [InlineData("UA1")]
    [InlineData("UA2")]
    public void MissingCmapRemainsAnEncodingFailure(string profile)
    {
        var program = Program([(3, 0)]);
        program[12] = (byte)'x';
        var report = Validate(profile, program);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-CMAP-INCOMPLETE"));
        Assert.True(report.Has(Prefix(profile) + "-FONT-SYMBOLIC-CMAP"));
    }

    [Theory]
    [InlineData("A2")]
    [InlineData("A3")]
    [InlineData("UA1")]
    [InlineData("UA2")]
    public void UnembeddedFontsRemainOutsideTheCmapCheck(string profile)
    {
        var report = Validate(profile, null);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-CMAP-INCOMPLETE"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-SYMBOLIC-CMAP"));
    }

    static void AssertIncomplete(ValidationReport report, string profile)
    {
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has(Prefix(profile) + "-FONT-SYMBOLIC-CMAP"));
        Assert.True(report.Has(Prefix(profile) + "-FONT-CMAP-INCOMPLETE"));
        Assert.All(report.Findings.Where(f => f.RuleId == Prefix(profile) + "-FONT-CMAP-INCOMPLETE"),
            finding => Assert.Equal(ValidationSeverity.Error, finding.Severity));
    }

    static string Prefix(string profile) => profile.StartsWith("UA", StringComparison.Ordinal) ? "PDFUA" : "PDFA";

    static ValidationReport Validate(string profile, byte[]? program, bool symbolic = true, string? filter = null) => profile switch
    {
        "A2" => PdfASymbolicCmapTests.Validate(PdfValidationProfile.PdfA2b, program, symbolic, filter: filter, openType: true),
        "A3" => PdfASymbolicCmapTests.Validate(PdfValidationProfile.PdfA3b, program, symbolic, filter: filter, openType: true),
        "UA1" => PdfUaSymbolicCmapTests.Validate(1, program, symbolic, filter: filter, openType: true),
        "UA2" => PdfUaSymbolicCmapTests.Validate(2, program, symbolic, filter: filter, openType: true),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
