using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamDecoderCompletionTests
{
    [Theory]
    [InlineData("DCTDecode", "DCTDecode")]
    [InlineData("DCT", "DCTDecode")]
    [InlineData("JPXDecode", "JPXDecode")]
    [InlineData("CCITTFaxDecode", "CCITTFaxDecode")]
    [InlineData("CCF", "CCITTFaxDecode")]
    [InlineData("JBIG2Decode", "JBIG2Decode")]
    public void Undecoded_image_payload_cannot_publish_object_stream_members(string codec, string canonical)
    {
        Assert.Contains(canonical, PdfFilters.ImageCodecs);
        foreach (var repair in new[] { false, true })
            foreach (var prefix in new[] { false, true })
            {
                var filter = prefix ? $"[/ASCIIHexDecode /{codec}]" : $"/{codec}";
                var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filter, "null", prefix, repair));
                Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
                Assert.True(file.Repaired);
                Assert.False(file.Xref.ContainsKey(2));
                Assert.IsType<PdfNull>(file.GetObject(2));
            }
    }

    [Theory]
    [InlineData("[/DCTDecode /ASCIIHexDecode]")]
    [InlineData("[/ASCIIHexDecode /JPXDecode /RunLengthDecode]")]
    public void Filters_after_an_unhandled_codec_cannot_be_silently_skipped(string filter)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filter, "null",
                filter.StartsWith("[/ASCIIHexDecode", StringComparison.Ordinal), repair));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("/ASCIIHexDecode", true)]
    [InlineData("[/ASCIIHexDecode]", true)]
    public void Completed_filter_pipeline_preserves_exact_member_and_cache(string? filter, bool hex)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filter, "null", hex, repair));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("DCTDecode", "DCTDecode")]
    [InlineData("DCT", "DCTDecode")]
    [InlineData("JPXDecode", "JPXDecode")]
    [InlineData("CCITTFaxDecode", "CCITTFaxDecode")]
    [InlineData("CCF", "CCITTFaxDecode")]
    [InlineData("JBIG2Decode", "JBIG2Decode")]
    public void Ordinary_image_streams_preserve_remaining_codec_bytes_and_parameters(string codec, string canonical)
    {
        foreach (var repair in new[] { false, true })
            foreach (var prefix in new[] { false, true })
            {
                var filter = prefix ? $"[/ASCIIHexDecode /{codec}]" : $"/{codec}";
                var parms = prefix ? "[null 5 0 R]" : "5 0 R";
                var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filter, "<< /Columns 23 >>",
                    prefix, repair, ordinary: true, decodeParms: parms));
                var stream = Assert.IsType<PdfStream>(file.GetObject(3));
                var bytes = file.DecodeStream(stream, out var remaining, out var parameters);
                Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), bytes);
                Assert.Equal(canonical, remaining);
                Assert.Same(file.GetObject(5), parameters);
                Assert.Equal(repair, file.Repaired);
            }
    }
}
