using System.Buffers.Binary;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class StrictFlateCompletionTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(7)]
    public void Matching_adler_does_not_replace_the_final_deflate_block(int padding)
    {
        var (encoded, expected) = Build(padding, false);
        Assert.Equal(expected, PdfFilters.Flate(encoded));
        Assert.Throws<InvalidDataException>(() =>
            PdfFilters.Decode(encoded, [("FlateDecode", null)], x => x, strictContentFilters: true));
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/ASCIIHexDecode /FlateDecode]",
                "null", false, repair, encodedOverride: Convert.ToHexString(encoded) + ">"));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Nonfinal_stored_block_followed_by_complete_empty_final_block_is_valid(int padding)
    {
        var (encoded, expected) = Build(padding, true);
        Assert.Equal(expected,
            PdfFilters.Decode(encoded, [("FlateDecode", null)], x => x, strictContentFilters: true).Data);
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/ASCIIHexDecode /FlateDecode]",
                "null", false, repair, encodedOverride: Convert.ToHexString(encoded) + ">"));
            var member = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", member.Get("Type").AsName());
            Assert.Same(member, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    static (byte[] Encoded, byte[] Expected) Build(int padding, bool complete)
    {
        byte[] expected = [.. "2 0 << /Type /Target >>"u8, .. Enumerable.Repeat((byte)32, padding)];
        var stored = new byte[5 + expected.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(stored.AsSpan(1), (ushort)expected.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(stored.AsSpan(3), (ushort)~expected.Length);
        expected.CopyTo(stored, 5);
        var checksum = PdfFilters.DeflateEncode(expected)[^4..];
        byte[] final = complete ? [1, 0, 0, 255, 255] : [];
        return ([0x78, 0x01, .. stored, .. final, .. checksum], expected);
    }
}
