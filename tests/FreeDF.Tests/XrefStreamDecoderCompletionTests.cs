using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamDecoderCompletionTests
{
    [Theory]
    [InlineData("DCTDecode")]
    [InlineData("DCT")]
    [InlineData("JPXDecode")]
    [InlineData("CCITTFaxDecode")]
    [InlineData("CCF")]
    [InlineData("JBIG2Decode")]
    public void Undecoded_codec_bytes_cannot_be_used_as_clean_xref_rows(string codec)
    {
        foreach (var prefix in new[] { false, true })
        {
            var filter = prefix ? $"[/ASCIIHexDecode /{codec}]" : $"/{codec}";
            var file = PdfFile.Load(XrefStreamCryptFilterTests.Build(filter, false, encode: prefix ? Hex : null));
            Assert.True(file.Repaired);
            AssertTarget(file);
        }
    }

    [Theory]
    [InlineData("[/DCTDecode /ASCIIHexDecode]")]
    [InlineData("[/ASCIIHexDecode /JPXDecode /RunLengthDecode]")]
    public void Remaining_stages_after_an_undecoded_codec_cannot_be_skipped(string filter)
    {
        var prefix = filter.StartsWith("[/ASCIIHexDecode", StringComparison.Ordinal);
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build(filter, false, encode: prefix ? Hex : null));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("/ASCIIHexDecode", true)]
    [InlineData("[/ASCIIHexDecode]", true)]
    public void Completed_pipelines_preserve_clean_index_loading(string? filter, bool hex)
    {
        var file = PdfFile.Load(XrefStreamCryptFilterTests.Build(filter, false, encode: hex ? Hex : null));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("DCTDecode", "DCTDecode")]
    [InlineData("DCT", "DCTDecode")]
    [InlineData("JPXDecode", "JPXDecode")]
    [InlineData("CCITTFaxDecode", "CCITTFaxDecode")]
    [InlineData("CCF", "CCITTFaxDecode")]
    [InlineData("JBIG2Decode", "JBIG2Decode")]
    public void Ordinary_decoder_keeps_remaining_codec_and_exact_bytes(string codec, string canonical)
    {
        var expected = "ordinary undecoded image bytes"u8.ToArray();
        var result = PdfFilters.Decode(Hex(expected),
            [("ASCIIHexDecode", null), (canonical, null)], item => item);
        Assert.Equal(expected, result.Data);
        Assert.Equal(canonical, result.ImageFilter);
        var dictionary = (PdfDictionary)new PdfLexer(Encoding.ASCII.GetBytes($"<< /Filter /{codec} >>")).ReadObject()!;
        Assert.Equal(canonical, Assert.Single(PdfFilters.GetFilters(dictionary, item => item)).Name);
    }

    static byte[] Hex(byte[] value) => Encoding.ASCII.GetBytes(Convert.ToHexString(value) + ">");

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
