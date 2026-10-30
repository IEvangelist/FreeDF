using System.Text;
using System.Text.Json;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class Jpeg12BitTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jpeg12");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Dir, "*.json").Order().Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    static (byte[] Jpeg, int Width, int Height, int Components, int[] Samples) Read(string name)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, name + ".json")));
        var root = json.RootElement;
        return (Convert.FromBase64String(root.GetProperty("jpeg").GetString()!),
            root.GetProperty("width").GetInt32(), root.GetProperty("height").GetInt32(),
            root.GetProperty("components").GetInt32(),
            root.GetProperty("samples").EnumerateArray().Select(x => x.GetInt32()).ToArray());
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void MatchesIndependentNativePrecisionReference(string name)
    {
        // T.81 floating-point IDCT reference, cross-checked against libjpeg-turbo's
        // 12-bit islow decoder. Integer IDCT rounding can straddle a >>4 boundary.
        var fixture = Read(name);
        var pixels = JpegDecoder.Decode(fixture.Jpeg, out var w, out var h, out var n);
        Assert.NotNull(pixels);
        Assert.Equal((fixture.Width, fixture.Height, fixture.Components), (w, h, n));
        Assert.Equal(fixture.Samples.Length, pixels.Length);
        for (var i = 0; i < pixels.Length; i++)
            Assert.True(Math.Abs(pixels[i] - (fixture.Samples[i] >> 4)) <= 1,
                $"{name} sample {i}: expected native {fixture.Samples[i]}, got byte {pixels[i]}");
    }

    [Fact]
    public void RestartAndProgressiveRefinementPreserveSequentialSamples()
    {
        var expected = JpegDecoder.Decode(Read("gray-extremes").Jpeg, out _, out _, out _);
        Assert.NotNull(expected);
        Assert.Equal(expected, JpegDecoder.Decode(Read("gray-restart").Jpeg, out _, out _, out _));
        Assert.Equal(expected, JpegDecoder.Decode(Read("gray-progressive").Jpeg, out _, out _, out _));
        Assert.Equal(JpegDecoder.Decode(Read("ycc-420").Jpeg, out _, out _, out _),
            JpegDecoder.Decode(Read("ycc-420-progressive").Jpeg, out _, out _, out _));
    }

    [Fact]
    public void ClampsAndTruncatesNativeSamplesInsteadOfRescalingTo255()
    {
        var pixels = JpegDecoder.Decode(Read("gray-extremes").Jpeg, out var w, out _, out _);
        Assert.NotNull(pixels);
        // DC-only blocks: native samples 0, 4095, 1, 2049 and 2047.
        byte[] expected = [0, 255, 0, 128, 127];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < w; x++)
                Assert.Equal(expected[x / 8], pixels[y * w + x]);
    }

    [Theory]
    [InlineData(0xC0, 12)]
    [InlineData(0xC9, 12)]
    [InlineData(0xCA, 12)]
    [InlineData(0xCB, 12)]
    [InlineData(0xC5, 12)]
    [InlineData(0xC6, 12)]
    [InlineData(0xC1, 9)]
    [InlineData(0xC1, 16)]
    [InlineData(0xC2, 16)]
    public void DoesNotEnableUnsupportedFrameModesOrPrecisions(int marker, int precision)
    {
        var data = Read("gray-extremes").Jpeg;
        var frame = FindFrame(data);
        data[frame + 1] = (byte)marker;
        data[frame + 4] = (byte)precision;
        Assert.Null(JpegDecoder.Decode(data, out _, out _, out _));
    }

    static int FindFrame(byte[] data)
    {
        for (var p = 2; p + 4 < data.Length; p += 2 + (data[p + 2] << 8 | data[p + 3]))
            if (data[p] == 255 && data[p + 1] is 0xC1 or 0xC2) return p;
        throw new InvalidOperationException("fixture has no DCT frame");
    }

    [Theory]
    [InlineData("gray-progressive")]
    [InlineData("rgb-interleaved")]
    [InlineData("ycc-420-progressive")]
    public void DctDecodeExposesEightBitSamplesToPdfImagePipeline(string name)
    {
        var fixture = Read(name);
        var space = fixture.Components == 1 ? "DeviceGray" : "DeviceRGB";
        using var pdf = new MemoryStream();
        void Write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
        Write("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n");
        Write("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /XObject << /Im0 4 0 R >> >> >> endobj\n");
        Write($"4 0 obj << /Type /XObject /Subtype /Image /Width {fixture.Width} /Height {fixture.Height} /ColorSpace /{space} /BitsPerComponent 12 /Filter /DCTDecode /Length {fixture.Jpeg.Length} >> stream\n");
        pdf.Write(fixture.Jpeg);
        Write("\nendstream\nendobj\ntrailer << /Root 1 0 R >>\n%%EOF");
        var doc = PdfDocument.Load(pdf.ToArray());
        var file = doc.File;
        var resources = doc.Pages[0].Resources;
        var objects = (PdfDictionary)file.Resolve(resources.Get("XObject"))!;
        var image = (PdfStream)file.Resolve(objects.Get("Im0"))!;
        var decoded = ImageDecoder.Decode(file, image, resources, out _, out var warning);
        Assert.Null(warning);
        Assert.NotNull(decoded);
        Assert.Equal((fixture.Width, fixture.Height), (decoded.Width, decoded.Height));
        var direct = JpegDecoder.Decode(fixture.Jpeg, out _, out _, out _)!;
        for (var i = 0; i < fixture.Width * fixture.Height; i++)
        {
            for (var k = 0; k < 3; k++)
                Assert.Equal(direct[i * fixture.Components + (fixture.Components == 1 ? 0 : k)], decoded.Rgba[i * 4 + k]);
            Assert.Equal(255, decoded.Rgba[i * 4 + 3]);
        }
    }
}
