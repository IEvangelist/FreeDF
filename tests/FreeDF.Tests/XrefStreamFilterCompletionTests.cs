using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamFilterCompletionTests
{
    [Theory]
    [InlineData("hex-eof")]
    [InlineData("hex-character")]
    [InlineData("hex-suffix")]
    [InlineData("a85-eof")]
    [InlineData("a85-character")]
    [InlineData("a85-suffix")]
    [InlineData("run-eof")]
    [InlineData("run-suffix")]
    [InlineData("run-repeat")]
    [InlineData("flate-truncated")]
    [InlineData("flate-checksum")]
    [InlineData("flate-suffix")]
    [InlineData("lzw-eof")]
    [InlineData("lzw-clear")]
    [InlineData("lzw-padding")]
    [InlineData("lzw-suffix")]
    public void Incomplete_or_excess_encoded_data_requires_explicit_xref_recovery(string variant)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build($"/{Name(variant)}", false,
            encode: bytes => Encode(bytes, variant)));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData("hex")]
    [InlineData("a85")]
    [InlineData("run")]
    [InlineData("flate")]
    [InlineData("lzw")]
    public void Complete_encoded_xref_streams_keep_exact_index_loading(string variant)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build($"/{Name(variant)}", false,
            encode: bytes => Encode(bytes, variant)));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("hex-eof")]
    [InlineData("a85-eof")]
    [InlineData("run-eof")]
    [InlineData("flate-truncated")]
    [InlineData("lzw-eof")]
    public void Ordinary_partial_filter_recovery_is_unchanged(string variant)
    {
        var expected = "ordinary encoded recovery"u8.ToArray();
        Assert.Equal(expected, PdfFilters.Decode(Encode(expected, variant),
            [(Name(variant), null)], item => item).Data);
    }

    static string Name(string variant) => variant.Split('-')[0] switch
    {
        "hex" => "ASCIIHexDecode",
        "a85" => "ASCII85Decode",
        "run" => "RunLengthDecode",
        "flate" => "FlateDecode",
        _ => "LZWDecode"
    };

    internal static byte[] Encode(byte[] decoded, string variant)
    {
        if (variant.StartsWith("a85", StringComparison.Ordinal))
        {
            var encoded = new StringBuilder();
            for (var start = 0; start < decoded.Length; start += 4)
            {
                uint value = 0;
                var count = Math.Min(4, decoded.Length - start);
                for (var index = 0; index < 4; index++)
                    value = value << 8 | (index < count ? decoded[start + index] : 0u);
                var group = new char[5];
                for (var index = 4; index >= 0; index--)
                {
                    group[index] = (char)(value % 85 + 33);
                    value /= 85;
                }
                encoded.Append(group, 0, count + 1);
            }
            if (variant == "a85-character") encoded.Insert(5, 'v');
            if (variant != "a85-eof") encoded.Append("~>");
            if (variant == "a85-suffix") encoded.Append(' ');
            return Encoding.ASCII.GetBytes(encoded.ToString());
        }
        if (variant.StartsWith("hex", StringComparison.Ordinal))
        {
            var hex = Convert.ToHexString(decoded);
            return Encoding.ASCII.GetBytes(variant switch
            {
                "hex-eof" => hex,
                "hex-character" => hex.Insert(4, "g") + ">",
                "hex-suffix" => hex + "> ",
                _ => hex + ">"
            });
        }
        if (variant.StartsWith("run", StringComparison.Ordinal))
            return variant switch
            {
                "run-eof" => [(byte)(decoded.Length - 1), .. decoded],
                "run-repeat" => [(byte)(decoded.Length - 1), .. decoded, 255],
                "run-suffix" => [(byte)(decoded.Length - 1), .. decoded, 128, 0],
                _ => [(byte)(decoded.Length - 1), .. decoded, 128]
            };
        if (variant.StartsWith("flate", StringComparison.Ordinal))
        {
            var encoded = PdfFilters.DeflateEncode(decoded);
            if (variant == "flate-truncated") return encoded[..^4];
            if (variant == "flate-checksum") encoded[^1] ^= 1;
            return variant == "flate-suffix" ? [.. encoded, 0] : encoded;
        }
        var codes = new List<int>();
        if (variant != "lzw-clear") codes.Add(256);
        codes.AddRange(decoded.Select(value => (int)value));
        if (variant != "lzw-eof") codes.Add(257);
        var bits = string.Concat(codes.Select(code => Convert.ToString(code, 2).PadLeft(9, '0')));
        bits = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
        var packed = Enumerable.Range(0, bits.Length / 8)
            .Select(index => Convert.ToByte(bits.Substring(index * 8, 8), 2)).ToArray();
        if (variant == "lzw-padding") packed[^1] |= 1;
        return variant == "lzw-suffix" ? [.. packed, 0] : packed;
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
