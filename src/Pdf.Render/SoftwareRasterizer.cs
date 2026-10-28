using Pdf.Content;

namespace Pdf.Render;

/// <summary>Managed anti-aliased software rasterizer implementing <see cref="IRenderBackend"/>. Surface is opaque RGB, white background.</summary>
public sealed class SoftwareRasterizer : IRenderBackend
{
    const int SubY = 4;
    readonly int _w, _h;
    readonly float[] _px; // RGB, 3 floats per pixel
    float[]? _clip;       // null = unclipped
    readonly Stack<float[]?> _stack = new();

    public int Width => _w;
    public int Height => _h;

    public SoftwareRasterizer(int width, int height)
    {
        _w = width; _h = height;
        _px = new float[width * height * 3];
        Array.Fill(_px, 1f);
    }

    public byte[] ToRgba()
    {
        var o = new byte[_w * _h * 4];
        for (var i = 0; i < _w * _h; i++)
        {
            o[i * 4] = (byte)Math.Clamp((int)Math.Round(_px[i * 3] * 255), 0, 255);
            o[i * 4 + 1] = (byte)Math.Clamp((int)Math.Round(_px[i * 3 + 1] * 255), 0, 255);
            o[i * 4 + 2] = (byte)Math.Clamp((int)Math.Round(_px[i * 3 + 2] * 255), 0, 255);
            o[i * 4 + 3] = 255;
        }
        return o;
    }

    public void Save() => _stack.Push(_clip);
    public void Restore() { if (_stack.Count > 0) _clip = _stack.Pop(); }

    public void Clip(PathData path, Matrix ctm, bool evenOdd)
    {
        var polys = ToPolys(path.Transform(ctm), true);
        var cov = Rasterize(polys, evenOdd, out var bx0, out var by0, out var bx1, out var by1);
        var nc = new float[_w * _h];
        if (cov != null)
            for (var y = by0; y < by1; y++)
                for (var x = bx0; x < bx1; x++)
                {
                    var c = cov[(y - by0) * (bx1 - bx0) + (x - bx0)];
                    nc[y * _w + x] = _clip == null ? c : c * _clip[y * _w + x];
                }
        _clip = nc;
    }

    public void Fill(PathData path, Matrix ctm, bool evenOdd, RgbColor color, double alpha, string blendMode)
    {
        var polys = ToPolys(path.Transform(ctm), true);
        Paint(polys, evenOdd, color, alpha, blendMode);
    }

    public void Stroke(PathData path, Matrix ctm, StrokeStyle style, RgbColor color, double alpha, string blendMode)
    {
        var scale = Math.Sqrt(Math.Abs(ctm.Det));
        var width = style.Width;
        if (width * scale < 1) width = scale > 0 ? 1 / scale : 1; // hairline: at least one device pixel
        var subs = path.Flatten(0.1 / Math.Max(scale, 1e-6));
        var polys = new List<List<(double X, double Y)>>();
        foreach (var (pts, closed) in subs)
        {
            foreach (var (dp, dclosed) in ApplyDash(pts, closed, style))
                Stroker.Build(dp, dclosed, width, style.Cap, style.Join, style.MiterLimit, polys);
        }
        var dev = polys.Select(p => p.Select(q => ctm.Apply(q.X, q.Y)).ToList()).ToList();
        Paint(dev, false, color, alpha, blendMode);
    }

    static IEnumerable<(List<(double X, double Y)>, bool)> ApplyDash(List<(double X, double Y)> pts, bool closed, StrokeStyle s)
    {
        if (s.Dash.Length == 0 || s.Dash.Sum() <= 0 || s.Dash.Any(d => d < 0)) { yield return (pts, closed); yield break; }
        var src = new List<(double X, double Y)>(pts);
        if (closed) src.Add(pts[0]);
        var idx = 0; var on = true;
        var phase = s.DashPhase % (s.Dash.Sum() * (s.Dash.Length % 2 == 1 ? 2 : 1));
        if (phase < 0) phase += s.Dash.Sum() * 2;
        var rem = s.Dash[0];
        while (phase > 0)
        {
            if (phase >= rem) { phase -= rem; idx = (idx + 1) % s.Dash.Length; rem = s.Dash[idx]; on = !on; }
            else { rem -= phase; phase = 0; }
        }
        var cur = new List<(double X, double Y)>();
        if (on) cur.Add(src[0]);
        var guard = 0;
        for (var i = 0; i + 1 < src.Count; i++)
        {
            var (x0, y0) = src[i]; var (x1, y1) = src[i + 1];
            var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            var pos = 0.0;
            while (len - pos > rem && guard++ < 200000)
            {
                pos += rem;
                var t = pos / len; var p = (x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
                if (on) { cur.Add(p); yield return (cur, false); cur = []; }
                else cur = [p];
                on = !on; idx = (idx + 1) % s.Dash.Length; rem = s.Dash[idx];
                if (rem <= 0 && s.Dash.All(d => d <= 0)) yield break;
            }
            rem -= len - pos;
            if (on) cur.Add((x1, y1));
        }
        if (on && cur.Count > 1) yield return (cur, false);
    }

    static List<List<(double X, double Y)>> ToPolys(PathData p, bool _)
        => p.Flatten(0.1).Select(s => s.Points).Where(l => l.Count > 1).ToList();

    void Paint(List<List<(double X, double Y)>> polys, bool evenOdd, RgbColor c, double alpha, string blend)
    {
        var cov = Rasterize(polys, evenOdd, out var bx0, out var by0, out var bx1, out var by1);
        if (cov == null) return;
        var bw = bx1 - bx0;
        for (var y = by0; y < by1; y++)
            for (var x = bx0; x < bx1; x++)
            {
                var a = cov[(y - by0) * bw + (x - bx0)];
                if (a <= 0) continue;
                if (_clip != null) a *= _clip[y * _w + x];
                if (a <= 0) continue;
                Blend(y * _w + x, (float)c.R, (float)c.G, (float)c.B, a * (float)alpha, blend);
            }
    }

    void Blend(int i, float r, float g, float b, float a, string mode)
    {
        var o = i * 3;
        if (mode is not ("Normal" or "Compatible"))
        {
            r = B(mode, _px[o], r); g = B(mode, _px[o + 1], g); b = B(mode, _px[o + 2], b);
        }
        _px[o] += (r - _px[o]) * a;
        _px[o + 1] += (g - _px[o + 1]) * a;
        _px[o + 2] += (b - _px[o + 2]) * a;
    }

    static float B(string mode, float cb, float cs) => mode switch
    {
        "Multiply" => cb * cs,
        "Screen" => cb + cs - cb * cs,
        "Overlay" => HardLight(cs, cb),
        "Darken" => Math.Min(cb, cs),
        "Lighten" => Math.Max(cb, cs),
        "ColorDodge" => cb == 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs)),
        "ColorBurn" => cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs),
        "HardLight" => HardLight(cb, cs),
        "SoftLight" => cs <= 0.5f ? cb - (1 - 2 * cs) * cb * (1 - cb)
            : cb + (2 * cs - 1) * ((cb <= 0.25f ? ((16 * cb - 12) * cb + 4) * cb : MathF.Sqrt(cb)) - cb),
        "Difference" => Math.Abs(cb - cs),
        "Exclusion" => cb + cs - 2 * cb * cs,
        _ => cs,
    };

    static float HardLight(float cb, float cs) => cs <= 0.5f ? cb * 2 * cs : cb + (2 * cs - 1) - cb * (2 * cs - 1);

    public void DrawImage(ImageData img, Matrix ctm, double alpha, RgbColor maskColor, string blendMode)
    {
        // Image space (u,v in px, v down) -> unit square (x = u/w, y = 1 - v/h) -> device.
        var m = new Matrix(1.0 / img.Width, 0, 0, -1.0 / img.Height, 0, 1).Then(ctm);
        var inv = m.Invert();
        var corners = new[] { ctm.Apply(0, 0), ctm.Apply(1, 0), ctm.Apply(0, 1), ctm.Apply(1, 1) };
        var x0 = Math.Max(0, (int)Math.Floor(corners.Min(c => c.X))); var x1 = Math.Min(_w, (int)Math.Ceiling(corners.Max(c => c.X)));
        var y0 = Math.Max(0, (int)Math.Floor(corners.Min(c => c.Y))); var y1 = Math.Min(_h, (int)Math.Ceiling(corners.Max(c => c.Y)));
        // Scale factor for box-filtering when downsampling.
        var sx = Math.Sqrt(inv.A * inv.A + inv.B * inv.B); var sy = Math.Sqrt(inv.C * inv.C + inv.D * inv.D);
        var rx = Math.Clamp((int)Math.Round(sx), 1, 8); var ry = Math.Clamp((int)Math.Round(sy), 1, 8);
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                double ar = 0, ag = 0, ab = 0, aa = 0; var n = 0;
                for (var j = 0; j < ry; j++)
                    for (var i = 0; i < rx; i++)
                    {
                        var (u, v) = inv.Apply(x + (i + 0.5) / rx, y + (j + 0.5) / ry);
                        var iu = (int)Math.Floor(u); var iv = (int)Math.Floor(v);
                        n++;
                        if (iu < 0 || iv < 0 || iu >= img.Width || iv >= img.Height) continue;
                        var o = (iv * img.Width + iu) * 4;
                        var pa = img.Rgba[o + 3] / 255.0;
                        if (img.IsMask) { ar += maskColor.R * pa; ag += maskColor.G * pa; ab += maskColor.B * pa; }
                        else { ar += img.Rgba[o] / 255.0 * pa; ag += img.Rgba[o + 1] / 255.0 * pa; ab += img.Rgba[o + 2] / 255.0 * pa; }
                        aa += pa;
                    }
                if (aa <= 0) continue;
                var cov = (float)(aa / n);
                if (_clip != null) cov *= _clip[y * _w + x];
                if (cov <= 0) continue;
                Blend(y * _w + x, (float)(ar / aa), (float)(ag / aa), (float)(ab / aa), cov * (float)alpha, blendMode);
            }
    }

    public void PaintShader(Func<double, double, RgbColor?> shader, double alpha, string blendMode)
    {
        for (var y = 0; y < _h; y++)
            for (var x = 0; x < _w; x++)
            {
                var cl = _clip == null ? 1f : _clip[y * _w + x];
                if (cl <= 0) continue;
                var c = shader(x + 0.5, y + 0.5);
                if (c is null) continue;
                Blend(y * _w + x, (float)c.Value.R, (float)c.Value.G, (float)c.Value.B, cl * (float)alpha, blendMode);
            }
    }

    /// <summary>Scanline coverage with SubY sub-scanlines and exact horizontal span coverage.</summary>
    float[]? Rasterize(List<List<(double X, double Y)>> polys, bool evenOdd, out int bx0, out int by0, out int bx1, out int by1)
    {
        bx0 = by0 = bx1 = by1 = 0;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in polys)
            foreach (var (x, y) in p)
            {
                if (double.IsNaN(x) || double.IsNaN(y)) return null;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        if (minX > maxX) return null;
        bx0 = Math.Max(0, (int)Math.Floor(minX)); bx1 = Math.Min(_w, (int)Math.Ceiling(maxX) + 1);
        by0 = Math.Max(0, (int)Math.Floor(minY)); by1 = Math.Min(_h, (int)Math.Ceiling(maxY) + 1);
        if (bx0 >= bx1 || by0 >= by1) return null;
        var bw = bx1 - bx0;
        var cov = new float[bw * (by1 - by0)];
        var edges = new List<(double X0, double Y0, double X1, double Y1, int Dir)>();
        foreach (var p in polys)
            for (var i = 0; i < p.Count; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Count];
                if (a.Y == b.Y) continue;
                edges.Add(a.Y < b.Y ? (a.X, a.Y, b.X, b.Y, 1) : (b.X, b.Y, a.X, a.Y, -1));
            }
        if (edges.Count == 0) return null;
        edges.Sort((l, r) => l.Y0.CompareTo(r.Y0));
        var active = new List<(double X0, double Y0, double X1, double Y1, int Dir)>();
        var next = 0;
        var xs = new List<(double X, int Dir)>();
        var weight = 1f / SubY;
        for (var y = by0; y < by1; y++)
            for (var s = 0; s < SubY; s++)
            {
                var sy = y + (s + 0.5) / SubY;
                while (next < edges.Count && edges[next].Y0 <= sy) active.Add(edges[next++]);
                active.RemoveAll(e => e.Y1 <= sy);
                if (active.Count == 0) continue;
                xs.Clear();
                foreach (var e in active) xs.Add((e.X0 + (e.X1 - e.X0) * (sy - e.Y0) / (e.Y1 - e.Y0), e.Dir));
                xs.Sort((l, r) => l.X.CompareTo(r.X));
                var wind = 0;
                for (var k = 0; k + 1 < xs.Count; k++)
                {
                    wind += xs[k].Dir;
                    var inside = evenOdd ? (k % 2 == 0) : wind != 0;
                    if (!inside) continue;
                    AddSpan(cov, bw, (y - by0), xs[k].X - bx0, xs[k + 1].X - bx0, weight);
                }
            }
        return cov;
    }

    static void AddSpan(float[] cov, int bw, int row, double xa, double xb, float weight)
    {
        if (xb <= 0 || xa >= bw) return;
        xa = Math.Max(0, xa); xb = Math.Min(bw, xb);
        if (xb <= xa) return;
        var ia = (int)xa; var ib = (int)xb;
        var o = row * bw;
        if (ia == ib) { cov[o + ia] += (float)(xb - xa) * weight; return; }
        cov[o + ia] += (float)(ia + 1 - xa) * weight;
        for (var i = ia + 1; i < ib; i++) cov[o + i] += weight;
        if (ib < bw) cov[o + ib] += (float)(xb - ib) * weight;
    }
}
