using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Render;

namespace FreeDF.Tests;

/// <summary>Deterministic guards for the rasterizer/codec fast paths: output equivalence with the simple reference behaviour and allocation ceilings (no wall-clock assertions).</summary>
public class RasterPerformanceTests
{
    static PathData Poly(Random rng, int w, int h, bool curved)
    {
        var p = new PathData();
        double X() => rng.NextDouble() * w * 1.2 - w * 0.1;
        double Y() => rng.NextDouble() * h * 1.2 - h * 0.1;
        p.MoveTo(X(), Y());
        for (var i = 0; i < 2 + rng.Next(4); i++)
        {
            if (curved) p.CurveTo(X(), Y(), X(), Y(), X(), Y());
            else p.LineTo(X(), Y());
        }
        p.Close();
        return p;
    }

    /// <summary>Records the same operations twice; the second pass flushes the deferred paint queue after every operation, i.e. behaves like immediate painting.</summary>
    static byte[] Render(bool flushEveryOperation, int seed)
    {
        const int W = 700, H = 500;
        using var r = new SoftwareRasterizer(W, H);
        var rng = new Random(seed);
        var id = Matrix.Identity;
        void Step() { if (flushEveryOperation) r.SetSoftMask(null, false, new RgbColor(0, 0, 0)); }
        r.BeginGroup(true, false, 1, "Normal");
        for (var i = 0; i < 600; i++)
        {
            var color = new RgbColor(rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
            var alpha = rng.Next(3) == 0 ? rng.NextDouble() : 1.0;
            switch (rng.Next(7))
            {
                case 0:
                    r.Save(); r.Clip(Poly(rng, W, H, true), id, false); Step();
                    r.Fill(Poly(rng, W, H, false), id, rng.Next(2) == 0, color, alpha, "Normal");
                    r.Restore();
                    break;
                case 1:
                    r.Stroke(Poly(rng, W, H, true), id, new StrokeStyle(0.3 + rng.NextDouble() * 6, default, default, 10, rng.Next(3) == 0 ? [5, 3] : [], 0), color, alpha, "Normal");
                    break;
                case 2:
                    r.BeginGroup(rng.Next(2) == 0, false, 0.5 + rng.NextDouble() / 2, "Normal");
                    r.Fill(Poly(rng, W, H, true), id, false, color, 1, "Normal");
                    r.EndGroup();
                    break;
                case 3:
                    var rect = new PathData(); rect.Rect(rng.Next(W), rng.Next(H), 1 + rng.Next(300), 1 + rng.Next(200));
                    r.Fill(rect, id, false, color, alpha, "Normal");
                    break;
                case 4:
                    r.Fill(Poly(rng, W, H, true), id, false, color, alpha, "Multiply");
                    break;
                default:
                    r.Fill(Poly(rng, W, H, true), id, rng.Next(2) == 0, color, alpha, "Normal");
                    break;
            }
            Step();
        }
        r.EndGroup();
        return r.ToRgba();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DeferredBandPaintingMatchesImmediatePainting(int seed) =>
        Assert.True(Render(false, seed).AsSpan().SequenceEqual(Render(true, seed)));

    [Fact]
    public void RepeatedLargeRendersReuseTheirLayerBuffers()
    {
        const int W = 1000, H = 1400;
        long Once()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            using var r = new SoftwareRasterizer(W, H);
            r.BeginGroup(true, false, 1, "Normal");
            var p = new PathData(); p.Rect(100, 100, 600, 800);
            r.Fill(p, Matrix.Identity, false, new RgbColor(0.2, 0.4, 0.6), 1, "Normal");
            r.EndGroup();
            _ = r.ToRgba();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Once();
        var allocated = Math.Min(Once(), Math.Min(Once(), Once())); // a concurrent gen2 GC legitimately trims the pool, so take the best of three
        // A fresh page (12 B/px) plus group layer (16 B/px) was ~39 MB; pooled renders only need the RGBA result and small bookkeeping.
        Assert.True(allocated < (long)W * H * 12 * 1.6, $"second render allocated {allocated:N0} bytes");
    }

    static byte[] ReferencePngPredictor(byte[] data, int rowBytes, int bpp)
    {
        var rows = data.Length / (rowBytes + 1) + (data.Length % (rowBytes + 1) > 1 ? 1 : 0);
        var res = new byte[rows * rowBytes];
        var prev = new byte[rowBytes]; var cur = new byte[rowBytes];
        for (var y = 0; y < rows; y++)
        {
            var off = y * (rowBytes + 1);
            if (off >= data.Length) break;
            var ft = data[off];
            var avail = Math.Min(rowBytes, data.Length - off - 1);
            Array.Clear(cur);
            Array.Copy(data, off + 1, cur, 0, avail);
            for (var i = 0; i < rowBytes; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                var p = a + b - c;
                int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                cur[i] = ft switch
                {
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + (a + b) / 2),
                    4 => (byte)(cur[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c)),
                    _ => cur[i],
                };
            }
            Array.Copy(cur, 0, res, y * rowBytes, rowBytes);
            (prev, cur) = (cur, prev);
        }
        return res;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void PngPredictorsMatchTheReferenceDecoder(int truncate)
    {
        const int Columns = 17, Colors = 3;
        var rowBytes = Columns * Colors;
        var rng = new Random(42);
        var data = new byte[24 * (rowBytes + 1) - truncate];
        rng.NextBytes(data);
        for (var y = 0; y * (rowBytes + 1) < data.Length; y++) data[y * (rowBytes + 1)] = (byte)(y % 6);
        var parms = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Predictor"] = new PdfInteger(15), ["Colors"] = new PdfInteger(Colors), ["BitsPerComponent"] = new PdfInteger(8), ["Columns"] = new PdfInteger(Columns),
        });
        var (decoded, _, _) = PdfFilters.Decode(PdfFilters.DeflateEncode(data), [("FlateDecode", parms)], o => o);
        Assert.Equal(ReferencePngPredictor(data, rowBytes, Colors), decoded);
    }

    [Fact]
    public void LayerBufferPoolStaysWithinItsRetentionCap()
    {
        using (var r = new SoftwareRasterizer(3000, 3000)) { r.BeginGroup(true, false, 1, "Normal"); r.EndGroup(); _ = r.ToRgba(); }
        Assert.True(SoftwareRasterizer.PoolRetainedBytes <= 64L << 20);
    }

    [Fact]
    public void IsolatedRootGroupReusesThePristinePageBuffer()
    {
        const int W = 2000, H = 2800;
        long Once()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            using var r = new SoftwareRasterizer(W, H);
            r.BeginGroup(true, false, 1, "Normal");
            var path = new PathData(); path.Rect(100, 100, 600, 800);
            r.Fill(path, Matrix.Identity, false, new RgbColor(0.2, 0.4, 0.6), 1, "Normal");
            r.EndGroup();
            _ = r.ToRgba();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var allocated = Once();
        Assert.True(allocated < (long)W * H * 24, $"isolated root group allocated {allocated:N0} bytes");
        Assert.True(SoftwareRasterizer.PoolRetainedBytes <= 64L << 20);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0.4, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void RootGroupBufferReuseMatchesAnExplicitWhiteBackdrop(double alpha, bool knockout)
    {
        byte[] Render(bool explicitBackdrop)
        {
            using var r = new SoftwareRasterizer(31, 23);
            if (explicitBackdrop)
            {
                var white = new PathData(); white.Rect(0, 0, 31, 23);
                r.Fill(white, Matrix.Identity, false, new RgbColor(1, 1, 1), 1, "Normal");
            }
            r.BeginGroup(true, knockout, alpha, "Normal");
            var first = new PathData(); first.Rect(2.2, 3.3, 7.4, 8.5);
            r.Fill(first, Matrix.Identity, false, new RgbColor(0.2, 0.4, 0.6), 0.3, "Normal");
            var second = new PathData(); second.Rect(7.1, 8.2, 11.3, 4.4);
            r.Fill(second, Matrix.Identity, false, new RgbColor(0.8, 0.3, 0.1), 0.6, "Multiply");
            r.EndGroup();
            return r.ToRgba();
        }
        Assert.Equal(Render(true), Render(false));
    }

    [Fact]
    public void ModifiedPageKeepsItsNonWhiteBackdropUnderAPartialGroup()
    {
        byte[] Render(bool grouped)
        {
            using var r = new SoftwareRasterizer(31, 23);
            var background = new PathData(); background.Rect(0, 0, 31, 23);
            r.Fill(background, Matrix.Identity, false, new RgbColor(0.9, 0.5, 0.1), 1, "Normal");
            if (grouped) r.BeginGroup(true, false, 0.5, "Normal");
            var path = new PathData(); path.Rect(2, 3, 7, 8);
            r.Fill(path, Matrix.Identity, false, new RgbColor(0.2, 0.4, 0.6), grouped ? 1 : 0.5, "Normal");
            if (grouped) r.EndGroup();
            return r.ToRgba();
        }
        Assert.Equal(Render(false), Render(true));
    }
}