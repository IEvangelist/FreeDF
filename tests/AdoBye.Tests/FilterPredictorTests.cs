using System.IO.Compression;
using PortableDocument.Core;
using Xunit;

namespace AdoBye.Tests;

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
    [Theory]
    [InlineData("pred2-16", 16, 9)]
    [InlineData("pred2-4", 4, 7)]
    public void TiffPredictor2AtNon8BitDepths(string name, int bpc, int columns) =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, name + ".exp")), Run(File.ReadAllBytes(Path.Combine(Dir, name + ".bin")), 2, 1, bpc, columns));

    [Theory]
    [InlineData(0, 1, 8)]
    [InlineData(10, 1, 8)]
    [InlineData(2, 0, 8)]
    [InlineData(2, 1, 3)]
    [InlineData(15, 99999999, 16)]
    public void DegeneratePredictorParametersTerminate(int columns, int colors, int bpc)
    {
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionMode.Compress, true)) zs.Write(new byte[64]);
        Assert.NotNull(Run(z.ToArray(), 2, colors, bpc, columns));
        Assert.NotNull(Run(z.ToArray(), 15, colors, bpc, columns));
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
