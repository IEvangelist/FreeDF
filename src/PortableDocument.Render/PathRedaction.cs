using System.Globalization;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;

namespace PortableDocument.Render;

/// <summary>
/// Removes redaction rectangles from an arbitrary painted path (any CTM, curves, even-odd / non-zero fills, strokes).
/// The path is flattened in device space with a bounded error, every filled polygon is split against the (slightly inflated)
/// rectangles with half-plane clipping, and the survivors are written back in user space. Strokes are converted to the
/// equivalent filled outline first, so nothing of the stroked geometry under a rectangle survives. Anything not understood
/// returns false so the caller removes the whole object (fail closed).
/// </summary>
static class PathRedaction
{
    const double Tolerance = 0.05;
    const double Margin = 0.001;
    const int MaxVertices = 40000;

    /// <summary>Returns false when the object must be removed whole; true with a null <paramref name="replacement"/> when no geometry touches a rectangle.</summary>
    public static bool TryRewrite(byte[] content, ContentInterpreter.PageObject obj, IReadOnlyList<Redactor.BoxRect> rects, out string? replacement)
    {
        replacement = null;
        var ctm = obj.Ctm;
        if (!Finite(ctm.A, ctm.B, ctm.C, ctm.D, ctm.E, ctm.F) || Math.Abs(ctm.Det) < 1e-12) return false;
        if (!TryParse(content, obj.Start, obj.End, out var path, out var clipOp, out var paintOp)) return false;

        var stroke = paintOp is "S" or "s" or "B" or "B*" or "b" or "b*";
        var fill = paintOp is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        var evenOdd = paintOp is "f*" or "B*" or "b*";
        var closeAll = paintOp is "s" or "b" or "b*";
        if (clipOp is not null && stroke) return false;
        if (clipOp is not null && fill && (clipOp == "W*") != evenOdd) return false;
        if (clipOp is not null && !fill && paintOp != "n") return false;
        if (clipOp is null && paintOp == "n") return false;
        if (stroke && (obj.ComplexPaint || obj.Style is null || Math.Abs(obj.StrokeAlpha - obj.FillAlpha) > 1e-9)) return false;
        if (clipOp == "W*") evenOdd = true;

        var cuts = new List<(double X0, double Y0, double X1, double Y1)>();
        foreach (var r in rects)
        {
            if (!(r.W > 0 && r.H > 0) || !Finite(r.X, r.Y, r.W, r.H)) continue;
            cuts.Add((r.X - Margin, r.Y - Margin, r.X + r.W + Margin, r.Y + r.H + Margin));
        }
        if (cuts.Count == 0 || cuts.Count > 64) return false;

        var sigma = MaxScale(ctm);
        if (!Flatten(path, Tolerance / sigma, out var subs)) return false;

        var sb = new StringBuilder();
        var written = new List<List<(double X, double Y)>>();
        var inv = ctm.Invert();
        var fillPolys = new List<List<(double X, double Y)>>();
        if (fill || clipOp is not null)
            foreach (var (pts, _) in subs)
                if (pts.Count >= 3) fillPolys.Add(pts.Select(p => ctm.Apply(p.X, p.Y)).ToList());

        var strokePolys = new List<List<(double X, double Y)>>();
        if (stroke)
        {
            var style = obj.Style!;
            var scale = Math.Sqrt(Math.Abs(ctm.Det));
            var width = style.Width;
            if (width * scale < 1) width = 1 / scale;
            var user = new List<List<(double X, double Y)>>();
            foreach (var (pts, closed) in subs)
            {
                var isClosed = closed || closeAll;
                if (style.Dash.Length == 0) Stroker.Build(pts, isClosed, width, style.Cap, style.Join, style.MiterLimit, user);
                else
                    foreach (var (dp, dclosed) in SoftwareRasterizer.ApplyDash(pts, isClosed, style))
                        Stroker.Build(dp, dclosed, width, style.Cap, style.Join, style.MiterLimit, user);
                if (user.Sum(p => p.Count) > MaxVertices) return false;
            }
            strokePolys = user.Select(p => p.Select(q => ctm.Apply(q.X, q.Y)).ToList()).ToList();
        }

        // Geometry that never comes near a rectangle is left exactly as authored.
        if (!fillPolys.Concat(strokePolys).Any(p => Touches(p, cuts)))
        {
            replacement = null;
            return true;
        }

        if (fill || clipOp is not null)
        {
            var polys = fillPolys;
            if (!Subtract(polys, cuts)) return false;
            written.AddRange(polys);
            if (polys.Count > 0) AppendPath(sb, polys, inv);
            else if (clipOp is not null) sb.Append("0 0 m 0 0 l ");
            if (clipOp is not null) sb.Append(clipOp).Append(' ');
            if (polys.Count > 0 || clipOp is not null)
                sb.Append(paintOp is "n" or "f" or "F" or "f*" ? paintOp : evenOdd ? "f*" : "f");
            sb.Append('\n');
        }

        if (stroke)
        {
            var polys = strokePolys;
            if (!Subtract(polys, cuts)) return false;
            written.AddRange(polys);
            if (polys.Count > 0)
            {
                var c = obj.Stroke;
                sb.Append(FormattableString.Invariant($"q {Num(c.R)} {Num(c.G)} {Num(c.B)} rg "));
                AppendPath(sb, polys, inv);
                sb.Append("f Q\n");
            }
        }

        // Re-measure what is actually written (after rounding) against the original rectangles.
        var invCtm = inv;
        foreach (var poly in written)
            foreach (var (px, py) in poly)
            {
                var (ux, uy) = invCtm.Apply(px, py);
                (ux, uy) = (double.Parse(Num(ux), CultureInfo.InvariantCulture), double.Parse(Num(uy), CultureInfo.InvariantCulture));
                var (dx, dy) = ctm.Apply(ux, uy);
                foreach (var r in rects)
                    if (dx > r.X + 1e-4 && dx < r.X + r.W - 1e-4 && dy > r.Y + 1e-4 && dy < r.Y + r.H - 1e-4) return false;
            }
        replacement = sb.ToString();
        return true;
    }

    static bool Finite(params double[] v) => v.All(double.IsFinite);

    static bool Touches(List<(double X, double Y)> poly, List<(double X0, double Y0, double X1, double Y1)> cuts)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (x, y) in poly) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        foreach (var c in cuts)
            if (minX < c.X1 + Tolerance && maxX > c.X0 - Tolerance && minY < c.Y1 + Tolerance && maxY > c.Y0 - Tolerance) return true;
        return false;
    }

    static double MaxScale(Matrix m)
    {
        double a = m.A * m.A + m.B * m.B + m.C * m.C + m.D * m.D, c = m.A * m.C + m.B * m.D, d = m.A * m.A + m.B * m.B - m.C * m.C - m.D * m.D;
        return Math.Max(Math.Sqrt((a + Math.Sqrt(d * d + 4 * c * c)) / 2), 1e-9);
    }

    static string Num(double v)
    {
        var s = v.ToString("0.##########", CultureInfo.InvariantCulture);
        return s is "-0" ? "0" : s;
    }

    static void AppendPath(StringBuilder sb, List<List<(double X, double Y)>> polys, Matrix inv)
    {
        foreach (var poly in polys)
        {
            for (var i = 0; i < poly.Count; i++)
            {
                var (x, y) = inv.Apply(poly[i].X, poly[i].Y);
                sb.Append(Num(x)).Append(' ').Append(Num(y)).Append(i == 0 ? " m " : " l ");
            }
            sb.Append("h ");
        }
    }

    static bool TryParse(byte[] content, int start, int end, out PathData path, out string? clipOp, out string paintOp)
    {
        path = new PathData(); clipOp = null; paintOp = "";
        var lexer = new PdfLexer(content, start);
        var n = new List<double>();
        var hasPoint = false;
        while (lexer.Pos < end && lexer.ReadObject(false) is { } token)
        {
            if (token.AsNumber() is { } v)
            {
                if (!double.IsFinite(v) || n.Count >= 6) return false;
                n.Add(v);
                continue;
            }
            if (token is not PdfKeyword k) return false;
            switch (k.Value)
            {
                case "m" when n.Count == 2: path.MoveTo(n[0], n[1]); hasPoint = true; break;
                case "l" when n.Count == 2 && hasPoint: path.LineTo(n[0], n[1]); break;
                case "c" when n.Count == 6 && hasPoint: path.CurveTo(n[0], n[1], n[2], n[3], n[4], n[5]); break;
                case "v" when n.Count == 4 && hasPoint: { var (cx, cy) = path.Current; path.CurveTo(cx, cy, n[0], n[1], n[2], n[3]); break; }
                case "y" when n.Count == 4 && hasPoint: path.CurveTo(n[0], n[1], n[2], n[3], n[2], n[3]); break;
                case "h" when n.Count == 0 && hasPoint: path.Close(); break;
                case "re" when n.Count == 4: path.Rect(n[0], n[1], n[2], n[3]); hasPoint = true; break;
                case "W" or "W*" when n.Count == 0 && clipOp is null: clipOp = k.Value; break;
                case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n" when n.Count == 0:
                    paintOp = k.Value;
                    return lexer.Pos == end;
                default: return false;
            }
            n.Clear();
        }
        return false;
    }

    // Flattens with a per-curve segment count derived from the second differences, so the chord error stays below the tolerance.
    static bool Flatten(PathData path, double tol, out List<(List<(double X, double Y)> Pts, bool Closed)> result)
    {
        result = [];
        List<(double X, double Y)>? cur = null;
        var total = 0; var pi = 0;
        (double X, double Y) last = default;
        foreach (var op in path.Ops)
        {
            var p = path.Pts;
            switch (op)
            {
                case PathOp.Move:
                    if (cur is { Count: > 0 }) result.Add((cur, false));
                    cur = [(p[pi], p[pi + 1])]; last = (p[pi], p[pi + 1]); pi += 2; total++; break;
                case PathOp.Line:
                    cur ??= [last];
                    cur.Add((p[pi], p[pi + 1])); last = (p[pi], p[pi + 1]); pi += 2; total++; break;
                case PathOp.Curve:
                {
                    cur ??= [last];
                    var (x1, y1, x2, y2, x3, y3) = (p[pi], p[pi + 1], p[pi + 2], p[pi + 3], p[pi + 4], p[pi + 5]);
                    pi += 6;
                    var m = Math.Max(Math.Sqrt(Sq(last.X - 2 * x1 + x2) + Sq(last.Y - 2 * y1 + y2)), Math.Sqrt(Sq(x1 - 2 * x2 + x3) + Sq(y1 - 2 * y2 + y3)));
                    var segs = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(0.75 * m / tol)));
                    if (segs > 4000) return false;
                    for (var i = 1; i <= segs; i++)
                    {
                        double t = (double)i / segs, u = 1 - t;
                        cur.Add((u * u * u * last.X + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
                                 u * u * u * last.Y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3));
                    }
                    total += segs; last = (x3, y3); break;
                }
                case PathOp.Close:
                    if (cur is { Count: > 0 }) { var s = cur[0]; result.Add((cur, true)); cur = null; last = s; }
                    break;
            }
            if (total > MaxVertices) return false;
        }
        if (cur is { Count: > 0 }) result.Add((cur, false));
        return true;
    }

    static double Sq(double v) => v * v;

    // polys := polys minus the union of the cuts. Each polygon is split into its parts left of, right of, below and above a cut.
    static bool Subtract(List<List<(double X, double Y)>> polys, List<(double X0, double Y0, double X1, double Y1)> cuts)
    {
        foreach (var cut in cuts)
        {
            var next = new List<List<(double X, double Y)>>(polys.Count);
            var vertices = 0;
            foreach (var poly in polys)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var (x, y) in poly) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                if (!(minX < cut.X1 && maxX > cut.X0 && minY < cut.Y1 && maxY > cut.Y0)) { next.Add(poly); vertices += poly.Count; continue; }
                var strip = Clip(Clip(poly, true, cut.X0, false), true, cut.X1, true);
                var parts = new[]
                {
                    Clip(poly, true, cut.X0, true), Clip(poly, true, cut.X1, false),
                    strip.Count > 0 ? Clip(strip, false, cut.Y0, true) : strip,
                    strip.Count > 0 ? Clip(strip, false, cut.Y1, false) : strip,
                };
                foreach (var part in parts)
                    if (part.Count >= 3) { next.Add(part); vertices += part.Count; }
                if (vertices > MaxVertices) return false;
            }
            polys.Clear();
            polys.AddRange(next);
        }
        return true;
    }

    // Sutherland-Hodgman against one axis-aligned half-plane; winding of the kept region is preserved for any simple or self-overlapping polygon.
    static List<(double X, double Y)> Clip(List<(double X, double Y)> poly, bool xAxis, double v, bool keepLess)
    {
        var res = new List<(double X, double Y)>(poly.Count + 2);
        double C((double X, double Y) p) => xAxis ? p.X : p.Y;
        bool In((double X, double Y) p) => keepLess ? C(p) <= v : C(p) >= v;
        (double X, double Y) Cross((double X, double Y) a, (double X, double Y) b)
        {
            var t = (v - C(a)) / (C(b) - C(a));
            return xAxis ? (v, a.Y + t * (b.Y - a.Y)) : (a.X + t * (b.X - a.X), v);
        }
        for (var i = 0; i < poly.Count; i++)
        {
            var cur = poly[i]; var prev = poly[(i + poly.Count - 1) % poly.Count];
            if (In(cur)) { if (!In(prev)) res.Add(Cross(prev, cur)); res.Add(cur); }
            else if (In(prev)) res.Add(Cross(prev, cur));
        }
        return res;
    }
}
