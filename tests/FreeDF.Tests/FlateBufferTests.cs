using PortableDocument.Core;

namespace FreeDF.Tests;

public class FlateBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(81919)]
    [InlineData(81920)]
    [InlineData(81921)]
    [InlineData(163840)]
    [InlineData(262144)]
    public void Decode_preserves_exact_bytes_across_scratch_boundaries(int size)
    {
        var source = new byte[size];
        new Random(42).NextBytes(source);
        byte[] encoded = size == 0 ? [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]
            : PdfFilters.DeflateEncode(source);
        foreach (var strict in new[] { false, true })
        {
            var decoded = Decode(encoded, strict);
            Assert.Equal(source, decoded);
            Array.Fill(decoded, (byte)0);
            Assert.Equal(source, Decode(encoded, strict));
            Assert.Equal(new byte[] { 1, 2, 3 }, Decode(PdfFilters.DeflateEncode([1, 2, 3]), strict));
        }
    }

    [Fact]
    public void Concurrent_decodes_preserve_independent_results()
    {
        Parallel.For(0, 32, index =>
        {
            var source = new byte[81921 + index];
            new Random(index).NextBytes(source);
            var encoded = PdfFilters.DeflateEncode(source);
            Assert.Equal(source, Decode(encoded, index % 2 == 0));
        });
    }

    [Fact]
    public void Strict_corruption_failure_preserves_next_valid_decode()
    {
        var source = new byte[262144];
        new Random(42).NextBytes(source);
        var encoded = PdfFilters.DeflateEncode(source);
        var corrupt = encoded.ToArray();
        corrupt[^1] ^= 1;
        for (var index = 0; index < 3; index++)
        {
            Assert.Throws<InvalidDataException>(() => Decode(corrupt, true));
            Assert.Equal(source, Decode(encoded, true));
        }
    }

    [Theory]
    [InlineData(5551)]
    [InlineData(5552)]
    [InlineData(5553)]
    [InlineData(11103)]
    [InlineData(11104)]
    [InlineData(11105)]
    [InlineData(1048576)]
    public void Strict_checksum_preserves_worst_case_maximal_byte_payloads(int size)
    {
        var source = new byte[size];
        Array.Fill(source, byte.MaxValue);
        var encoded = PdfFilters.DeflateEncode(source);
        Assert.Equal(source, Decode(encoded, true));
    }

    static byte[] Decode(byte[] encoded, bool strict) =>
        PdfFilters.Decode(encoded, [("FlateDecode", null)], x => x, strictContentFilters: strict).Data;
}
