using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Security.Cryptography;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

/// <summary>Band renders (worker-pool tiles) must stitch into exactly the whole-page raster.</summary>
public class BandRenderTests
{
    static string? Samples()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "samples")) && File.Exists(Path.Combine(d.FullName, "PortableDocument.slnx")))
                return Path.Combine(d.FullName, "samples");
        return null;
    }

    public static IEnumerable<object[]> Fixtures()
    {
        var fx = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var f in new[] { "graphics/transparency.pdf", "graphics/mesh.pdf", "graphics/colorspaces.pdf", "features/features.pdf", "shapes.pdf", "showcase.pdf" })
            yield return [Path.Combine(fx, f)];
        var samples = Samples();
        if (samples is null) yield break;
        foreach (var f in new[] { "05-vector-graphics", "06-patterns-shadings", "07-transparency", "09-images", "13-page-boxes-rotation", "15-optional-content", "20-report", "24-poster" })
            if (File.Exists(Path.Combine(samples, f + ".pdf"))) yield return [Path.Combine(samples, f + ".pdf")];
    }

    static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    [Theory, MemberData(nameof(Fixtures))]
    public void BandsStitchToTheWholePageRaster(string path)
    {
        var doc = PdfDocument.Open(path);
        var options = new PdfRenderOptions();
        var bandOptions = new PdfRenderOptions { ImageCache = new PortableDocument.Content.ImageDecodeCache() };
        foreach (var page in doc.Pages.Take(6))
        {
            var whole = DocumentRenderer.RenderPage(page, options, 96);
            foreach (var bands in new[] { 2, 3, 7 })
            {
                var stitched = new byte[whole.Rgba.Length];
                var rows = 0;
                for (var i = 0; i < bands; i++)
                {
                    // Uneven, odd-sized splits put band edges away from any natural content boundary.
                    var y1 = i == bands - 1 ? whole.Height : Math.Min(whole.Height, rows + whole.Height / bands + (i % 2 == 0 ? 1 : -1));
                    if (y1 <= rows) continue;
                    var band = DocumentRenderer.RenderBand(page, bandOptions, 96, rows, y1);
                    Assert.Equal(whole.Width, band.Width);
                    Assert.Equal(y1 - rows, band.Height);
                    band.Rgba.CopyTo(stitched, rows * whole.Width * 4);
                    rows = y1;
                }
                Assert.Equal(whole.Height, rows);
                Assert.Equal(Hash(whole.Rgba), Hash(stitched));
            }
        }
    }

    [Fact]
    public void SingleRowBandsAndClampingMatch()
    {
        var page = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "graphics", "transparency.pdf")).Pages[0];
        var whole = DocumentRenderer.RenderPage(page, new PdfRenderOptions(), 48);
        var stride = whole.Width * 4;
        foreach (var y in new[] { 0, 1, whole.Height / 2, whole.Height - 1 })
        {
            var row = DocumentRenderer.RenderBand(page, new PdfRenderOptions(), 48, y, y + 1);
            Assert.Equal(whole.Rgba.AsSpan(y * stride, stride).ToArray(), row.Rgba);
        }
        var tail = DocumentRenderer.RenderBand(page, new PdfRenderOptions(), 48, whole.Height - 3, whole.Height + 100);
        Assert.Equal(3, tail.Height);
        Assert.Equal(DocumentRenderer.RasterSize(page, 48), (whole.Width, whole.Height));
    }

    static void AssertBandsMatch(byte[] pdf, int dpi, int bands)
    {
        var page = PdfDocument.Load(pdf).Pages[0];
        var options = new PdfRenderOptions();
        var whole = DocumentRenderer.RenderPage(page, options, dpi);
        var stitched = new byte[whole.Rgba.Length];
        var rows = 0;
        for (var i = 0; i < bands; i++)
        {
            var y1 = i == bands - 1 ? whole.Height : Math.Min(whole.Height, rows + Math.Max(1, whole.Height / bands + (i % 2 == 0 ? 1 : -1)));
            if (y1 <= rows) continue;
            DocumentRenderer.RenderBand(page, options, dpi, rows, y1).Rgba.CopyTo(stitched, rows * whole.Width * 4);
            rows = y1;
        }
        Assert.Equal(whole.Height, rows);
        Assert.Equal(Hash(whole.Rgba), Hash(stitched));
    }

    [Theory]
    [InlineData("q 1 0 0 100 0 0 cm 1 w 1 M 10 1.2 m 190 1.2 l S Q")]
    [InlineData("q 1 0 0 100 0 0 cm 0.5 w 1 M 0 J 10 0.8 m 190 0.8 l S Q")]
    [InlineData("q 100 0 0 1 0 0 cm 1 w 0.5 20 m 0.5 180 l S Q")]
    [InlineData("q 1 0 0 40 0 0 cm 0 w 10 3.9 m 190 3.9 l S Q")]
    public void AnisotropicStrokesWhoseCenterlineIsOutsideABandStillMatch(string content)
    {
        foreach (var bands in new[] { 2, 3, 5, 9, 16 }) AssertBandsMatch(FreeDF.Tests.TestPdf.Build(content, brokenXref: true), 72, bands);
    }

    [Fact]
    public void RandomStrokesFillsAndTransformsStitchExactly()
    {
        var rnd = new Random(32000);
        double R(double lo, double hi) => lo + rnd.NextDouble() * (hi - lo);
        string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        for (var seed = 0; seed < 120; seed++)
        {
            var sb = new System.Text.StringBuilder();
            for (var s = 0; s < 6; s++)
            {
                sb.Append($"q {N(R(.2, 6))} {N(R(-1, 1))} {N(R(-1, 1))} {N(R(.2, 60))} {N(R(0, 100))} {N(R(0, 100))} cm ");
                sb.Append($"{N(R(0, 12))} w {rnd.Next(3)} J {rnd.Next(3)} j {N(R(1, 10))} M ");
                if (rnd.Next(3) == 0) sb.Append($"[{N(R(.5, 8))} {N(R(.5, 8))}] {N(R(0, 5))} d ");
                sb.Append($"{N(R(0, 1))} {N(R(0, 1))} {N(R(0, 1))} RG {N(R(0, 1))} {N(R(0, 1))} {N(R(0, 1))} rg ");
                sb.Append($"{N(R(-30, 60))} {N(R(-3, 3))} m {N(R(-30, 60))} {N(R(-3, 3))} {N(R(-30, 60))} {N(R(-3, 3))} {N(R(-30, 60))} {N(R(-3, 3))} c ");
                sb.Append($"{N(R(-30, 60))} {N(R(-3, 3))} l {(rnd.Next(2) == 0 ? "h " : "")}{new[] { "S", "f", "B", "f*", "b" }[rnd.Next(5)]} Q ");
            }
            AssertBandsMatch(FreeDF.Tests.TestPdf.Build(sb.ToString(), brokenXref: true), 72, 3 + seed % 7);
        }
    }}
