using System.Buffers.Binary;
using System.Text;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAIdentityCidSetListingTests
{
    public static IEnumerable<object[]> Listings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subset in new[] { false, true })
                foreach (var indirectMap in new[] { false, true })
                    foreach (var (bitmap, rejected) in new (byte[], bool)[]
                    {
                        ([255, 192], false), ([255, 192, 0, 0], false), ([], true),
                        ([127, 192], true), ([191, 192], true), ([223, 192], true),
                        ([255, 224], true), ([255, 192, 1], true),
                        ([192, 0], true), ([255, 255], true),
                    })
                        yield return [profile, subset, indirectMap, bitmap, subset && rejected];
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void IdentityMapRequiresAllProgramCidsIncludingZeroAndUnusedGlyphs(
        PdfValidationProfile profile, bool subset, bool indirectMap, byte[] bitmap, bool rejected)
    {
        var report = PdfValidator.Validate(Document(Program(10), bitmap, subset, indirectMap), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.Equal(rejected, report.Has("PDFA-FONT-CIDSET-LIST"));
        AssertClauses(report, profile);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MalformedMaxpMetadataIsExplicitlyIncomplete(PdfValidationProfile profile)
    {
        var valid = Program(10);
        var malformed = Enumerable.Range(0, valid.Length).Select(length => valid[..length]).ToList();
        foreach (var (offset, value) in new (int, byte)[]
        {
            (0, 79), (5, 0), (12, 0), (23, 0), (23, 255), (27, 31), (28, 1), (29, 0), (33, 0),
        })
        {
            var data = valid.ToArray();
            data[offset] = value;
            malformed.Add(data);
        }
        malformed.Add(Program(0));
        var duplicate = new byte[76];
        valid.AsSpan(0, 28).CopyTo(duplicate);
        duplicate[5] = 2;
        duplicate[23] = 44;
        duplicate.AsSpan(12, 16).CopyTo(duplicate.AsSpan(28));
        valid.AsSpan(28).CopyTo(duplicate.AsSpan(44));
        malformed.Add(duplicate);
        foreach (var data in malformed)
        {
            var report = PdfValidator.Validate(Document(data, [255, 192]), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
            Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
            AssertClauses(report, profile);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(65535)]
    public void GlyphCountsCrossByteAndMaximumBoundaries(int count)
    {
        var bitmap = new byte[(count + 7) / 8];
        for (var gid = 0; gid < count; gid++) bitmap[gid / 8] |= (byte)(128 >> (gid % 8));
        var report = PdfValidator.Validate(Document(Program(count), bitmap), PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b, 1048576, false)]
    [InlineData(PdfValidationProfile.PdfA1b, 1048577, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 1048576, false)]
    [InlineData(PdfValidationProfile.PdfA2b, 1048577, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 1048576, false)]
    [InlineData(PdfValidationProfile.PdfA3b, 1048577, true)]
    public void DecodedBitmapBoundIsExplicit(PdfValidationProfile profile, int length, bool incomplete)
    {
        var bitmap = new byte[length];
        bitmap[0] = 255;
        bitmap[1] = 192;
        var report = PdfValidator.Validate(Document(Program(10), bitmap), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
    }

    [Theory]
    [InlineData(16777216, false)]
    [InlineData(16777217, true)]
    public void DecodedProgramBoundIsExplicit(int length, bool incomplete)
    {
        var data = new byte[length];
        Program(10).CopyTo(data, 0);
        var report = PdfValidator.Validate(Document(data, [255, 192]), PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
    }

    [Theory]
    [InlineData("/Other")]
    [InlineData("null")]
    [InlineData("12 0 R")]
    public void OtherCidMappingsRemainOutsideThisSlice(string map)
    {
        var report = PdfValidator.Validate(Document([], [], map: map), PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-INCOMPLETE"));
        Assert.False(report.Has("PDFA-FONT-CIDSET-LIST"));
    }

    static byte[] Program(int count)
    {
        var data = new byte[60];
        BinaryPrimitives.WriteUInt32BigEndian(data, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1);
        "maxp"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 28);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), 32);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(32), (ushort)count);
        return data;
    }

    static PdfDocument Document(byte[] program, byte[] bitmap, bool subset = true, bool indirectMap = false, string? map = null)
    {
        var name = (subset ? "ABCDEF+" : "") + "TestFont";
        return PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F 5 0 R >> >> /Contents 4 0 R >>",
            ObjPdf.Stream("BT /F 12 Tf <0001> Tj ET"),
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding /Identity-H /DescendantFonts [6 0 R] >>",
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /CIDToGIDMap {map ?? (indirectMap ? "11 0 R" : "/Identity")} /FontDescriptor 7 0 R >>",
            $"<< /Type /FontDescriptor /FontName /{name} /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 8 0 R /CIDSet 10 0 R >>",
            ObjPdf.Stream(Encoding.Latin1.GetString(program)),
            "null",
            ObjPdf.Stream(Encoding.Latin1.GetString(bitmap)),
            "/Identity",
            ObjPdf.Stream("")));
    }

    static void AssertClauses(ValidationReport report, PdfValidationProfile profile)
    {
        Assert.All(report.Findings.Where(f => f.RuleId is "PDFA-FONT-CIDSET-LIST" or "PDFA-FONT-CIDSET-INCOMPLETE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.3.5",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.2.11.4.2",
                _ => "ISO 19005-3 6.2.11.4.2",
            }, finding.Clause);
        });
    }
}
