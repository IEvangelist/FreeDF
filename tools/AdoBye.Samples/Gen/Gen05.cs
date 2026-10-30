using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

static class Gen05
{
    public const string Out = "05-vector-graphics.pdf";

    static void Star(Content pg, double cx, double cy, double r, int pts = 5, int skip = 2)
    {
        for (var i = 0; i < pts; i++)
        {
            var a = Math.PI / 2 + i * skip * 2 * Math.PI / pts;
            if (i == 0) pg.Move(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            else pg.Line(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }
        pg.Close();
    }

    public static void Build(string path)
    {
        var d = new Doc("Vector graphics", subject: "Paths, curves, dashes, joins, caps, winding rules, parametric art", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(Theme.A4.W, Theme.A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Vector graphics", "Graphics  /  01", 1, 2);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Everything on this page is drawn with path operators: lines, Bézier curves, strokes and fills.", color: P["slate"]);

        var y = pg.H - 150;
        Eyebrow(pg, t, M, y, "Line caps");
        var caps = new[] { (0, "butt"), (1, "round"), (2, "square") };
        for (var i = 0; i < caps.Length; i++)
        {
            var x = M + i * 70;
            pg.Save(); pg.Stroke(P["indigo"]); pg.Width(18); pg.Cap(caps[i].Item1);
            pg.Move(x + 10, y - 20); pg.Line(x + 60, y - 20); pg.StrokePath(); pg.Restore();
            pg.Save(); pg.Stroke(P["rose"]); pg.Width(0.8); pg.Move(x + 10, y - 32); pg.Line(x + 10, y - 8); pg.Move(x + 60, y - 32); pg.Line(x + 60, y - 8); pg.StrokePath(); pg.Restore();
            pg.Text(t.Sans, 8, x + 10, y - 42, caps[i].Item2, color: P["slate"], kern: false);
        }
        Eyebrow(pg, t, M + 250, y, "Line joins");
        var joins = new[] { (0, "miter"), (1, "round"), (2, "bevel") };
        for (var i = 0; i < joins.Length; i++)
        {
            var x = M + 250 + i * 70;
            pg.Save(); pg.Stroke(P["teal"]); pg.Width(14); pg.Join(joins[i].Item1); pg.Miter(10);
            pg.Move(x + 4, y - 44); pg.Line(x + 24, y - 20); pg.Line(x + 44, y - 44); pg.StrokePath(); pg.Restore();
            pg.Text(t.Sans, 8, x + 8, y - 56, joins[i].Item2, color: P["slate"], kern: false);
        }

        y -= 90;
        Eyebrow(pg, t, M, y, "Dash patterns");
        (double[] Arr, double Ph)[] pats = [([], 0), ([6, 3], 0), ([1, 4], 0), ([12, 3, 2, 3], 0), ([8, 4], 6), ([0.1, 5], 0)];
        for (var i = 0; i < pats.Length; i++)
        {
            var (arr, ph) = pats[i];
            var yy = y - 18 - i * 14;
            pg.Save(); pg.Stroke(P["ink"]); pg.Width(i == 5 ? 2.5 : 1.6); pg.Cap(i == 5 ? 1 : 0);
            pg.Dash(arr, ph); pg.Move(M, yy); pg.Line(M + 230, yy); pg.StrokePath(); pg.Restore();
            pg.Text(t.Mono, 7, M + 250, yy - 2, $"[{string.Join(" ", arr.Select(K.N))}] {K.N(ph)} d", color: P["slate"], kern: false);
        }

        var yfr = y;
        Eyebrow(pg, t, M + 340, yfr, "Fill rules");
        for (var i = 0; i < 2; i++)
        {
            var eo = i == 1;
            var cx = M + 380 + i * 100;
            pg.Save(); pg.Fill(P["violet"]); pg.Stroke(P["indigo_d"]); pg.Width(1.5);
            Star(pg, cx, yfr - 52, 38);
            if (eo) pg.Raw("B*"); else pg.FillStroke();
            pg.Restore();
            pg.Text(t.Sans, 8, cx - 28, yfr - 108, eo ? "even-odd (f*)" : "non-zero (f)", color: P["slate"], kern: false);
        }

        const double yArt = 340;
        Eyebrow(pg, t, M, yArt, "Parametric roses (cubic Béziers)");
        double rcx = M + 110, rcy = yArt - 120;
        var roses = new[] { (5, P["indigo"]), (7, P["rose"]), (3, P["teal"]) };
        for (var k = 0; k < roses.Length; k++)
        {
            var (kk, col) = roses[k];
            pg.Save(); pg.Stroke(col); pg.Width(0.9); pg.Gs(("CA", 0.8));
            const int steps = 360;
            var pts = new (double X, double Y)[steps + 1];
            for (var s = 0; s <= steps; s++)
            {
                var th = (double)s / steps * 2 * Math.PI;
                var rr = (96 - k * 12) * Math.Cos(kk * th);
                pts[s] = (rcx + rr * Math.Cos(th), rcy + rr * Math.Sin(th));
            }
            pg.Move(pts[0].X, pts[0].Y);
            for (var i = 0; i < pts.Length - 1; i++)
            {
                var p0 = pts[Math.Max(0, i - 1)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Math.Min(pts.Length - 1, i + 2)];
                pg.Curve(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6, p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6, p2.X, p2.Y);
            }
            pg.StrokePath(); pg.Restore();
        }

        double bx = M + 270, by = yArt;
        Eyebrow(pg, t, bx, by, "Anatomy of a cubic Bézier");
        (double X, double Y) b0 = (bx + 6, by - 150), b1 = (bx + 40, by - 20), b2 = (bx + 170, by - 40), b3 = (bx + 220, by - 140);
        pg.Save(); pg.Stroke(P["muted"]); pg.Width(0.8); pg.Dash([3, 3]);
        pg.Move(b0.X, b0.Y); pg.Line(b1.X, b1.Y); pg.Move(b3.X, b3.Y); pg.Line(b2.X, b2.Y); pg.StrokePath(); pg.Restore();
        pg.Save(); pg.Stroke(P["indigo"]); pg.Width(3); pg.Cap(1);
        pg.Move(b0.X, b0.Y); pg.Curve(b1.X, b1.Y, b2.X, b2.Y, b3.X, b3.Y); pg.StrokePath(); pg.Restore();
        var ctl = new[] { b0, b1, b2, b3 };
        for (var i = 0; i < ctl.Length; i++)
        {
            var (px, py) = ctl[i];
            pg.Save(); Box(pg, px - 4, py - 4, 8, 8, fill: i is 1 or 2 ? P["rose"] : P["ink"], r: 4); pg.Restore();
            pg.Text(t.Mono, 8, px + 8, py + 4, $"P{i}", color: P["slate"], kern: false);
        }
        pg.Text(t.Sans, 8.5, bx, by - 180, "B(t) = (1-t)³P0 + 3(1-t)²t P1 + 3(1-t)t² P2 + t³P3", color: P["slate"], kern: false);

        var p2g = d.Page(Theme.A4.W, Theme.A4.H);
        Background(p2g, P["ink"]);
        Eyebrow(p2g, t, M, p2g.H - 36, "Graphics  /  01", color: "#A5B4FC");
        p2g.Text(t.Sans, 8, p2g.W - M - 20, p2g.H - 36, "2 / 2", color: P["muted"]);
        p2g.Text(t.SerifB, 26, M, p2g.H - 84, "Bézier art", color: P["white"]);
        double scx = p2g.W / 2, scy = p2g.H - 330;
        const double R = 200, rIn = 77, dd = 120;
        p2g.Save(); p2g.Width(0.45); p2g.Gs(("CA", 0.55));
        for (var layer = 0; layer < 5; layer++)
        {
            p2g.Stroke(Mix(P["sky"], P["rose"], layer / 4.0));
            var ph = layer * Math.PI / 10;
            for (var s = 0; s < 2400; s++)
            {
                var th = s * 0.0262;
                var x = (R - rIn) * Math.Cos(th + ph) + dd * Math.Cos((R - rIn) / rIn * th);
                var yv = (R - rIn) * Math.Sin(th + ph) - dd * Math.Sin((R - rIn) / rIn * th);
                if (s == 0) p2g.Move(scx + x * 0.95, scy + yv * 0.95); else p2g.Line(scx + x * 0.95, scy + yv * 0.95);
            }
            p2g.StrokePath();
        }
        p2g.Restore();

        const double baseY = 190;
        for (var b = 0; b < 14; b++)
        {
            p2g.Save(); p2g.Stroke(Mix(P["indigo"], P["emerald"], b / 13.0)); p2g.Width(1.4); p2g.Gs(("CA", 0.85));
            var y0 = baseY + b * 6;
            p2g.Move(M, y0 + Math.Sin(b * 0.4) * 10);
            for (var s = 0; s < 5; s++)
            {
                var x0 = M + s * (p2g.W - 2 * M) / 5;
                var x1 = M + (s + 1) * (p2g.W - 2 * M) / 5;
                var amp = 46 * Math.Sin(b * 0.22 + 0.3 * s + 0.5);
                p2g.Curve(x0 + 40, y0 + amp, x1 - 40, y0 - amp, x1, y0 + Math.Sin(b * 0.4 + s) * 10);
            }
            p2g.StrokePath(); p2g.Restore();
        }
        p2g.Text(t.Sans, 9, M, 80, "Spirograph (hypotrochoid, 5 phase-shifted strokes with 55% stroke alpha) and 14 stitched cubic wave strands.", color: P["muted"]);
        d.Save(path);
    }
}
