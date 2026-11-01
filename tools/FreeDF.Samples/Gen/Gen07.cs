using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen07
{
    public const string Out = "07-transparency.pdf";

    static readonly string[] Modes =
    [
        "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight", "SoftLight",
        "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity",
    ];

    public static void Build(string path)
    {
        var d = new Doc("Transparency", subject: "Constant alpha, 16 blend modes, luminosity/alpha soft masks, isolated and knockout groups", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["white"]);
        PageHeader(pg, t, "Transparency", "Graphics  /  03", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "PDF 1.4 transparency model: alpha, blend modes, soft masks and groups, composited over a checkerboard.", color: P["slate"]);

        var chk = new XForm(d, [0, 0, 16, 16]);
        chk.Fill("#F1F5F9"); chk.Rect(0, 0, 16, 16); chk.FillPath();
        chk.Fill("#E2E8F0"); chk.Rect(0, 0, 8, 8); chk.Rect(8, 8, 8, 8); chk.FillPath();
        var chkRef = chk.Build("pattern", 16, 16);

        void Checker(double x, double y, double w, double h)
        {
            pg.Save(); pg.PatternFill(chkRef); pg.Rect(x, y, w, h); pg.FillPath(); pg.Restore();
        }

        double x0 = M, y0 = pg.H - 280;
        Eyebrow(pg, t, x0, y0 + 150, "Constant alpha (ca / CA)");
        Checker(x0, y0, 220, 140);
        (double Cx, string Col)[] circles = [(70, P["rose"]), (110, P["amber"]), (150, P["sky"])];
        for (var i = 0; i < 3; i++)
        {
            pg.Save(); pg.Gs(("ca", 0.6), ("CA", 0.9)); pg.Fill(circles[i].Col); pg.Stroke(P["ink"]); pg.Width(2);
            pg.Circle(x0 + circles[i].Cx, y0 + 80 + (i % 2) * -20, 46); pg.FillStroke(); pg.Restore();
        }

        var gx = M + 250;
        Eyebrow(pg, t, gx, y0 + 150, "Group composited as one object");
        Checker(gx, y0, 220, 140);
        (double, double, string)[] blobs = [(34, 85, P["indigo"]), (62, 85, P["teal"]), (48, 56, P["rose"])];
        foreach (var (kind, ox) in new[] { ("separate", 0), ("group", 110) })
        {
            pg.Save(); pg.Translate(gx + ox, y0);
            if (kind == "separate")
            {
                pg.Gs(("ca", 0.55));
                foreach (var (cx, cy, col) in blobs) { pg.Fill(col); pg.Circle(cx, cy, 30); pg.FillPath(); }
            }
            else
            {
                var g = new XForm(d, [0, 0, 110, 140], group: "<< /S /Transparency /I true /K true >>");
                foreach (var (cx, cy, col) in blobs) { g.Fill(col); g.Circle(cx, cy, 30); g.FillPath(); }
                pg.Gs(("ca", 0.55)); pg.XObj(g.Build());
            }
            pg.Restore();
            pg.Text(t.Sans, 8, gx + ox + 14, y0 - 12, kind == "separate" ? "per-object ca 0.55" : "isolated knockout group, ca 0.55", color: P["slate"], kern: false);
        }

        var by = pg.H - 330;
        Eyebrow(pg, t, M, by, "Blend modes (BM)");
        const int cols = 4;
        var tw = (pg.W - 2 * M - 3 * 12) / 4;
        const double th = 50;
        for (var i = 0; i < Modes.Length; i++)
        {
            int c = i % cols, r = i / cols;
            var x = M + c * (tw + 12);
            var y = by - 20 - th - r * (th + 22);
            pg.Save(); pg.RRect(x, y, tw, th, 5); pg.Clip();
            pg.Sh(Axial(x, y, x + tw, y + th, [(0, P["teal"]), (0.5, P["amber"]), (1, P["rose"])]));
            pg.Gs(("BM", $"/{Modes[i]}"));
            pg.Fill(P["indigo"]); pg.Circle(x + tw * 0.35, y + th / 2, 21); pg.FillPath();
            pg.Fill("#C4B5FD"); pg.Rect(x + tw * 0.55, y - 2, 34, th + 4); pg.FillPath();
            pg.Restore();
            pg.Text(t.Sans, 8, x, y - 10, Modes[i], color: P["ink"], kern: false);
        }

        const double sy = 118;
        Eyebrow(pg, t, M, sy + 66, "Soft masks (SMask)");
        var lum = new XForm(d, [0, 0, 150, 60], group: "<< /S /Transparency /CS /DeviceGray >>");
        lum.Sh(Radial(75, 30, 0, 75, 30, 72, [(0, "#FFFFFF"), (0.55, "#FFFFFF"), (1, "#000000")], extend: false, cs: "/DeviceRGB"));
        var lref = lum.Build();
        var alp = new XForm(d, [0, 0, 150, 60], group: "<< /S /Transparency >>");
        alp.Gs(("ca", 1));
        for (var i = 0; i < 15; i++)
        {
            alp.Save(); alp.Gs(("ca", (i + 1) / 15.0)); alp.Fill("#000000"); alp.Rect(i * 10, 0, 10, 60); alp.FillPath(); alp.Restore();
        }
        var aref = alp.Build();
        foreach (var (k, (kind, rf)) in new[] { ("Luminosity", lref), ("Alpha", aref) }.Select((e, k) => (k, e)))
        {
            var x = M + k * 250;
            Checker(x, sy, 150, 60);
            pg.Save(); pg.Translate(x, sy);
            pg.Gs(("SMask", $"<< /Type /Mask /S /{kind} /G {rf} 0 R >>"));
            pg.Sh(Axial(0, 0, 150, 60, [(0, P["indigo"]), (1, P["rose"])]));
            pg.Restore();
            pg.Text(t.Sans, 8, x + 160, sy + 30, $"/S /{kind}", color: P["slate"], kern: false);
        }
        d.Save(path);
    }
}
