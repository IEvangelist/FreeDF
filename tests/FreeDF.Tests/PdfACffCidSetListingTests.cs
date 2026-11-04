using PortableDocument.Document;
using static FreeDF.Tests.PdfAIdentityCidSetListingTests;

namespace FreeDF.Tests;

public class PdfACffCidSetListingTests
{
    public static IEnumerable<object[]> Listings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subset in new[] { false, true })
                foreach (var format in new[] { 0, 1, 2 })
                    foreach (var variant in new[] { "full", "zero-tail", "empty", "missing-zero", "missing-unused", "extra", "gid-bitmap" })
                        yield return [profile, subset, format, variant, subset && variant is not ("full" or "zero-tail")];
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void CffCharsetsListActualCidsNotGlyphIndices(
        PdfValidationProfile profile, bool subset, int format, string variant, bool rejected)
    {
        var bitmap = new byte[variant == "zero-tail" ? 8193 : 8192];
        foreach (var cid in new[] { 0, 1, 41, 65535 }) bitmap[cid / 8] |= (byte)(128 >> (cid % 8));
        if (variant == "empty") bitmap = [];
        if (variant == "missing-zero") bitmap[0] &= 127;
        if (variant == "missing-unused") bitmap[0] &= 191;
        if (variant == "extra") bitmap[0] |= 32;
        if (variant == "gid-bitmap") bitmap = [240];
        var report = PdfValidator.Validate(Document(CffProgram(format), bitmap, subset, cff: true), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.Equal(rejected, report.Has("PDFA-FONT-CIDSET-LIST"));
        AssertClauses(report, profile);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MalformedOrUnsupportedProgramsAreExplicitlyIncomplete(PdfValidationProfile profile)
    {
        var valid = CffProgram(0);
        var invalid = Enumerable.Range(0, valid.Length).Select(length => valid[..length]).ToList();
        invalid.Add(CffGlyphNamesTests.Build(2, [0, 255, 255]));
        invalid.Add(CffGlyphNamesTests.Build(2, [0, 0, 0], prefix: [173, 174, 139, 12, 30]));
        invalid.Add(CffGlyphNamesTests.Build(2, null, prefix: [173, 174, 139, 12, 30]));
        foreach (var data in invalid)
        {
            var report = PdfValidator.Validate(Document(data, [], cff: true), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
            Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
            AssertClauses(report, profile);
        }
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void NameKeyedProgramsUseSequentialGidsRegardlessOfCharsetSids(PdfValidationProfile profile)
    {
        foreach (var predefined in new[] { 0, 1, 2 })
            foreach (var subset in new[] { false, true })
                foreach (var (bitmap, rejected) in new (byte[], bool)[]
                {
                    ([240], false), ([240, 0], false), ([], true), ([112], true),
                    ([224], true), ([248], true), ([192, 0, 0, 0, 48], true),
                })
                {
                    var program = CffGlyphNamesTests.Build(4, null, predefined);
                    var report = PdfValidator.Validate(Document(program, bitmap, subset, cff: true), profile);
                    Assert.False(report.Has("VALIDATION-INTERNAL"));
                    Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
                    Assert.Equal(subset && rejected, report.Has("PDFA-FONT-CIDSET-LIST"));
                    AssertClauses(report, profile);
                }
    }

    [Theory]
    [InlineData(1048576, false)]
    [InlineData(1048577, true)]
    public void DecodedBitmapBoundIsExplicit(int length, bool incomplete)
    {
        var bitmap = new byte[length];
        foreach (var cid in new[] { 0, 1, 41, 65535 }) bitmap[cid / 8] |= (byte)(128 >> (cid % 8));
        var report = PdfValidator.Validate(Document(CffProgram(0), bitmap, cff: true), PdfValidationProfile.PdfA2b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
    }

    [Theory]
    [InlineData(16777216, false)]
    [InlineData(16777217, true)]
    public void DecodedProgramBoundIsExplicit(int length, bool incomplete)
    {
        var program = new byte[length];
        CffProgram(0).CopyTo(program, 0);
        var bitmap = new byte[8192];
        foreach (var cid in new[] { 0, 1, 41, 65535 }) bitmap[cid / 8] |= (byte)(128 >> (cid % 8));
        var report = PdfValidator.Validate(Document(program, bitmap, cff: true), PdfValidationProfile.PdfA3b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
    }

    internal static byte[] CffProgram(int format) => CffGlyphNamesTests.Build(4, format switch
    {
        0 => [0, 0, 1, 0, 41, 255, 255],
        1 => [1, 0, 1, 0, 0, 41, 0, 255, 255, 0],
        _ => [2, 0, 1, 0, 0, 0, 41, 0, 0, 255, 255, 0, 0],
    }, prefix: [173, 174, 139, 12, 30]);
}
