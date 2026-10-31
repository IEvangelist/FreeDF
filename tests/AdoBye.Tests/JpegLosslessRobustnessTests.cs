using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

// Robustness budgets for the lossless JPEG paths (SOF3 subsampled, SOF11) from tools/make_jpeg_lossless_arith_fixtures.py.
public class JpegLosslessRobustnessTests
{
    static byte[] Jpeg(string name)
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "jpegx", name + ".pdf")));
        var xo = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        return ((PdfStream)doc.File.Resolve(xo.Get("Im0"))!).RawData;
    }

    [Theory]
    [InlineData("lossless-arith-gray8-pred4")]
    [InlineData("lossless-arith-rgb8-restart")]
    [InlineData("lossless-sub-arith-420-restart")]
    [InlineData("lossless-sub-huff-420-restart")]
    [InlineData("lossless-sub-arith-mixed-odd")]
    public void CorruptedAndTruncatedStreamsNeverThrow(string name)
    {
        var good = Jpeg(name);
        var rnd = new Random(42);
        for (var t = 0; t < 150; t++)
        {
            var d = (byte[])good.Clone();
            if (t % 3 == 0) d = d[..rnd.Next(2, d.Length)];
            else for (var j = 0; j < 6; j++) d[rnd.Next(d.Length)] = (byte)rnd.Next(256);
            JpegDecoder.Decode(d, out _, out _, out _);
        }
    }

    [Theory]
    [InlineData("hier-lossless-huff-2lvl")]
    [InlineData("hier-lossless-arith-3lvl-odd")]
    [InlineData("hier-dct-huff-2lvl-lossless")]
    [InlineData("hier-dct-arith-rgb-lossless")]
    public void HierarchicalCorruptionNeverThrows(string name)
    {
        var good = Jpeg(name);
        var rnd = new Random(7);
        for (var t = 0; t < 150; t++)
        {
            var d = (byte[])good.Clone();
            if (t % 3 == 0) d = d[..rnd.Next(2, d.Length)];
            else for (var j = 0; j < 6; j++) d[rnd.Next(d.Length)] = (byte)rnd.Next(256);
            JpegDecoder.Decode(d, out _, out _, out _);
        }
    }

    static int Find(byte[] d, byte marker)
    {
        for (var i = 2; i + 1 < d.Length; i++) if (d[i] == 0xFF && d[i + 1] == marker) return i;
        return -1;
    }

    [Fact]
    public void HierarchicalStructuralViolationsAreRejected()
    {
        var good = Jpeg("hier-lossless-huff-2lvl");
        Assert.NotNull(JpegDecoder.Decode(good, out _, out _, out _));

        var prec = (byte[])good.Clone(); prec[Find(prec, 0xDE) + 4] = 12; // DHP precision differs from the frames (T.81 B.3.1)
        Assert.Null(JpegDecoder.Decode(prec, out _, out _, out _));

        var huge = (byte[])good.Clone(); var dhp = Find(huge, 0xDE);
        huge[dhp + 5] = huge[dhp + 6] = huge[dhp + 7] = huge[dhp + 8] = 0xFF; // 65535 x 65535 completed image
        Assert.Null(JpegDecoder.Decode(huge, out _, out _, out _));

        var noRef = (byte[])good.Clone(); // turn the first (non-differential SOF3) frame into a differential SOF7 one
        noRef[Find(noRef, 0xC3) + 1] = 0xC7;
        Assert.Null(JpegDecoder.Decode(noRef, out _, out _, out _));

        var noDhp = (byte[])good.Clone(); noDhp[Find(noDhp, 0xDE) + 1] = 0xFE; // DHP missing: differential frame without hierarchy
        Assert.Null(JpegDecoder.Decode(noDhp, out _, out _, out _));
    }

    [Theory]
    [InlineData(0xC3)]
    [InlineData(0xCB)]
    public void HugeDeclaredDimensionsAreRejectedWithoutAllocation(int sof)
    {
        byte[] jpg = [0xFF, 0xD8, 0xFF, (byte)sof, 0, 11, 16, 0xFF, 0xFF, 0xFF, 0xFF, 1, 1, 0x11, 0, 0xFF, 0xD9];
        Assert.Null(JpegDecoder.Decode(jpg, out _, out _, out _));
    }
}
