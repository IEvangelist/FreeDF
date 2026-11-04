using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using PortableDocument.Render;

namespace FreeDF.Tests;

public class PngDecodeTests
{
    [Theory]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(16, false)]
    [InlineData(16, true)]
    public void RgbAndRgbaDecodeEveryRowFilterAtTheDeclaredBitDepth(int depth, bool alpha)
    {
        var (png, expected) = Fixture(depth, alpha);
        var decoded = PngWriter.Decode(png);
        Assert.Equal(3, decoded.W);
        Assert.Equal(5, decoded.H);
        Assert.Equal(expected, decoded.Rgba);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(4, 0)]
    [InlineData(32, 0)]
    [InlineData(8, 1)]
    [InlineData(16, 1)]
    public void UnsupportedDepthOrInterlaceIsRejectedInsteadOfReturningCorruptPixels(int depth, int interlace)
    {
        var (png, _) = Fixture(depth, false, interlace);
        Assert.Throws<NotSupportedException>(() => PngWriter.Decode(png));
    }

    internal static (byte[] Png, byte[] Rgba) Fixture(int depth, bool alpha, int interlace = 0)
    {
        const int width = 3, height = 5;
        var channels = alpha ? 4 : 3;
        var sampleBytes = depth == 16 ? 2 : 1;
        var bpp = channels * sampleBytes;
        var stride = width * bpp;
        var pixels = new byte[stride * height];
        var expected = new byte[width * height * 4];
        for (var p = 0; p < width * height; p++)
        {
            expected[p * 4 + 3] = 255;
            for (var c = 0; c < channels; c++)
            {
                var value = (byte)(p * 47 + c * 83);
                pixels[p * bpp + c * sampleBytes] = value;
                if (sampleBytes == 2) pixels[p * bpp + c * sampleBytes + 1] = (byte)(255 - value);
                expected[p * 4 + c] = value;
            }
        }
        var filtered = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            filtered[y * (stride + 1)] = (byte)y;
            for (var x = 0; x < stride; x++)
            {
                int a = x >= bpp ? pixels[y * stride + x - bpp] : 0;
                int b = y > 0 ? pixels[(y - 1) * stride + x] : 0;
                int c = y > 0 && x >= bpp ? pixels[(y - 1) * stride + x - bpp] : 0;
                var p = a + b - c;
                var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
                var predictor = y switch
                {
                    1 => a, 2 => b, 3 => (a + b) / 2,
                    4 => pa <= pb && pa <= pc ? a : pb <= pc ? b : c,
                    _ => 0
                };
                filtered[y * (stride + 1) + x + 1] = unchecked((byte)(pixels[y * stride + x] - predictor));
            }
        }
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        void Chunk(string type, byte[] data)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            output.Write(length);
            var name = Encoding.ASCII.GetBytes(type);
            output.Write(name);
            output.Write(data);
            var crc = new Crc32();
            crc.Append(name);
            crc.Append(data);
            BinaryPrimitives.WriteUInt32BigEndian(length, crc.GetCurrentHashAsUInt32());
            output.Write(length);
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)depth; header[9] = alpha ? (byte)6 : (byte)2; header[12] = (byte)interlace;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(filtered);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return (output.ToArray(), expected);
    }
}
