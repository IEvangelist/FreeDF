using System.Buffers.Binary;
using PortableDocument.Document;
using static FreeDF.Tests.PdfAIdentityCidSetListingTests;

namespace FreeDF.Tests;

public class PdfAStreamCidSetListingTests
{
    public static IEnumerable<object[]> Listings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subset in new[] { false, true })
                foreach (var kind in new[] { "identity", "alias", "permutation", "sparse" })
                {
                    var entries = kind switch { "alias" => 12, "permutation" => 20, "sparse" => 2, _ => 10 };
                    var mapping = new byte[entries * 2];
                    for (var cid = 0; cid < Math.Min(entries, 10); cid++)
                        BinaryPrimitives.WriteUInt16BigEndian(mapping.AsSpan(cid * 2), (ushort)cid);
                    if (kind == "alias") mapping[23] = 1;
                    if (kind == "permutation") { mapping[3] = 0; mapping[39] = 1; }
                    var full = Bitmap(entries);
                    var withoutZero = full.ToArray();
                    withoutZero[0] &= 127;
                    var withoutUsed = full.ToArray();
                    withoutUsed[0] &= 191;
                    foreach (var (label, bitmap, rejected) in new (string, byte[], bool)[]
                    {
                        ("full", full, false), ("empty", [], true), ("missing-zero", withoutZero, true),
                        ("missing-used", withoutUsed, true), ("gid-bitmap", Bitmap(10), kind != "identity"),
                        ("extra", Bitmap(entries + 1), true),
                    })
                        yield return [profile, subset, kind + "-" + label, mapping, bitmap, subset && rejected];
                }
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void EveryExplicitMappingEntryDefinesACidIncludingZeroGidAliases(
        PdfValidationProfile profile, bool subset, string variant, byte[] mapping, byte[] bitmap, bool rejected)
    {
        var report = PdfValidator.Validate(Document(Program(10), bitmap, subset, mapping: mapping), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.True(rejected == report.Has("PDFA-FONT-CIDSET-LIST"), variant);
        AssertClauses(report, profile);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b, 1, true)]
    [InlineData(PdfValidationProfile.PdfA1b, 131072, false)]
    [InlineData(PdfValidationProfile.PdfA1b, 131074, true)]
    [InlineData(PdfValidationProfile.PdfA1b, -1, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 1, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 131072, false)]
    [InlineData(PdfValidationProfile.PdfA2b, 131074, true)]
    [InlineData(PdfValidationProfile.PdfA2b, -1, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 1, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 131072, false)]
    [InlineData(PdfValidationProfile.PdfA3b, 131074, true)]
    [InlineData(PdfValidationProfile.PdfA3b, -1, true)]
    public void MalformedOrOutOfRangeMappingsAreExplicitlyIncomplete(
        PdfValidationProfile profile, int length, bool incomplete)
    {
        var mapping = length == -1 ? new byte[] { 0, 0, 0, 10 } : new byte[length];
        var report = PdfValidator.Validate(Document(Program(10), Bitmap(Math.Max(1, mapping.Length / 2)), mapping: mapping), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        AssertClauses(report, profile);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void EmptyMappingStillRequiresTheMandatoryCidZero(PdfValidationProfile profile)
    {
        foreach (var (bitmap, rejected) in new (byte[], bool)[] { ([], true), ([128], false) })
        {
            var report = PdfValidator.Validate(Document(Program(10), bitmap, mapping: []), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
            Assert.Equal(rejected, report.Has("PDFA-FONT-CIDSET-LIST"));
        }
    }

    static byte[] Bitmap(int count)
    {
        var result = new byte[(count + 7) / 8];
        for (var cid = 0; cid < count; cid++) result[cid / 8] |= (byte)(128 >> (cid % 8));
        return result;
    }
}
