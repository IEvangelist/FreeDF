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
    public void DecodesAgainstReferenceSamples(string name)
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
    [InlineData("ppm-lossless", 0xFF60)]
    [InlineData("ppt-lossless", 0xFF61)]
    public void PackedHeadersEndAtCompletePacketBoundariesAndPreserveLegacyRgbSamples(string name, int marker)
    {
        var data = Codestream(name);
        var cod = FindMarker(data, 0xFF52);
        Assert.Equal(1, ReadU16(data, cod + 6));
        Assert.Equal(0, data[cod + 8]);
        Assert.Equal(0, data[cod + 9]);
        var sot = FindMarker(data, 0xFF90);
        var sod = FindMarker(data, 0xFF93);
        var first = FindMarker(data, marker);
        var second = first + 2 + ReadU16(data, first + 2);
        Assert.True(marker == 0xFF60 ? second < sot : first > sot && second < sod);
        Assert.Equal(marker, ReadU16(data, second));
        Assert.Equal(0, data[first + 4]);
        Assert.Equal(1, data[second + 4]);
        Assert.Equal(marker == 0xFF60 ? 10 : 6, ReadU16(data, first + 2));
        Assert.Equal(9, ReadU16(data, second + 2));
        Assert.Equal(marker == 0xFF60 ? sot : sod, second + 2 + ReadU16(data, second + 2));
        Assert.Equal((uint)(data.Length - 2 - sot), ReadU32(data, sot + 6));
        Assert.Equal(0xFFD9, ReadU16(data, data.Length - 2));

        var firstHeaders = data.AsSpan(first + 5, ReadU16(data, first + 2) - 3).ToArray();
        if (marker == 0xFF60)
        {
            Assert.Equal(9u, ReadU32(firstHeaders, 0));
            firstHeaders = firstHeaders[4..];
        }
        // Each component's complete single-code-block header is CF B4 B8 (T.800 A.7.4/A.7.5).
        Assert.Equal(new byte[] { 0xCF, 0xB4, 0xB8 }, firstHeaders);
        Assert.Equal(new byte[] { 0xCF, 0xB4, 0xB8, 0xCF, 0xB4, 0xB8 },
            data.AsSpan(second + 5, ReadU16(data, second + 2) - 3).ToArray());

        var expected = Enumerable.Range(64, 64).SelectMany(value => Enumerable.Repeat((byte)value, 3)).ToArray();
        Assert.Equal("RGB 8 8 0", File.ReadAllText(Path.Combine(Dir, name + ".meta")));
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(Dir, name + ".bin")));
        var image = JpxDecoder.Decode(data, 3, false, false, out var warning);
        Assert.NotNull(image);
        Assert.Null(warning);
        Assert.Equal(8, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal(3, image.Components);
        Assert.Equal(expected, image.Samples);
    }

    [Theory]
    [InlineData("ppm-lossless", 0xFF60)]
    [InlineData("ppt-lossless", 0xFF61)]
    public void PackedMarkerSplitsRequireCompletePacketHeaders(string name, int marker)
    {
        for (var split = 1; split < 9; split++)
        {
            var data = SplitPackedMarkers(name, marker, split + (marker == 0xFF60 ? 4 : 0));
            var image = JpxDecoder.Decode(data, 3, false, false, out var warning);
            if (split % 3 == 0)
            {
                Assert.NotNull(image);
                Assert.Null(warning);
                Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, name + ".bin")), image.Samples);
            }
            else
            {
                Assert.Null(image);
                Assert.Contains("packet header crosses a packed marker segment boundary", warning);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsPpmSegmentEndingWithinOrImmediatelyAfterNppm(int split)
    {
        var data = SplitPackedMarkers("ppm-lossless", 0xFF60, split);
        Assert.Null(JpxDecoder.Decode(data, 3, false, false, out var warning));
        Assert.Contains("PPM marker segment ends before a completed packet header", warning);
    }

    static byte[] SplitPackedMarkers(string name, int marker, int split)
    {
        var data = Codestream(name);
        var first = FindMarker(data, marker);
        var second = first + 2 + ReadU16(data, first + 2);
        var end = second + 2 + ReadU16(data, second + 2);
        var payload = data.AsSpan(first + 5, ReadU16(data, first + 2) - 3).ToArray()
            .Concat(data.AsSpan(second + 5, ReadU16(data, second + 2) - 3).ToArray()).ToArray();
        var result = data[..first].ToList();
        void Segment(int index, ReadOnlySpan<byte> bytes)
        {
            result.Add((byte)(marker >> 8)); result.Add((byte)marker);
            var length = bytes.Length + 3;
            result.Add((byte)(length >> 8)); result.Add((byte)length);
            result.Add((byte)index);
            result.AddRange(bytes.ToArray());
        }
        Segment(0, payload.AsSpan(0, split));
        Segment(1, payload.AsSpan(split));
        result.AddRange(data[end..]);
        Assert.Equal(data.Length, result.Count);
        return [.. result];
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

    [Theory]
    [InlineData("poc-ppm-lossless", 0xFF60)]
    [InlineData("poc-ppt-lossless", 0xFF61)]
    public void PocPackedHeadersPreserveProgressionAndComponentSamples(string name, int marker)
    {
        var data = Codestream(name);
        var cod = FindMarker(data, 0xFF52);
        Assert.Equal(0, data[cod + 5]); // Default LRCP differs from the POC schedule.
        Assert.Equal(1, ReadU16(data, cod + 6));
        Assert.Equal(0, data[cod + 8]);
        Assert.Equal(0, data[cod + 9]);
        var poc = FindMarker(data, 0xFF5F);
        Assert.Equal(16, ReadU16(data, poc + 2));
        Assert.Equal(
            new byte[] { 0, 1, 0, 1, 1, 3, 1, 0, 0, 0, 1, 1, 1, 4 },
            data.AsSpan(poc + 4, 14).ToArray());

        var sot = FindMarker(data, 0xFF90);
        var sod = FindMarker(data, 0xFF93);
        var first = FindMarker(data, marker);
        var second = first + 2 + ReadU16(data, first + 2);
        Assert.True(poc < sot);
        Assert.True(marker == 0xFF60 ? second < sot : first > sot && second < sod);
        Assert.Equal(marker, ReadU16(data, second));
        Assert.Equal(0, data[first + 4]);
        Assert.Equal(1, data[second + 4]);
        // Each segment ends after a complete three-byte packet header (T.800 A.7.4/A.7.5).
        Assert.Equal(marker == 0xFF60 ? 10 : 6, ReadU16(data, first + 2));
        Assert.Equal(9, ReadU16(data, second + 2));
        Assert.Equal((uint)(data.Length - 2 - sot), ReadU32(data, sot + 6));
        Assert.Equal(0xFFD9, ReadU16(data, data.Length - 2));

        var headers = data.AsSpan(first + 5, ReadU16(data, first + 2) - 3).ToArray()
            .Concat(data.AsSpan(second + 5, ReadU16(data, second + 2) - 3).ToArray()).ToArray();
        if (marker == 0xFF60)
        {
            Assert.Equal(9u, ReadU32(headers, 0));
            headers = headers[4..];
        }
        Assert.Equal(new byte[] { 0xDF, 0x81, 0xE0, 0xDF, 0x81, 0xE0, 0xDF, 0x81, 0xD0 }, headers);

        var image = JpxDecoder.Decode(data, 3, false, false, out var warning);
        Assert.NotNull(image);
        Assert.Null(warning);
        Assert.Equal(3, image.Components);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, name + ".bin")), image.Samples);
    }

    [Theory]
    [InlineData("ppm-multitile-multipart-lossless", 0xFF60)]
    [InlineData("ppt-multitile-multipart-lossless", 0xFF61)]
    public void PackedHeadersStayAssociatedAcrossTilesAndTileParts(string name, int marker)
    {
        var data = Codestream(name);
        var firstSot = FindMarker(data, 0xFF90);
        var mainPpmIndices = new List<int>();
        var ppmSegments = new List<byte[]>();
        for (var pos = 2; pos < firstSot;)
        {
            var current = ReadU16(data, pos);
            var length = ReadU16(data, pos + 2);
            if (current == 0xFF60)
            {
                mainPpmIndices.Add(data[pos + 4]);
                ppmSegments.Add(data.AsSpan(pos + 5, length - 3).ToArray());
            }
            pos += 2 + length;
        }

        if (marker == 0xFF60)
        {
            Assert.Equal(new[] { 0, 1 }, mainPpmIndices);
            var segmentLengths = ppmSegments.Select(ReadPpmHeaderLengths).ToArray();
            Assert.All(segmentLengths, lengths => Assert.Equal(2, lengths.Count));
            var nppmLengths = segmentLengths.SelectMany(lengths => lengths).ToArray();
            Assert.Equal(4, nppmLengths.Length);
            Assert.Equal(nppmLengths[0], nppmLengths[2]);
            Assert.Equal(nppmLengths[1], nppmLengths[3]);
            Assert.True(nppmLengths[1] > nppmLengths[0]);
        }
        else
        {
            Assert.Empty(mainPpmIndices);
        }

        var parts = ReadTileParts(data);
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1) },
            parts.Select(part => (part.Tile, part.Index)));
        foreach (var part in parts)
        {
            Assert.Equal(2, part.Count);
            Assert.True(part.End > part.Sod + 2, "tile-part has no packet body");
            var pptIndices = new List<int>();
            for (var pos = part.Start + 12; pos < part.Sod;)
            {
                var current = ReadU16(data, pos);
                var length = ReadU16(data, pos + 2);
                if (current == 0xFF61) pptIndices.Add(data[pos + 4]);
                pos += 2 + length;
            }
            if (marker == 0xFF61)
                Assert.Equal(part.Index == 0 ? new[] { 0 } : new[] { 0, 1 }, pptIndices);
            else
                Assert.Empty(pptIndices);
        }

        var image = JpxDecoder.Decode(data, 3, false, false, out var warning);
        Assert.NotNull(image);
        Assert.Null(warning);
        Assert.Equal(16, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, name + ".bin")), image.Samples);
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

    readonly record struct TilePart(int Tile, int Index, int Count, int Start, int Sod, int End);

    [Theory]
    [InlineData("ppt-early-headers-multitile-multipart-lossless", false)]
    [InlineData("ppt-header-only-first-multitile-multipart-lossless", true)]
    public void EarlierPptHeadersRemainAvailableForLaterBodiesAndInterleavedTiles(string name, bool headerOnlyFirst)
    {
        var data = Codestream(name);
        var parts = ReadTileParts(data);
        Assert.Equal(4, parts.Count);
        foreach (var tile in parts.GroupBy(part => part.Tile))
        {
            var first = tile.First();
            var second = tile.Last();
            Assert.Equal(0xFF61, ReadU16(data, first.Start + 12));
            Assert.Equal(second.Start + 12, second.Sod);
            Assert.Equal(headerOnlyFirst, first.End == first.Sod + 2);
            Assert.True(second.End > second.Sod + 2);
        }
        foreach (var order in new[] { new[] { 0, 1, 2, 3 }, new[] { 0, 2, 1, 3 } })
        {
            var reordered = data[..parts[0].Start].Concat(order.SelectMany(i => data[parts[i].Start..parts[i].End])).Concat(data[parts[^1].End..]).ToArray();
            var image = JpxDecoder.Decode(reordered, 3, false, false, out var warning);
            Assert.NotNull(image);
            Assert.Null(warning);
            Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, name + ".bin")), image.Samples);
        }
    }

    [Fact]
    public void EarlierPptMarkerSegmentsStillRequireCompleteHeaders()
    {
        const string name = "ppt-early-headers-multitile-multipart-lossless";
        for (var split = 1; split < 9; split++)
        {
            var data = SplitPackedMarkers(name, 0xFF61, split);
            var image = JpxDecoder.Decode(data, 3, false, false, out var warning);
            if (split % 3 == 0)
            {
                Assert.NotNull(image);
                Assert.Null(warning);
                Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, name + ".bin")), image.Samples);
            }
            else
            {
                Assert.Null(image);
                Assert.Contains("packet header crosses", warning);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PptHeadersCannotBeLaterThanTheirCorrespondingBodies(bool headerOnlyFirst)
    {
        var data = Codestream(headerOnlyFirst ? "ppt-header-only-first-multitile-multipart-lossless" : "ppt-early-headers-multitile-multipart-lossless");
        var parts = ReadTileParts(data);
        var headers = data[(parts[0].Start + 12)..parts[0].Sod];
        var first = data[parts[0].Start..(parts[0].Start + 12)].Concat(data[parts[0].Sod..parts[0].End]).ToArray();
        var second = data[parts[1].Start..(parts[1].Start + 12)].Concat(headers).Concat(data[(parts[1].Start + 12)..parts[1].End]).ToArray();
        WriteU32(first, 6, (uint)first.Length);
        WriteU32(second, 6, (uint)second.Length);
        var changed = data[..parts[0].Start].Concat(first).Concat(second).Concat(data[parts[1].End..]).ToArray();
        var image = JpxDecoder.Decode(changed, 3, false, false, out var warning);
        if (headerOnlyFirst)
        {
            // Moving headers into the body-bearing part is legal when the earlier part has no data.
            Assert.NotNull(image);
            Assert.Null(warning);
        }
        else
        {
            Assert.Null(image);
            Assert.Contains("packet data precedes its PPT header", warning);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    public void RejectsInconsistentTilePartOrderOrCounts(int index, int count)
    {
        var data = Codestream("ppt-early-headers-multitile-multipart-lossless");
        var second = ReadTileParts(data)[1];
        data[second.Start + 10] = (byte)index;
        data[second.Start + 11] = (byte)count;
        Assert.Null(JpxDecoder.Decode(data, 3, false, false, out var warning));
        Assert.Contains("tile-part order or count", warning);
    }

    [Fact]
    public void EarlierHeadersDoNotPermitPacketBodiesToCrossTilePartBoundaries()
    {
        var data = Codestream("ppt-early-headers-multitile-multipart-lossless");
        var parts = ReadTileParts(data);
        var first = data[parts[0].Start..(parts[0].End - 1)];
        var second = data[parts[1].Start..(parts[1].Sod + 2)]
            .Concat(data[(parts[0].End - 1)..parts[0].End])
            .Concat(data[(parts[1].Sod + 2)..parts[1].End]).ToArray();
        WriteU32(first, 6, (uint)first.Length);
        WriteU32(second, 6, (uint)second.Length);
        var changed = data[..parts[0].Start].Concat(first).Concat(second).Concat(data[parts[1].End..]).ToArray();
        Assert.Null(JpxDecoder.Decode(changed, 3, false, false, out var warning));
        Assert.Contains("truncated packet data", warning);
    }

    [Fact]
    public void QueuedHeadersWithNoRemainingBodyAreRejected()
    {
        var data = Codestream("ppt-header-only-first-multitile-multipart-lossless");
        var parts = ReadTileParts(data);
        var last = parts[^1];
        var changed = data[..(last.Sod + 2)].Concat(data[last.End..]).ToArray();
        WriteU32(changed, last.Start + 6, (uint)(last.Sod + 2 - last.Start));
        Assert.Null(JpxDecoder.Decode(changed, 3, false, false, out var warning));
        Assert.Contains("truncated packet data", warning);
    }

    static List<TilePart> ReadTileParts(byte[] data)
    {
        var parts = new List<TilePart>();
        var pos = FindMarker(data, 0xFF90);
        while (pos + 12 <= data.Length && ReadU16(data, pos) == 0xFF90)
        {
            var end = pos + (int)ReadU32(data, pos + 6);
            var sod = pos + 12;
            while (sod + 2 <= end && ReadU16(data, sod) != 0xFF93)
                sod += 2 + ReadU16(data, sod + 2);
            Assert.True(sod + 2 <= end, "tile-part has no SOD marker");
            parts.Add(new TilePart(ReadU16(data, pos + 4), data[pos + 10], data[pos + 11], pos, sod, end));
            pos = end;
        }
        return parts;
    }

    static List<uint> ReadPpmHeaderLengths(byte[] data)
    {
        var lengths = new List<uint>();
        for (var pos = 0; pos < data.Length;)
        {
            Assert.True(data.Length - pos >= 4, "PPM data ends inside an Nppm field");
            var length = ReadU32(data, pos);
            pos += 4;
            Assert.InRange(length, 1u, (uint)(data.Length - pos));
            lengths.Add(length);
            pos += (int)length;
        }
        return lengths;
    }

    static byte[] PpmMarker() => [0xFF, 0x60, 0, 8, 0, 0, 0, 0, 1, 0];

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
