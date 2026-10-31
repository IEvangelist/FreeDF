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
    [InlineData(0xC3)]
    [InlineData(0xCB)]
    public void HugeDeclaredDimensionsAreRejectedWithoutAllocation(int sof)
    {
        byte[] jpg = [0xFF, 0xD8, 0xFF, (byte)sof, 0, 11, 16, 0xFF, 0xFF, 0xFF, 0xFF, 1, 1, 0x11, 0, 0xFF, 0xD9];
        Assert.Null(JpegDecoder.Decode(jpg, out _, out _, out _));
    }
}
