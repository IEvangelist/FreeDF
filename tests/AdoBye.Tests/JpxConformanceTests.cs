using System.Diagnostics;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

/// <summary>
/// JPEG 2000 conformance matrix (T.800 Part 1 corner cases, JP2 boxes, ISO 32000-2 7.4.9 JPXDecode semantics and
/// robustness). Fixtures come from tools/make_jpx_matrix_fixtures.py; their .meta provenance is either "openjpeg"
/// (pixels decoded by OpenJPEG from the same bytes) or "derived" (hand-derived model, see the generator).
/// </summary>
public class JpxConformanceTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jpxmatrix");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Dir, "*.pdf").Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(a => (string)a[0]);

    static byte[] RawStream(string name)
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var xo = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        return ((PdfStream)doc.File.Resolve(xo.Get("Im0"))!).RawData;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void MatrixFixturesMatchExpectedPixels(string name)
    {
        var meta = File.ReadAllText(Path.Combine(Dir, name + ".meta")).Split(' ');
        string mode = meta[0];
        int w = int.Parse(meta[1]), h = int.Parse(meta[2]), tol = int.Parse(meta[3]);
        Assert.Contains(meta[4], new[] { "openjpeg", "openjpeg-samples+rule", "derived" });
        Assert.True(meta[4] == "openjpeg" || meta.Length > 5, name + ": non-openjpeg provenance needs a recorded reason");
        var expected = File.ReadAllBytes(Path.Combine(Dir, name + ".bin"));

        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var xo = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)doc.File.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(doc.File, img, doc.Pages[0].Resources, out _, out var warn);
        Assert.NotNull(data);
        Assert.Null(warn);
        Assert.Equal(w, data!.Width);
        Assert.Equal(h, data.Height);

        var nc = mode.Length;
        int max = 0, bad = 0;
        for (var i = 0; i < w * h; i++)
            for (var k = 0; k < nc; k++)
            {
                var got = mode == "L" ? data.Rgba[i * 4] : data.Rgba[i * 4 + k];
                var diff = Math.Abs(got - expected[i * nc + k]);
                if (diff > tol) bad++;
                if (diff > max) max = diff;
            }
        Assert.True(bad == 0, $"{name}: {bad} samples differ by more than {tol} (max diff {max})");
    }

    // Hand-built single-component codestream whose only packet is empty (header bit 0), so every sample decodes to the
    // DC level shift: 2^(depth-1) for unsigned data and 0 for signed data, both displayed as mid-scale.
    static byte[] EmptyCodestream(int depth, bool signed, int comps = 1, int size = 8)
    {
        var m = new List<byte>();
        void U16(int v) { m.Add((byte)(v >> 8)); m.Add((byte)v); }
        void U32(long v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }
        U16(0xFF4F);
        U16(0xFF51); U16(38 + 3 * comps); U16(0); U32(size); U32(size); U32(0); U32(0); U32(size); U32(size); U32(0); U32(0); U16(comps);
        for (var c = 0; c < comps; c++) { m.Add((byte)((depth - 1) | (signed ? 0x80 : 0))); m.Add(1); m.Add(1); }
        U16(0xFF52); U16(12); m.Add(0); m.Add(0); U16(1); m.Add(0); m.Add(0); m.Add(4); m.Add(4); m.Add(0); m.Add(1);
        U16(0xFF5C); U16(4); m.Add(0x40); m.Add(0x40);
        U16(0xFF90); U16(10); U16(0);         U32(14 + comps); m.Add(0); m.Add(1);
                U16(0xFF93); m.AddRange(new byte[comps]);
        U16(0xFFD9);
        return [.. m];
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(16, true)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(38, false)]
    [InlineData(38, true)]
    public void DeepComponentsDecodeToMidScaleWithoutOverflow(int depth, bool signed)
    {
        var image = JpxDecoder.Decode(EmptyCodestream(depth, signed), 1, false, false, out var warning, allowWide: true);
        Assert.NotNull(image);
        Assert.Null(warning);
        Assert.All(image.Samples, s => Assert.Equal(128, s));
        if (depth > 8)
        {
            Assert.NotNull(image.Samples16);
            for (var i = 0; i < 64; i++) Assert.Equal(0x8000, image.Samples16![i * 2] << 8 | image.Samples16[i * 2 + 1]);
        }
    }

    static byte[] Patch(byte[] src, Action<byte[]> edit)
    {
        var copy = (byte[])src.Clone();
        edit(copy);
        return copy;
    }

    static void PutU32(byte[] d, int p, long v) { d[p] = (byte)(v >> 24); d[p + 1] = (byte)(v >> 16); d[p + 2] = (byte)(v >> 8); d[p + 3] = (byte)v; }

    // Offsets inside SIZ for an EmptyCodestream: SOC(2) SIZ marker(2) Lsiz(2) Rsiz(2) then Xsiz@8, Ysiz@12, XOsiz@16, ...
    [Theory]
    [InlineData("huge-image", 8, 0x7FFFFFFF)]
    [InlineData("huge-height", 12, 0x7FFFFFFF)]
    [InlineData("negative-width", 8, 0x80000000)]
    [InlineData("image-offset-beyond-image", 16, 9)]
    [InlineData("tile-offset-beyond-image-offset", 24 + 8, 1)]
    [InlineData("zero-tile-width", 24, 0)]
    public void MalformedGeometryIsRejectedWithoutAllocating(string label, int offset, long value)
    {
        var data = Patch(EmptyCodestream(8, false), d => PutU32(d, offset, value));
        var sw = Stopwatch.StartNew();
        var image = JpxDecoder.Decode(data, 1, false, false, out var warning);
        Assert.True(sw.ElapsedMilliseconds < 5000, label);
        Assert.Null(image);
        Assert.False(string.IsNullOrEmpty(warning), label);
    }

    [Fact]
    public void MoreThan65535TilesAreRejected()
    {
        var data = Patch(EmptyCodestream(8, false), d => { PutU32(d, 8, 4096); PutU32(d, 12, 4096); PutU32(d, 24, 4); PutU32(d, 28, 4); });
        Assert.Null(JpxDecoder.Decode(data, 1, false, false, out var warning));
        Assert.Contains("tiles", warning);
    }

    [Fact]
    public void SixteenThousandThreeHundredEightyFourComponentsDecode()
    {
        var image = JpxDecoder.Decode(EmptyCodestream(8, false, comps: 16384), 0, false, false, out var warning);
        Assert.NotNull(image);
        Assert.Null(warning);
    }

    [Fact]
    public void ComponentCountBeyondSampleBudgetIsRejectedBeforeAllocation()
    {
        // 16384 components of 256x256 samples would need ~2 GiB of planes.
        var sw = Stopwatch.StartNew();
        Assert.Null(JpxDecoder.Decode(EmptyCodestream(8, false, comps: 16384, size: 256), 0, false, false, out var warning));
        Assert.Contains("size", warning);
        Assert.True(sw.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public void SizLengthMustMatchComponentCount()
    {
        var data = Patch(EmptyCodestream(8, false), d => { d[4] = 0; d[5] = 44; });
        Assert.Null(JpxDecoder.Decode(data, 1, false, false, out var warning));
        Assert.False(string.IsNullOrEmpty(warning));
    }

    [Theory]
    [InlineData("mx-lrcp-53-tiles-offsets-layers")]
    [InlineData("mx-rpcl-97-tiles-offsets-layers")]
    [InlineData("pdf-smaskindata1-nocdef")]
    public void TruncatedAndMutatedStreamsNeverThrow(string name)
    {
        var data = RawStream(name);
        var rng = new Random(12345);
        var sw = Stopwatch.StartNew();
        for (var cut = 1; cut < data.Length; cut += Math.Max(1, data.Length / 40))
            _ = JpxDecoder.Decode(data[..cut], 0, false, false, out _);
        // Mutations stay clear of the SIZ geometry (first 50 bytes) so each trial keeps a small allocation.
        for (var i = 0; i < 250; i++)
        {
            var copy = (byte[])data.Clone();
            var span = 1 + rng.Next(3);
            for (var k = 0; k < span; k++) copy[50 + rng.Next(copy.Length - 50)] = (byte)rng.Next(256);
            _ = JpxDecoder.Decode(copy, 0, false, false, out _);
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"{name} took {sw.Elapsed}");
    }

    [Fact]
    public void OversizedPaletteAndUnassociatedColourChannelsAreIgnored()
    {
        var jp2 = RawStream("ct-palette-3col");
        var pclr = FindBox(jp2, "pclr");
        var big = Patch(jp2, d => { d[pclr + 8] = 0x04; d[pclr + 9] = 0x01; });
        var image = JpxDecoder.Decode(big, 0, false, false, out _);
        Assert.NotNull(image);
        Assert.Equal(1, image.Components);

        var cdefFile = RawStream("ct-cdef-bgr-reordered");
        var cdef = FindBox(cdefFile, "cdef");
        var unassociated = Patch(cdefFile, d => { d[cdef + 8 + 2 + 4] = 0xFF; d[cdef + 8 + 2 + 5] = 0xFF; });
        var two = JpxDecoder.Decode(unassociated, 0, false, false, out _);
        Assert.NotNull(two);
        Assert.Equal(2, two.Components);
    }

    static int FindBox(byte[] d, string type)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        for (var i = 4; i + 4 <= d.Length; i++)
            if (d[i] == t[0] && d[i + 1] == t[1] && d[i + 2] == t[2] && d[i + 3] == t[3]) return i - 4;
        throw new InvalidDataException(type);
    }

    [Fact]
    public void CmykEnumeratedColourSpaceKeepsFourChannels()
    {
        var jp2 = File.ReadAllBytes(Path.Combine(Dir, "raw-ct-colr-cmyk.jp2"));
        var expected = File.ReadAllBytes(Path.Combine(Dir, "raw-ct-colr-cmyk.bin"));
        var image = JpxDecoder.Decode(jp2, 0, false, false, out var warning);
        Assert.NotNull(image);
        Assert.Null(warning);
        Assert.Equal(4, image.Components);
        Assert.Equal(expected, image.Samples);
    }
}
