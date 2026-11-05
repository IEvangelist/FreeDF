using System.Buffers.Binary;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class StrictFlateExtentTests
{
    [Theory]
    [InlineData("zero")]
    [InlineData("garbage")]
    [InlineData("newline")]
    [InlineData("checksum")]
    [InlineData("second-stream")]
    [InlineData("long")]
    [InlineData("one-byte")]
    [InlineData("two-bytes")]
    [InlineData("three-bytes")]
    public void Strict_flate_rejects_encoded_bytes_after_a_complete_zlib_stream(string tail)
    {
        var expected = "2 0 << /Type /Target >>"u8.ToArray();
        var complete = PdfFilters.DeflateEncode(expected);
        var checksum = complete[^4..];
        byte[] extra = tail switch
        {
            "one-byte" => [0], "two-bytes" => [0, 1], "three-bytes" => [0, 1, 2],
            "zero" => [0, .. checksum], "garbage" => [255, .. checksum],
            "newline" => [13, 10, .. checksum], "checksum" => checksum,
            "second-stream" => complete, _ => [.. new byte[16384], .. checksum]
        };
        byte[] encoded = [.. complete, .. extra];
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
    [InlineData(11)]
    [InlineData(8191)]
    [InlineData(8192)]
    [InlineData(8193)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(16385)]
    [InlineData(65546)]
    public void Strict_flate_preserves_exact_input_at_read_buffer_boundaries(int encodedLength)
    {
        var expected = new byte[encodedLength - 11];
        new Random(42).NextBytes(expected);
        var stored = new byte[expected.Length + 5];
        stored[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(stored.AsSpan(1), (ushort)expected.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(stored.AsSpan(3), (ushort)~expected.Length);
        expected.CopyTo(stored, 5);
        byte[] encoded = [0x78, 0x01, .. stored, .. PdfFilters.DeflateEncode(expected)[^4..]];
        Assert.Equal(encodedLength, encoded.Length);
        Assert.Equal(expected,
            PdfFilters.Decode(encoded, [("FlateDecode", null)], x => x, strictContentFilters: true).Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Strict_flate_validates_each_actual_checksum_byte(int index)
    {
        var encoded = PdfFilters.DeflateEncode("2 0 << /Type /Target >>"u8.ToArray());
        encoded[encoded.Length - 4 + index] ^= 1;
        Assert.Throws<InvalidDataException>(() =>
            PdfFilters.Decode(encoded, [("FlateDecode", null)], x => x, strictContentFilters: true));
    }
}
