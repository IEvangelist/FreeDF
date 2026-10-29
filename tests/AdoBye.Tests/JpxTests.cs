using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

public class JpxTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jpx");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Dir, "*.pdf")
            .Where(p => !Path.GetFileName(p).StartsWith("poc-malformed-", StringComparison.Ordinal))
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) })
            .OrderBy(a => (string)a[0]);

    public static IEnumerable<object[]> MalformedPocFixtures() =>
        Directory.GetFiles(Dir, "poc-malformed-*.pdf")
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) })
            .OrderBy(a => (string)a[0]);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DecodesAgainstOpenJpegReference(string name)
    {
        var meta = File.ReadAllText(Path.Combine(Dir, name + ".meta")).Split(' ');
        string mode = meta[0];
        int w = int.Parse(meta[1]), h = int.Parse(meta[2]), tol = int.Parse(meta[3]);
        var expected = File.ReadAllBytes(Path.Combine(Dir, name + ".bin"));

        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var f = doc.File;
        var xo = (PdfDictionary)f.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)f.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(f, img, doc.Pages[0].Resources, out _, out var warn);
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

    [Theory]
    [InlineData("ppm-lossless", 0xFF60, "duplicate PPM")]
    [InlineData("ppt-lossless", 0xFF61, "duplicate PPT")]
    public void RejectsDuplicatePackedHeaderMarkerIndices(string name, int marker, string warning)
    {
        var data = Codestream(name);
        var markerPos = FindMarker(data, marker);
        var markerLength = ReadU16(data, markerPos + 2);
        var segmentLength = markerLength + 2;
        var insertPos = markerPos + segmentLength;
        var duplicate = new byte[data.Length + segmentLength];
        Buffer.BlockCopy(data, 0, duplicate, 0, insertPos);
        Buffer.BlockCopy(data, markerPos, duplicate, insertPos, segmentLength);
        Buffer.BlockCopy(data, insertPos, duplicate, insertPos + segmentLength, data.Length - insertPos);

        if (marker == 0xFF61)
        {
            var sot = FindMarker(duplicate, 0xFF90);
            WriteU32(duplicate, sot + 6, ReadU32(duplicate, sot + 6) + (uint)segmentLength);
        }

        Assert.Null(JpxDecoder.Decode(duplicate, 0, false, false, out var actualWarning));
        Assert.Contains(warning, actualWarning);
    }

    [Theory]
    [InlineData("ppm-lossless", 0xFF60, "missing PPM marker index")]
    [InlineData("ppt-lossless", 0xFF61, "missing PPT marker index")]
    public void RejectsNoncontiguousPackedHeaderMarkerIndices(string name, int marker, string warning)
    {
        var data = Codestream(name);
        var first = FindMarker(data, marker);
        var second = first + 2 + ReadU16(data, first + 2);
        data[second + 4] = 2;

        Assert.Null(JpxDecoder.Decode(data, 0, false, false, out var actualWarning));
        Assert.Contains(warning, actualWarning);
    }

    [Theory]
    [InlineData("ppm-lossless", 0xFF60)]
    [InlineData("ppt-lossless", 0xFF61)]
    public void RejectsMalformedPackedHeaderMarkerLengths(string name, int marker)
    {
        var data = Codestream(name);
        var markerPos = FindMarker(data, marker);
        WriteU16(data, markerPos + 2, 2);

        Assert.Null(JpxDecoder.Decode(data, 0, false, false, out var warning));
        Assert.Contains($"malformed {(marker == 0xFF60 ? "PPM" : "PPT")} marker", warning);
    }

    [Fact]
    public void RejectsTruncatedPpmPacketHeaderPayload()
    {
        var data = Codestream("ppm-lossless");
        var markerPos = FindMarker(data, 0xFF60);
        WriteU32(data, markerPos + 5, uint.MaxValue);

        Assert.Null(JpxDecoder.Decode(data, 0, false, false, out var warning));
        Assert.Contains("truncated PPM packet-header payload", warning);
    }

    [Fact]
    public void RejectsUnassignedPpmPacketHeaderData()
    {
        var data = Codestream("ppm-lossless");
        var markerPos = FindMarker(data, 0xFF60);
        WriteU32(data, markerPos + 5, 0);

        Assert.Null(JpxDecoder.Decode(data, 0, false, false, out var warning));
        Assert.Contains("unused PPM packet-header data", warning);
    }

    [Fact]
    public void RejectsTruncatedPptPacketHeaderPayload()
    {
        var data = Codestream("ppt-lossless");
        var first = FindMarker(data, 0xFF61);
        var second = first + 2 + ReadU16(data, first + 2);
        var length = ReadU16(data, second + 2);
        var remove = second + 2 + length - 1;
        var truncated = new byte[data.Length - 1];
        Buffer.BlockCopy(data, 0, truncated, 0, remove);
        Buffer.BlockCopy(data, remove + 1, truncated, remove, data.Length - remove - 1);
        WriteU16(truncated, second + 2, length - 1);
        var sot = FindMarker(truncated, 0xFF90);
        WriteU32(truncated, sot + 6, ReadU32(truncated, sot + 6) - 1);

        Assert.Null(JpxDecoder.Decode(truncated, 0, false, false, out var warning));
        Assert.Contains("truncated packet header", warning);
    }

    [Fact]
    public void RejectsPptMarkerInMainHeader()
    {
        var data = Codestream("ppt-lossless");
        var markerPos = FindMarker(data, 0xFF61);
        var segmentLength = ReadU16(data, markerPos + 2) + 2;
        var mainHeaderSot = FindMarker(data, 0xFF90);
        var misplaced = Insert(data, mainHeaderSot, data.AsSpan(markerPos, segmentLength).ToArray());

        Assert.Null(JpxDecoder.Decode(misplaced, 0, false, false, out var warning));
        Assert.Contains("PPT marker segment is only valid in a tile-part header", warning);
    }

    [Fact]
    public void MultiRangeSpatialPocFixtureContainsBothProgressionChanges()
    {
        var data = Codestream("poc-spatial-multirange");
        var markerPos = FindMarker(data, 0xFF5F);

        Assert.Equal(16, ReadU16(data, markerPos + 2));
        Assert.Equal(
            new byte[] { 0, 0, 0, 1, 4, 3, 3, 0, 0, 0, 3, 4, 3, 4 },
            data.AsSpan(markerPos + 4, 14).ToArray());
    }

    [Fact]
    public void RejectsPpmMarkerInTilePartHeader()
    {
        var data = Codestream("ppt-lossless");
        var sot = FindMarker(data, 0xFF90);
        var marker = PpmMarker();
        var misplaced = Insert(data, sot + 12, marker);
        WriteU32(misplaced, sot + 6, ReadU32(misplaced, sot + 6) + (uint)marker.Length);

        Assert.Null(JpxDecoder.Decode(misplaced, 0, false, false, out var warning));
        Assert.Contains("PPM marker segment is only valid in the main header", warning);
    }

    [Fact]
    public void RejectsCombinedPpmAndPptMarkers()
    {
        var data = Codestream("ppt-lossless");
        var sot = FindMarker(data, 0xFF90);
        var combined = Insert(data, sot, PpmMarker());

        Assert.Null(JpxDecoder.Decode(combined, 0, false, false, out var warning));
        Assert.Contains("PPM and PPT markers cannot be combined", warning);
    }

    static byte[] Codestream(string name)
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var xo = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        return ((PdfStream)doc.File.Resolve(xo.Get("Im0"))!).RawData;
    }

    static int FindMarker(byte[] data, int marker)
    {
        for (var i = 0; i + 1 < data.Length; i++)
            if (data[i] == (byte)(marker >> 8) && data[i + 1] == (byte)marker) return i;
        throw new InvalidDataException($"marker 0x{marker:X4} not found");
    }

    static byte[] PpmMarker() => [0xFF, 0x60, 0, 7, 0, 0, 0, 0, 0];

    static byte[] Insert(byte[] data, int offset, byte[] insert)
    {
        var result = new byte[data.Length + insert.Length];
        Buffer.BlockCopy(data, 0, result, 0, offset);
        Buffer.BlockCopy(insert, 0, result, offset, insert.Length);
        Buffer.BlockCopy(data, offset, result, offset + insert.Length, data.Length - offset);
        return result;
    }

    static int ReadU16(byte[] data, int offset) => data[offset] << 8 | data[offset + 1];

    static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);

    static void WriteU16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    [Theory]
    [MemberData(nameof(MalformedPocFixtures))]
    public void ReportsMalformedProgressionOrderChanges(string name)
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var f = doc.File;
        var xo = (PdfDictionary)f.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)f.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(f, img, doc.Pages[0].Resources, out _, out var warn);

        Assert.Null(data);
        Assert.NotNull(warn);
        Assert.True(warn!.Contains("POC", StringComparison.OrdinalIgnoreCase));
    }
}
