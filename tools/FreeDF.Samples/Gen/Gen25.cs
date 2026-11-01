using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen25
{
    public const string Out = "25-dashboard-charts.pdf";

    static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    static readonly int[] Rev = [42, 47, 51, 49, 58, 64, 61, 70, 76, 74, 83, 91];
    static readonly int[] Cost = [31, 33, 36, 35, 39, 42, 43, 46, 49, 50, 54, 57];
    static readonly (string Label, int Value, string Color)[] Split = [("Subscriptions", 46, P["indigo"]), ("Services", 24, P["teal"]), ("Marketplace", 17, P["amber"]), ("Licensing", 13, P["rose"])];

    static void Smooth(Content c, (double X, double Y)[] pts)
    {
        c.Move(pts[0].X, pts[0].Y);
        for (var i = 0; i < pts.Length - 1; i++)
        {
            var (p0, p1, p2, p3) = (pts[Math.Max(i - 1, 0)], pts[i], pts[i + 1], pts[Math.Min(i + 2, pts.Length - 1)]);
            c.Curve(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6, p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6, p2.X, p2.Y);
        }
    }

    public static void Build(string path)
    {
        var d = new Doc("Q4 Performance dashboard", "Pdf sample generator", "Bar, line/area and donut charts built from vector path operators", "", "en-US");
        var t = new Faces(d);
        const double W = 841.89, H = 595.276;
        var pg = d.Page(W, H);
        Background(pg, "#EEF2F7");
        const double m = 36;
        pg.Text(t.SansB, 8, m, H - 38, "FINANCE  /  FY 2026", color: P["indigo"], tracking: 1.8, kern: false);
        pg.Text(t.SerifB, 28, m, H - 68, "Performance dashboard", color: P["ink"]);
        const string upd = "Updated 31 Dec 2026  ·  all figures in $ thousands";
        pg.Text(t.Sans, 10, W - m - t.Sans.Width(upd, 10), H - 62, upd, color: P["slate"]);

        void Card(double x, double y, double w, double h, double r = 10)
        {
            pg.Save(); pg.Gs(("ca", 0.07)); pg.Fill("#0F172A"); pg.RRect(x + 1, y - 2, w, h, r); pg.FillPath(); pg.Restore();
            Box(pg, x, y, w, h, fill: P["white"], r: r);
        }

        var kw = (W - 2 * m - 3 * 14) / 4;
        var net = Rev.Zip(Cost, (a, b) => (double)(a - b)).ToArray();
        (string Lab, string Val, string Delta, string Col, double[] Series)[] kpis =
        [
            ("Revenue", "$786k", "+18.4 %", P["indigo"], Rev.Select(v => (double)v).ToArray()),
            ("Gross margin", "41.2 %", "+2.1 pt", P["teal"], net),
            ("Active customers", "4,812", "+9.7 %", P["amber"], [20, 22, 21, 25, 28, 27, 30, 33, 35, 34, 38, 41]),
            ("Churn", "1.9 %", "-0.3 pt", P["rose"], [3, 2.8, 3.1, 2.7, 2.5, 2.6, 2.4, 2.2, 2.3, 2.1, 2.0, 1.9]),
        ];
        const double ky = H - 160;
        for (var i = 0; i < kpis.Length; i++)
        {
            var (lab, val, delta, col, series) = kpis[i];
            var x = m + i * (kw + 14);
            Card(x, ky, kw, 78);
            pg.Text(t.SansB, 8, x + 14, ky + 60, lab.ToUpperInvariant(), color: P["muted"], tracking: 1.4, kern: false);
            pg.Text(t.SerifB, 24, x + 14, ky + 28, val, color: P["ink"]);
            var good = !delta.StartsWith('-') || lab == "Churn";
            pg.Text(t.SansB, 9, x + 14, ky + 11, delta, color: good ? P["emerald"] : P["rose"]);
            double sx0 = x + kw - 92, sx1 = x + kw - 14, sy0 = ky + 22, sy1 = ky + 56;
            double lo = series.Min(), hi = series.Max();
            var pts = series.Select((v, k) => (X: sx0 + k * (sx1 - sx0) / 11, Y: sy0 + (v - lo) / (hi - lo) * (sy1 - sy0))).ToArray();
            pg.Save(); pg.Stroke(col); pg.Width(1.6); pg.Join(1); pg.Cap(1); pg.Move(pts[0].X, pts[0].Y);
            foreach (var p in pts[1..]) pg.Line(p.X, p.Y);
            pg.StrokePath(); pg.Fill(col); pg.Circle(pts[^1].X, pts[^1].Y, 2.6); pg.FillPath(); pg.Restore();
        }

        // bar chart
        const double cy0 = 36, ch = 345, bx = m, bw = 350;
        Card(bx, cy0, bw, ch);
        pg.Text(t.SansB, 11, bx + 16, cy0 + ch - 26, "Monthly revenue vs cost", color: P["ink"]);
        pg.Text(t.Sans, 8.5, bx + 16, cy0 + ch - 40, "Clustered bars, $k", color: P["muted"]);
        double px0 = bx + 40, px1 = bx + bw - 16, py0 = cy0 + 34, py1 = cy0 + ch - 60;
        for (var g = 0; g <= 100; g += 25)
        {
            var y = py0 + g / 100.0 * (py1 - py0);
            pg.Save(); pg.Stroke(P["mist"]); pg.Width(0.6); pg.Move(px0, y); pg.Line(px1, y); pg.StrokePath(); pg.Restore();
            pg.Text(t.Sans, 7.5, px0 - 8 - t.Sans.Width(g.ToString(), 7.5), y - 2.5, g.ToString(), color: P["muted"]);
        }
        var slot = (px1 - px0) / 12;
        var costCol = Mix(P["indigo"], "#FFFFFF", 0.65);
        for (var i = 0; i < 12; i++)
        {
            var x = px0 + i * slot + slot * 0.16;
            var bwid = slot * 0.32;
            var bars = new[] { (Rev[i], P["indigo"]), (Cost[i], costCol) };
            for (var k = 0; k < 2; k++)
            {
                var h = bars[k].Item1 / 100.0 * (py1 - py0);
                pg.Save(); pg.Fill(bars[k].Item2); pg.RRect(x + k * (bwid + 1.5), py0, bwid, h, 1.5); pg.FillPath(); pg.Restore();
            }
            pg.Text(t.Sans, 7.5, px0 + i * slot + slot / 2 - t.Sans.Width(Months[i], 7.5) / 2, py0 - 14, Months[i], color: P["muted"]);
        }
        var legend = new[] { ("Revenue", P["indigo"]), ("Cost", costCol) };
        for (var k = 0; k < 2; k++)
        {
            var lx = bx + bw - 130 + k * 66;
            Box(pg, lx, cy0 + ch - 30, 9, 9, fill: legend[k].Item2, r: 2);
            pg.Text(t.Sans, 8.5, lx + 14, cy0 + ch - 28, legend[k].Item1, color: P["slate"]);
        }

        // line/area chart
        const double lx0 = bx + bw + 14, lw = 230;
        Card(lx0, cy0, lw, ch);
        pg.Text(t.SansB, 11, lx0 + 16, cy0 + ch - 26, "Margin trend", color: P["ink"]);
        pg.Text(t.Sans, 8.5, lx0 + 16, cy0 + ch - 40, "Revenue minus cost, $k", color: P["muted"]);
        double ax0 = lx0 + 30, ax1 = lx0 + lw - 16, ay0 = cy0 + 34, ay1 = cy0 + ch - 60;
        for (var g = 0; g <= 40; g += 10)
        {
            var y = ay0 + g / 40.0 * (ay1 - ay0);
            pg.Save(); pg.Stroke(P["mist"]); pg.Width(0.6); pg.Move(ax0, y); pg.Line(ax1, y); pg.StrokePath(); pg.Restore();
            pg.Text(t.Sans, 7.5, ax0 - 6 - t.Sans.Width(g.ToString(), 7.5), y - 2.5, g.ToString(), color: P["muted"]);
        }
        var npts = net.Select((v, i) => (X: ax0 + i * (ax1 - ax0) / 11, Y: ay0 + v / 40 * (ay1 - ay0))).ToArray();
        pg.Save(); Smooth(pg, npts); pg.Line(npts[^1].X, ay0); pg.Line(npts[0].X, ay0); pg.Close(); pg.Clip();
        pg.Sh(Axial(0, ay0, 0, ay1, [(0, "#FFFFFF"), (1, Lighten(P["teal"], 0.45))]));
        pg.Restore();
        pg.Save(); pg.Stroke(P["teal"]); pg.Width(2.2); pg.Cap(1); pg.Join(1); Smooth(pg, npts); pg.StrokePath();
        foreach (var p in npts)
        {
            pg.Fill("#FFFFFF"); pg.Stroke(P["teal"]); pg.Width(1.6); pg.Circle(p.X, p.Y, 3); pg.FillStroke();
        }
        pg.Restore();
        foreach (var i in new[] { 0, 3, 6, 9, 11 })
            pg.Text(t.Sans, 7.5, npts[i].X - t.Sans.Width(Months[i], 7.5) / 2, ay0 - 14, Months[i], color: P["muted"]);
        pg.Text(t.SansB, 9, npts[^1].X - 28, npts[^1].Y + 10, $"{net[^1]}", color: P["teal"]);

        // donut
        const double dx0 = lx0 + lw + 14, dw = W - m - dx0;
        Card(dx0, cy0, dw, ch);
        pg.Text(t.SansB, 11, dx0 + 16, cy0 + ch - 26, "Revenue mix", color: P["ink"]);
        pg.Text(t.Sans, 8.5, dx0 + 16, cy0 + ch - 40, "Share by stream", color: P["muted"]);
        double cx = dx0 + dw / 2, cyy = cy0 + ch - 130, ro = 62, ri = 38;
        var a = 90.0;
        var tot = Split.Sum(s => s.Value);
        foreach (var (_, v, c) in Split)
        {
            var sweep = -360.0 * v / tot;
            pg.Save(); pg.Fill(c); pg.Stroke("#FFFFFF"); pg.Width(2); pg.Join(1);
            pg.ArcPath(cx, cyy, ro, a, a + sweep, true);
            pg.Line(cx + ri * Math.Cos((a + sweep) * Math.PI / 180), cyy + ri * Math.Sin((a + sweep) * Math.PI / 180));
            pg.ArcPath(cx, cyy, ri, a + sweep, a, false);
            pg.Close(); pg.FillStroke(); pg.Restore();
            a += sweep;
        }
        pg.Text(t.SerifB, 18, cx - t.SerifB.Width("$786k", 18) / 2, cyy - 2, "$786k", color: P["ink"]);
        pg.Text(t.Sans, 8, cx - t.Sans.Width("FY total", 8) / 2, cyy - 14, "FY total", color: P["muted"]);
        for (var k = 0; k < Split.Length; k++)
        {
            var (lab, v, c) = Split[k];
            var yy = cy0 + 78 - k * 17;
            Box(pg, dx0 + 16, yy - 1, 9, 9, fill: c, r: 2);
            pg.Text(t.Sans, 9, dx0 + 32, yy, lab, color: P["slate"]);
            pg.Text(t.SansB, 9, dx0 + dw - 16 - t.SansB.Width($"{v} %", 9), yy, $"{v} %", color: P["ink"]);
        }
        d.Save(path);
    }
}
