using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

static class Gen02
{
    public const string Out = "02-standard-14-fonts.pdf";

    static readonly string[] Fonts =
    [
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique", "Times-Roman", "Times-Bold", "Times-Italic",
        "Times-BoldItalic", "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Symbol", "ZapfDingbats",
    ];

    public static void Build(string path)
    {
        var d = new Doc("The standard 14 fonts", subject: "Base-14 Type 1 fonts, text state operators", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(Theme.A4.W, Theme.A4.H);
        Background(pg, P["white"]);
        PageHeader(pg, t, "The standard 14 fonts", "Typography  /  02", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Every conforming reader supplies these Type 1 fonts; no embedding needed.", color: P["slate"]);
        var y = pg.H - 150;
        for (var i = 0; i < Fonts.Length; i++)
        {
            var name = Fonts[i];
            var f = d.Std(name);
            const double rowh = 34;
            if (i % 2 == 0) Box(pg, M - 8, y - 11, pg.W - 2 * M + 16, rowh, fill: P["paper"]);
            pg.Text(t.SansB, 8, M, y + 10, $"{i + 1:00}", color: P["indigo"], kern: false);
            pg.Text(t.Mono, 7.5, M + 22, y + 10, name, color: P["slate"], kern: false);
            var sample = !f.Symbolic ? "Sphinx of black quartz, judge my vow 0123"
                : name == "Symbol" ? "abcdefghij ABCDEFGHIJ klmnop" : "abcdefghijklmnopqrstuvwxyz 0123456789";
            pg.Text(f, 16, M + 22, y - 6, sample, color: P["ink"], kern: false);
            y -= rowh;
        }

        y -= 22;
        Eyebrow(pg, t, M, y, "Text state operators on Times and Helvetica");
        var hn = pg.Reg("Font", $"{d.Std("Helvetica").Ref} 0 R");
        var tn = pg.Reg("Font", $"{d.Std("Times-Italic").Ref} 0 R");
        y -= 24;
        pg.Raw($"BT {((Color)P["ink"]).Ops} rg /{hn} 13 Tf 1 0 0 1 {K.N(M)} {K.N(y)} Tm 15 TL " +
               "(Word spacing ) Tj 12 Tw (adds space between words) Tj 0 Tw T* " +
               "60 Tz (Horizontal scaling 60%) Tj 100 Tz 120 Tz ( then 120%) Tj 100 Tz T* " +
               "3 Tc (Character spacing) Tj 0 Tc T* " +
               "(Rise: base) Tj 5 Ts (super) Tj -4 Ts (sub) Tj 0 Ts ( and back.) Tj T* " +
               $"/{tn} 13 Tf (Italic via a second Tf, same T* leading.) Tj ET");
        d.Save(path);
    }
}
