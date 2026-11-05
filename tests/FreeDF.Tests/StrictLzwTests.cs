using PortableDocument.Core;

namespace FreeDF.Tests;

public class StrictLzwTests
{
    [Theory]
    [InlineData("eof")]
    [InlineData("partial")]
    [InlineData("clear")]
    [InlineData("padding")]
    [InlineData("code")]
    public void Strict_lzw_rejects_malformed_input_but_ordinary_recovery_is_preserved(string variant)
    {
        var (encoded, expected) = Encode(variant);
        Assert.Equal(expected, Decode(encoded, false));
        Assert.Throws<InvalidDataException>(() => Decode(encoded, true));
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/ASCIIHexDecode /LZWDecode]",
                "null", false, repair, encodedOverride: Convert.ToHexString(encoded) + ">"));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Complete_lzw_preserves_literal_members_and_cache(int early)
    {
        var (encoded, expected) = Encode("complete");
        Assert.Equal(expected, Decode(encoded, true, early));
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("[/ASCIIHexDecode /LZWDecode]",
                "null", false, repair, decodeParms: $"[null << /EarlyChange {early} >>]",
                encodedOverride: Convert.ToHexString(encoded) + ">"));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Strict_lzw_supports_complete_empty_streams(int early)
        => Assert.Equal(Array.Empty<byte>(), Decode(Pack([256, 257]), true, early));

    [Fact]
    public void Strict_lzw_matches_independent_libtiff_fixture()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "filters");
        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "lzw-libtiff.exp")),
            Decode(File.ReadAllBytes(Path.Combine(dir, "lzw-libtiff.bin")), true));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(1, 600)]
    [InlineData(0, 12000)]
    [InlineData(1, 12000)]
    public void Strict_lzw_preserves_width_transitions_and_full_table_resets(int early, int size)
    {
        var source = new byte[size];
        new Random(42).NextBytes(source);
        var codes = new List<int> { 256 };
        var literals = 0;
        foreach (var value in source)
        {
            if (literals == 3839)
            {
                codes.Add(256);
                literals = 0;
            }
            codes.Add(value);
            literals++;
        }
        codes.Add(257);
        var encoded = Pack(codes, early);
        Assert.Equal(source, Decode(encoded, false, early));
        Assert.Equal(source, Decode(encoded, true, early));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Strict_lzw_rejects_out_of_range_early_change(int early)
    {
        var (encoded, expected) = Encode("complete");
        Assert.Equal(expected, Decode(encoded, false, early));
        Assert.Throws<InvalidDataException>(() => Decode(encoded, true, early));
    }

    [Theory]
    [InlineData("boolean")]
    [InlineData("name")]
    [InlineData("array")]
    public void Strict_lzw_requires_integer_early_change(string kind)
    {
        PdfObject value = kind switch
        {
            "boolean" => new PdfBoolean(true), "name" => new PdfName("One"), _ => new PdfArray([])
        };
        var parms = new PdfDictionary(new Dictionary<string, PdfObject> { ["EarlyChange"] = value });
        var (encoded, expected) = Encode("complete");
        Assert.Equal(expected, PdfFilters.Decode(encoded, [("LZWDecode", parms)], x => x).Data);
        Assert.Throws<InvalidDataException>(() =>
            PdfFilters.Decode(encoded, [("LZWDecode", parms)], x => x, strictContentFilters: true));
    }

    [Fact]
    public void Strict_lzw_preserves_absent_and_null_early_change_defaults()
    {
        var (encoded, expected) = Encode("complete");
        foreach (var parms in new PdfDictionary?[]
        {
            null, new(new Dictionary<string, PdfObject> { ["EarlyChange"] = PdfNull.Instance })
        })
            Assert.Equal(expected,
                PdfFilters.Decode(encoded, [("LZWDecode", parms)], x => x, strictContentFilters: true).Data);
    }

    [Fact]
    public void Strict_lzw_accepts_the_next_entry_special_case()
        => Assert.Equal("AAA"u8.ToArray(), Decode(Pack([256, 65, 258, 257]), true));

    [Fact]
    public void Strict_lzw_rejects_growth_beyond_the_full_table()
    {
        var codes = new List<int> { 256 };
        codes.AddRange(Enumerable.Repeat(32, 3840));
        codes.Add(257);
        var encoded = Pack(codes);
        Assert.Equal(Enumerable.Repeat((byte)32, 3840), Decode(encoded, false));
        Assert.Throws<InvalidDataException>(() => Decode(encoded, true));
    }

    internal static (byte[] Encoded, byte[] Expected) Encode(string variant)
    {
        var expected = "2 0 << /Type /Target >>"u8.ToArray();
        var codes = new List<int>();
        if (variant != "clear") codes.Add(256);
        codes.AddRange(expected.Select(x => (int)x));
        if (variant == "code")
        {
            codes.Add(32);
            codes.Add(511);
            expected = [.. expected, 32, 32, 32];
        }
        if (variant != "eof") codes.Add(257);
        var encoded = Pack(codes);
        if (variant == "partial") encoded = encoded[..^1];
        if (variant == "padding") encoded[^1] |= 1;
        return (encoded, expected);
    }

    static byte[] Pack(IEnumerable<int> codes, int early = 1)
    {
        var chunks = new List<string>();
        var width = 9;
        var next = 258;
        var previous = false;
        foreach (var code in codes)
        {
            chunks.Add(Convert.ToString(code, 2).PadLeft(width, '0'));
            if (code == 256) { width = 9; next = 258; previous = false; continue; }
            if (code == 257) break;
            if (previous) next++;
            previous = true;
            if (width < 12 && next + early >= 1 << width) width++;
        }
        var bits = string.Concat(chunks);
        bits = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
        return Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
    }

    static byte[] Decode(byte[] encoded, bool strict, int early = 1) =>
        PdfFilters.Decode(encoded, [("LZWDecode", new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["EarlyChange"] = new PdfInteger(early)
        }))], x => x, strictContentFilters: strict).Data;
}
