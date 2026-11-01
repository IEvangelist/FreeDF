using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

public class CcittTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "ccitt");

    public static IEnumerable<object[]> OptionCases() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "ccitt-options", "manifest.txt"))
            .Where(l => l.Length > 0).Select(l => (object[])[l]);

    // Streams encoded by libtiff (Pillow 12.1.1); expectations are the encoder's source bitmaps (independently verified).
    [Theory]
    [MemberData(nameof(OptionCases))]
    public void DecodesLibtiffOptionMatrix(string line)
    {
        var p = line.Split(' ');
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "ccitt-options");
        var data = File.ReadAllBytes(Path.Combine(dir, p[0] + ".bin"));
        var exp = File.ReadAllBytes(Path.Combine(dir, p[0] + ".exp"));
        var k = int.Parse(p[1]); var cols = int.Parse(p[2]); var rows = int.Parse(p[3]); var align = p[4] == "1";
        var stride = (cols + 7) / 8;
        var dec = CcittDecoder.Decode(data, k, cols, rows, false, align, rows);
        var inv = CcittDecoder.Decode(data, k, cols, rows, true, align, rows);
        Assert.Equal(stride * rows, dec.Length);
        var bad = 0;
        for (var r = 0; r < rows; r++)
            for (var x = 0; x < cols; x++)
            {
                var i = r * stride + (x >> 3); var sh = 7 - (x & 7);
                var e = exp[i] >> sh & 1;
                if ((dec[i] >> sh & 1) != e || (inv[i] >> sh & 1) != (e ^ 1)) bad++;
            }
        Assert.Equal(0, bad);
    }

    [Fact]
    public void ResynchronisesOnNextEolAfterDamagedRow()
    {
        const string eol = "000000000001";
        var bits = eol + "10011" + eol + "000000001011" + eol + "00110101" + "000101" + eol + "10011";
        bits += new string('0', (8 - bits.Length % 8) % 8);
        var bytes = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var rows = CcittDecoder.Decode(bytes, 0, 8, 4, false, false, 4);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0x00, 0xFF }, rows);
    }

    [Fact]
    public void CorruptedAndTruncatedStreamsNeverThrow()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "ccitt-options");
        var rnd = new Random(7);
        foreach (var (name, k, cols, align) in new[] { ("g4-200", -1, 200, false), ("g3-eol-200", 0, 200, false), ("g3-2d-200", 4, 200, false), ("rle-align-200", 0, 200, true) })
        {
            var good = File.ReadAllBytes(Path.Combine(dir, name + ".bin"));
            for (var t = 0; t < 200; t++)
            {
                var d = (byte[])good.Clone();
                if (t % 2 == 0) d = d[..rnd.Next(d.Length)];
                else for (var j = 0; j < 4; j++) d[rnd.Next(d.Length)] ^= (byte)(1 << rnd.Next(8));
                Assert.Equal((cols + 7) / 8 * 24, CcittDecoder.Decode(d, k, cols, 24, false, align, 24).Length);
            }
        }
    }

    [Theory]
    [InlineData("g4")]
    [InlineData("g3-1d")]
    public void DecodesAgainstLibtiffEncoding(string name)
    {
        var expected = File.ReadAllText(Path.Combine(Dir, "expected.txt"));
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var f = doc.File;
        var xo = (PdfDictionary)f.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)f.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(f, img, doc.Pages[0].Resources, out _, out var warn);
        Assert.NotNull(data);
        Assert.Null(warn);
        var bad = 0;
        for (var i = 0; i < expected.Length; i++)
            if ((data!.Rgba[i * 4] > 127) != (expected[i] == '1')) bad++;
        Assert.Equal(0, bad);
    }
}
