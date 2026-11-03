using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace PortableDocument.Render;

internal struct RasterEdge { public double Y0, Y1, X0, Slope; public int Dir; }

/// <summary>
/// Scanline coverage generator: 4 sub-scanlines per pixel row with exact horizontal coverage, accumulated in <see cref="Cov"/>.
/// Rows are independent of each other, so several scanners can process disjoint row ranges of one edge list concurrently.
/// </summary>
internal sealed class EdgeScanner
{
    const int SubY = 4;
    const float Weight = 1f / SubY;
    static readonly double[] SubOffset = [0.125, 0.375, 0.625, 0.875]; // (s + 0.5) / SubY, exactly

    public readonly float[] Cov;
    RasterEdge[] _edges = [];
    int _en;
    int[] _active = new int[64]; int _an;
    double[] _xs = new double[64]; int[] _xd = new int[64]; int[] _xi = new int[64];
    int _next, _y, _by1, _bx0, _bx1, _tmin, _tmax; bool _eo;

    public EdgeScanner(int width) => Cov = new float[width + 2];

    /// <summary>Starts scanning rows [y0, y1) of edges [first, end) of the Y0-sorted edge list, spans clipped to columns [bx0, bx1).</summary>
    public void Begin(RasterEdge[] edges, int first, int end, bool evenOdd, int y0, int y1, int bx0, int bx1)
    {
        _edges = edges; _en = end; _eo = evenOdd;
        _next = first; _an = 0; _y = y0; _by1 = y1; _bx0 = bx0; _bx1 = bx1;
        _tmin = int.MaxValue; _tmax = -1;
    }

    /// <summary>Advances to the next row with coverage; <see cref="Cov"/>[xa..xb) holds it and is cleared on the following call.</summary>
    public bool NextRow(out int y, out int xa, out int xb)
    {
        var cov = Cov;
        if (_tmax >= _tmin) ZeroRange(cov, _tmin, _tmax - _tmin + 1);
        _tmin = int.MaxValue; _tmax = -1;
        y = xa = xb = 0;
        var edges = _edges; var en = _en;
        while (_y < _by1)
        {
            var cy = _y;
            if (_an == 0)
            {
                if (_next >= en) return false;
                var ny = (int)Math.Floor(edges[_next].Y0);
                if (ny > cy) { _y = ny; continue; }
            }
            _y++;
            var cnt = 0;
            var uniform = false;
            var lastSy = cy + SubOffset[SubY - 1];
            for (var s = 0; s < SubY; s++)
            {
                if (!uniform)
                {
                    var sy = cy + SubOffset[s];
                    while (_next < en && edges[_next].Y0 <= sy)
                    {
                        if (_an == _active.Length) Array.Resize(ref _active, _an * 2);
                        _active[_an++] = _next++;
                    }
                    if (_an > _xs.Length) { Array.Resize(ref _xs, _an * 2); Array.Resize(ref _xd, _an * 2); Array.Resize(ref _xi, _an * 2); }
                    var active = _active; var xs = _xs; var xd = _xd; var xi = _xi;
                    cnt = 0;
                    var vertical = true;
                    var shifts = 0;
                    var sort = false;
                    for (var i = 0; i < _an; i++)
                    {
                        var idx = active[i];
                        ref var e = ref edges[idx];
                        if (e.Y1 <= sy) continue;
                        if (e.Slope != 0 || e.Y1 <= lastSy) vertical = false;
                        var x = e.X0 + (sy - e.Y0) * e.Slope;
                        var j = cnt++;
                        if (!sort)
                            while (j > 0 && xs[j - 1] > x)
                            {
                                xs[j] = xs[j - 1]; xd[j] = xd[j - 1]; xi[j] = xi[j - 1]; j--;
                                if (++shifts >= 4096) { sort = true; break; }
                            }
                        xs[j] = x; xd[j] = e.Dir; xi[j] = idx;
                    }
                    // Nearly sorted crossings use insertion sort; hostile paths must not incur quadratic work.
                    if (sort)
                    {
                        Array.Sort(xs, xi, 0, cnt);
                        for (var i = 0; i < cnt; i++) xd[i] = edges[xi[i]].Dir;
                    }
                    // The active list follows the crossing order, so the next sub-scanline starts almost sorted.
                    for (var i = 0; i < cnt; i++) active[i] = xi[i];
                    _an = cnt;
                    // Only vertical edges that span every remaining sub-scanline and no edge starting inside the row: the crossings cannot change.
                    uniform = s == 0 && vertical && (_next >= en || edges[_next].Y0 > lastSy);
                }
                var wxs = _xs; var wxd = _xd;
                var wind = 0;
                for (var k = 0; k + 1 < cnt; k++)
                {
                    wind += wxd[k];
                    if (_eo ? (k & 1) != 0 : wind == 0) continue;
                    AddSpan(wxs[k], wxs[k + 1]);
                }
            }
            if (_tmax >= _tmin) { y = cy; xa = _tmin; xb = _tmax + 1; return true; }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void AddSpan(double xa, double xb)
    {
        if (xb <= _bx0 || xa >= _bx1) return;
        if (xa < _bx0) xa = _bx0;
        if (xb > _bx1) xb = _bx1;
        if (xb <= xa) return;
        var ia = (int)xa; var ib = (int)xb;
        var cov = Cov;
        var last = ib < _bx1 ? ib : _bx1 - 1;
        if (ia < _tmin) _tmin = ia;
        if (last > _tmax) _tmax = last;
        if (ia == ib) { cov[ia] += (float)(xb - xa) * Weight; return; }
        cov[ia] += (float)(ia + 1 - xa) * Weight;
        AddWeight(cov, ia + 1, ib);
        if (ib < _bx1) cov[ib] += (float)(xb - ib) * Weight;
    }

    /// <summary>Inline vector zeroing: coverage rows are short (~100 floats on average), where the runtime's out-of-line memset call dominates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ZeroRange(float[] a, int from, int count)
    {
        ref var r = ref a[from];
        var i = 0;
        if (count >= 2048) { a.AsSpan(from, count).Clear(); return; }
        if (Vector128.IsHardwareAccelerated)
        {
            var z = Vector128<float>.Zero;
            for (; i <= count - 4; i += 4) z.StoreUnsafe(ref r, (nuint)i);
        }
        for (; i < count; i++) Unsafe.Add(ref r, i) = 0f;
    }

    /// <summary>cov[from..to) += Weight; each lane performs the same single-precision add as the scalar loop, so results are bit-identical.</summary>
    static void AddWeight(float[] cov, int from, int to)
    {
        var n = to - from;
        if (n <= 0) return;
        ref var r = ref cov[from];
        var i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            var w = Vector256.Create(Weight);
            for (; i <= n - 8; i += 8) (Vector256.LoadUnsafe(ref r, (nuint)i) + w).StoreUnsafe(ref r, (nuint)i);
        }
        if (Vector128.IsHardwareAccelerated)
        {
            var w = Vector128.Create(Weight);
            for (; i <= n - 4; i += 4) (Vector128.LoadUnsafe(ref r, (nuint)i) + w).StoreUnsafe(ref r, (nuint)i);
        }
        for (; i < n; i++) Unsafe.Add(ref r, i) += Weight;
    }
}
