using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

public class PngStreamingTests
{
    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(257, 13, false)]
    [InlineData(17, 513, true)]
    [InlineData(1985, 23, true)]
    [InlineData(17000, 3, true)]
    public void RowStreamingPreservesExactPixels(int width, int height, bool noise)
    {
        var rgba = new byte[width * height * 4];
        if (noise) new Random(42).NextBytes(rgba);
        else for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i / 4 % 7);
        var expected = PngWriter.Encode(width, height, rgba, false);
        var streamed = PngWriter.Encode(width, height, rgba, true);
        var decoded = PngWriter.Decode(streamed);
        Assert.Equal(PngWriter.Decode(expected).Rgba, decoded.Rgba);
        Assert.Equal(width, decoded.W);
        Assert.Equal(height, decoded.H);
        Assert.Equal(rgba, decoded.Rgba);
    }

    [Fact]
    public void RowStreamingAvoidsFullFilteredPageAllocation()
    {
        const int width = 512, height = 384;
        var rgba = new byte[width * height * 4];
        PngWriter.Encode(1, 1, [0, 0, 0, 255], false);
        PngWriter.Encode(1, 1, [0, 0, 0, 255], true);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var expected = PngWriter.Encode(width, height, rgba, false);
        var bufferedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var streamed = PngWriter.Encode(width, height, rgba, true);
        var streamedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(PngWriter.Decode(expected).Rgba, PngWriter.Decode(streamed).Rgba);
        Assert.True(bufferedBytes - streamedBytes >= width * height * 3 && streamedBytes < width * height,
            $"Expected the filtered page allocation to disappear: buffered {bufferedBytes}, streamed {streamedBytes}.");
    }
}
