using PortableDocument.Document;
using static FreeDF.Tests.PdfAIdentityCidSetListingTests;
using static FreeDF.Tests.PdfAOpenTypeCharSetTests;

namespace FreeDF.Tests;

public class PdfAOpenTypeCffCidSetTests
{
    public static IEnumerable<object[]> Listings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var keyed in new[] { false, true })
                foreach (var subset in new[] { false, true })
                    foreach (var format in new[] { 0, 1, 2 })
                        foreach (var variant in new[] { "full", "zero-tail", "empty", "missing-zero", "missing-unused", "extra", "wrong-index" })
                            yield return [profile, keyed, subset, format, variant, subset && variant is not ("full" or "zero-tail")];
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void OpenTypeCffUsesCidsForKeyedProgramsAndGidsForNameKeyedPrograms(
        PdfValidationProfile profile, bool keyed, bool subset, int format, string variant, bool rejected)
    {
        var bitmap = Bitmap(keyed);
        if (variant == "zero-tail") bitmap = [.. bitmap, 0];
        if (variant == "empty") bitmap = [];
        if (variant == "missing-zero") bitmap[0] &= 127;
        if (variant == "missing-unused") bitmap[0] &= 191;
        if (variant == "extra")
        {
            if (keyed) bitmap[0] |= 32;
            else bitmap[0] |= 8;
        }
        if (variant == "wrong-index") bitmap = Bitmap(!keyed);
        var report = PdfValidator.Validate(Document(Wrap(CffProgram(keyed, format)), bitmap, subset, cff: true, openType: true), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.Equal(rejected, report.Has("PDFA-FONT-CIDSET-LIST"));
        AssertClauses(report, profile);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void UnsupportedOrMalformedWrapperAndCffMetadataAreExplicitlyIncomplete(PdfValidationProfile profile)
    {
        foreach (var keyed in new[] { false, true })
        {
            var cff = CffProgram(keyed, 0);
            var valid = Wrap(cff);
            var invalid = Enumerable.Range(0, valid.Length).Select(length => valid[..length]).ToList();
            invalid.Add(cff);
            invalid.Add(Wrap([]));
            invalid.Add(Wrap([2, 0, 4, 4]));
            invalid.Add(Wrap(CffGlyphNamesTests.Build(2, [0, 255, 255])));
            foreach (var (offset, value) in new (int, byte)[]
            {
                (0, 0), (5, 0), (5, 255), (12, 0), (23, 0), (23, 255), (27, 0), (27, 255),
            })
            {
                var data = valid.ToArray();
                data[offset] = value;
                invalid.Add(data);
            }
            var duplicate = new byte[44 + cff.Length];
            valid.AsSpan(0, 28).CopyTo(duplicate);
            duplicate[5] = 2;
            duplicate[23] = 44;
            duplicate.AsSpan(12, 16).CopyTo(duplicate.AsSpan(28));
            cff.CopyTo(duplicate, 44);
            invalid.Add(duplicate);
            foreach (var data in invalid)
            {
                var report = PdfValidator.Validate(Document(data, Bitmap(keyed), cff: true, openType: true), profile);
                Assert.False(report.Has("VALIDATION-INTERNAL"));
                Assert.True(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
                Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
                AssertClauses(report, profile);
            }
        }
    }

    [Theory]
    [InlineData(16777216, false)]
    [InlineData(16777217, true)]
    public void DecodedWrapperLimitIncludesAllTables(int length, bool incomplete)
    {
        var data = new byte[length];
        Wrap(CffProgram(true, 0)).CopyTo(data, 0);
        var report = PdfValidator.Validate(Document(data, Bitmap(true), cff: true, openType: true), PdfValidationProfile.PdfA3b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
    }

    static byte[] Bitmap(bool keyed)
    {
        var bitmap = new byte[keyed ? 8192 : 1];
        foreach (var cid in keyed ? new[] { 0, 1, 41, 65535 } : [0, 1, 2, 3])
            bitmap[cid / 8] |= (byte)(128 >> (cid % 8));
        return bitmap;
    }

    static byte[] CffProgram(bool keyed, int format) => keyed ? PdfACffCidSetListingTests.CffProgram(format) :
        CffGlyphNamesTests.Build(4, format switch
        {
            0 => [0, 0, 34, 0, 35, 0, 1],
            1 => [1, 0, 34, 1, 0, 1, 0],
            _ => [2, 0, 34, 0, 1, 0, 1, 0, 0],
        });
}
