using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen16
{
    public const string Out = "16-truetype-specimen.pdf";

    /// <summary>Subsets a TrueType font as a simple font: /Subtype /TrueType, WinAnsiEncoding, /Widths, FontFile2.</summary>
    static (int Ref, int[] Widths) SimpleTrueType(Doc d, string filename, string prefix = "AAAAAA")
    {
        var ft = FontFile.Load(Path.Combine(Paths.Fonts, filename));
        var widths = Enumerable.Range(32, 224).Select(code =>
        {
            var g = ft.GlyphForCodePoint(code);
            return g != 0 ? (int)Math.Round(ft.AdvanceUnits(g) * 1000 / ft.UnitsPerEm) : 0;
        }).ToArray();
        var data = ft.SubsetForCodePoints(Enumerable.Range(32, 95).Concat([0xA0, 0xA9, 0xB0, 0xB1, 0xD7, 0xE9, 0xFC, 0x2022, 0x2013, 0x2014]));
        var ff = d.Stream($"/Length1 {data.Length}", data);
        var sc = 1000.0 / ft.UnitsPerEm;
        var name = $"{prefix}+{ft.PostScriptName}";
        var desc = d.Add($"<< /Type /FontDescriptor /FontName /{name} /Flags 32 /FontBBox [{(int)(ft.XMin * sc)} {(int)(ft.YMin * sc)} {(int)(ft.XMax * sc)} {(int)(ft.YMax * sc)}] " +
            $"/ItalicAngle 0 /Ascent {(int)(ft.Ascent * sc)} /Descent {(int)(ft.Descent * sc)} /CapHeight 729 /StemV 80 /FontFile2 {ff} 0 R >>");
        var font = d.Add($"<< /Type /Font /Subtype /TrueType /BaseFont /{name} /FirstChar 32 /LastChar 255 /Widths [{string.Join(" ", widths)}] " +
            $"/Encoding /WinAnsiEncoding /FontDescriptor {desc} 0 R >>");
        return (font, widths);
    }

    public static void Build(string path)
    {
        var d = new Doc("TrueType specimen", subject: "Bitstream Vera Sans: composite (Identity-H, CIDFontType2) and simple (/TrueType, WinAnsi) embedded subsets", lang: "en-US");
        var t = new Faces(d);
        var vera = d.Font("Vera.ttf");
        var veraB = d.Font("VeraBd.ttf");
        var (sref, widths) = SimpleTrueType(d, "Vera.ttf");
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "TrueType specimen", "Typography  /  TrueType", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Bitstream Vera Sans embedded as glyf-outline TrueType, in both PDF flavours.", color: P["slate"]);

        Box(pg, M, pg.H - 330, pg.W - 2 * M, 190, fill: P["ink"], r: 14);
        pg.Text(veraB, 46, M + 28, pg.H - 220, "Hamburgefonts", color: "#FFFFFF");
        pg.Text(vera, 46, M + 28, pg.H - 280, "Quick brown fox", color: "#A5B4FC");
        pg.Text(vera, 10, M + 30, pg.H - 318, "Composite font: /Type0 + /CIDFontType2 + /CIDToGIDMap /Identity + FontFile2 subset", color: "#94A3B8", kern: false);

        var y = pg.H - 380;
        Eyebrow(pg, t, M, y + 12, "Size ladder");
        foreach (var sz in new[] { 6, 8, 10, 12, 16, 20, 24 })
        {
            pg.Text(vera, sz, M, y - sz * 0.2, sz > 16 ? "The five boxing wizards jump." : "The five boxing wizards jump quickly.", color: P["ink"]);
            pg.Text(t.Mono, 7, pg.W - M - 40, y - 2, $"{sz} pt", color: P["muted"], kern: false);
            y -= sz * 1.25 + 8;
        }

        y -= 14;
        Eyebrow(pg, t, M, y, "Simple font (/TrueType, WinAnsiEncoding) - codes 32 to 126");
        var fname = pg.Reg("Font", $"{sref} 0 R");
        y -= 24;
        const int per = 32, count = 94;
        var cell = (pg.W - 2 * M) / per;
        var ink = ((Color)P["ink"]).Ops;
        var slate = ((Color)P["slate"]).Ops;
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < per; c++)
            {
                var i = r * per + c;
                if (i >= count) break;
                var code = 33 + i;
                var x = M + c * cell;
                var esc = code is '\\' or '(' or ')' ? "\\" + (char)code : ((char)code).ToString();
                pg.Raw($"BT /{fname} 15 Tf {ink} rg 1 0 0 1 {K.N(x + (cell - widths[code - 32] * 15 / 1000.0) / 2)} {K.N(y - r * 30)} Tm ({esc}) Tj ET");
            }
        y -= 3 * 30 + 12;
        pg.Raw($"BT /{fname} 11 Tf {slate} rg 1 0 0 1 {K.N(M)} {K.N(y)} Tm (Simple fonts address glyphs through 8-bit codes; the viewer maps codes to glyphs via the cmap.) Tj ET");
        pg.Raw($"BT /{fname} 11 Tf {slate} rg 1 0 0 1 {K.N(M)} {K.N(y - 16)} Tm (\\251 2026   \\260 \\261 \\327 \\351 \\374  \\225  \\226 \\227) Tj ET");

        y -= 60;
        Paragraph(pg, vera, 10.5, M, y, 300,
            "Bitstream Vera is a free typeface with a permissive licence: it may be embedded, subset and redistributed. TrueType outlines are quadratic splines; this page renders them as filled paths in the page's own coordinate space.",
            color: P["ink"], leading: 15.5, kern: false);
        d.Save(path);
    }
}
