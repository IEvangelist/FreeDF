namespace AdoBye.Samples.Kit;

/// <summary>Shading/function strings, text layout and colour helpers.</summary>
public static class Helpers
{
    static string N(double v) => K.N(v);

    public static string FnExp(Color c0, Color c1, double n = 1) =>
        $"<< /FunctionType 2 /Domain [0 1] /C0 [{c0.Ops}] /C1 [{c1.Ops}] /N {N(n)} >>";

    /// <summary>Type 2 function for two stops, stitched type 3 for more.</summary>
    public static string FnStops(IReadOnlyList<(double T, Color C)> stops)
    {
        if (stops.Count == 2) return FnExp(stops[0].C, stops[1].C);
        var fns = string.Join(" ", Enumerable.Range(0, stops.Count - 1).Select(i => FnExp(stops[i].C, stops[i + 1].C)));
        var bounds = string.Join(" ", stops.Skip(1).Take(stops.Count - 2).Select(s => N(s.T)));
        var enc = string.Join(" ", Enumerable.Repeat("0 1", stops.Count - 1));
        return $"<< /FunctionType 3 /Domain [0 1] /Functions [{fns}] /Bounds [{bounds}] /Encode [{enc}] >>";
    }

    public static string Axial(double x0, double y0, double x1, double y1, IReadOnlyList<(double, Color)> stops, bool extend = true, string cs = "/DeviceRGB") =>
        $"<< /ShadingType 2 /ColorSpace {cs} /Coords [{N(x0)} {N(y0)} {N(x1)} {N(y1)}] /Function {FnStops(stops)} /Extend {(extend ? "[true true]" : "[false false]")} >>";

    public static string Radial(double x0, double y0, double r0, double x1, double y1, double r1, IReadOnlyList<(double, Color)> stops, bool extend = true, string cs = "/DeviceRGB") =>
        $"<< /ShadingType 3 /ColorSpace {cs} /Coords [{N(x0)} {N(y0)} {N(r0)} {N(x1)} {N(y1)} {N(r1)}] /Function {FnStops(stops)} /Extend {(extend ? "[true true]" : "[false false]")} >>";

    public static List<string> Wrap(IFont font, double size, string text, double width, bool kern = true, double tracking = 0)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var cur = "";
            foreach (var w in para.Split(' '))
            {
                var t = cur.Length > 0 ? cur + " " + w : w;
                if (cur.Length > 0 && font.Width(t, size, kern, tracking) > width) { lines.Add(cur); cur = w; }
                else cur = t;
            }
            lines.Add(cur);
        }
        return lines;
    }

    /// <summary>Draws wrapped text with its first baseline at <paramref name="y"/>; returns the next baseline.</summary>
    public static double Paragraph(Content c, IFont font, double size, double x, double y, double width, string text, double leading = 0,
        string align = "left", Color? color = null, bool kern = true, double tracking = 0, double indent = 0, double? firstIndent = null)
    {
        if (leading == 0) leading = size * 1.4;
        var first = firstIndent ?? indent;
        var lines = new List<(string Text, bool Last)>();
        foreach (var para in text.Split('\n'))
        {
            var cur = "";
            var start = lines.Count;
            foreach (var w in para.Split(' '))
            {
                var t = cur.Length > 0 ? cur + " " + w : w;
                var avail = width - (lines.Count == start ? first : indent);
                if (cur.Length > 0 && font.Width(t, size, kern, tracking) > avail) { lines.Add((cur, false)); cur = w; }
                else cur = t;
            }
            lines.Add((cur, true));
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var (ln, last) = lines[i];
            var off = i == 0 ? first : indent;
            var avail = width - off;
            var w = font.Width(ln, size, kern, tracking);
            double xx = x + off, extra = 0;
            var spaces = ln.Count(ch => ch == ' ');
            if (align == "right") xx = x + width - w;
            else if (align == "center") xx = x + off + (avail - w) / 2;
            else if (align == "justify" && !last && spaces > 0) extra = (avail - w) / spaces;
            c.Text(font, size, xx, y, ln, color: color, kern: kern, tracking: tracking, spaceExtra: extra);
            y -= leading;
        }
        return y;
    }

    static string Hex(double r, double g, double b) => $"#{(int)(255 * r):x2}{(int)(255 * g):x2}{(int)(255 * b):x2}";

    public static string Lighten(Color c, double t) => Hex(c.R + (1 - c.R) * t, c.G + (1 - c.G) * t, c.B + (1 - c.B) * t);

    public static string Mix(Color a, Color b, double t) => Hex(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
}
