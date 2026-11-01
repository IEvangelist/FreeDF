using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen04
{
    public const string Out = "04-text-render-modes.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Text rendering modes and clipping", subject: "Tr 0-7, text clipping with shadings and patterns, text on a path", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(Theme.A4.W, Theme.A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Text rendering modes", "Typography  /  02", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "The eight text rendering modes (Tr 0-7), then text as a clipping path for gradients and patterns.", color: P["slate"]);

        string[] modes = ["Fill", "Stroke", "Fill + stroke", "Invisible", "Fill, add to clip", "Stroke, add to clip", "Fill + stroke, clip", "Clip only"];
        var cw = (pg.W - 2 * M - 3 * 12) / 4;
        const double ch = 112;
        for (var m = 0; m < modes.Length; m++)
        {
            int col = m % 4, row = m / 4;
            var x = M + col * (cw + 12);
            var y = pg.H - 140 - ch - row * (ch + 12);
            Box(pg, x, y, cw, ch, fill: P["white"], stroke: P["mist"], r: 6);
            pg.Save();
            pg.Rect(x + 1, y + 1, cw - 2, ch - 2);
            pg.Clip();
            pg.Text(t.SerifB, 64, x + 12, y + 38, "Ag", color: m == 3 ? "#C7D2FE" : P["indigo"], strokeColor: P["rose"], lw: m != 1 ? 2 : 1.4, mode: m);
            if (m >= 4) pg.Sh(Axial(x, y, x + cw, y + ch, [(0, P["amber"]), (0.5, P["rose"]), (1, P["violet"])]));
            pg.Restore();
            pg.Text(t.SansB, 8, x + 10, y + ch - 14, $"Tr {m}", color: P["indigo"], tracking: 0.8, kern: false);
            pg.Text(t.Sans, 8, x + 10, y + 10, modes[m], color: P["slate"], kern: false);
        }

        const double y0 = 405;
        Eyebrow(pg, t, M, y0 + 24, "Clipping text with a shading");
        pg.Save();
        pg.Text(t.SerifB, 118, M, y0 - 70, "Aurora", mode: 7, color: P["ink"]);
        pg.Sh(Axial(M, y0 - 80, M + 360, y0 + 40, [(0, P["indigo"]), (0.4, P["sky"]), (0.7, P["emerald"]), (1, P["amber"])]));
        pg.Restore();

        var pat = new XForm(d, [0, 0, 8, 8]);
        pat.Fill(P["rose"]);
        pat.Rect(0, 0, 4, 4);
        pat.Rect(4, 4, 4, 4);
        pat.FillPath();
        pat.Fill(P["ink"]);
        pat.Rect(4, 0, 4, 4);
        pat.Rect(0, 4, 4, 4);
        pat.FillPath();
        var pref = pat.Build("pattern", 8, 8);
        Eyebrow(pg, t, M, y0 - 118, "Clipping text with a tiling pattern");
        pg.Save();
        pg.Text(t.SerifB, 96, M, y0 - 200, "Checker", mode: 7, color: P["ink"]);
        pg.Rect(M - 10, y0 - 220, 480, 120);
        pg.PatternFill(pref);
        pg.FillPath();
        pg.Restore();

        const double y1 = 106;
        Eyebrow(pg, t, M, y1 + 44, "Text on a curve");
        double cx = pg.W / 2, cy = y1 - 168, r = 200;
        const string s = "Every glyph is positioned and rotated individually along the arc ";
        var total = s.Sum(c => t.SerifI.Width(c.ToString(), 16));
        var ang = 0.5 * total / r;
        var a = Math.PI / 2 + ang;
        pg.Save();
        foreach (var c in s)
        {
            var g = c.ToString();
            var w = t.SerifI.Width(g, 16);
            var da = w / r;
            var mid = a - da / 2;
            double px = cx + r * Math.Cos(mid), py = cy + r * Math.Sin(mid);
            var rot = mid - Math.PI / 2;
            pg.Save();
            pg.Cm(Math.Cos(rot), Math.Sin(rot), -Math.Sin(rot), Math.Cos(rot), px, py);
            pg.Text(t.SerifI, 16, -w / 2, 0, g, color: Mix(P["indigo"], P["rose"], (a - (Math.PI / 2 - ang)) / (2 * ang)), kern: false);
            pg.Restore();
            a -= da;
        }
        pg.Restore();
        d.Save(path);
    }
}
