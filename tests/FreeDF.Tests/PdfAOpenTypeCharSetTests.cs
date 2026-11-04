using System.Buffers.Binary;
using PortableDocument.Document;
using static FreeDF.Tests.PdfAType1CCharSetListingTests;

namespace FreeDF.Tests;

public class PdfAOpenTypeCharSetTests
{
    static readonly byte[] Cff = CffGlyphNamesTests.Build(4, [0, 0, 34, 0, 35, 0, 1]);

    public static IEnumerable<object[]> Listings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subset in new[] { false, true })
                foreach (var indirect in new[] { false, true })
                    foreach (var (value, rejected) in new (string, bool)[]
                    {
                        ("/A/B/space", false), ("/#41/B/space", false), ("/space/A/B/A", false),
                        ("", true), ("/A/B", true), ("/A/B/space/extra", true),
                        ("/A/B/space/.notdef", true), ("A/B/space", true),
                    })
                        yield return [profile, subset, indirect, value, subset && rejected];
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void OpenTypeCffListsAllDefinedGlyphNames(
        PdfValidationProfile profile, bool subset, bool indirect, string value, bool rejected)
    {
        var report = PdfValidator.Validate(Document(value, Wrap(Cff), subset, indirect, openType: true), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
        Assert.Equal(rejected, report.Has("PDFA-FONT-CHARSET-LIST"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MalformedMissingOrDuplicateCffTablesAreExplicitlyIncomplete(PdfValidationProfile profile)
    {
        var valid = Wrap(Cff);
        var invalid = Enumerable.Range(0, valid.Length).Select(length => valid[..length]).ToList();
        foreach (var (offset, value) in new (int, byte)[]
        {
            (0, 0), (5, 0), (5, 255), (12, 0), (23, 0), (23, 255), (27, 0), (27, 255),
        })
        {
            var data = valid.ToArray();
            data[offset] = value;
            invalid.Add(data);
        }
        var duplicate = new byte[44 + Cff.Length];
        valid.AsSpan(0, 28).CopyTo(duplicate);
        duplicate[5] = 2;
        duplicate[23] = 44;
        duplicate.AsSpan(12, 16).CopyTo(duplicate.AsSpan(28));
        Cff.CopyTo(duplicate, 44);
        invalid.Add(duplicate);
        invalid.Add(Cff);
        foreach (var data in invalid)
        {
            var report = PdfValidator.Validate(Document("/A/B/space", data, openType: true), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
            Assert.False(report.Has("PDFA-FONT-CHARSET-LIST"));
        }
    }

    [Theory]
    [InlineData(16777216, false)]
    [InlineData(16777217, true)]
    public void DecodedWrapperLimitIncludesAllTables(int length, bool incomplete)
    {
        var data = new byte[length];
        Wrap(Cff).CopyTo(data, 0);
        var report = PdfValidator.Validate(Document("/A/B/space", data, openType: true), PdfValidationProfile.PdfA2b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CHARSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
    }

    internal static byte[] Wrap(byte[] cff)
    {
        var data = new byte[28 + cff.Length];
        "OTTO"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1);
        "CFF "u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 28);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), (uint)cff.Length);
        cff.CopyTo(data, 28);
        return data;
    }
}
