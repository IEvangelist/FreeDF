namespace AdoBye.Samples.Kit;

/// <summary>Shared design system for the sample documents: palette, type scale and small drawing helpers.</summary>
public static class Theme
{
    public static readonly Dictionary<string, string> P = new()
    {
        ["ink"] = "#0F172A", ["slate"] = "#475569", ["muted"] = "#94A3B8", ["mist"] = "#E2E8F0", ["paper"] = "#F8FAFC", ["white"] = "#FFFFFF",
        ["indigo"] = "#4F46E5", ["indigo_d"] = "#312E81", ["violet"] = "#7C3AED", ["teal"] = "#0D9488", ["sky"] = "#0EA5E9",
        ["amber"] = "#F59E0B", ["rose"] = "#E11D48", ["emerald"] = "#10B981", ["orange"] = "#F97316",
    };

    public static readonly (double W, double H) A4 = (595.276, 841.89);
    public static readonly (double W, double H) Letter = (612, 792);

    /// <summary>Outer margin: six units of the 9 pt baseline grid.</summary>
    public const double M = 54;

    public static void Background(Page pg, Color color)
    {
        pg.Save(); pg.Fill(color); pg.Rect(0, 0, pg.W, pg.H); pg.FillPath(); pg.Restore();
    }

    public static void Rule(Page pg, double x0, double y, double x1, Color? color = null, double w = 0.75)
    {
        pg.Save(); pg.Stroke(color ?? P["mist"]); pg.Width(w); pg.Move(x0, y); pg.Line(x1, y); pg.StrokePath(); pg.Restore();
    }

    public static void Box(Content pg, double x, double y, double w, double h, Color? fill = null, Color? stroke = null, double r = 0, double lw = 0.75)
    {
        pg.Save();
        if (r != 0) pg.RRect(x, y, w, h, r); else pg.Rect(x, y, w, h);
        if (fill is { } f && stroke is { } s) { pg.Fill(f); pg.Stroke(s); pg.Width(lw); pg.FillStroke(); }
        else if (fill is { } f2) { pg.Fill(f2); pg.FillPath(); }
        else if (stroke is { } s2) { pg.Stroke(s2); pg.Width(lw); pg.StrokePath(); }
        pg.Restore();
    }

    public static void Eyebrow(Page pg, Faces t, double x, double y, string text, Color? color = null) =>
        pg.Text(t.SansB, 8, x, y, text.ToUpperInvariant(), color: color ?? P["indigo"], tracking: 1.6, kern: false);

    /// <summary>Running head: kicker left, folio right, hairline below, then the title.</summary>
    public static void PageHeader(Page pg, Faces t, string title, string kicker, int? num = null, int? total = null)
    {
        Eyebrow(pg, t, M, pg.H - 36, kicker);
        if (num is { } n)
        {
            var s = total is null ? $"{n}" : $"{n} / {total}";
            pg.Text(t.Sans, 8, pg.W - M - t.Sans.Width(s, 8), pg.H - 36, s, color: P["muted"]);
        }
        Rule(pg, M, pg.H - 44, pg.W - M);
        pg.Text(t.SerifB, 26, M, pg.H - 84, title, color: P["ink"]);
    }

    /// <summary>Two overlapping rounded squares.</summary>
    public static void LogoMark(Page pg, double x, double y, double s, Color? a = null, Color? b = null)
    {
        pg.Save();
        pg.Gs(("ca", 0.9));
        Box(pg, x, y, s * 0.7, s * 0.7, fill: a ?? P["indigo"], r: s * 0.14);
        pg.Gs(("ca", 0.85));
        Box(pg, x + s * 0.3, y + s * 0.3, s * 0.7, s * 0.7, fill: b ?? P["violet"], r: s * 0.14);
        pg.Restore();
    }
}

/// <summary>Source Serif 4 for display, Source Sans 3 for text, Source Code Pro for code (OFL, embedded as OpenType/CFF subsets).</summary>
public sealed class Faces(Doc doc)
{
    public EmbFont Serif { get; } = doc.Font("SourceSerif4-Regular.otf");
    public EmbFont SerifB { get; } = doc.Font("SourceSerif4-Semibold.otf");
    public EmbFont SerifI { get; } = doc.Font("SourceSerif4-It.otf");
    public EmbFont Sans { get; } = doc.Font("SourceSans3-Regular.otf");
    public EmbFont SansB { get; } = doc.Font("SourceSans3-Semibold.otf");
    public EmbFont SansI { get; } = doc.Font("SourceSans3-It.otf");
    public EmbFont Mono { get; } = doc.Font("SourceCodePro-Regular.otf");
}
