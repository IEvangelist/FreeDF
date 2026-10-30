using PortableDocument.Content;
using PortableDocument.Core;
using Xunit;

namespace AdoBye.Tests;

public class ImageDecoderFastPathTests
{
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(3, false, false)]
    [InlineData(1, true, false)]
    [InlineData(3, true, false)]
    [InlineData(1, false, true)]
    [InlineData(3, false, true)]
    public void IdentityByteConversionMatchesGeneralPath(int components, bool truncated, bool softMask)
    {
        var file = PdfFile.Load(TestPdf.Build(""));
        var resources = new PdfDictionary();
        var bytes = Enumerable.Range(0, 257 * 3 * components).Select(i => (byte)(i * 73)).ToArray();
        if (truncated) bytes = bytes[..^5];
        var dictionary = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Width"] = new PdfInteger(257), ["Height"] = new PdfInteger(3),
            ["BitsPerComponent"] = new PdfInteger(8),
            ["ColorSpace"] = new PdfName(components == 3 ? "DeviceRGB" : "DeviceGray"),
            ["Decode"] = new PdfArray(Enumerable.Range(0, components).SelectMany(_ =>
                new PdfObject[] { new PdfInteger(0), new PdfInteger(1) }).ToList()),
        });
        if (softMask)
        {
            dictionary.Entries["SMask"] = new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Width"] = new PdfInteger(2), ["Height"] = new PdfInteger(1),
                ["BitsPerComponent"] = new PdfInteger(8), ["ColorSpace"] = new PdfName("DeviceGray"),
            }), [0, 127]);
        }
        var image = new PdfStream(dictionary, bytes);
        foreach (var intent in Enum.GetValues<RenderingIntent>())
        foreach (var bpc in Enum.GetValues<BlackPointCompensation>())
        {
            var actual = ImageDecoder.Decode(file, image, resources, out var mask, out var warning, intent, bpc);
            Assert.False(mask);
            Assert.Null(warning);
            // An impossible color-key interval takes the general conversion path without masking pixels.
            dictionary.Entries["Mask"] = new PdfArray(Enumerable.Range(0, components * 2)
                .Select(_ => (PdfObject)new PdfInteger(-1)).ToList());
            var reference = ImageDecoder.Decode(file, image, resources, out _, out var referenceWarning, intent, bpc);
            dictionary.Entries.Remove("Mask");
            Assert.Null(referenceWarning);
            Assert.Equal(reference!.Rgba, actual!.Rgba);
            Assert.Equal(reference.Width, actual.Width);
            Assert.Equal(reference.Height, actual.Height);
        }
    }

    [Fact]
    public void NonIdentityDecodeAndMatchingColorKeyStillUseTheirSpecifiedValues()
    {
        var file = PdfFile.Load(TestPdf.Build(""));
        var dictionary = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Width"] = new PdfInteger(2), ["Height"] = new PdfInteger(1),
            ["BitsPerComponent"] = new PdfInteger(8), ["ColorSpace"] = new PdfName("DeviceGray"),
            ["Decode"] = new PdfArray([new PdfInteger(1), new PdfInteger(0)]),
            ["Mask"] = new PdfArray([new PdfInteger(0), new PdfInteger(0)]),
        });
        var image = ImageDecoder.Decode(file, new PdfStream(dictionary, [0, 255]), new PdfDictionary(), out _, out var warning);
        Assert.Null(warning);
        Assert.Equal(new byte[] { 255, 255, 255, 0, 0, 0, 0, 255 }, image!.Rgba);
    }
}
