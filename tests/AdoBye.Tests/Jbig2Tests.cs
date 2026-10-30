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
