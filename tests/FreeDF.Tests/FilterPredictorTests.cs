using System.IO.Compression;
using PortableDocument.Core;
using Xunit;

namespace FreeDF.Tests;

public class FilterPredictorTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "filters");

    static byte[] Run(byte[] z, int predictor, int colors, int bpc, int columns)
    {
        var parms = new PdfDictionary();
        parms.Entries["Predictor"] = new PdfInteger(predictor);
        parms.Entries["Colors"] = new PdfInteger(colors);
        parms.Entries["BitsPerComponent"] = new PdfInteger(bpc);
        parms.Entries["Columns"] = new PdfInteger(columns);
        return PdfFilters.Decode(z, [("FlateDecode", parms)], o => o).Data;
    }

    // pred2-16 is verified by libtiff (tools/make_filter_fixtures.py asserts it); pred2-4 is hand-derived.
    [Fact]
    public void LzwMatchesLibtiffStream() =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, "lzw-libtiff.exp")),
            PdfFilters.Decode(File.ReadAllBytes(Path.Combine(Dir, "lzw-libtiff.bin")), [("LZWDecode", null)], o => o).Data);

    [Fact]
    public void LzwSpecExample()
    {
        // ISO 32000-2 §7.4.4.2 example: 45 45 45 45 45 65 45 45 45 66
        byte[] enc = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];
        Assert.Equal(new byte[] { 45, 45, 45, 45, 45, 65, 45, 45, 45, 66 }, PdfFilters.Decode(enc, [("LZWDecode", null)], o => o).Data);
    }

    // Vectors from Python base64.a85encode (independent).
    [Theory]
    [InlineData("<~87cURD_*#TDfTZ)+TMKB!+B=~>", "Hello, world!\0\0\0\0ab")]
    [InlineData("<~z!!~>", "\0\0\0\0\0")]
    [InlineData("@:E_W+Ct~>", "abcd e")]
    [InlineData("<~87cUR D_*#T\nDfTZ)+TMKB!+B=~>", "Hello, world!\0\0\0\0ab")]
    public void Ascii85MatchesPythonEncoder(string enc, string expected) =>
        Assert.Equal(expected.Select(c => (byte)c).ToArray(), PdfFilters.Decode(System.Text.Encoding.ASCII.GetBytes(enc), [("ASCII85Decode", null)], o => o).Data);

    [Fact]
    public void FilterChainAppliesDecodeParmsPerStage()
    {
        // ASCIIHex -> Flate(+PNG Sub predictor); parms array has null for the first stage
        var raw = new byte[] { 1, 1, 1, 1, 1 };
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionMode.Compress, true)) zs.Write(raw);
        var hex = System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(z.ToArray()) + ">");
        var d = new PdfDictionary();
        d.Entries["Filter"] = new PdfArray([new PdfName("ASCIIHexDecode"), new PdfName("FlateDecode")]);
        var pr = new PdfDictionary();
        pr.Entries["Predictor"] = new PdfInteger(11); pr.Entries["Columns"] = new PdfInteger(4);
        d.Entries["DecodeParms"] = new PdfArray([PdfNull.Instance, pr]);
        var fl = PdfFilters.GetFilters(d, o => o);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, PdfFilters.Decode(hex, fl, o => o).Data);
    }

    [Fact]
    public void RunLengthEdges()
    {
        // literal run, repeat run, truncated repeat run and EOD marker
        byte[] enc = [1, 9, 8, 254, 7, 128, 0, 99];
        Assert.Equal(new byte[] { 9, 8, 7, 7, 7 }, PdfFilters.Decode(enc, [("RunLengthDecode", null)], o => o).Data);
    }

    // Self-encoded (hand-derived): libtiff cannot produce or read these, so the expectation is an independent sample-by-sample
    // differencing of random data per colour component (ISO 32000-2 §7.4.4.4, TIFF predictor 2).
    [Theory]
    [InlineData(3, 4, 5)]
    [InlineData(3, 2, 7)]
    [InlineData(4, 1, 9)]
    [InlineData(3, 16, 5)]
    [InlineData(2, 4, 3)]
    [InlineData(4, 16, 2)]
    public void TiffPredictor2MultiComponentRoundTrip(int colors, int bpc, int columns)
    {
        const int rows = 4;
        var rnd = new Random(colors * 100 + bpc);
        var max = (1 << bpc) - 1;
        var rowBytes = (colors * bpc * columns + 7) / 8;
        var plain = new byte[rows * rowBytes];
        var diff = new byte[plain.Length];
        void Put(byte[] buf, int row, int idx, int v)
        {
            if (bpc == 16) { buf[row * rowBytes + idx * 2] = (byte)(v >> 8); buf[row * rowBytes + idx * 2 + 1] = (byte)v; return; }
            var bit = idx * bpc;
            buf[row * rowBytes + bit / 8] |= (byte)(v << (8 - bpc - bit % 8));
        }
        for (var r = 0; r < rows; r++)
        {
            var prev = new int[colors];
            for (var s = 0; s < colors * columns; s++)
            {
                var v = rnd.Next(max + 1); var c = s % colors;
                Put(plain, r, s, v);
                Put(diff, r, s, (v - prev[c]) & max);
                prev[c] = v;
            }
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionMode.Compress, true)) zs.Write(diff);
        Assert.Equal(plain, Run(z.ToArray(), 2, colors, bpc, columns));
    }

    [Theory]
    [InlineData("pred2-16", 16, 9)]
    [InlineData("pred2-4", 4, 7)]
    public void TiffPredictor2AtNon8BitDepths(string name, int bpc, int columns) =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, name + ".exp")), Run(File.ReadAllBytes(Path.Combine(Dir, name + ".bin")), 2, 1, bpc, columns));

    [Theory]
    [InlineData(0, 1, 8)]
    [InlineData(2, 0, 8)]
    [InlineData(2, 1, 3)]
    [InlineData(15, 99999999, 16)]
    [InlineData(-1, 1, 8)]
    [InlineData(1, -1, 8)]
    [InlineData(int.MaxValue, int.MaxValue, 16)]
    public void DegeneratePredictorParametersAreRejected(int columns, int colors, int bpc)
    {
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionMode.Compress, true)) zs.Write(new byte[64]);
        Assert.Throws<PdfParseException>(() => Run(z.ToArray(), 2, colors, bpc, columns));
        Assert.Throws<PdfParseException>(() => Run(z.ToArray(), 15, colors, bpc, columns));
    }

    [Fact]
    public void PngSubPredictorRoundsPackedPixelSizeUpToBytes()
    {
        // Two RGB pixels at four bits per component occupy three bytes; the byte offset is ceil(12/8).
        byte[] encoded = [1, 0x12, 0x34, 0x44];
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionMode.Compress, true)) zs.Write(encoded);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56 }, Run(z.ToArray(), 11, 3, 4, 2));
    }

    [Fact]
    public void PngPredictorHandlesTruncatedFinalRow()
    {
        // two rows of 4 bytes with Sub filter; second row truncated to 2 bytes: missing samples are treated as zero
        var raw = new byte[] { 1, 1, 1, 1, 1, 1, 5, 5 };
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionMode.Compress, true)) zs.Write(raw);
        var o = Run(z.ToArray(), 11, 1, 8, 4);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 10, 10, 10 }, o);
    }
}
