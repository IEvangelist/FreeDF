using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class StrictMarkerExtentTests
{
    [Theory]
    [InlineData("ASCIIHexDecode", "zero")]
    [InlineData("ASCIIHexDecode", "garbage")]
    [InlineData("ASCIIHexDecode", "newline")]
    [InlineData("ASCIIHexDecode", "marker")]
    [InlineData("ASCIIHexDecode", "second-stream")]
    [InlineData("ASCII85Decode", "zero")]
    [InlineData("ASCII85Decode", "garbage")]
    [InlineData("ASCII85Decode", "newline")]
    [InlineData("ASCII85Decode", "marker")]
    [InlineData("ASCII85Decode", "second-stream")]
    [InlineData("RunLengthDecode", "zero")]
    [InlineData("RunLengthDecode", "garbage")]
    [InlineData("RunLengthDecode", "newline")]
    [InlineData("RunLengthDecode", "marker")]
    [InlineData("RunLengthDecode", "second-stream")]
    public void Strict_markers_require_end_of_logical_encoded_input(string filter, string tail)
    {
        var expected = "2 0 << /Type /Target >>"u8.ToArray();
        var complete = Encode(filter, expected);
        byte[] extra = tail switch
        {
            "zero" => [0], "garbage" => [255], "newline" => [13, 10],
            "marker" => filter switch
            {
                "ASCIIHexDecode" => [62], "ASCII85Decode" => [126, 62], _ => [128]
            },
            _ => complete
        };
        byte[] encoded = [.. complete, .. extra];
        Assert.Equal(expected, PdfFilters.Decode(encoded, [(filter, null)], x => x).Data);
        Assert.Throws<InvalidDataException>(() =>
            PdfFilters.Decode(encoded, [(filter, null)], x => x, strictContentFilters: true));
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build($"[/ASCIIHexDecode /{filter}]",
                "null", false, repair, encodedOverride: Convert.ToHexString(encoded) + ">"));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("ASCIIHexDecode")]
    [InlineData("ASCII85Decode")]
    [InlineData("RunLengthDecode")]
    public void Exact_markers_preserve_empty_streams_and_physical_eol_outside_length(string filter)
    {
        Assert.Empty(PdfFilters.Decode(Encode(filter, []), [(filter, null)], x => x,
            strictContentFilters: true).Data);
        var expected = "2 0 << /Type /Target >>"u8.ToArray();
        var encoded = Encode(filter, expected);
        Assert.Equal(expected, PdfFilters.Decode(encoded, [(filter, null)], x => x,
            strictContentFilters: true).Data);
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build($"/{filter}", "null", false, repair,
                encodedBytesOverride: encoded));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    static byte[] Encode(string filter, byte[] decoded) => filter switch
    {
        "ASCIIHexDecode" => Encoding.ASCII.GetBytes(Convert.ToHexString(decoded) + ">"),
        "ASCII85Decode" => decoded.Length == 0 ? "~>"u8.ToArray()
            : "1*A7n4?O`><-`Fo+>6]&EbAs*+?hs~>"u8.ToArray(),
        _ => decoded.Length == 0 ? [128] : [(byte)(decoded.Length - 1), .. decoded, 128]
    };
}
