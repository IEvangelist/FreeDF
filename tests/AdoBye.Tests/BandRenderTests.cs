using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Security.Cryptography;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

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
}
