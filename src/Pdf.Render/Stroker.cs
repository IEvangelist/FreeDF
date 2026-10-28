using Pdf.Content;

namespace Pdf.Render;

/// <summary>Builds stroke outlines as a set of consistently oriented convex polygons (to be filled non-zero).</summary>
static class Stroker
{
    public static void Build(List<(double X, double Y)> pts, bool closed, double width, LineCap cap, LineJoin join, double miter, List<List<(double X, double Y)>> output)
    {
        var p = new List<(double X, double Y)>();
        foreach (var q in pts) if (p.Count == 0 || Math.Abs(q.X - p[^1].X) > 1e-9 || Math.Abs(q.Y - p[^1].Y) > 1e-9) p.Add(q);
        if (closed && p.Count > 1 && Math.Abs(p[0].X - p[^1].X) < 1e-9 && Math.Abs(p[0].Y - p[^1].Y) < 1e-9) p.RemoveAt(p.Count - 1);
        var hw = width / 2;
        if (p.Count == 1)
        {
            // Zero-length subpath: dots for round/square caps only.
            if (cap == LineCap.Round) output.Add(Circle(p[0], hw));
            else if (cap == LineCap.Square) Add(output, [(p[0].X - hw, p[0].Y - hw), (p[0].X + hw, p[0].Y - hw), (p[0].X + hw, p[0].Y + hw), (p[0].X - hw, p[0].Y + hw)]);
            return;
        }
        if (p.Count < 2) return;
        var segCount = closed ? p.Count : p.Count - 1;
        for (var i = 0; i < segCount; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            var (dx, dy) = Dir(a, b);
            var (nx, ny) = (-dy * hw, dx * hw);
            var ea = a; var eb = b;
            if (!closed && cap == LineCap.Square)
            {
                if (i == 0) ea = (a.X - dx * hw, a.Y - dy * hw);
                if (i == segCount - 1) eb = (b.X + dx * hw, b.Y + dy * hw);
            }
            Add(output, [(ea.X + nx, ea.Y + ny), (eb.X + nx, eb.Y + ny), (eb.X - nx, eb.Y - ny), (ea.X - nx, ea.Y - ny)]);
        }
        if (!closed && cap == LineCap.Round) { output.Add(Circle(p[0], hw)); output.Add(Circle(p[^1], hw)); }
        var jStart = closed ? 0 : 1; var jEnd = closed ? p.Count : p.Count - 1;
        for (var i = jStart; i < jEnd; i++)
        {
            var prev = p[(i - 1 + p.Count) % p.Count]; var cur = p[i]; var nxt = p[(i + 1) % p.Count];
            Join(prev, cur, nxt, hw, join, miter, output);
        }
    }

    static (double, double) Dir((double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y; var l = Math.Sqrt(dx * dx + dy * dy);
        return l == 0 ? (1, 0) : (dx / l, dy / l);
    }

    static void Join((double X, double Y) prev, (double X, double Y) cur, (double X, double Y) nxt, double hw, LineJoin join, double miter, List<List<(double X, double Y)>> output)
    {
        var (d1x, d1y) = Dir(prev, cur); var (d2x, d2y) = Dir(cur, nxt);
        var cross = d1x * d2y - d1y * d2x; var dot = d1x * d2x + d1y * d2y;
        if (Math.Abs(cross) < 1e-9 && dot > 0) return;
        if (join == LineJoin.Round) { output.Add(Circle(cur, hw)); return; }
        // Outer side is opposite the turn direction.
        var sgn = cross > 0 ? -1 : 1;
        var p1 = (cur.X + sgn * -d1y * hw, cur.Y + sgn * d1x * hw);
        var p2 = (cur.X + sgn * -d2y * hw, cur.Y + sgn * d2x * hw);
        var poly = new List<(double X, double Y)> { cur, p1 };
        if (join == LineJoin.Miter)
        {
            var cosTheta = -dot; // angle between segments at the join
            var sinHalf = Math.Sqrt(Math.Max(0, (1 - cosTheta) / 2));
            if (sinHalf > 1e-9 && 1 / sinHalf <= miter)
            {
                var mx = p1.Item1 + p2.Item1 - 2 * cur.X; var my = p1.Item2 + p2.Item2 - 2 * cur.Y;
                var ml = Math.Sqrt(mx * mx + my * my);
                if (ml > 1e-9)
                {
                    var len = hw / sinHalf;
                    poly.Add((cur.X + mx / ml * len, cur.Y + my / ml * len));
                }
            }
        }
        poly.Add(p2);
        Add(output, poly);
    }

    static List<(double X, double Y)> Circle((double X, double Y) c, double r)
    {
        var n = Math.Clamp((int)(r * 3) + 12, 12, 48);
        var l = new List<(double X, double Y)>(n);
        for (var i = 0; i < n; i++) { var t = 2 * Math.PI * i / n; l.Add((c.X + r * Math.Cos(t), c.Y + r * Math.Sin(t))); }
        return l;
    }

    // Normalise winding so overlapping polygons union under the non-zero rule.
    static void Add(List<List<(double X, double Y)>> output, List<(double X, double Y)> poly)
    {
        double area = 0;
        for (var i = 0; i < poly.Count; i++) { var a = poly[i]; var b = poly[(i + 1) % poly.Count]; area += a.X * b.Y - b.X * a.Y; }
        if (area < 0) poly.Reverse();
        output.Add(poly);
    }
}
