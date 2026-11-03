using System.Security.Cryptography;
using System.Text;
using PortableDocument.Document;
using PortableDocument.FallbackFonts;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

/// <summary>
/// Golden RGBA hashes of every fixture and sample page, using bundled fonts for host-independent output.
/// Set FREEDF_WRITE_GOLDEN=&lt;path&gt; to regenerate; only do that when an output change is intended and justified.
/// </summary>
public class RasterGoldenTests
{
    const double Dpi = 96;
    static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures");

    static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(FixtureRoot, "*.pdf", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(FixtureRoot, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("signatures/", StringComparison.Ordinal) && f != "samples/27-full-collection.pdf")
            .Order(StringComparer.Ordinal);

    static List<string> Compute()
    {
        var lines = new List<string>();
        var options = new PdfRenderOptions { FontFallback = new BundledFontFallback(new DirectoryAssetSource()) };
        foreach (var rel in Files())
        {
            try
            {
                var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(FixtureRoot, rel)));
                var i = 0;
                foreach (var page in doc.Pages)
                {
                    string hash;
                    try { hash = Convert.ToHexString(SHA256.HashData(DocumentRenderer.RenderPage(page, options, Dpi).Rgba)); }
                    catch (Exception e) { hash = "ERR:" + e.GetType().Name; }
                    lines.Add($"{rel}#{i++}\t{hash}");
                }
            }
            catch (Exception e) { lines.Add($"{rel}\tERR:{e.GetType().Name}"); }
        }
        return lines;
    }

    [Fact]
    public void RenderedPagesMatchPreOptimisationGoldenHashes()
    {
        var write = Environment.GetEnvironmentVariable("FREEDF_WRITE_GOLDEN");
        if (!string.IsNullOrEmpty(write))
        {
            File.WriteAllLines(write, Compute());
            return;
        }
        var golden = File.ReadAllLines(Path.Combine(FixtureRoot, "raster-golden.tsv"));
        foreach (var sequential in new[] { true, false })
        {
            SoftwareRasterizer.ForceSequential = sequential;
            try
            {
                var actual = Compute();
                Assert.True(actual.Count > 100, "corpus unexpectedly small");
                var diffs = golden.Except(actual).Concat(actual.Except(golden)).Take(8).ToList();
                Assert.True(diffs.Count == 0, $"sequential={sequential}: {string.Join(" | ", diffs)}");
            }
            finally { SoftwareRasterizer.ForceSequential = false; }
        }
    }

    [Fact]
    public void ReversedCrossingsRetainFullCoverage()
    {
        const int width = 2048;
        var edges = new RasterEdge[width * 2];
        for (var i = 0; i < width; i++)
        {
            var x = width - i - 1;
            edges[i * 2] = new RasterEdge { Y0 = 0, Y1 = 2, X0 = x, Dir = 1 };
            edges[i * 2 + 1] = new RasterEdge { Y0 = 0, Y1 = 2, X0 = x + 1, Dir = -1 };
        }
        foreach (var evenOdd in new[] { false, true })
        {
            var scanner = new EdgeScanner(width);
            scanner.Begin(edges, 0, edges.Length, evenOdd, 0, 2, 0, width);
            for (var row = 0; row < 2; row++)
            {
                Assert.True(scanner.NextRow(out var y, out var xa, out var xb));
                Assert.Equal(row, y);
                Assert.Equal(0, xa);
                Assert.Equal(width, xb);
                Assert.All(scanner.Cov.Take(width), coverage => Assert.Equal(1f, coverage));
            }
            Assert.False(scanner.NextRow(out _, out _, out _));
        }
    }

    [Fact]
    public void RunEndMatchesScalarScan()
    {
        var rng = new Random(5);
        var cov = new float[300];
        for (var trial = 0; trial < 2000; trial++)
        {
            for (var i = 0; i < cov.Length; i++) cov[i] = rng.Next(40) == 0 ? (float)rng.NextDouble() : rng.Next(60) == 0 ? 1.0000001f : 1f;
            var from = rng.Next(0, 100); var to = rng.Next(from, cov.Length);
            var expected = from;
            while (expected < to && cov[expected] >= 1f) expected++;
            Assert.Equal(expected, SoftwareRasterizer.RunEnd(cov, from, to));
        }
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(false, 0.37f)]
    [InlineData(true, 1f)]
    public void FillOpaqueRunMatchesPerPixelReference(bool layer, float opacity)
    {
        var rng = new Random(11);
        const int N = 61, Pad = 3;
        for (var trial = 0; trial < 300; trial++)
        {
            var px = new float[(N + 2 * Pad) * 3];
            var al = layer ? new float[N + 2 * Pad] : null;
            for (var i = 0; i < px.Length; i++) px[i] = (float)rng.NextDouble();
            if (al is not null)
                for (var i = 0; i < al.Length; i++)
                {
                    var k = rng.Next(4);
                    al[i] = k == 0 ? 0f : k == 1 ? 1f : (float)rng.NextDouble();
                    if (al[i] == 0f) { px[i * 3] = px[i * 3 + 1] = px[i * 3 + 2] = 0f; } // layer invariant: colour is zero where alpha is zero
                }
            float r = (float)rng.NextDouble(), g = (float)rng.NextDouble(), b = (float)rng.NextDouble();
            var start = Pad + rng.Next(0, 4); var n = rng.Next(0, N - 4);
            var px2 = (float[])px.Clone(); var al2 = (float[]?)al?.Clone();
            SoftwareRasterizer.FillOpaqueRun(px, al, start, n, r, g, b, opacity);
            for (var i = 0; i < n; i++) SoftwareRasterizer.OpaquePixel(px2, al2, start + i, r, g, b, opacity);
            Assert.True(px.AsSpan().SequenceEqual(px2));
            if (al is not null) Assert.True(al.AsSpan().SequenceEqual(al2!));
        }
    }

    [Fact]
    public void EmptyBackdropShortcutEqualsTheGeneralBlend()
    {
        // The deferred painter stores the source colour directly when layer alpha is 0 (colour is 0 there by invariant).
        var rng = new Random(3);
        for (var i = 0; i < 100_000; i++)
        {
            var s = (float)rng.NextDouble(); if (s <= 0f) continue;
            var c = (float)rng.NextDouble();
            float px = 0f, ab = 0f;
            var oa = ab + s - ab * s;
            var k = s / oa;
            px += (c - px) * k;
            Assert.Equal(c, px);
            Assert.Equal(s, oa);
        }
    }
}
