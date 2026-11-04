using PortableDocument.Document;
using static FreeDF.Tests.PdfAIdentityCidSetListingTests;

namespace FreeDF.Tests;

public class PdfAOpenTypeGlyfCidSetTests
{
    public static IEnumerable<object[]> Listings()
    {
        foreach (var row in PdfAIdentityCidSetListingTests.Listings())
            if ((PdfValidationProfile)row[0] != PdfValidationProfile.PdfA1b) yield return row;
    }

    public static IEnumerable<object[]> StreamListings()
    {
        foreach (var row in PdfAStreamCidSetListingTests.Listings())
            if ((PdfValidationProfile)row[0] != PdfValidationProfile.PdfA1b) yield return row;
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void IdentityMapRequiresCompleteOpenTypeGlyfCidSet(
        PdfValidationProfile profile, bool subset, bool indirectMap, byte[] bitmap, bool rejected)
    {
        var report = PdfValidator.Validate(Document(Program(10), bitmap, subset, indirectMap, openType: true), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.Equal(rejected, report.Has("PDFA-FONT-CIDSET-LIST"));
        AssertClauses(report, profile);
    }

    [Theory]
    [MemberData(nameof(StreamListings))]
    public void ExplicitMapsAssignCidsIncludingZeroGidAliases(
        PdfValidationProfile profile, bool subset, string variant, byte[] mapping, byte[] bitmap, bool rejected)
    {
        var report = PdfValidator.Validate(Document(Program(10), bitmap, subset, mapping: mapping, openType: true), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.True(rejected == report.Has("PDFA-FONT-CIDSET-LIST"), variant);
        AssertClauses(report, profile);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MalformedMaxpAndCffWrappersAreExplicitlyIncomplete(PdfValidationProfile profile)
    {
        var valid = Program(10);
        var invalid = Enumerable.Range(0, valid.Length).Select(length => valid[..length]).ToList();
        invalid.Add(Program(0));
        invalid.Add(PdfAOpenTypeCharSetTests.Wrap(CffGlyphNamesTests.Build(4, null)));
        foreach (var (offset, value) in new (int, byte)[]
        {
            (0, 79), (5, 0), (12, 0), (23, 0), (23, 255), (27, 31), (28, 1), (29, 0),
        })
        {
            var data = valid.ToArray();
            data[offset] = value;
            invalid.Add(data);
        }
        foreach (var data in invalid)
        {
            var report = PdfValidator.Validate(Document(data, [255, 192], openType: true), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
            Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
            AssertClauses(report, profile);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(131072, false)]
    [InlineData(131074, true)]
    [InlineData(-1, true)]
    public void MalformedMappingsRemainExplicitlyIncomplete(int length, bool incomplete)
    {
        var mapping = length == -1 ? new byte[] { 0, 0, 0, 10 } : new byte[length];
        var count = Math.Max(1, mapping.Length / 2);
        var bitmap = new byte[(count + 7) / 8];
        for (var cid = 0; cid < count; cid++) bitmap[cid / 8] |= (byte)(128 >> (cid % 8));
        var report = PdfValidator.Validate(Document(Program(10), bitmap, mapping: mapping, openType: true), PdfValidationProfile.PdfA2b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
    }

    [Theory]
    [InlineData(16777216, false)]
    [InlineData(16777217, true)]
    public void DecodedWrapperLimitIncludesAllTables(int length, bool incomplete)
    {
        var program = new byte[length];
        Program(10).CopyTo(program, 0);
        var report = PdfValidator.Validate(Document(program, [255, 192], openType: true), PdfValidationProfile.PdfA3b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
    }
}
