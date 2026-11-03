using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

namespace FreeDF.Tests;

public class ImageInterpolationTests
{
    const string Resources = "/XObject << /Im 5 0 R >>";
    const string Gray = "/ColorSpace /DeviceGray /BitsPerComponent 8";

    static string Image(int n, int w, int h, string dict, string hex) =>
        $"{n} 0 obj\n<< /Type /XObject /Subtype /Image /Width {w} /Height {h} {dict} /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream\nendobj\n";

    static PdfPage Page(string content, string objects, string resources = Resources) =>
        PdfDocument.Load(TestPdf.Build(content, resources, objects, "[0 0 40 40]", brokenXref: true)).Pages[0];

    static RenderedPage Render(string dict, string matrix = "20 0 0 20 10 10", string prefix = "") =>
        DocumentRenderer.RenderPage(Page($"{prefix} q {matrix} cm /Im Do Q", Image(5, 2, 1, $"{Gray} {dict}", "00FF>")), 72);

    static (int R, int G, int B) Pixel(RenderedPage p, int x, int y)
    {
        var i = (y * p.Width + x) * 4;
        return (p.Rgba[i], p.Rgba[i + 1], p.Rgba[i + 2]);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("/Interpolate false", false)]
    [InlineData("/Interpolate true", true)]
    [InlineData("/Interpolate 7 0 R", true)]
    public void DecoderRetainsTheResolvedFlag(string flag, bool expected)
    {
        var file = PdfFile.Load(TestPdf.Build("", Resources, Image(5, 2, 1, $"{Gray} {flag}", "00FF>")
            + "7 0 obj\ntrue\nendobj\n", brokenXref: true));
        var stream = Assert.IsType<PdfStream>(file.Resolve(new PdfRef(5, 0)));
        var image = ImageDecoder.Decode(file, stream, new PdfDictionary(), out _, out var warning);
        Assert.Null(warning);
        Assert.Equal(expected, image!.Interpolate);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 }, image.Rgba);
    }

    [Theory]
    [InlineData("20 0 0 20 10 10", 19, 20, 115)]
    [InlineData("0 20 -20 0 30 10", 20, 20, 115)]
    [InlineData("20 0 10 20 5 10", 20, 20, 147)]
    [InlineData("-20 0 0 20 30 10", 20, 20, 115)]
    public void TrueSmoothsScaledRotatedShearedAndReflectedImages(string matrix, int x, int y, int expected)
    {
        var smooth = Render("/Interpolate true", matrix);
        var nearest = Render("/Interpolate false", matrix);
        Assert.InRange(Pixel(smooth, x, y).R, expected - 1, expected + 1);
        Assert.Contains(Pixel(nearest, x, y).R, new[] { 0, 255 });
        Assert.Equal(nearest.Rgba, Render("", matrix).Rgba);
    }

    [Theory]
    [InlineData("I")]
    [InlineData("Interpolate")]
    public void InlineImagesUseBothFlagSpellings(string key)
    {
        RenderedPage Inline(string flag) => DocumentRenderer.RenderPage(Page(
            $"q 20 0 0 20 10 10 cm BI /W 2 /H 1 /BPC 8 /CS /G {flag} /F /AHx ID 00FF> EI Q", "", ""), 72);
        Assert.Equal(Render("/Interpolate true").Rgba, Inline($"/{key} true").Rgba);
        Assert.Equal(Render("").Rgba, Inline($"/{key} false").Rgba);
        Assert.Equal(Render("").Rgba, Inline("").Rgba);
    }

    [Fact]
    public void BilinearSamplingUsesFourPixelCentresAndExtendsEdgeSamples()
    {
        var image = new ImageData(2, 2, [0, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255])
            { Interpolate = true };
        Assert.Equal((63.75, 63.75, 63.75, 255.0), image.SamplePremultiplied(1, 1));
        Assert.Equal((255.0, 0.0, 0.0, 255.0), image.SamplePremultiplied(1.9, 0.1));
        var p = Render("/Interpolate true", prefix: "10 10 10 20 re W n");
        Assert.Equal((255, 255, 255), Pixel(p, 20, 20));
        Assert.Equal((0, 0, 0), Pixel(p, 10, 20));
        Assert.InRange(Pixel(p, 19, 20).R, 114, 116);
        Assert.Equal((255, 255, 255), Pixel(p, 9, 20));
        Assert.Equal((255, 255, 255), Pixel(p, 15, 9));
    }

    [Fact]
    public void RgbInterpolationReachesTheRendererInBothDimensions()
    {
        var objects = Image(5, 2, 2, "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Interpolate true",
            "000000FF000000FF000000FF>");
        var p = DocumentRenderer.RenderPage(Page("q 20 0 0 20 10 10 cm /Im Do Q", objects), 72);
        // Pixel centre (19.5,19.5) gives weights .3025, .2475, .2475, .2025.
        Assert.Equal((63, 63, 52), Pixel(p, 19, 19));
        Assert.Equal((0, 0, 0), Pixel(p, 10, 10));
        Assert.Equal((0, 0, 255), Pixel(p, 29, 29));
        Assert.Equal((255, 255, 255), Pixel(Render("/Interpolate true", "20 0 10 20 5 10"), 6, 11));
    }

    [Fact]
    public void ConstantAlphaBlendAndGraphicsSoftMaskStillApplyToInterpolatedImages()
    {
        var image = Image(5, 2, 1, $"{Gray} /Interpolate true", "00FF>");
        var res = Resources + " /ExtGState << /G << /ca 0.5 /BM /Multiply >> >>";
        var p = DocumentRenderer.RenderPage(Page("0 1 0 rg 0 0 40 40 re f /G gs q 20 0 0 20 10 10 cm /Im Do Q", image, res), 72);
        var pixel = Pixel(p, 19, 20);
        Assert.Equal(0, pixel.R);
        Assert.InRange(pixel.G, 184, 186);
        Assert.Equal(0, pixel.B);

        const string maskContent = "0.5 g 0 0 40 40 re f";
        var form = $"6 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 40 40] /Group << /S /Transparency /CS /DeviceGray >> /Length {maskContent.Length} >>\nstream\n{maskContent}\nendstream\nendobj\n";
        res = Resources + " /ExtGState << /G << /SMask << /S /Luminosity /G 6 0 R >> >> >>";
        var masked = DocumentRenderer.RenderPage(Page("/G gs q 20 0 0 20 10 10 cm /Im Do Q", image + form, res), 72);
        Assert.InRange(Pixel(masked, 19, 20).R, 184, 186);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StencilInterpolationSmoothsCoverageNotThePaintColour(bool inline)
    {
        RenderedPage Stencil(bool interpolate)
        {
            var flag = interpolate ? "true" : "false";
            var content = inline
                ? $"BI /W 2 /H 1 /IM true /I {flag} /F /AHx ID 40> EI"
                : "/Im Do";
            return DocumentRenderer.RenderPage(Page($"1 0 0 rg q 20 0 0 20 10 10 cm {content} Q",
                Image(5, 2, 1, $"/ImageMask true /Interpolate {flag}", "40>")), 72);
        }
        Assert.Equal((255, 0, 0), Pixel(Stencil(false), 19, 20));
        var p = Pixel(Stencil(true), 19, 20);
        Assert.Equal(255, p.R);
        Assert.InRange(p.G, 114, 116);
        Assert.Equal(p.G, p.B);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeSoftAndExplicitMaskFlagsAreIndependentOfTheParent(bool parentInterpolate, bool stencil)
    {
        RenderedPage Masked(bool maskInterpolate)
        {
            var objects = Image(5, 1, 1, $"{Gray} /Interpolate {parentInterpolate.ToString().ToLowerInvariant()} /{(stencil ? "Mask" : "SMask")} 6 0 R", "00>")
                + Image(6, 2, 1, $"{(stencil ? "/ImageMask true /Decode [1 0]" : Gray)} /Interpolate {maskInterpolate.ToString().ToLowerInvariant()}", stencil ? "40>" : "00FF>");
            return DocumentRenderer.RenderPage(Page("q 20 0 0 20 10 10 cm /Im Do Q", objects), 72);
        }
        Assert.Equal((255, 255, 255), Pixel(Masked(false), 19, 20));
        Assert.InRange(Pixel(Masked(true), 19, 20).R, 139, 141);
        Assert.InRange(Pixel(Masked(true), 20, 20).R, 114, 116);
    }

    [Fact]
    public void ParentInterpolationDoesNotSmoothAFalseMask()
    {
        var objects = Image(5, 2, 1, $"{Gray} /Interpolate true /SMask 6 0 R", "0000>")
            + Image(6, 2, 1, Gray, "00FF>");
        var p = DocumentRenderer.RenderPage(Page("q 20 0 0 20 10 10 cm /Im Do Q", objects), 72);
        Assert.Equal((255, 255, 255), Pixel(p, 19, 20));
        Assert.Equal((0, 0, 0), Pixel(p, 20, 20));
    }

    [Fact]
    public void TransparentColourKeySamplesDoNotBleedColour()
    {
        var objects = Image(5, 2, 1, "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Interpolate true /Mask [0 0 0 0 255 255]", "FF00000000FF>");
        var p = DocumentRenderer.RenderPage(Page("q 20 0 0 20 10 10 cm /Im Do Q", objects), 72);
        var pixel = Pixel(p, 19, 20);
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 114, 116);
        Assert.Equal(pixel.G, pixel.B);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Interpolate true")]
    public void MaskMatteAndTransferAreAppliedWithoutChangingAlpha(string flag)
    {
        var objects = Image(5, 1, 1, $"{Gray} {flag} /SMask 6 0 R", "40>")
            + Image(6, 2, 1, $"{Gray} /Interpolate true /Matte [0]", "8080>");
        var res = Resources + " /ExtGState << /G << /TR2 << /FunctionType 2 /Domain [0 1] /N 2 >> >> >>";
        var plain = DocumentRenderer.RenderPage(Page("q 20 0 0 20 10 10 cm /Im Do Q", objects), 72);
        var mapped = DocumentRenderer.RenderPage(Page("/G gs q 20 0 0 20 10 10 cm /Im Do Q", objects, res), 72);
        Assert.InRange(Pixel(plain, 19, 20).R, 190, 192);
        Assert.InRange(Pixel(mapped, 19, 20).R, 158, 160);
    }

    [Fact]
    public void CachedBandAndWholePageRendersAgreeForTransformedInterpolatedMasks()
    {
        var objects = Image(5, 2, 1, $"{Gray} /Interpolate true /SMask 6 0 R", "00FF>")
            + Image(6, 1, 2, $"{Gray} /Interpolate true", "30D0>");
        var page = Page("3 4 32 30 re W n q 24 6 -4 23 10 5 cm /Im Do Q", objects);
        var whole = DocumentRenderer.RenderPage(page, 72);
        var options = new PdfRenderOptions { ImageCache = new ImageDecodeCache() };
        var stitched = new byte[whole.Rgba.Length];
        for (var y = 0; y < 40; y += 5)
            DocumentRenderer.RenderBand(page, options, 72, y, y + 5).Rgba.CopyTo(stitched, y * whole.Width * 4);
        Assert.Equal(whole.Rgba, stitched);
    }

    [Fact]
    public void TrueRetainsTheBoundedDownsamplingFilter()
    {
        var objects = Image(5, 16, 1, $"{Gray} /Interpolate true", string.Concat(Enumerable.Repeat("00FF", 8)) + ">");
        var p = DocumentRenderer.RenderPage(Page("q 2 0 0 10 10 10 cm /Im Do Q", objects), 72);
        Assert.InRange(Pixel(p, 10, 25).R, 126, 129);
        Assert.Equal((255, 255, 255), Pixel(p, 9, 25));
    }

    [Fact]
    public void DecodeCacheBudgetIncludesNativeInterpolationBuffers()
    {
        var file = PdfFile.Load(TestPdf.Build("", Resources,
            Image(5, 1, 1, $"{Gray} /SMask 6 0 R", "00>")
            + Image(6, 2, 1, $"{Gray} /Interpolate true", "00FF>"), brokenXref: true));
        var stream = Assert.IsType<PdfStream>(file.Resolve(new PdfRef(5, 0)));
        var res = new PdfDictionary();
        var image = ImageDecoder.Decode(file, stream, res, out var isMask, out var warning);
        Assert.Null(warning);
        Assert.Equal(16, image!.DecodedByteLength);
        var key = new ImageDecodeCache.Key(stream, res, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Default);
        var small = new ImageDecodeCache(12);
        small.Add(key, image, isMask, warning);
        Assert.False(small.TryGet(key, out _, out _, out _));
        var enough = new ImageDecodeCache(16);
        enough.Add(key, image, isMask, warning);
        Assert.True(enough.TryGet(key, out var cached, out _, out _));
        Assert.Same(image, cached);
    }
}
