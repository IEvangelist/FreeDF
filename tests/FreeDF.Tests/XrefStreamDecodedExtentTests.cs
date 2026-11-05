using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamDecodedExtentTests
{
    public static IEnumerable<object[]> SurplusCases()
    {
        foreach (var codec in new[] { "raw", "hex", "a85", "run", "flate", "lzw" })
            foreach (var tail in new[] { "byte", "partial-row", "row", "row-and-byte", "two-rows", "lf", "crlf" })
                foreach (var split in new[] { false, true })
                    yield return [codec, tail, split];
    }

    public static IEnumerable<object?[]> DefaultIndexCases()
    {
        foreach (var codec in new[] { "raw", "hex", "a85", "run", "flate", "lzw" })
            foreach (var tail in new[] { null, "partial-row", "row" })
                yield return [codec, tail];
    }

    [Theory]
    [MemberData(nameof(SurplusCases))]
    public void Surplus_decoded_data_requires_repair_before_index_publication(string codec, string tail, bool split)
    {
        var file = PdfFile.Load(Build(codec, tail, split));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData("raw", false)]
    [InlineData("raw", true)]
    [InlineData("hex", false)]
    [InlineData("hex", true)]
    [InlineData("a85", false)]
    [InlineData("a85", true)]
    [InlineData("run", false)]
    [InlineData("run", true)]
    [InlineData("flate", false)]
    [InlineData("flate", true)]
    [InlineData("lzw", false)]
    [InlineData("lzw", true)]
    public void Exact_rows_preserve_subsections_and_physical_eol_outside_Length(string codec, bool split)
    {
        var bytes = Build(codec, null, split);
        foreach (var eol in new[] { "\n", "\r\n" })
        {
            var text = Encoding.Latin1.GetString(bytes).Replace("\nendstream", eol + "endstream", StringComparison.Ordinal);
            var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
            Assert.False(file.Repaired);
            AssertTarget(file);
            Assert.False(file.Repaired);
        }
    }

    [Theory]
    [MemberData(nameof(DefaultIndexCases))]
    public void Omitted_Index_uses_Size_to_require_exact_rows(string codec, string? tail)
    {
        var file = PdfFile.Load(Build(codec, tail, false, defaultIndex: true));
        Assert.Equal(tail is not null, file.Repaired);
        AssertTarget(file);
        Assert.Equal(tail is not null, file.Repaired);
    }

    static byte[] Build(string codec, string? tail, bool split, bool defaultIndex = false)
    {
        var name = codec switch
        {
            "hex" => "/ASCIIHexDecode", "a85" => "/ASCII85Decode", "run" => "/RunLengthDecode",
            "flate" => "/FlateDecode", "lzw" => "/LZWDecode", _ => null
        };
        var bytes = XrefStreamCryptFilterTests.Build(name, false, encode: rows =>
        {
            byte[] extra = tail switch
            {
                null => [], "byte" => [0], "partial-row" => new byte[6], "row" => new byte[7],
                "row-and-byte" => new byte[8], "two-rows" => new byte[14], "lf" => [10], _ => [13, 10]
            };
            byte[] decoded = defaultIndex ? [0, 0, 0, 0, 0, 255, 255, .. rows, .. extra] : [.. rows, .. extra];
            return codec == "raw" ? decoded : XrefStreamFilterCompletionTests.Encode(decoded, codec);
        });
        if (!split && !defaultIndex) return bytes;
        var text = Encoding.Latin1.GetString(bytes).Replace("/Index [1 3]",
            defaultIndex ? "" : "/Index [1 1 2 2]", StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(text);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
