using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PortableDocument.Content;

namespace PortableDocument.Render;

/// <summary>
/// Managed anti-aliased software rasterizer implementing <see cref="IRenderBackend"/>. Output is opaque RGB over white; groups may blend in RGB or gray.
/// Scan conversion is row-at-a-time (4 sub-scanlines, exact horizontal coverage) with struct edges and no per-fill allocations.
/// </summary>
public sealed class SoftwareRasterizer : IRenderBackend, IDisposable
{
    const int SubY = 4;
    const float Weight = 1f / SubY;

    struct Edge { public double Y0, Y1, X0, Slope; public int Dir; }

    /// <summary>Clip region: a bounding box plus optional coverage (null = fully covered). Immutable; crops share the coverage array.</summary>
    sealed class ClipMask
    {
        public int X0, Y0, X1, Y1;
        public float[]? Cov; public int Stride, CovX0, CovY0;
        public bool Empty => X1 <= X0 || Y1 <= Y0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float At(int x, int y) => Cov is null ? 1f : Cov[(y - CovY0) * Stride + (x - CovX0)];
    }

    readonly int _w, _h;
    float[] _px; // RGB, 3 floats per pixel
    // Current layer: the page (opaque, origin 0,0) or a transparency-group buffer covering device rect [_ox,_ox+_lw) x [_oy,_oy+_lh).
    int _ox, _oy, _lw, _lh;
    float[]? _a;   // per-pixel alpha (null = opaque page)
    float[]? _ag;  // group-only alpha, for non-isolated groups
    float[]? _ip, _ia; // initial backdrop colour/alpha (non-isolated groups)
    bool _ko;      // knockout group
    bool _gray;    // current blending space (gray is stored as three equal components)
    GroupBlendSpace? _bs; // non-RGB/Gray blending space (CMYK, CalGray, CalRGB); layers stay sRGB, only blend functions run in this space
    bool _conv;    // colours must be projected into a one-component blending space before compositing
    bool _ais;     // ExtGState /AIS: constant alpha is shape, not opacity (only distinguishable inside knockout groups)
    readonly Stack<Group> _groups = new();
    ClipMask _clip;
    readonly Stack<(ClipMask, ClipMask?, bool)> _stack = new();

    Edge[] _edges = new Edge[512]; int _en;
    double _minX, _minY, _maxX, _maxY; bool _bad;
    int[] _active = new int[64]; int _an;
    double[] _xs = new double[64]; int[] _xd = new int[64];
    readonly float[] _cov;
    int _next, _y, _by1, _bx0, _bx1, _tmin, _tmax; bool _eo;

    static readonly Comparison<Edge> ByY0 = static (a, b) => a.Y0.CompareTo(b.Y0);

    public int Width => _w;
    public int Height => _h;

    public const int MaxDimension = 1 << 16;
    public const long MaxPixels = 100_000_000;

    public SoftwareRasterizer(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new global::PortableDocument.Core.PdfParseException($"Invalid raster size {width}x{height}");
        _w = width; _h = height; _lw = width; _lh = height;
        _px = System.Buffers.ArrayPool<float>.Shared.Rent(width * height * 3);
        _px.AsSpan(0, width * height * 3).Fill(1f);
        _cov = new float[width + 2];
        _clip = new ClipMask { X0 = 0, Y0 = 0, X1 = width, Y1 = height };
    }

    /// <summary>Returns the pixel buffer to the pool; the rasterizer must not be used afterwards.</summary>
    public void Dispose()
    {
        var p = _px; _px = [];
        if (p.Length > 0) System.Buffers.ArrayPool<float>.Shared.Return(p);
    }

    public byte[] ToRgba()
    {
        var o = GC.AllocateUninitializedArray<byte>(_w * _h * 4);
        var px = _px;
        var w = _w;
        void Rows(int y0, int y1)
        {
            for (var y = y0; y < y1; y++)
            {
                var s = px.AsSpan(y * w * 3, w * 3);
                var d = o.AsSpan(y * w * 4, w * 4);
                for (int i = 0, j = 0, k = 0; i < w; i++, j += 3, k += 4)
                {
                    d[k] = ToByte(s[j]); d[k + 1] = ToByte(s[j + 1]); d[k + 2] = ToByte(s[j + 2]); d[k + 3] = 255;
                }
            }
        }
        if (!OperatingSystem.IsBrowser() && (long)_w * _h > 100_000)
        {
            const int Band = 32;
            Parallel.For(0, (_h + Band - 1) / Band, b => Rows(b * Band, Math.Min(_h, b * Band + Band)));
        }
        else Rows(0, _h);
        return o;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte ToByte(float v) { var i = (int)(v * 255f + 0.5f); return (byte)(i < 0 ? 0 : i > 255 ? 255 : i); }

    public void Save() => _stack.Push((_clip, _preMask, _ais));
    public void Restore() { if (_stack.Count > 0) (_clip, _preMask, _ais) = _stack.Pop(); }

    public void SetAlphaIsShape(bool alphaIsShape) => _ais = alphaIsShape;

    ClipMask? _preMask; // clip in force before the current soft mask was applied

    void FillBackground(RgbColor c) 
    {
        var s = _px.AsSpan(0, _w * _h * 3);
        for (var i = 0; i < s.Length; i += 3) { s[i] = (float)c.R; s[i + 1] = (float)c.G; s[i + 2] = (float)c.B; }
    }

    float[] Luminosity()
    {
        var m = new float[_w * _h];
        var p = _px;
        for (var i = 0; i < m.Length; i++) m[i] = 0.3f * p[i * 3] + 0.59f * p[i * 3 + 1] + 0.11f * p[i * 3 + 2];
        return m;
    }

    public void SetSoftMask(Action<IRenderBackend>? draw, bool luminosity, RgbColor backdrop) => SetSoftMask(draw, luminosity, backdrop, null);

    public void SetSoftMask(Action<IRenderBackend>? draw, bool luminosity, RgbColor backdrop, Func<double, double>? transfer)
    {
        if (draw is null) { if (_preMask is not null) { _clip = _preMask; _preMask = null; } return; }
        float[] m;
        if (luminosity)
        {
            using var off = new SoftwareRasterizer(_w, _h);
            off.FillBackground(backdrop); draw(off); m = off.Luminosity();
        }
        else
        {
            // Alpha = 1 - (white-backed render - black-backed render).
            using var ob = new SoftwareRasterizer(_w, _h); ob.FillBackground(new RgbColor(0, 0, 0)); draw(ob);
            using var ow = new SoftwareRasterizer(_w, _h); ow.FillBackground(new RgbColor(1, 1, 1)); draw(ow);
            var lb = ob.Luminosity(); m = ow.Luminosity();
            for (var i = 0; i < m.Length; i++) m[i] = Math.Clamp(1f - (m[i] - lb[i]), 0f, 1f);
        }
        if (transfer is not null)
        {
            // /TR is applied to the mask value (over the whole surface, so the area outside the group maps through it too).
            const int N = 1024;
            var lut = new float[N + 1];
            for (var i = 0; i <= N; i++) lut[i] = (float)Math.Clamp(transfer((double)i / N), 0, 1);
            for (var i = 0; i < m.Length; i++) { var p = Math.Clamp(m[i], 0f, 1f) * N; var k = Math.Min((int)p, N - 1); m[i] = lut[k] + (lut[k + 1] - lut[k]) * (p - k); }
        }
        var b = _preMask ?? _clip;
        if (b.Empty) return;
        int bw = b.X1 - b.X0, bh = b.Y1 - b.Y0;
        var cov = new float[bw * bh];
        for (var y = 0; y < bh; y++)
            for (var x = 0; x < bw; x++)
                cov[y * bw + x] = b.At(b.X0 + x, b.Y0 + y) * m[(b.Y0 + y) * _w + b.X0 + x];
        _preMask = b;
        _clip = new ClipMask { X0 = b.X0, Y0 = b.Y0, X1 = b.X1, Y1 = b.Y1, Cov = cov, Stride = bw, CovX0 = b.X0, CovY0 = b.Y0 };
    }

    // ---------------------------------------------------------------- edge building

    void BeginEdges() { _en = 0; _minX = _minY = double.MaxValue; _maxX = _maxY = double.MinValue; _bad = false; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Line(double x0, double y0, double x1, double y1)
    {
        if (double.IsNaN(x0 + y0 + x1 + y1) || double.IsInfinity(x0 + y0 + x1 + y1)) { _bad = true; return; }
        if (x0 < _minX) _minX = x0; if (x0 > _maxX) _maxX = x0; if (y0 < _minY) _minY = y0; if (y0 > _maxY) _maxY = y0;
        if (x1 < _minX) _minX = x1; if (x1 > _maxX) _maxX = x1; if (y1 < _minY) _minY = y1; if (y1 > _maxY) _maxY = y1;
        if (y0 == y1) return;
        if (_en == _edges.Length) Array.Resize(ref _edges, _en * 2);
        ref var e = ref _edges[_en++];
        if (y0 < y1) { e.Y0 = y0; e.Y1 = y1; e.X0 = x0; e.Slope = (x1 - x0) / (y1 - y0); e.Dir = 1; }
        else { e.Y0 = y1; e.Y1 = y0; e.X0 = x1; e.Slope = (x0 - x1) / (y0 - y1); e.Dir = -1; }
    }

    void AddPath(PathData path, in Matrix m, double tol)
    {
        var ops = CollectionsMarshal.AsSpan(path.Ops);
        var pts = CollectionsMarshal.AsSpan(path.Pts);
        var pi = 0;
        double sx = 0, sy = 0, cx = 0, cy = 0; var open = false;
        foreach (var op in ops)
        {
            switch (op)
            {
                case PathOp.Move:
                    if (open && (cx != sx || cy != sy)) Line(cx, cy, sx, sy);
                    sx = cx = m.A * pts[pi] + m.C * pts[pi + 1] + m.E; sy = cy = m.B * pts[pi] + m.D * pts[pi + 1] + m.F; pi += 2; open = true; break;
                case PathOp.Line:
                    {
                        var x = m.A * pts[pi] + m.C * pts[pi + 1] + m.E; var y = m.B * pts[pi] + m.D * pts[pi + 1] + m.F; pi += 2;
                        Line(cx, cy, x, y); cx = x; cy = y; break;
                    }
                case PathOp.Curve:
                    {
                        double x1 = m.A * pts[pi] + m.C * pts[pi + 1] + m.E, y1 = m.B * pts[pi] + m.D * pts[pi + 1] + m.F;
                        double x2 = m.A * pts[pi + 2] + m.C * pts[pi + 3] + m.E, y2 = m.B * pts[pi + 2] + m.D * pts[pi + 3] + m.F;
                        double x3 = m.A * pts[pi + 4] + m.C * pts[pi + 5] + m.E, y3 = m.B * pts[pi + 4] + m.D * pts[pi + 5] + m.F;
                        pi += 6;
                        var dd = Math.Abs(cx - 2 * x1 + x2) + Math.Abs(cy - 2 * y1 + y2) + Math.Abs(x1 - 2 * x2 + x3) + Math.Abs(y1 - 2 * y2 + y3);
                        var n = Math.Clamp((int)Math.Ceiling(Math.Sqrt(dd / tol) * 0.75), 2, 100);
                        // Cubic in power basis, evaluated with Horner's rule.
                        double ax = -cx + 3 * x1 - 3 * x2 + x3, bx = 3 * cx - 6 * x1 + 3 * x2, ccx = -3 * cx + 3 * x1;
                        double ay = -cy + 3 * y1 - 3 * y2 + y3, by = 3 * cy - 6 * y1 + 3 * y2, ccy = -3 * cy + 3 * y1;
                        double px = cx, py = cy;
                        for (var i = 1; i <= n; i++)
                        {
                            var t = (double)i / n;
                            double qx = i == n ? x3 : ((ax * t + bx) * t + ccx) * t + cx;
                            double qy = i == n ? y3 : ((ay * t + by) * t + ccy) * t + cy;
                            Line(px, py, qx, qy); px = qx; py = qy;
                        }
                        cx = x3; cy = y3; break;
                    }
                case PathOp.Close:
                    if (open) { Line(cx, cy, sx, sy); cx = sx; cy = sy; }
                    break;
            }
        }
        if (open && (cx != sx || cy != sy)) Line(cx, cy, sx, sy);
    }

    void AddPoly(List<(double X, double Y)> p, in Matrix m)
    {
        if (p.Count < 2) return;
        var span = CollectionsMarshal.AsSpan(p);
        var (fx, fy) = m.Apply(span[0].X, span[0].Y);
        double px = fx, py = fy;
        for (var i = 1; i < span.Length; i++)
        {
            var qx = m.A * span[i].X + m.C * span[i].Y + m.E; var qy = m.B * span[i].X + m.D * span[i].Y + m.F;
            Line(px, py, qx, qy); px = qx; py = qy;
        }
        Line(px, py, fx, fy);
    }

    // ---------------------------------------------------------------- scan conversion

    bool BeginScan(bool evenOdd, out int bx0, out int by0, out int bx1, out int by1)
    {
        bx0 = by0 = bx1 = by1 = 0;
        if (_bad || _en == 0 || _minX > _maxX) return false;
        bx0 = Math.Max(_clip.X0, (int)Math.Floor(Math.Max(_minX, -1)));
        bx1 = Math.Min(_clip.X1, (int)Math.Ceiling(Math.Min(_maxX, _w + 1)) + 1);
        by0 = Math.Max(_clip.Y0, (int)Math.Floor(Math.Max(_minY, -1)));
        by1 = Math.Min(_clip.Y1, (int)Math.Ceiling(Math.Min(_maxY, _h + 1)) + 1);
        if (bx0 >= bx1 || by0 >= by1) return false;
        _edges.AsSpan(0, _en).Sort(ByY0);
        _next = 0; _an = 0; _eo = evenOdd; _y = by0; _by1 = by1; _bx0 = bx0; _bx1 = bx1;
        _tmin = int.MaxValue; _tmax = -1;
        return true;
    }

    bool NextRow(out int y, out int xa, out int xb)
    {
        if (_tmax >= _tmin) Array.Clear(_cov, _tmin, _tmax - _tmin + 1);
        _tmin = int.MaxValue; _tmax = -1;
        y = xa = xb = 0;
        while (_y < _by1)
        {
            var cy = _y;
            if (_an == 0)
            {
                if (_next >= _en) return false;
                var ny = (int)Math.Floor(_edges[_next].Y0);
                if (ny > cy) { _y = ny; continue; }
            }
            _y++;
            for (var s = 0; s < SubY; s++)
            {
                var sy = cy + (s + 0.5) / SubY;
                while (_next < _en && _edges[_next].Y0 <= sy)
                {
                    if (_an == _active.Length) Array.Resize(ref _active, _an * 2);
                    _active[_an++] = _next++;
                }
                if (_an > _xs.Length) { Array.Resize(ref _xs, _an * 2); Array.Resize(ref _xd, _an * 2); }
                int keep = 0, cnt = 0;
                for (var i = 0; i < _an; i++)
                {
                    var idx = _active[i];
                    ref var e = ref _edges[idx];
                    if (e.Y1 <= sy) continue;
                    _active[keep++] = idx;
                    var x = e.X0 + (sy - e.Y0) * e.Slope;
                    var j = cnt++;
                    while (j > 0 && _xs[j - 1] > x) { _xs[j] = _xs[j - 1]; _xd[j] = _xd[j - 1]; j--; }
                    _xs[j] = x; _xd[j] = e.Dir;
                }
                _an = keep;
                var wind = 0;
                for (var k = 0; k + 1 < cnt; k++)
                {
                    wind += _xd[k];
                    if (_eo ? (k & 1) != 0 : wind == 0) continue;
                    AddSpan(_xs[k], _xs[k + 1]);
                }
            }
            if (_tmax >= _tmin) { y = cy; xa = _tmin; xb = _tmax + 1; return true; }
        }
        return false;
    }

    void AddSpan(double xa, double xb)
    {
        if (xb <= _bx0 || xa >= _bx1) return;
        if (xa < _bx0) xa = _bx0;
        if (xb > _bx1) xb = _bx1;
        if (xb <= xa) return;
        var ia = (int)xa; var ib = (int)xb;
        var cov = _cov;
        var last = ib < _bx1 ? ib : _bx1 - 1;
        if (ia < _tmin) _tmin = ia;
        if (last > _tmax) _tmax = last;
        if (ia == ib) { cov[ia] += (float)(xb - xa) * Weight; return; }
        cov[ia] += (float)(ia + 1 - xa) * Weight;
        for (var i = ia + 1; i < ib; i++) cov[i] += Weight;
        if (ib < _bx1) cov[ib] += (float)(xb - ib) * Weight;
    }

    // ---------------------------------------------------------------- clipping

    public void Clip(PathData path, Matrix ctm, bool evenOdd)
    {
        if (_preMask is null) { ClipCore(path, ctm, evenOdd); return; }
        // A soft mask is folded into the clip; keep the mask-free clip in step so it survives clearing the mask.
        var masked = _clip;
        _clip = _preMask;
        ClipCore(path, ctm, evenOdd);
        _preMask = _clip;
        _clip = masked;
        ClipCore(path, ctm, evenOdd);
    }

    void ClipCore(PathData path, Matrix ctm, bool evenOdd)
    {
        if (_clip.Empty) return;
        if (TryRectClip(path, ctm)) return;
        BeginEdges(); AddPath(path, ctm, 0.1);
        if (!BeginScan(evenOdd, out var bx0, out var by0, out var bx1, out var by1)) { _clip = new ClipMask(); return; }
        var bw = bx1 - bx0;
        var mask = new float[bw * (by1 - by0)];
        var old = _clip;
        var lo = int.MaxValue; var hi = int.MinValue;
        while (NextRow(out var y, out var xa, out var xb))
        {
            var row = y - by0;
            for (var x = xa; x < xb; x++)
            {
                var c = _cov[x]; if (c > 1f) c = 1f;
                if (old.Cov is not null) c *= old.At(x, y);
                mask[row * bw + (x - bx0)] = c;
            }
            if (row < lo) lo = row; if (row > hi) hi = row;
        }
        _clip = new ClipMask { X0 = bx0, Y0 = by0, X1 = bx1, Y1 = by1, Cov = mask, Stride = bw, CovX0 = bx0, CovY0 = by0 };
    }

    /// <summary>Axis-aligned, pixel-aligned rectangles (the overwhelmingly common clip) just crop the clip box.</summary>
    bool TryRectClip(PathData path, in Matrix m)
    {
        var ops = path.Ops;
        if (ops.Count is not (4 or 5) || ops[0] != PathOp.Move || ops[1] != PathOp.Line || ops[2] != PathOp.Line || ops[3] != PathOp.Line) return false;
        if (ops.Count == 5 && ops[4] != PathOp.Close) return false;
        var p = path.Pts;
        double[] xs = new double[4], ys = new double[4];
        for (var i = 0; i < 4; i++) { var (x, y) = m.Apply(p[i * 2], p[i * 2 + 1]); xs[i] = x; ys[i] = y; }
        const double eps = 1e-3;
        bool a = Math.Abs(xs[0] - xs[1]) < eps && Math.Abs(ys[1] - ys[2]) < eps && Math.Abs(xs[2] - xs[3]) < eps && Math.Abs(ys[3] - ys[0]) < eps;
        bool b = Math.Abs(ys[0] - ys[1]) < eps && Math.Abs(xs[1] - xs[2]) < eps && Math.Abs(ys[2] - ys[3]) < eps && Math.Abs(xs[3] - xs[0]) < eps;
        if (!a && !b) return false;
        double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
        static bool Whole(double v) => Math.Abs(v - Math.Round(v)) < eps;
        if (!Whole(minX) || !Whole(maxX) || !Whole(minY) || !Whole(maxY)) return false;
        var o = _clip;
        _clip = new ClipMask
        {
            X0 = Math.Max(o.X0, (int)Math.Round(minX)), X1 = Math.Min(o.X1, (int)Math.Round(maxX)),
            Y0 = Math.Max(o.Y0, (int)Math.Round(minY)), Y1 = Math.Min(o.Y1, (int)Math.Round(maxY)),
            Cov = o.Cov, Stride = o.Stride, CovX0 = o.CovX0, CovY0 = o.CovY0,
        };
        return true;
    }

    // ---------------------------------------------------------------- painting

    public void Fill(PathData path, Matrix ctm, bool evenOdd, RgbColor color, double alpha, string blendMode)
    {
        if (_clip.Empty) return;
        BeginEdges(); AddPath(path, ctm, 0.1);
        PaintScan(evenOdd, color, alpha, blendMode);
    }

    public void Stroke(PathData path, Matrix ctm, StrokeStyle style, RgbColor color, double alpha, string blendMode)
    {
        if (_clip.Empty) return;
        var scale = Math.Sqrt(Math.Abs(ctm.Det));
        var width = style.Width;
        if (width * scale < 1) width = scale > 0 ? 1 / scale : 1; // hairline: at least one device pixel
        var subs = path.Flatten(0.1 / Math.Max(scale, 1e-6));
        var polys = new List<List<(double X, double Y)>>();
        foreach (var (pts, closed) in subs)
        {
            if (style.Dash.Length == 0) Stroker.Build(pts, closed, width, style.Cap, style.Join, style.MiterLimit, polys);
            else foreach (var (dp, dclosed) in ApplyDash(pts, closed, style))
                    Stroker.Build(dp, dclosed, width, style.Cap, style.Join, style.MiterLimit, polys);
        }
        BeginEdges();
        foreach (var p in polys) AddPoly(p, ctm);
        PaintScan(false, color, alpha, blendMode);
    }

    internal static IEnumerable<(List<(double X, double Y)>, bool)> ApplyDash(List<(double X, double Y)> pts, bool closed, StrokeStyle s)
    {
        var dash = s.Dash;
        double sum = 0; var anyNeg = false; var allZero = true;
        foreach (var d in dash) { sum += d; if (d < 0) anyNeg = true; if (d > 0) allZero = false; }
        if (sum <= 0 || anyNeg) { yield return (pts, closed); yield break; }
        var src = new List<(double X, double Y)>(pts);
        if (closed) src.Add(pts[0]);
        var idx = 0; var on = true;
        var phase = s.DashPhase % (sum * (dash.Length % 2 == 1 ? 2 : 1));
        if (phase < 0) phase += sum * 2;
        var rem = dash[0];
        while (phase > 0)
        {
            if (phase >= rem) { phase -= rem; idx = (idx + 1) % dash.Length; rem = dash[idx]; on = !on; }
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
                on = !on; idx = (idx + 1) % dash.Length; rem = dash[idx];
                if (rem <= 0 && allZero) yield break;
            }
            rem -= len - pos;
            if (on) cur.Add((x1, y1));
        }
        if (on && cur.Count > 1) yield return (cur, false);
    }

    OverprintPaint? _op;

    public void Fill(PathData path, Matrix ctm, bool evenOdd, RgbColor color, double alpha, string blendMode, OverprintPaint overprint)
    {
        _op = overprint;
        try { Fill(path, ctm, evenOdd, color, alpha, blendMode); }
        finally { _op = null; }
    }

    public void Stroke(PathData path, Matrix ctm, StrokeStyle style, RgbColor color, double alpha, string blendMode, OverprintPaint overprint)
    {
        _op = overprint;
        try { Stroke(path, ctm, style, color, alpha, blendMode); }
        finally { _op = null; }
    }

    /// <summary>
    /// Process-colorant overprint simulation (CompatibleOverprint, 11.7.4.3): the backdrop is decomposed to CMYK with the naive complement model,
    /// unpainted colorants keep it, and the colour change relative to the source's own naive value is applied to the (profile-converted) source RGB.
    /// </summary>
    static void ProcessOverprint(OverprintPaint op, float br, float bg, float bb, float sr, float sg, float sb, out float r, out float g, out float b)
    {
        var k = 1f - MathF.Max(br, MathF.Max(bg, bb));
        float c = 0, m = 0, y = 0;
        if (k < 0.9999f) { var d = 1f - k; c = (1f - br - k) / d; m = (1f - bg - k) / d; y = (1f - bb - k) / d; }
        var mask = op.Mask;
        var tc = (mask & 1) != 0 ? op.C : c; var tm = (mask & 2) != 0 ? op.M : m;
        var ty = (mask & 4) != 0 ? op.Y : y; var tk = (mask & 8) != 0 ? op.K : k;
        var kc = 1f - op.K;
        r = Math.Clamp(sr + (1f - tc) * (1f - tk) - (1f - op.C) * kc, 0f, 1f);
        g = Math.Clamp(sg + (1f - tm) * (1f - tk) - (1f - op.M) * kc, 0f, 1f);
        b = Math.Clamp(sb + (1f - ty) * (1f - tk) - (1f - op.Y) * kc, 0f, 1f);
    }

    void PaintScan(bool evenOdd, RgbColor c, double alpha, string blendMode)
    {
        if (!BeginScan(evenOdd, out _, out _, out _, out _)) return;
        float r = (float)c.R, g = (float)c.G, b = (float)c.B, al = (float)alpha;
        var mode = ParseBlend(blendMode);
        var clip = _clip;
        var px = _px;
        if (_op is { } op)
        {
            if (op.Spot) { if (mode == Blend.Normal) mode = Blend.Multiply; }
            else if (mode == Blend.Normal)
            {
                var layerAlpha = _a;
                while (NextRow(out var y, out var xa, out var xb))
                {
                    var rowBase = (y - _oy) * _lw - _ox;
                    for (var x = xa; x < xb; x++)
                    {
                        var a = _cov[x];
                        if (a <= 0f) continue;
                        if (a > 1f) a = 1f;
                        if (clip.Cov is not null) a *= clip.At(x, y);
                        if (a * al <= 0f) continue;
                        var o = rowBase + x;
                        var sr = r; var sg = g; var sb = b;
                        if (layerAlpha is null || layerAlpha[o] > 0f)
                            ProcessOverprint(op, px[o * 3], px[o * 3 + 1], px[o * 3 + 2], r, g, b, out sr, out sg, out sb);
                        Put(o, sr, sg, sb, a, al, mode);
                    }
                }
                return;
            }
        }
        while (NextRow(out var y, out var xa, out var xb))
        {
            var rowBase = (y - _oy) * _lw - _ox;
            for (var x = xa; x < xb; x++)
            {
                var a = _cov[x];
                if (a <= 0f) continue;
                if (a > 1f) a = 1f;
                if (clip.Cov is not null) a *= clip.At(x, y);
                if (a * al <= 0f) continue;
                Put(rowBase + x, r, g, b, a, al, mode);
            }
        }
    }

    enum Blend : byte { Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity }

    static Blend ParseBlend(string m) => m switch
    {
        "Multiply" => Blend.Multiply, "Screen" => Blend.Screen, "Overlay" => Blend.Overlay, "Darken" => Blend.Darken, "Lighten" => Blend.Lighten,
        "ColorDodge" => Blend.ColorDodge, "ColorBurn" => Blend.ColorBurn, "HardLight" => Blend.HardLight, "SoftLight" => Blend.SoftLight,
        "Difference" => Blend.Difference, "Exclusion" => Blend.Exclusion,
        "Hue" => Blend.Hue, "Saturation" => Blend.Saturation, "Color" => Blend.Color, "Luminosity" => Blend.Luminosity, _ => Blend.Normal,
    };

    static void BlendRgb(Blend mode, float br, float bg, float bb, float sr, float sg, float sb, out float r, out float g, out float b)
    {
        if (mode < Blend.Hue) { r = B(mode, br, sr); g = B(mode, bg, sg); b = B(mode, bb, sb); return; }
        Span<float> c = stackalloc float[3];
        switch (mode)
        {
            case Blend.Hue: c[0] = sr; c[1] = sg; c[2] = sb; SetSat(c, Sat(br, bg, bb)); SetLum(c, Lum(br, bg, bb)); break;
            case Blend.Saturation: c[0] = br; c[1] = bg; c[2] = bb; SetSat(c, Sat(sr, sg, sb)); SetLum(c, Lum(br, bg, bb)); break;
            case Blend.Color: c[0] = sr; c[1] = sg; c[2] = sb; SetLum(c, Lum(br, bg, bb)); break;
            default: c[0] = br; c[1] = bg; c[2] = bb; SetLum(c, Lum(sr, sg, sb)); break;
        }
        r = c[0]; g = c[1]; b = c[2];
    }

    static float Lum(float r, float g, float b) => 0.3f * r + 0.59f * g + 0.11f * b;
    static float Sat(float r, float g, float b) => Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

    static void SetLum(Span<float> c, float l)
    {
        var d = l - Lum(c[0], c[1], c[2]);
        c[0] += d; c[1] += d; c[2] += d;
        var lum = Lum(c[0], c[1], c[2]);
        var n = Math.Min(c[0], Math.Min(c[1], c[2])); var x = Math.Max(c[0], Math.Max(c[1], c[2]));
        if (n < 0f) for (var i = 0; i < 3; i++) c[i] = lum + (c[i] - lum) * lum / (lum - n);
        if (x > 1f) for (var i = 0; i < 3; i++) c[i] = lum + (c[i] - lum) * (1 - lum) / (x - lum);
    }

    static void SetSat(Span<float> c, float s)
    {
        int imax = 0, imin = 0;
        for (var i = 1; i < 3; i++) { if (c[i] > c[imax]) imax = i; if (c[i] < c[imin]) imin = i; }
        if (imax == imin) { c[0] = c[1] = c[2] = 0; return; }
        var imid = 3 - imax - imin;
        var mx = c[imax]; var mn = c[imin];
        c[imid] = (c[imid] - mn) * s / (mx - mn);
        c[imax] = s; c[imin] = 0;
    }

    /// <summary>Composites one source sample (shape × opacity) onto the current layer, honouring blend mode, group alpha and knockout.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Put(int o, float r, float g, float b, float shape, float q, Blend mode)
    {
        // Classic device conversion (10.4.2.2), before compositing rather than after flattening.
        if (_conv) Project(ref r, ref g, ref b);
        var a = _a;
        if (a is null) { BlendOpaque(_px, o * 3, r, g, b, shape * q, mode); return; }
        PutLayer(o, r, g, b, shape, q, mode);
    }

    void Project(ref float r, ref float g, ref float b)
    {
        if (_bs is null) { r = g = b = Lum(r, g, b); return; }
        Span<float> c = stackalloc float[4];
        _bs.FromRgb(r, g, b, c);
        _bs.ToRgb(c, out r, out g, out b);
    }

    /// <summary>Blend function in the current group's blending space (11.3.5): RGB/Gray directly, other spaces through <see cref="GroupBlendSpace"/>.</summary>
    void Mix(Blend mode, float br, float bg, float bb, float sr, float sg, float sb, out float r, out float g, out float b)
    {
        if (_bs is null) BlendRgb(mode, br, bg, bb, sr, sg, sb, out r, out g, out b);
        else BlendIn(_bs, mode, br, bg, bb, sr, sg, sb, out r, out g, out b);
    }

    static void BlendIn(GroupBlendSpace s, Blend mode, float br, float bg, float bb, float sr, float sg, float sb, out float r, out float g, out float b)
    {
        Span<float> cb = stackalloc float[4], cs = stackalloc float[4], o = stackalloc float[4];
        var n = s.Components;
        s.FromRgb(br, bg, bb, cb); s.FromRgb(sr, sg, sb, cs);
        // Subtractive spaces are complemented so that the additive blend functions apply (11.3.5.2).
        if (s.Subtractive) for (var i = 0; i < n; i++) { cb[i] = 1f - cb[i]; cs[i] = 1f - cs[i]; }
        if (mode < Blend.Hue) for (var i = 0; i < n; i++) o[i] = B(mode, cb[i], cs[i]);
        else if (n == 1) { BlendRgb(mode, cb[0], cb[0], cb[0], cs[0], cs[0], cs[0], out o[0], out _, out _); }
        else
        {
            BlendRgb(mode, cb[0], cb[1], cb[2], cs[0], cs[1], cs[2], out o[0], out o[1], out o[2]);
            // CMYK nonseparable modes act on the CMY complements; K comes from the source for Luminosity and the backdrop otherwise (11.3.5.3).
            if (n == 4) o[3] = mode == Blend.Luminosity ? cs[3] : cb[3];
        }
        if (s.Subtractive) for (var i = 0; i < n; i++) o[i] = 1f - o[i];
        s.ToRgb(o, out r, out g, out b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void BlendOpaque(float[] px, int o, float r, float g, float b, float a, Blend mode)
    {
        if (mode != Blend.Normal) Mix(mode, px[o], px[o + 1], px[o + 2], r, g, b, out r, out g, out b);
        if (a >= 0.9999f) { px[o] = r; px[o + 1] = g; px[o + 2] = b; return; }
        px[o] += (r - px[o]) * a;
        px[o + 1] += (g - px[o + 1]) * a;
        px[o + 2] += (b - px[o + 2]) * a;
    }

    /// <summary>ISO 32000-2 11.3.6 compositing of source (cs, as) over backdrop (cb, ab).</summary>
    void Comp(float cbr, float cbg, float cbb, float ab, float r, float g, float b, float s, Blend mode,
        out float orr, out float og, out float ob, out float oa)
    {
        if (mode != Blend.Normal && ab > 0f)
        {
            Mix(mode, cbr, cbg, cbb, r, g, b, out var br, out var bg, out var bb);
            r += ab * (br - r); g += ab * (bg - g); b += ab * (bb - b);
        }
        oa = ab + s - ab * s;
        if (oa <= 0f) { orr = og = ob = 0; return; }
        var k = s / oa;
        orr = cbr + (r - cbr) * k; og = cbg + (g - cbg) * k; ob = cbb + (b - cbb) * k;
    }

    void PutLayer(int o, float r, float g, float b, float shape, float q, Blend mode)
    {
        var px = _px; var a = _a!; var o3 = o * 3;
        if (!_ko)
        {
            var s = shape * q;
            if (s <= 0f) return;
            Comp(px[o3], px[o3 + 1], px[o3 + 2], a[o], r, g, b, s, mode, out px[o3], out px[o3 + 1], out px[o3 + 2], out a[o]);
            if (_ag is { } ga) ga[o] += s - ga[o] * s;
            return;
        }
        if (shape <= 0f) return;
        if (_ais) { shape *= q; q = 1f; } // /AIS true: constant alpha is shape, so it fades the knockout instead of the opacity (11.6.4.4)
        float ir = 0, ig = 0, ib = 0, ia = 0;
        if (_ip is { } ip) { ir = ip[o3]; ig = ip[o3 + 1]; ib = ip[o3 + 2]; ia = _ia![o]; }
        Comp(ir, ig, ib, ia, r, g, b, q, mode, out var nr, out var ng, out var nb, out var na);
        var pa = a[o];
        var ra = pa + (na - pa) * shape;
        if (ra > 0f)
        {
            var inv = 1f / ra;
            px[o3] = (px[o3] * pa + (nr * na - px[o3] * pa) * shape) * inv;
            px[o3 + 1] = (px[o3 + 1] * pa + (ng * na - px[o3 + 1] * pa) * shape) * inv;
            px[o3 + 2] = (px[o3 + 2] * pa + (nb * na - px[o3 + 2] * pa) * shape) * inv;
        }
        a[o] = ra;
        if (_ag is { } gk) gk[o] += (q - gk[o]) * shape;
    }

    static float B(Blend mode, float cb, float cs) => mode switch
    {
        Blend.Multiply => cb * cs,
        Blend.Screen => cb + cs - cb * cs,
        Blend.Overlay => HardLight(cs, cb),
        Blend.Darken => Math.Min(cb, cs),
        Blend.Lighten => Math.Max(cb, cs),
        Blend.ColorDodge => cb == 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs)),
        Blend.ColorBurn => cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs),
        Blend.HardLight => HardLight(cb, cs),
        Blend.SoftLight => cs <= 0.5f ? cb - (1 - 2 * cs) * cb * (1 - cb)
            : cb + (2 * cs - 1) * ((cb <= 0.25f ? ((16 * cb - 12) * cb + 4) * cb : MathF.Sqrt(cb)) - cb),
        Blend.Difference => Math.Abs(cb - cs),
        Blend.Exclusion => cb + cs - 2 * cb * cs,
        _ => cs,
    };

    static float HardLight(float cb, float cs) => cs <= 0.5f ? cb * 2 * cs : cb + (2 * cs - 1) - cb * (2 * cs - 1);

    public void DrawImage(ImageData img, Matrix ctm, double alpha, RgbColor maskColor, string blendMode)
    {
        if (_clip.Empty) return;
        // Image space (u,v in px, v down) -> unit square (x = u/w, y = 1 - v/h) -> device.
        var inv = new Matrix(1.0 / img.Width, 0, 0, -1.0 / img.Height, 0, 1).Then(ctm).Invert();
        var (c0x, c0y) = ctm.Apply(0, 0); var (c1x, c1y) = ctm.Apply(1, 0); var (c2x, c2y) = ctm.Apply(0, 1); var (c3x, c3y) = ctm.Apply(1, 1);
        var x0 = Math.Max(_clip.X0, (int)Math.Floor(Math.Min(Math.Min(c0x, c1x), Math.Min(c2x, c3x))));
        var x1 = Math.Min(_clip.X1, (int)Math.Ceiling(Math.Max(Math.Max(c0x, c1x), Math.Max(c2x, c3x))));
        var y0 = Math.Max(_clip.Y0, (int)Math.Floor(Math.Min(Math.Min(c0y, c1y), Math.Min(c2y, c3y))));
        var y1 = Math.Min(_clip.Y1, (int)Math.Ceiling(Math.Max(Math.Max(c0y, c1y), Math.Max(c2y, c3y))));
        if (x0 >= x1 || y0 >= y1) return;
        // Box-filter when downsampling.
        var sx = Math.Sqrt(inv.A * inv.A + inv.B * inv.B); var sy = Math.Sqrt(inv.C * inv.C + inv.D * inv.D);
        var rx = Math.Clamp((int)Math.Round(sx), 1, 8); var ry = Math.Clamp((int)Math.Round(sy), 1, 8);
        var ns = rx * ry;
        var ou = new double[ns]; var ov = new double[ns];
        for (var j = 0; j < ry; j++)
            for (var i = 0; i < rx; i++)
            {
                double dx = (i + 0.5) / rx, dy = (j + 0.5) / ry;
                ou[j * rx + i] = inv.A * dx + inv.C * dy; ov[j * rx + i] = inv.B * dx + inv.D * dy;
            }
        var mode = ParseBlend(blendMode);
        float mr = (float)maskColor.R, mg = (float)maskColor.G, mb = (float)maskColor.B, al = (float)alpha;
        var src = img.Rgba; int iw = img.Width, ih = img.Height; var isMask = img.IsMask;
        var clip = _clip; var px = _px; var wpx = _w;
        var invNs = 1f / ns;

        void Row(int y)
        {
            var u = inv.A * x0 + inv.C * y + inv.E; var v = inv.B * x0 + inv.D * y + inv.F;
            for (var x = x0; x < x1; x++, u += inv.A, v += inv.B)
            {
                long sr = 0, sg = 0, sb = 0, sa = 0;
                for (var k = 0; k < ns; k++)
                {
                    var uu = u + ou[k]; var vv = v + ov[k];
                    if (uu < 0 || vv < 0) continue;
                    int iu = (int)uu, iv = (int)vv;
                    if (iu >= iw || iv >= ih) continue;
                    var o = (iv * iw + iu) * 4;
                    int pa = src[o + 3];
                    if (pa == 0) continue;
                    if (pa == 255) { sr += src[o]; sg += src[o + 1]; sb += src[o + 2]; }
                    else { sr += src[o] * pa / 255; sg += src[o + 1] * pa / 255; sb += src[o + 2] * pa / 255; }
                    sa += pa;
                }
                if (sa == 0) continue;
                var cov = sa * (1f / 255f) * invNs;
                if (clip.Cov is not null) cov *= clip.At(x, y);
                if (cov * al <= 0f) continue;
                float r, g, b;
                if (isMask) { r = mr; g = mg; b = mb; }
                else { var k = 255f / sa; r = sr * k * (1f / 255f); g = sg * k * (1f / 255f); b = sb * k * (1f / 255f); }
                Put((y - _oy) * _lw + (x - _ox), r, g, b, cov, al, mode);
            }
        }
        if (!OperatingSystem.IsBrowser() && (long)(x1 - x0) * (y1 - y0) > 50_000) Parallel.For(y0, y1, Row);
        else for (var y = y0; y < y1; y++) Row(y);
    }

    public void PaintShader(Func<double, double, RgbColor?> shader, double alpha, string blendMode)
    {
        var clip = _clip;
        if (clip.Empty) return;
        var mode = ParseBlend(blendMode); var al = (float)alpha;
        for (var y = clip.Y0; y < clip.Y1; y++)
            for (var x = clip.X0; x < clip.X1; x++)
            {
                var cl = clip.At(x, y);
                if (cl <= 0) continue;
                var c = shader(x + 0.5, y + 0.5);
                if (c is null) continue;
                Put((y - _oy) * _lw + (x - _ox), (float)c.Value.R, (float)c.Value.G, (float)c.Value.B, cl, al, mode);
            }
    }

    // ---------------------------------------------------------------- transparency groups (ISO 32000-2 11.4)

    sealed class Group
    {
        public float[] Px = [], PPx = []; public float[]? A, Ag, Ip, Ia, PA, PAg, PIp, PIa;
        public int Ox, Oy, Lw, Lh, POx, POy, PLw, PLh;
        public bool Ko, PKo, PGray, Isolated, Pooled;
        public GroupBlendSpace? PBs;
        public ClipMask Outer = null!; public ClipMask? OuterPre;
        public float Alpha; public Blend Mode;
    }

    static float[] Rent(int n, bool clear)
    {
        var a = System.Buffers.ArrayPool<float>.Shared.Rent(Math.Max(n, 1));
        if (clear) a.AsSpan(0, n).Clear();
        return a;
    }

    static void Return(float[]? a) { if (a is not null && a.Length > 0) System.Buffers.ArrayPool<float>.Shared.Return(a); }

    public void BeginGroup(bool isolated, bool knockout, double alpha, string blendMode) =>
        BeginGroup(isolated, knockout, alpha, blendMode, null);

    public void BeginGroup(bool isolated, bool knockout, double alpha, string blendMode, PdfColorSpace? colorSpace)
    {
        bool gray; GroupBlendSpace? bs;
        if (colorSpace is null) { gray = _gray; bs = _bs; }
        else if (colorSpace == PdfColorSpace.Gray) { gray = true; bs = null; }
        else if (colorSpace == PdfColorSpace.Rgb) { gray = false; bs = null; }
        else if (colorSpace is GroupBlendColorSpace gb) { gray = false; bs = gb.Space; }
        else throw new NotSupportedException("Only DeviceGray, DeviceRGB, DeviceCMYK, CalGray, CalRGB and ICCBased transparency group blending are implemented.");
        if (!isolated && (gray != _gray || !ReferenceEquals(bs, _bs)))
            throw new NotSupportedException("Non-isolated transparency groups must inherit the backdrop's colour space.");
        var c = _clip;
        var g = new Group
        {
            PPx = _px, PA = _a, PAg = _ag, PIp = _ip, PIa = _ia, POx = _ox, POy = _oy, PLw = _lw, PLh = _lh, PKo = _ko,
            Ko = knockout, PGray = _gray, PBs = _bs, Isolated = isolated, Outer = c, OuterPre = _preMask, Alpha = (float)alpha, Mode = ParseBlend(blendMode),
        };
        _groups.Push(g);
        _preMask = null;
        if (c.Empty)
        {
            _clip = new ClipMask(); // stays empty; nothing is painted into the layer
            g.Px = _px; g.A = _a; g.Ag = _ag; g.Ip = _ip; g.Ia = _ia; g.Ox = _ox; g.Oy = _oy; g.Lw = _lw; g.Lh = _lh; g.Pooled = false;
            return;
        }
        int w = c.X1 - c.X0, h = c.Y1 - c.Y0, n = w * h;
        g.Ox = c.X0; g.Oy = c.Y0; g.Lw = w; g.Lh = h; g.Pooled = true;
        g.Px = Rent(n * 3, isolated); g.A = Rent(n, isolated);
        if (!isolated)
        {
            g.Ag = Rent(n, true); g.Ip = Rent(n * 3, false); g.Ia = Rent(n, false);
            for (var y = 0; y < h; y++)
            {
                var pi = (c.Y0 + y - g.POy) * g.PLw + (c.X0 - g.POx);
                var li = y * w;
                Array.Copy(g.PPx, pi * 3, g.Px, li * 3, w * 3);
                if (g.PA is null) Array.Fill(g.A, 1f, li, w); else Array.Copy(g.PA, pi, g.A, li, w);
            }
            Array.Copy(g.Px, g.Ip, n * 3); Array.Copy(g.A, g.Ia, n);
        }
        _px = g.Px; _a = g.A; _ag = g.Ag; _ip = g.Ip; _ia = g.Ia; _ox = g.Ox; _oy = g.Oy; _lw = w; _lh = h; _ko = knockout; _gray = gray;
        _bs = bs; _conv = gray || (bs?.Lossy ?? false);
        _clip = new ClipMask { X0 = c.X0, Y0 = c.Y0, X1 = c.X1, Y1 = c.Y1 };
    }

    public void EndGroup()
    {
        if (_groups.Count == 0) return;
        var g = _groups.Pop();
        _px = g.PPx; _a = g.PA; _ag = g.PAg; _ip = g.PIp; _ia = g.PIa; _ox = g.POx; _oy = g.POy; _lw = g.PLw; _lh = g.PLh; _ko = g.PKo; _gray = g.PGray;
        _bs = g.PBs; _conv = _gray || (_bs?.Lossy ?? false);
        _clip = g.Outer; _preMask = g.OuterPre;
        if (!g.Pooled) return;
        var c = g.Outer;
        void Row(int y)
        {
            for (var x = c.X0; x < c.X1; x++)
            {
                var cov = c.At(x, y);
                if (cov <= 0f) continue;
                var li = (y - g.Oy) * g.Lw + (x - g.Ox); var l3 = li * 3;
                var ag = g.Isolated ? g.A![li] : g.Ag![li];
                if (ag <= 0f) continue;
                float r = g.Px[l3], gg = g.Px[l3 + 1], b = g.Px[l3 + 2];
                if (!g.Isolated)
                {
                    // Remove the backdrop contribution (11.4.8): C = Cn + (Cn - C0) * (a0 / ag - a0).
                    var a0 = g.Ia![li]; var k = a0 / ag - a0;
                    r += (r - g.Ip![l3]) * k; gg += (gg - g.Ip[l3 + 1]) * k; b += (b - g.Ip[l3 + 2]) * k;
                    r = Math.Clamp(r, 0f, 1f); gg = Math.Clamp(gg, 0f, 1f); b = Math.Clamp(b, 0f, 1f);
                }
                Put((y - _oy) * _lw + (x - _ox), r, gg, b, cov, ag * g.Alpha, g.Mode);
            }
        }
        for (var y = c.Y0; y < c.Y1; y++) Row(y);
        Return(g.Px); Return(g.A); Return(g.Ag); Return(g.Ip); Return(g.Ia);
    }
}
