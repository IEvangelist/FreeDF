namespace Pdf.Content;

public readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
{
    public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

    /// <summary>this × other, row-vector convention as in PDF (apply this first, then other).</summary>
    public Matrix Then(Matrix o) => new(
        A * o.A + B * o.C, A * o.B + B * o.D,
        C * o.A + D * o.C, C * o.B + D * o.D,
        E * o.A + F * o.C + o.E, E * o.B + F * o.D + o.F);

    public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
    public double Det => A * D - B * C;

    public Matrix Invert()
    {
        var det = Det;
        if (Math.Abs(det) < 1e-12) return Identity;
        var ia = D / det; var ib = -B / det; var ic = -C / det; var id = A / det;
        return new Matrix(ia, ib, ic, id, -(E * ia + F * ic), -(E * ib + F * id));
    }

    public static Matrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Matrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);
}

public enum PathOp : byte { Move, Line, Curve, Close }

public sealed class PathData
{
    public readonly List<PathOp> Ops = [];
    public readonly List<double> Pts = [];
    double _sx, _sy, _cx, _cy;
    public bool IsEmpty => Ops.Count == 0;
    public (double X, double Y) Current => (_cx, _cy);

    public void MoveTo(double x, double y) { Ops.Add(PathOp.Move); Pts.Add(x); Pts.Add(y); _sx = _cx = x; _sy = _cy = y; }
    public void LineTo(double x, double y) { if (Ops.Count == 0) { MoveTo(x, y); return; } Ops.Add(PathOp.Line); Pts.Add(x); Pts.Add(y); _cx = x; _cy = y; }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (Ops.Count == 0) MoveTo(x1, y1);
        Ops.Add(PathOp.Curve);
        Pts.AddRange([x1, y1, x2, y2, x3, y3]);
        _cx = x3; _cy = y3;
    }

    public void Close() { if (Ops.Count == 0) return; Ops.Add(PathOp.Close); _cx = _sx; _cy = _sy; }
    public void Rect(double x, double y, double w, double h) { MoveTo(x, y); LineTo(x + w, y); LineTo(x + w, y + h); LineTo(x, y + h); Close(); }

    public PathData Transform(Matrix m)
    {
        var p = new PathData();
        p.Ops.AddRange(Ops);
        for (var i = 0; i < Pts.Count; i += 2) { var (x, y) = m.Apply(Pts[i], Pts[i + 1]); p.Pts.Add(x); p.Pts.Add(y); }
        return p;
    }

    public void Append(PathData o)
    {
        Ops.AddRange(o.Ops); Pts.AddRange(o.Pts);
    }

    /// <summary>Flattens into polylines; the bool says whether the subpath was explicitly closed.</summary>
    public List<(List<(double X, double Y)> Points, bool Closed)> Flatten(double tolerance = 0.1)
    {
        var res = new List<(List<(double, double)>, bool)>();
        List<(double, double)>? cur = null;
        var pi = 0;
        (double X, double Y) last = default;
        foreach (var op in Ops)
        {
            switch (op)
            {
                case PathOp.Move:
                    if (cur is { Count: > 0 }) res.Add((cur, false));
                    cur = [(Pts[pi], Pts[pi + 1])]; last = (Pts[pi], Pts[pi + 1]); pi += 2; break;
                case PathOp.Line:
                    cur ??= [last];
                    cur.Add((Pts[pi], Pts[pi + 1])); last = (Pts[pi], Pts[pi + 1]); pi += 2; break;
                case PathOp.Curve:
                    cur ??= [last];
                    var (x1, y1, x2, y2, x3, y3) = (Pts[pi], Pts[pi + 1], Pts[pi + 2], Pts[pi + 3], Pts[pi + 4], Pts[pi + 5]);
                    pi += 6;
                    var dd = Math.Abs(last.X - 2 * x1 + x2) + Math.Abs(last.Y - 2 * y1 + y2) + Math.Abs(x1 - 2 * x2 + x3) + Math.Abs(y1 - 2 * y2 + y3);
                    var n = Math.Clamp((int)Math.Ceiling(Math.Sqrt(dd / tolerance) * 0.75), 2, 100);
                    for (var i = 1; i <= n; i++)
                    {
                        var t = (double)i / n; var u = 1 - t;
                        cur.Add((u * u * u * last.X + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
                                 u * u * u * last.Y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3));
                    }
                    last = (x3, y3); break;
                case PathOp.Close:
                    if (cur is { Count: > 0 }) { var start = cur[0]; res.Add((cur, true)); cur = null; last = start; }
                    break;
            }
        }
        if (cur is { Count: > 0 }) res.Add((cur, false));
        return res;
    }
}

public readonly record struct RgbColor(double R, double G, double B)
{
    public static readonly RgbColor Black = new(0, 0, 0);
}

public enum LineCap { Butt, Round, Square }
public enum LineJoin { Miter, Round, Bevel }

public sealed record StrokeStyle(double Width, LineCap Cap, LineJoin Join, double MiterLimit, double[] Dash, double DashPhase);

public sealed record ImageData(int Width, int Height, byte[] Rgba, bool IsMask = false);

/// <summary>Pluggable drawing surface. Paths are in user space with the CTM supplied so strokes follow spec geometry.</summary>
public interface IRenderBackend
{
    void Save();
    void Restore();
    void Clip(PathData path, Matrix ctm, bool evenOdd);
    void Fill(PathData path, Matrix ctm, bool evenOdd, RgbColor color, double alpha, string blendMode);
    void Stroke(PathData path, Matrix ctm, StrokeStyle style, RgbColor color, double alpha, string blendMode);
    /// <summary>Draws an image into the unit square transformed by ctm.</summary>
    void DrawImage(ImageData image, Matrix ctm, double alpha, RgbColor maskColor, string blendMode);
    /// <summary>Fills the current clip with a per-pixel shader (device -> color) used for shadings.</summary>
    void PaintShader(Func<double, double, RgbColor?> deviceToColor, double alpha, string blendMode);
    /// <summary>Sets (or with null clears) a soft mask. <paramref name="draw"/> renders the mask group onto an offscreen backend of the same size.</summary>
    void SetSoftMask(Action<IRenderBackend>? draw, bool luminosity, RgbColor backdrop);
}
