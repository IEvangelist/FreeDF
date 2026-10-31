using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;

namespace AdoBye.Samples.Gen;

sealed partial class Gen28
{
    sealed record BarRow(string Label, string? Sub, double Ref, string RefText, double Ours, string OursText, string Pill, Color PillBg, Color PillFg);

    static string FmtUs(double us) => us >= 1000 ? Num(us / 1000, "0.00") + " ms" : Num(us, "0.00") + " \u00B5s";

    static string Ratio(double r) => Num(r, "0.00") + "\u00D7";

    Node Fig(string alt, Action draw) => T("Figure", draw, alt: alt);

    void Legend(double x, double y, params (string Name, Color C, bool Round)[] items)
    {
        foreach (var (name, c, round) in items)
        {
            if (round) Dot(x + 4, y + 2.6, 3.6, c); else Box(_pg, x, y - 1, 8, 7.2, fill: c, r: 1.5);
            Tx(_t.Sans, 8.5, x + 13, y, name, Ink);
            x += 13 + _t.Sans.Width(name, 8.5) + 16;
        }
    }

    /// <summary>Two bars per row, each row scaled to its own larger value (so rows compare engines, not corpora). Returns the bottom y.</summary>
    double PairedBars(double x, double top, double w, IReadOnlyList<BarRow> rows, (string Name, Color C) refSeries, (string Name, Color C) oursSeries, double rh = 30, double labelW = 150)
    {
        const double valW = 58, pillW = 52;
        var barW = w - labelW - valW - pillW - 10;
        Legend(x + labelW, top - 8, (refSeries.Name, refSeries.C, false), (oursSeries.Name, oursSeries.C, false));
        var y = top - 20;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i % 2 == 0) Box(_pg, x, y - rh, w, rh, fill: White);
            var mid = y - rh / 2;
            if (r.Sub != null)
            {
                Tx(_t.SansB, 8.6, x + 8, mid + 2.5, r.Label, Ink);
                Tx(_t.Sans, 7.6, x + 8, mid - 8, r.Sub, Slate);
            }
            else Tx(_t.SansB, 8.6, x + 8, mid - 3, r.Label, Ink);
            var max = Math.Max(r.Ref, r.Ours);
            var bx = x + labelW;
            var refLen = Math.Max(2, r.Ref / max * barW);
            var oursLen = Math.Max(2, r.Ours / max * barW);
            Box(_pg, bx, mid + 1, refLen, 8, fill: refSeries.C, r: 2);
            Box(_pg, bx, mid - 9, oursLen, 8, fill: oursSeries.C, r: 2);
            Tx(_t.Sans, 8.3, bx + refLen + 5, mid + 2.3, r.RefText, Slate);
            Tx(_t.SansB, 8.3, bx + oursLen + 5, mid - 7.7, r.OursText, Ink);
            Pill(x + w - pillW - 4, mid - 8, pillW, 16, r.Pill, r.PillBg, r.PillFg, 8.2);
            y -= rh;
        }
        return y;
    }

    static double LogPos(double v, double lo, double hi, double p0, double p1) => p0 + (Math.Log10(v) - Math.Log10(lo)) / (Math.Log10(hi) - Math.Log10(lo)) * (p1 - p0);

    void LogGrid(double x0, double x1, double yTop, double yBot, double lo, double hi, (double V, string L)[] ticks)
    {
        foreach (var (v, l) in ticks)
        {
            var px = LogPos(v, lo, hi, x0, x1);
            Line(px, yTop, px, yBot, Line_, 0.5, [2, 2]);
            Tx(_t.Sans, 7.6, px, yBot - 10, l, Slate, 'c');
        }
    }

    static readonly Color Line_ = "#CBD5E1";

    /// <summary>Cleveland dot plot on a log axis: both engines per corpus, mean with its 99.9% confidence half-width as a whisker.</summary>
    double DotPlotLog(double x, double top, double w, IReadOnlyList<Pair> rows, (string Name, Color C) ours, (string Name, Color C) theirs,
        double lo, double hi, (double V, string L)[] ticks, string axisLabel)
    {
        const double labelW = 152, pillW = 50, rh = 34;
        var p0 = x + labelW + 6;
        var p1 = x + w - pillW - 14;
        Legend(x + labelW, top - 8, (ours.Name + " (circle)", ours.C, true), (theirs.Name + " (square)", theirs.C, false));
        var y0 = top - 22;
        var yEnd = y0 - rows.Count * rh;
        LogGrid(p0, p1, y0, yEnd, lo, hi, ticks);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var y = y0 - i * rh;
            var mid = y - rh / 2;
            if (i % 2 == 0) { _pg.Save(); _pg.Gs(("ca", 0.55)); Box(_pg, x, y - rh, w, rh, fill: White); _pg.Restore(); }
            Tx(_t.SansB, 8.6, x + 8, mid + (r.Note != "" ? 3.5 : -3), r.Corpus, Ink);
            if (r.Note != "") Tx(_t.SansI, 7.4, x + 8, mid - 7.5, r.Note, Rose);
            var xo = LogPos(r.Ours, lo, hi, p0, p1);
            var xt = LogPos(r.Theirs, lo, hi, p0, p1);
            Line(xo, mid, xt, mid, "#94A3B8", 1);
            Whisker(r.Ours, r.OursErr, mid, lo, hi, p0, p1, ours.C);
            Whisker(r.Theirs, r.TheirsErr, mid, lo, hi, p0, p1, theirs.C);
            Dot(xo, mid, 3.9, ours.C, White);
            Box(_pg, xt - 3.6, mid - 3.6, 7.2, 7.2, fill: theirs.C, stroke: White, lw: 1.1);
            Tx(_t.SansB, 7.9, LogPos(Math.Max(lo * 1.001, r.Ours - r.OursErr), lo, hi, p0, p1) - 7, mid - 2.7, FmtUs(r.Ours), Indigo, 'r');
            Tx(_t.Sans, 7.9, LogPos(r.Theirs + r.TheirsErr, lo, hi, p0, p1) + 7, mid - 2.7, FmtUs(r.Theirs), Slate);
            Pill(x + w - pillW - 4, mid - 8, pillW, 16, Ratio(r.Theirs / r.Ours), "#E0E7FF", IndigoD, 8.2);
        }
        Tx(_t.SansI, 7.6, p1, yEnd - 22, axisLabel, Slate, 'r');
        return yEnd - 30;
    }

    void Whisker(double mean, double err, double y, double lo, double hi, double p0, double p1, Color c)
    {
        var a = LogPos(Math.Max(lo * 1.001, mean - err), lo, hi, p0, p1);
        var b = LogPos(mean + err, lo, hi, p0, p1);
        _pg.Save(); _pg.Stroke(c); _pg.Width(1.6); _pg.Cap(1);
        _pg.Move(a, y); _pg.Line(b, y); _pg.StrokePath();
        _pg.Move(a, y - 3); _pg.Line(a, y + 3); _pg.StrokePath();
        _pg.Move(b, y - 3); _pg.Line(b, y + 3); _pg.StrokePath();
        _pg.Restore();
    }

    /// <summary>Lollipop rows on a log axis with the confidence half-width as a whisker and the managed allocation as a note.</summary>
    double Lollipops(double x, double top, double w, IReadOnlyList<Stage> rows, double lo, double hi, (double V, string L)[] ticks)
    {
        const double labelW = 170, rh = 30, valW = 118;
        var p0 = x + labelW + 6;
        var p1 = x + w - valW;
        var y0 = top - 4;
        var yEnd = y0 - rows.Count * rh;
        LogGrid(p0, p1, y0, yEnd, lo, hi, ticks);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var y = y0 - i * rh;
            var mid = y - rh / 2;
            if (i % 2 == 0) { _pg.Save(); _pg.Gs(("ca", 0.55)); Box(_pg, x, y - rh, w, rh, fill: White); _pg.Restore(); }
            Tx(_t.SansB, 8.6, x + 8, mid + 2.5, r.Name, Ink);
            Tx(_t.Sans, 7.6, x + 8, mid - 8, r.What, Slate);
            var xm = LogPos(r.Mean, lo, hi, p0, p1);
            Line(p0, mid, xm, mid, Mix(Indigo, White, 0.45), 2.2);
            Whisker(r.Mean, r.Err, mid, lo, hi, p0, p1, IndigoD);
            Dot(xm, mid, 4, Indigo, White);
            Tx(_t.SansB, 8.2, p1 + 12, mid + 2, FmtUs(r.Mean), Ink);
            Tx(_t.Sans, 7.4, p1 + 12, mid - 8, $"\u00B1{Num(r.Err, r.Err < 10 ? "0.00" : "0.0")} \u00B5s, {Num(r.AllocKb, "0")} KB", Slate);
        }
        return yEnd - 18;
    }

    /// <summary>Diverging percent-change bars: faster to the left, slower to the right of the unchanged line.</summary>
    double DivergingBars(double x, double top, double w, IReadOnlyList<Delta> rows, double lo, double hi, double[] ticks)
    {
        const double labelW = 176, rh = 25, textW = 128;
        var p0 = x + labelW;
        var p1 = x + w - textW;
        double Px(double v) => p0 + (v - lo) / (hi - lo) * (p1 - p0);
        var y0 = top - 16;
        var yEnd = y0 - rows.Count * rh;
        Tx(_t.SansB, 7.8, Px(0) - 6, top - 6, "\u2190 faster", Teal, 'r');
        Tx(_t.SansB, 7.8, Px(0) + 6, top - 6, "slower \u2192", Rose);
        foreach (var t in ticks)
        {
            Line(Px(t), y0, Px(t), yEnd, t == 0 ? Ink : Line_, t == 0 ? 1 : 0.5, t == 0 ? null : [2, 2]);
            Tx(_t.Sans, 7.6, Px(t), yEnd - 10, (t > 0 ? "+" : t < 0 ? "\u2212" : "") + Math.Abs(t).ToString("0") + "%", Slate, 'c');
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var y = y0 - i * rh;
            var mid = y - rh / 2;
            if (i % 2 == 0) { _pg.Save(); _pg.Gs(("ca", 0.55)); Box(_pg, x, y - rh, w, rh, fill: White); _pg.Restore(); }
            var pct = (r.After / r.Before - 1) * 100;
            Tx(_t.SansB, 8.5, x + 8, mid - 3, r.Name, Ink);
            var bx = pct < 0 ? Px(pct) : Px(0);
            var bw = Math.Max(1.5, Math.Abs(Px(pct) - Px(0)));
            Box(_pg, bx, mid - 5, bw, 10, fill: pct < 0 ? Teal : Rose, r: 2);
            Tx(_t.Sans, 8.2, p1 + 12, mid - 3, $"{Num(r.Before, "0")} \u2192 {Num(r.After, "0")} ms", Slate);
            Tx(_t.SansB, 8.2, x + w - 4, mid - 3, (pct > 0 ? "+" : "\u2212") + Num(Math.Abs(pct), "0.0") + "%", pct < 0 ? Teal : Rose, 'r');
        }
        return yEnd - 18;
    }

    /// <summary>Before-to-after dumbbells on a log axis, with each end labelled.</summary>
    double DumbbellsLog(double x, double top, double w, IReadOnlyList<Delta> rows, double lo, double hi, (double V, string L)[] ticks)
    {
        const double labelW = 150, rh = 26;
        var p0 = x + labelW + 30;
        var p1 = x + w - 52;
        Legend(x + labelW, top - 8, ("before", Pig, true), ("after", Teal, true));
        var y0 = top - 20;
        var yEnd = y0 - rows.Count * rh;
        LogGrid(p0, p1, y0, yEnd, lo, hi, ticks);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var y = y0 - i * rh;
            var mid = y - rh / 2;
            if (i % 2 == 0) { _pg.Save(); _pg.Gs(("ca", 0.55)); Box(_pg, x, y - rh, w, rh, fill: White); _pg.Restore(); }
            Tx(_t.SansB, 8.5, x + 8, mid - 3, r.Name, Ink);
            double a = LogPos(r.Before, lo, hi, p0, p1), b = LogPos(r.After, lo, hi, p0, p1);
            Line(a, mid, b, mid, "#94A3B8", 1.6);
            Dot(a, mid, 3.8, Pig, White);
            Dot(b, mid, 3.8, Teal, White);
            Tx(_t.Sans, 7.8, a + 7, mid - 2.6, Num(r.Before, "0.0"), Slate);
            Tx(_t.SansB, 7.8, b - 7, mid - 2.6, Num(r.After, "0.0"), Teal, 'r');
        }
        return yEnd - 18;
    }

    /// <summary>Measured p50 as a fraction of its p50 budget: the Done line sits at 1.0.</summary>
    double Bullets(double x, double top, double w, IReadOnlyList<Goal> rows, double max, string heading)
    {
        const double labelW = 176, rh = 27, textW = 130;
        var p0 = x + labelW;
        var p1 = x + w - textW;
        double Px(double v) => p0 + v / max * (p1 - p0);
        Tx(_t.SansB, 8.2, x + 8, top - 8, heading, IndigoD);
        var y0 = top - 16;
        var yEnd = y0 - rows.Count * rh;
        _pg.Save(); _pg.Gs(("ca", 0.18)); Box(_pg, p0, yEnd, Px(1) - p0, y0 - yEnd, fill: Emerald); _pg.Restore();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var mid = y0 - i * rh - rh / 2;
            var f = r.P50 / r.Budget50;
            Tx(_t.SansB, 8.5, x + 8, mid - 3, r.Name, Ink);
            Box(_pg, p0, mid - 5, Px(f) - p0, 10, fill: Rose, r: 2);
            Tx(_t.SansB, 8, Px(f) + 5, mid - 3, Ratio(f), Rose);
            Tx(_t.Sans, 7.9, x + w - 4, mid + 2.2, $"p50 {Num(r.P50, "0.#")} vs {Num(r.Budget50, "0")} ms", Ink, 'r');
            Tx(_t.Sans, 7.9, x + w - 4, mid - 7.8, $"p95 {Num(r.P95, "0.#")} vs {Num(r.Budget95, "0")} ms", Slate, 'r');
        }
        Line(Px(1), y0 + 2, Px(1), yEnd - 2, Ink, 1.4);
        for (double t = 0; t <= max + 0.001; t += 0.5) Tx(_t.Sans, 7.6, Px(t), yEnd - 10, Num(t, "0.0") + "\u00D7", Slate, 'c');
        Tx(_t.SansB, 7.6, Px(1) + 4, y0 + 4, "goal (1.0\u00D7)", Ink);
        return yEnd - 20;
    }

    /// <summary>Three-point trails drawn as sparklines, each row scaled to its own range.</summary>
    double TrailChart(double x, double top, double w, IReadOnlyList<Trail> rows)
    {
        const double labelW = 176, rh = 34, spW = 110;
        var y0 = top - 14;
        Tx(_t.SansB, 7, x + labelW + 6, top - 2, "BEFORE", Slate, tracking: 1);
        Tx(_t.SansB, 7, x + labelW + spW / 2 + 3, top - 2, "AFTER", Slate, 'c', tracking: 1);
        Tx(_t.SansB, 7, x + labelW + spW + 6, top - 2, "AOT", Slate, 'r', tracking: 1);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var y = y0 - i * rh;
            var mid = y - rh / 2;
            if (i % 2 == 0) { _pg.Save(); _pg.Gs(("ca", 0.55)); Box(_pg, x, y - rh, w, rh, fill: White); _pg.Restore(); }
            Tx(_t.SansB, 8.5, x + 8, mid - 3, r.Name, Ink);
            double[] v = [r.A, r.B, r.C];
            double lo = v.Min(), hi = v.Max();
            var sx = x + labelW + 6;
            (double X, double Y) P(int k) => (sx + k * (spW - 12) / 2.0, mid - 10 + (v[k] - lo) / Math.Max(1e-9, hi - lo) * 20);
            _pg.Save(); _pg.Stroke(Indigo); _pg.Width(1.8); _pg.Join(1); _pg.Cap(1);
            _pg.Move(P(0).X, P(0).Y); _pg.Line(P(1).X, P(1).Y); _pg.Line(P(2).X, P(2).Y); _pg.StrokePath(); _pg.Restore();
            for (var k = 0; k < 3; k++) Dot(P(k).X, P(k).Y, 3, k == 2 ? Teal : k == 0 ? Pig : Indigo, White);
            Tx(_t.Sans, 8.2, x + labelW + spW + 18, mid - 3, $"{Num(r.A, "0")} \u2192 {Num(r.B, "0")} \u2192 {Num(r.C, "0")} ms", Slate);
            Pill(x + w - 56, mid - 8, 52, 16, Ratio(r.A / r.C), r.A / r.C >= 2 ? "#CCFBF1" : "#FEF3C7", r.A / r.C >= 2 ? "#134E4A" : "#78350F", 8.2);
        }
        return y0 - rows.Count * rh - 8;
    }
}
