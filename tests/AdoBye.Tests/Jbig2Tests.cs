using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

public class Jbig2Tests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jbig2");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "jbig2"), "*.pdf")
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(a => (string)a[0]);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DecodesAgainstReferencePixels(string name)
    {
        var expected = File.ReadAllText(Path.Combine(Dir, name + ".txt"));
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var f = doc.File;
        var xo = (PdfDictionary)f.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)f.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(f, img, doc.Pages[0].Resources, out _, out var warn);
        Assert.NotNull(data);
        Assert.Null(warn);
        Assert.Equal(expected.Length, data!.Width * data.Height);
        var bad = 0;
        for (var i = 0; i < expected.Length; i++)
            if ((data.Rgba[i * 4] > 127) != (expected[i] == '1')) bad++;
        Assert.Equal(0, bad);
    }

    [Fact]
    public void HuffmanRefinementAggregateStreamsSurviveTruncationAndCorruption()
    {
        foreach (var name in new[] { "huffman-refagg-single", "huffman-refagg-aggregate" })
        {
            var pdf = File.ReadAllBytes(Path.Combine(Dir, name + ".pdf"));
            var text = System.Text.Encoding.Latin1.GetString(pdf);
            var start = text.IndexOf("stream\n", text.IndexOf("/JBIG2Decode", StringComparison.Ordinal), StringComparison.Ordinal) + 7;
            var end = text.LastIndexOf("\nendstream", StringComparison.Ordinal);
            var stream = pdf[start..end];
            Assert.NotNull(Jbig2Decoder.Decode(stream, null, 64, 20, out var warning));
            Assert.Null(warning);

            for (var len = 0; len < stream.Length; len++)
                Jbig2Decoder.Decode(stream[..len], null, 64, 20, out _);
            var rng = new Random(7);
            for (var i = 0; i < 400; i++)
            {
                var mutated = (byte[])stream.Clone();
                mutated[rng.Next(mutated.Length)] ^= (byte)(1 << rng.Next(8));
                Jbig2Decoder.Decode(mutated, null, 64, 20, out _);
            }
        }
    }

    [Fact]
    public void DecodesExtendedTemplateAndColourFixturesFromRawStreams()
    {
        foreach (var name in new[] { "gen-t0-ext", "gen-t0-ext-at", "gen-t0-ext-tpgd", "colour-generic", "colour-text", "stripes-unknown-height" })
        {
            var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
            var xo = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
            var raw = ((PdfStream)doc.File.Resolve(xo.Get("Im0"))!).RawData;
            var expected = File.ReadAllText(Path.Combine(Dir, name + ".txt"));
            var w = name == "colour-text" ? 200 : 240;
            var h = expected.Length / w;
            var data = Jbig2Decoder.Decode(raw, null, w, h, out var warning);
            Assert.Null(warning);
            Assert.NotNull(data);
            var stride = (w + 7) / 8;
            for (var i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i] == '1', (data![i / w * stride + i % w / 8] >> (7 - i % w % 8) & 1) == 1);
        }
    }

    static byte[] Be(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    static byte[] Bits(string bits)
    {
        var packed = new byte[(bits.Length + 7) / 8];
        for (var i = 0; i < bits.Length; i++)
            if (bits[i] == '1') packed[i >> 3] |= (byte)(0x80 >> (i & 7));
        return packed;
    }

    static byte[] PageInfoSegment(int w, int h, byte flags, int striping) =>
        [.. Be(w), .. Be(h), .. Be(0), .. Be(0), flags, (byte)(striping >> 8), (byte)striping];

    // 8x2 MMR generic region with a black pixel at x = 1 in both rows; the colour extension appends a 4-byte GBFGCOLID.
    static byte[] MmrRegion(byte regionFlags, int y = 0, bool colourTrailer = false)
    {
        const string eofb = "000000000001000000000001";
        byte[] payload = [.. Be(8), .. Be(2), .. Be(0), .. Be(y), regionFlags, 1, .. Bits("001" + "000111" + "010" + "1" + "111" + eofb)];
        return colourTrailer ? [.. payload, .. Be(32)] : payload;
    }

    static byte[] Header(int num, byte type, int length) => [.. Be(num), type, 0, 1, .. Be(length)];

    static byte[] Embedded(params (int Num, byte Type, byte[] Payload)[] segs) =>
        [.. segs.SelectMany(s => Header(s.Num, s.Type, s.Payload.Length).Concat(s.Payload))];

    static readonly byte[] FileMagic = [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void SequentialAndRandomAccessFileOrganisationsMatchEmbeddedStream()
    {
        (int, byte, byte[])[] segs = [(1, 48, PageInfoSegment(8, 2, 0, 0)), (2, 38, MmrRegion(0)), (3, 49, []), (4, 51, [])];
        var embedded = Embedded(segs);
        var sequential = (byte[])[.. FileMagic, 1, .. Be(1), .. embedded];
        var random = (byte[])[.. FileMagic, 0, .. Be(1), .. segs.SelectMany(s => Header(s.Item1, s.Item2, s.Item3.Length)), .. segs.SelectMany(s => s.Item3)];
        foreach (var stream in new[] { embedded, sequential, random })
        {
            var data = Jbig2Decoder.Decode(stream, null, 8, 2, out var warning);
            Assert.Null(warning);
            Assert.Equal(new byte[] { 0xBF, 0xBF }, data);
        }
    }

    [Fact]
    public void SecondPageInAFileIsIgnored()
    {
        // Page 2 would replace the page with an all-black one if it were processed.
        var second = MmrRegion(0);
        byte[] page2 = [.. Be(4), 48, 0, 2, .. Be(19), .. PageInfoSegment(8, 2, 0x04, 0), .. Be(5), 38, 0, 2, .. Be(second.Length), .. second];
        byte[] stream = [.. FileMagic, 1, .. Be(2), .. Embedded((1, 48, PageInfoSegment(8, 2, 0, 0)), (2, 38, MmrRegion(0)), (3, 49, [])), .. page2];
        Assert.Equal(new byte[] { 0xBF, 0xBF }, Jbig2Decoder.Decode(stream, null, 8, 2, out _));
    }

    [Fact]
    public void ColourRegionIsDecodedAsTransparentMonochromeInk()
    {
        // COLEXTFLAG (bit 3) + REPLACE onto a black page: the transparent background leaves the page unchanged.
        var colour = Embedded((1, 48, PageInfoSegment(8, 2, 0x84, 0)), (2, 38, MmrRegion(4 | 8, colourTrailer: true)), (3, 49, []));
        Assert.Equal(new byte[] { 0x00, 0x00 }, Jbig2Decoder.Decode(colour, null, 8, 2, out var warning));
        Assert.Null(warning);
        // Without COLEXTFLAG the same REPLACE region overwrites the black page.
        var mono = Embedded((1, 48, PageInfoSegment(8, 2, 0x04, 0)), (2, 38, MmrRegion(4)), (3, 49, []));
        Assert.Equal(new byte[] { 0xBF, 0xBF }, Jbig2Decoder.Decode(mono, null, 8, 2, out _));
        // Colour region on a white page: ink only.
        var onWhite = Embedded((1, 48, PageInfoSegment(8, 2, 0x80, 0)), (2, 38, MmrRegion(4 | 8, colourTrailer: true)), (3, 49, []));
        Assert.Equal(new byte[] { 0xBF, 0xBF }, Jbig2Decoder.Decode(onWhite, null, 8, 2, out _));
    }

    [Fact]
    public void StripedPageOfUnknownHeightGrowsToEndOfStripeRow()
    {
        var stream = Embedded((1, 48, PageInfoSegment(8, -1, 0x04, 0x8002)), (2, 38, MmrRegion(4)),
            (3, 50, Be(3)), (4, 49, []));
        // Page height is 0xFFFFFFFF (unknown); the end-of-stripe row 3 extends the black-filled page below the 2-row region.
        Assert.Equal(new byte[] { 0xBF, 0xBF, 0x00, 0x00 }, Jbig2Decoder.Decode(stream, null, 8, 4, out var warning));
        Assert.Null(warning);
    }

    [Fact]
    public void MalformedHeadersAndHugeDimensionsAreRejectedWithoutThrowing()
    {
        var good = Embedded((1, 48, PageInfoSegment(8, 2, 0, 0)), (2, 38, MmrRegion(0)), (3, 49, []));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < Math.Min(good.Length, 80); i++)
            foreach (var v in new byte[] { 0x00, 0x7F, 0x80, 0xFF })
            {
                var m = (byte[])good.Clone();
                m[i] = v;
                Jbig2Decoder.Decode(m, null, 8, 2, out _);
            }
        // Page and region dimensions near int.MaxValue, a 0xFFFFFFFF-adjacent segment length and an absurd referred-segment count.
        var hugePage = Embedded((1, 48, PageInfoSegment(int.MaxValue, int.MaxValue, 0, 0)), (2, 38, MmrRegion(0)));
        Assert.Null(Jbig2Decoder.Decode(hugePage, null, 8, 2, out _));
        byte[] hugeLen = [.. Be(1), 48, 0, 1, .. Be(0x7FFFFFFE), .. PageInfoSegment(8, 2, 0, 0)];
        Jbig2Decoder.Decode(hugeLen, null, 8, 2, out _);
        byte[] hugeRefs = [.. Be(1), 38, 0xE0, 0x1F, 0xFF, 0xFF, 0xFF, 1, .. Be(0), 0, 0, 0];
        Jbig2Decoder.Decode(hugeRefs, null, 8, 2, out _);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Fact]
    public void NewFixtureStreamsSurviveTruncationAndCorruption()
    {
        var rng = new Random(11);
        foreach (var name in new[] { "gen-t0-ext-tpgd", "colour-text", "stripes-unknown-height", "comb-def1-02134", "intermediate-refine", "refine-page-offset",
                     "halftone-skip-xor", "halftone-skip-replace", "halftone-mmr-xor-rot", "generic-unknown-length", "symdict-ctx-retained-partial-export",
                     "huffman-custom-tables", "huffman-oddwidth-refagg-aggregate-rdwrdh", "huffman-oddwidth-mmr" })
        {
            var path = Path.Combine(Dir, name + ".pdf");
            if (!File.Exists(path)) continue;
            var doc = PdfDocument.Load(File.ReadAllBytes(path));
            var xo = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
            var raw = ((PdfStream)doc.File.Resolve(xo.Get("Im0"))!).RawData;
            for (var len = 0; len < raw.Length; len += 13) Jbig2Decoder.Decode(raw[..len], null, 64, 64, out _);
            for (var i = 0; i < 60; i++)
            {
                var m = (byte[])raw.Clone();
                m[rng.Next(m.Length)] ^= (byte)(1 << rng.Next(8));
                Jbig2Decoder.Decode(m, null, 64, 64, out _);
            }
        }
    }

    [Fact]
    public void DecodesMmrHalftonePatternAndGridPlanes()
    {
        const string eofb = "000000000001000000000001";
        var stream = new List<byte>();
        AddSegment(stream, 1, 48, PageInfo(4, 4));
        AddSegment(stream, 2, 16, PatternDictionary());
        AddSegment(stream, 3, 22, HalftoneRegion(), [2]);
        AddSegment(stream, 4, 49, []);
        AddSegment(stream, 5, 51, []);

        var data = Jbig2Decoder.Decode([.. stream], null, 4, 4, out var warning);

        Assert.Null(warning);
        // Two 2x2 patterns (white, black), selected by grid rows 01 / 10.
        Assert.Equal(new byte[] { 0xCF, 0xCF, 0x3F, 0x3F }, data);

        byte[] PageInfo(int width, int height)
        {
            var payload = new List<byte>();
            WriteInt(payload, width);
            WriteInt(payload, height);
            WriteInt(payload, 0);
            WriteInt(payload, 0);
            payload.Add(0);
            payload.AddRange([0, 0]);
            return [.. payload];
        }

        byte[] PatternDictionary()
        {
            var payload = new List<byte> { 1, 2, 2 };
            WriteInt(payload, 1);
            payload.AddRange(PackBits("001011111" + "11" + eofb));
            return [.. payload];
        }

        byte[] HalftoneRegion()
        {
            var payload = new List<byte>();
            WriteInt(payload, 4);
            WriteInt(payload, 4);
            WriteInt(payload, 0);
            WriteInt(payload, 0);
            payload.Add(0);
            payload.Add(1);
            WriteInt(payload, 2);
            WriteInt(payload, 2);
            WriteInt(payload, 0);
            WriteInt(payload, 0);
            payload.AddRange([2, 0, 0, 0]);
            payload.AddRange(PackBits(
                "001" + "000111" + "010" + // Horizontal: white 1, black 1.
                "001" + "00110101" + "010" + "1" + // Horizontal: white 0, black 1; vertical 0 ends white 1.
                eofb));
            return [.. payload];
        }

        static byte[] PackBits(string bits)
        {
            var packed = new byte[(bits.Length + 7) / 8];
            for (var i = 0; i < bits.Length; i++)
                if (bits[i] == '1')
                    packed[i >> 3] |= (byte)(0x80 >> (i & 7));
            return packed;
        }

        static void AddSegment(List<byte> destination, byte number, byte type, byte[] payload, byte[]? references = null)
        {
            references ??= [];
            WriteInt(destination, number);
            destination.Add(type);
            destination.Add((byte)(references.Length << 5));
            destination.AddRange(references);
            destination.Add(type is 16 or 51 ? (byte)0 : (byte)1);
            WriteInt(destination, payload.Length);
            destination.AddRange(payload);
        }

        static void WriteInt(List<byte> destination, int value)
        {
            destination.Add((byte)(value >> 24));
            destination.Add((byte)(value >> 16));
            destination.Add((byte)(value >> 8));
            destination.Add((byte)value);
        }
    }
}
