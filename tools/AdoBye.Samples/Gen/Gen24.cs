using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Theme;
using static AdoBye.Samples.Kit.Helpers;

namespace AdoBye.Samples.Gen;

static class Gen24
{
    public const string Out = "24-poster.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Poster: Pages in motion", subject: "A3 poster: layered gradients, blend modes, large display type, clipped text", lang: "en-US");
        var t = new Faces(d);
        const double W = 841.89, H = 1190.55;
        var pg = d.Page(W, H);
        pg.Boxes["TrimBox"] = [0, 0, W, H];
        pg.Fill(P["ink"]); pg.Rect(0, 0, W, H); pg.FillPath();

        pg.Save(); pg.Rect(0, 0, W, H); pg.Clip();
        pg.Sh(Axial(0, H, W, 0, [(0, "#1E1B4B"), (0.45, "#4338CA"), (0.8, "#7C3AED"), (1, "#DB2777")]));
        double cx = W * 0.5, cy = H * 0.58, R = 250;
        pg.Save(); pg.Circle(cx, cy, R); pg.Clip();
        pg.Sh(Axial(cx, cy + R, cx, cy - R, [(0, "#FDE68A"), (0.5, "#FB923C"), (1, "#E11D48")]));
        pg.Fill("#4338CA");
        for (var i = 0; i < 9; i++)
        {
            var h = 3 + i * 2.6;
            var y = cy - R * 0.15 - i * 30;
            pg.Rect(cx - R, y - h, 2 * R, h);
        }
        pg.FillPath(); pg.Restore();

        pg.Save(); pg.Stroke("#FFFFFF"); pg.Gs(("CA", 0.16));
        for (var k = 1; k < 9; k++)
        {
            pg.Width(1 + k * 0.25); pg.Circle(cx, cy, R + k * 38); pg.StrokePath();
        }
        pg.Restore();

        pg.Save(); pg.Gs(("BM", "/Screen"), ("ca", 0.9));
        pg.Fill(P["sky"]); pg.Circle(W * 0.2, 190, 110); pg.FillPath();
        pg.Fill(P["emerald"]); pg.Circle(W * 0.32, 170, 110); pg.FillPath();
        pg.Fill(P["amber"]); pg.Circle(W * 0.26, 250, 110); pg.FillPath();
        pg.Restore();
        pg.Restore();

        const double M2 = 56;
        pg.Text(t.SansB, 15, M2, H - 70, "PORTABLE DOCUMENT FESTIVAL  ·  2026", color: "#E0E7FF", tracking: 4, kern: false);
        var words = new[] { "PAGES", "IN", "MOTION" };
        for (var i = 0; i < words.Length; i++)
            pg.Text(t.SerifB, words[i] != "IN" ? 190 : 120, M2 - 4, H - 230 - i * 190, words[i], color: "#FFFFFF");
        pg.Save();
        pg.Text(t.SerifB, 70, M2, H - 230 - 3 * 190 + 40, "every operator counts", mode: 1, strokeColor: "#FFFFFF", lw: 1.2);
        pg.Restore();

        Box(pg, 0, 0, W, 170, fill: "#0B1020");
        pg.Text(t.SansB, 9, M2, 128, "WHEN", color: "#A5B4FC", tracking: 2, kern: false);
        pg.Text(t.SerifB, 24, M2, 98, "12 – 14 June 2026", color: "#FFFFFF");
        pg.Text(t.SansB, 9, M2 + 290, 128, "WHERE", color: "#A5B4FC", tracking: 2, kern: false);
        pg.Text(t.SerifB, 24, M2 + 290, 98, "The Old Print Hall, Bristol", color: "#FFFFFF");
        pg.Text(t.SansB, 9, M2, 62, "TALKS ON", color: "#A5B4FC", tracking: 2, kern: false);
        pg.Text(t.Sans, 12, M2, 42, "Rendering  ·  Fonts & text  ·  Colour  ·  Accessibility  ·  Security  ·  Editing", color: "#CBD5E1");
        LogoMark(pg, W - M2 - 60, 60, 60, "#6366F1", "#EC4899");
        d.Save(path);
    }
}
