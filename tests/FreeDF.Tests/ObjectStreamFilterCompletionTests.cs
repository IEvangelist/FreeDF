using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamFilterCompletionTests
{
    [Theory]
    [InlineData("hex-eof")]
    [InlineData("hex-character")]
    [InlineData("a85-eof")]
    [InlineData("a85-character")]
    [InlineData("a85-marker")]
    [InlineData("run-eof")]
    [InlineData("run-literal")]
    [InlineData("run-repeat")]
    [InlineData("flate-truncated")]
    [InlineData("flate-checksum")]
    public void Incomplete_or_invalid_filters_cannot_publish_recovered_member_bytes(string variant)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(variant, repair));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.IsType<PdfNull>(file.GetObject(2));
        }
    }

    [Theory]
    [InlineData("hex")]
    [InlineData("a85")]
    [InlineData("run")]
    [InlineData("flate")]
    public void Complete_filters_preserve_exact_member_and_cache(string variant)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(variant, repair));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("hex-eof")]
    [InlineData("a85-eof")]
    [InlineData("run-eof")]
    [InlineData("flate-truncated")]
    public void Ordinary_stream_partial_decode_recovery_is_unchanged(string variant)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(variant, repair, ordinary: true));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(),
                file.DecodeStream(Assert.IsType<PdfStream>(file.GetObject(3))));
            Assert.Equal(repair, file.Repaired);
        }
    }

    static byte[] Build(string variant, bool repair, bool ordinary = false)
    {
        var decoded = "2 0 << /Type /Target >>"u8.ToArray();
        var filter = variant.Split('-')[0];
        byte[] encoded;
        if (filter == "hex")
        {
            var hex = Convert.ToHexString(decoded);
            encoded = Encoding.ASCII.GetBytes(variant switch
            {
                "hex-eof" => hex,
                "hex-character" => hex.Insert(4, "g") + ">",
                _ => hex + ">"
            });
        }
        else if (filter == "a85")
        {
            const string text = "1*A7n4?O`><-`Fo+>6]&EbAs*+?hs";
            encoded = Encoding.ASCII.GetBytes(variant switch
            {
                "a85-eof" => text,
                "a85-character" => text.Insert(5, "v") + "~>",
                "a85-marker" => text + "~x",
                _ => text + "~>"
            });
        }
        else if (filter == "run")
            encoded = variant switch
            {
                "run-eof" => [(byte)(decoded.Length - 1), .. decoded],
                "run-literal" => [(byte)decoded.Length, .. decoded],
                "run-repeat" => [(byte)(decoded.Length - 1), .. decoded, 255],
                _ => [(byte)(decoded.Length - 1), .. decoded, 128]
            };
        else
        {
            encoded = PdfFilters.DeflateEncode(decoded);
            if (variant == "flate-truncated") encoded = encoded[..^4];
            if (variant == "flate-checksum") encoded[^1] ^= 1;
        }
        var name = filter switch
        {
            "hex" => "ASCIIHexDecode", "a85" => "ASCII85Decode",
            "run" => "RunLengthDecode", _ => "FlateDecode"
        };
        return ObjectStreamFilterNameTests.Build($"[/ASCIIHexDecode /{name}]", "null", false, repair,
            ordinary, encodedOverride: Convert.ToHexString(encoded) + ">");
    }
}
