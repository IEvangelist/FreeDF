using System.Text;
using PortableDocument.Core;

namespace AdoBye.Tests;

/// <summary>ISO 32000-2 7.4 stream filters: vectors are from the standard or computed from the filter definitions.</summary>
public class StreamFilterTests
{
    static byte[] Run(byte[] raw, params string[] filters) =>
        PdfFilters.Decode(raw, filters.Select(f => (f, (PdfDictionary?)null)).ToList(), o => o).Data;

    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void LzwDecodesTheSpecificationExample() =>
        Assert.Equal(new byte[] { 45, 45, 45, 45, 45, 65, 45, 45, 45, 66 }, Run([0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01], "LZWDecode"));

    [Fact]
    public void Ascii85DecodesFullGroupsZeroShortcutAndPartialGroup()
    {
        Assert.Equal(Ascii("Man "), Run(Ascii("9jqo^~>"), "ASCII85Decode"));
        Assert.Equal(new byte[4], Run(Ascii("z~>"), "ASCII85Decode"));
        Assert.Equal(Ascii("Ma"), Run(Ascii("9jn~>"), "ASCII85Decode"));
    }

    [Fact]
    public void Ascii85IgnoresWhitespaceAndLeadingMarker() =>
        Assert.Equal(Ascii("Man Man "), Run(Ascii("<~9j\nqo^ 9jqo^~>"), "ASCII85Decode"));

    [Fact]
    public void AsciiHexIgnoresWhitespaceAndPadsAnOddFinalDigit()
    {
        Assert.Equal(Ascii("Hello"), Run(Ascii("48 65\n6C6c6F>"), "ASCIIHexDecode"));
        Assert.Equal(new byte[] { 0x70 }, Run(Ascii("7>"), "ASCIIHexDecode"));
    }

    [Fact]
    public void RunLengthCopiesLiteralsRepeatsAndStopsAtEod() =>
        Assert.Equal(Ascii("ABCDDDE"), Run([0x02, 0x41, 0x42, 0x43, 0xFE, 0x44, 0x00, 0x45, 0x80, 0x46], "RunLengthDecode"));

    [Fact]
    public void FilterChainsApplyInOrder()
    {
        var deflated = PdfFilters.DeflateEncode(Ascii("chain"));
        var hex = Ascii(Convert.ToHexString(deflated) + ">");
        Assert.Equal(Ascii("chain"), Run(hex, "ASCIIHexDecode", "FlateDecode"));
    }

    [Fact]
    public void UnsupportedFilterIsReported() =>
        Assert.Throws<NotSupportedException>(() => Run([1], "NoSuchDecode"));

    [Fact]
    public void ImageCodecStopsTheChainAndIsReturned()
    {
        var (data, image, _) = PdfFilters.Decode(Ascii("00>"), [("ASCIIHexDecode", null), ("DCTDecode", null)], o => o);
        Assert.Equal("DCTDecode", image);
        Assert.Equal(new byte[] { 0 }, data);
    }

    [Fact]
    public void FlateKeepsDataDecodedBeforeCorruption()
    {
        var payload = Enumerable.Range(0, 4000).Select(i => (byte)(i * 7)).ToArray();
        var z = PdfFilters.DeflateEncode(payload);
        var truncated = z[..(z.Length / 2)];
        var got = Run(truncated, "FlateDecode");
        Assert.NotEmpty(got);
        Assert.True(payload.AsSpan(0, got.Length).SequenceEqual(got));
    }
}

