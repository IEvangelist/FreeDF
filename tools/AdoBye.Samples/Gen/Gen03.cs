using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

static class Gen03
{
    public const string Out = "03-typography-embedded.pdf";

    const string Text = "Good typography is invisible. The reader notices the argument, never the letterforms; spacing, rhythm and contrast do their work " +
        "quietly beneath the surface. A well-set page offers a clear entry point, a steady measure, and enough air around each block " +
        "for the eye to rest before it moves on. Kerning adjusts the space between particular pairs of glyphs, so that the optical " +
        "colour of a line stays even whether it contains a wide W or a narrow l.";

    public static void Build(string path)
    {
        var d = new Doc("Typography: embedded fonts", subject: "Embedded OpenType/CFF subsets, kerning, alignment, columns, drop caps", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(Theme.A4.W, Theme.A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Embedded typography", "Typography  /  01", 1, 2);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Source Serif 4, Source Sans 3 and Source Code Pro: embedded OpenType (CFF) subsets, GPOS kerning.", color: P["slate"]);

        var y = pg.H - 170;
        Eyebrow(pg, t, M, y + 30, "Pair kerning, off versus on");
        var cw = (pg.W - 2 * M - 24) / 2;
        var kerning = new[] { ("Kerning off", false), ("Kerning on (GPOS kern feature)", true) };
        for (var i = 0; i < kerning.Length; i++)
        {
            var (label, k) = kerning[i];
            var x = M + i * (cw + 24);
            Box(pg, x, y - 62, cw, 80, fill: P["white"], stroke: P["mist"], r: 6);
            pg.Text(t.SerifB, 34, x + 14, y - 26, "AVATAR To", color: P["ink"], kern: k);
            pg.Text(t.Sans, 8, x + 14, y - 52, label, color: P["muted"], tracking: 0.4, kern: false);
        }

        y -= 112;
        Eyebrow(pg, t, M, y + 6, "Alignment");
        cw = (pg.W - 2 * M - 3 * 14) / 4;
        var aligns = new[] { "left", "center", "right", "justify" };
        for (var i = 0; i < aligns.Length; i++)
        {
            var x = M + i * (cw + 14);
            Box(pg, x, y - 160, cw, 150, fill: P["white"], stroke: P["mist"], r: 6);
            Paragraph(pg, t.Serif, 8.6, x + 10, y - 38, cw - 20, Text[..200], leading: 12, align: aligns[i], color: P["ink"]);
            pg.Text(t.SansB, 7, x + 10, y - 24, aligns[i].ToUpperInvariant(), color: P["indigo"], tracking: 1.2, kern: false);
        }

        y -= 200;
        Eyebrow(pg, t, M, y + 6, "Columns and a drop cap");
        var colw = (pg.W - 2 * M - 28) / 2;
        var top = y - 20;
        const double capSize = 58;
        pg.Text(t.SerifB, capSize, M - 1, top - capSize * 0.66, "G", color: P["indigo"]);
        var capw = t.SerifB.Width("G", capSize) + 6;
        var words = (Text[1..] + " " + Text + " " + Text).Split(' ');
        var lines = new List<string>();
        var cur = "";
        const int perCol = 11;
        foreach (var w in words)
        {
            var avail = colw - (lines.Count < 4 ? capw : 0);
            var tt = cur.Length > 0 ? cur + " " + w : w;
            if (cur.Length > 0 && t.Serif.Width(tt, 10) > avail) { lines.Add(cur); cur = w; }
            else cur = tt;
        }
        lines.Add(cur);
        for (var i = 0; i < Math.Min(lines.Count, perCol * 2); i++)
        {
            var ln = lines[i];
            int ci = i / perCol, ri = i % perCol;
            var indent = ci == 0 && ri < 4 ? capw : 0;
            var avail = colw - indent;
            var spaces = ln.Count(ch => ch == ' ');
            var extra = spaces > 0 && i < lines.Count - 1 ? (avail - t.Serif.Width(ln, 10)) / spaces : 0;
            pg.Text(t.Serif, 10, M + ci * (colw + 28) + indent, top - 10 - ri * 14, ln, color: P["ink"], spaceExtra: Math.Max(0, Math.Min(extra, 3)));
        }

        y = 150;
        Box(pg, M, y - 90, pg.W - 2 * M, 100, fill: P["ink"], r: 8);
        string[] code = ["BT", "  /F1 12 Tf", "  1 0 0 1 72 720 Tm", "  [<0041> -80 <0056> 40 <0041>] TJ", "ET"];
        for (var i = 0; i < code.Length; i++)
            pg.Text(t.Mono, 9.5, M + 18, y - 4 - i * 14, code[i], color: "#A5B4FC", kern: false);
        pg.Text(t.SansB, 8, pg.W - M - 140, y - 4, "TJ ARRAY KERNING", color: P["muted"], tracking: 1.4, kern: false);

        var p2 = d.Page(Theme.A4.W, Theme.A4.H);
        Background(p2, P["paper"]);
        PageHeader(p2, t, "Scale, tracking, glyph coverage", "Typography  /  01", 2, 2);
        y = p2.H - 150;
        foreach (var (size, fam) in new (int, EmbFont)[] { (46, t.SerifB), (36, t.Serif), (24, t.SerifI), (16, t.Sans), (12, t.SansB), (9, t.SansI) })
        {
            p2.Text(fam, size, M, y, "Hamburgefonstiv 0123456789", color: P["ink"]);
            p2.Text(t.Sans, 7.5, p2.W - M - 40, y, $"{size} pt", color: P["muted"], kern: false);
            y -= size * 1.35;
        }
        y -= 14;
        Rule(p2, M, y + 6, p2.W - M);
        Eyebrow(p2, t, M, y - 12, "Letterspacing (Tc)");
        var tracks = new[] { (0.0, "0"), (1.5, "1.5"), (4.0, "4") };
        for (var i = 0; i < tracks.Length; i++)
        {
            p2.Text(t.SansB, 12, M, y - 36 - i * 22, "CAPITALS NEED AIR", color: P["indigo_d"], tracking: tracks[i].Item1, kern: false);
            p2.Text(t.Sans, 8, M + 260, y - 36 - i * 22, $"Tc = {tracks[i].Item2}", color: P["muted"], kern: false);
        }
        y -= 120;
        Eyebrow(p2, t, M, y, "Glyph coverage");
        string[] samples =
        [
            "Latin: Zażółć gęślą jaźń — café, naïve, Œuvre, ß, ñ, å, ø, Þ",
            "Greek: Αλφάβητος · α β γ δ ε ζ η θ · Σ Ω Δ",
            "Cyrillic: Съешь же ещё этих мягких французских булок",
            "Symbols: © ® ™ § ¶ † ‡ • … « » ‹ › € £ ¥ ¢ ° ± × ÷ ≈ ≠ ≤ ≥ ∞ → ←",
        ];
        for (var i = 0; i < samples.Length; i++)
            Paragraph(p2, t.Serif, 13, M, y - 28 - i * 30, pg.W - 2 * M, samples[i], color: P["ink"]);
        d.Save(path);
    }
}
