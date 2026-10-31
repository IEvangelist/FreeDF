using PortableDocument.Content.Shaping;
using LB = PortableDocument.Content.Shaping.UnicodeData.LB;

namespace PortableDocument.Content.Shaping;

public enum LineBreakKind : byte { None, Allowed, Mandatory }

/// <summary>Unicode Line Breaking Algorithm (UAX #14) basics: LB4-LB31 for the common classes (spaces, quotes, brackets, punctuation, numbers, CJK ideographs, regional indicators, emoji modifiers). Tailoring and East-Asian-width rules are not implemented.</summary>
public static class LineBreaker
{
    /// <summary>Break kind before each UTF-16 index (index 0 is always None, index text.Length is the end-of-text break).</summary>
    public static LineBreakKind[] FindBreaks(string text)
    {
        var result = new LineBreakKind[text.Length + 1];
        if (text.Length == 0) return result;
        var cps = new List<int>(text.Length); var starts = new List<int>(text.Length);
        for (var i = 0; i < text.Length;)
        {
            var cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            cps.Add(cp); starts.Add(i); i += cp > 0xFFFF ? 2 : 1;
        }
        var n = cps.Count;
        var raw = new LB[n]; var eff = new LB[n];
        for (var i = 0; i < n; i++) raw[i] = UnicodeData.GetLineBreakClass(cps[i]);
        for (var i = 0; i < n; i++)
        {
            if (raw[i] is LB.CM or LB.ZWJ)
            {
                if (i == 0 || eff[i - 1] is LB.BK or LB.CR or LB.LF or LB.NL or LB.SP or LB.ZW) eff[i] = raw[i] == LB.ZWJ ? LB.ZWJ : LB.AL;
                else eff[i] = eff[i - 1];
            }
            else eff[i] = raw[i] == LB.XX ? LB.AL : raw[i];
        }

        var lastNonSpace = LB.XX;
        var riRun = 0;
        for (var k = 0; k < n; k++)
        {
            if (k > 0) result[starts[k]] = Decide(k);
            if (eff[k] != LB.SP) lastNonSpace = eff[k];
            riRun = eff[k] == LB.RI ? riRun + 1 : 0;
        }
        result[text.Length] = LineBreakKind.Mandatory;
        return result;

        LineBreakKind Decide(int k)
        {
            LB a = eff[k - 1], b = eff[k], ra = raw[k - 1], rb = raw[k];
            // lastNonSpace tracks the class before k (spaces skipped); riRun the number of regional indicators ending at k-1.
            var pa = lastNonSpace;
            if (a is LB.BK or LB.LF or LB.NL || a == LB.CR && b != LB.LF) return LineBreakKind.Mandatory;
            if (a == LB.CR) return LineBreakKind.None;
            if (b is LB.BK or LB.CR or LB.LF or LB.NL) return LineBreakKind.None;
            if (b is LB.SP or LB.ZW) return LineBreakKind.None;
            if (pa == LB.ZW) return LineBreakKind.Allowed;
            if (ra == LB.ZWJ) return LineBreakKind.None;
            if (rb is LB.CM or LB.ZWJ && a is not (LB.SP or LB.ZW)) return LineBreakKind.None;
            if (a == LB.WJ || b == LB.WJ) return LineBreakKind.None;
            if (a == LB.GL) return LineBreakKind.None;
            if (b == LB.GL && a is not (LB.SP or LB.BA or LB.HY)) return LineBreakKind.None;
            if (b is LB.CL or LB.CP or LB.EX or LB.IS or LB.SY) return LineBreakKind.None;
            if (pa == LB.OP) return LineBreakKind.None;
            if (pa == LB.QU && b == LB.OP) return LineBreakKind.None;
            if (pa is LB.CL or LB.CP && b == LB.NS) return LineBreakKind.None;
            if (pa == LB.B2 && b == LB.B2) return LineBreakKind.None;
            if (a == LB.SP) return LineBreakKind.Allowed;
            if (a == LB.QU || b == LB.QU) return LineBreakKind.None;
            if (b is LB.BA or LB.HY or LB.NS || a == LB.BB) return LineBreakKind.None;
            if (a is LB.HY or LB.BA && k >= 2 && eff[k - 2] == LB.HL) return LineBreakKind.None;
            if (a == LB.SY && b == LB.HL) return LineBreakKind.None;
            if (b == LB.IN) return LineBreakKind.None;
            if (a is LB.AL or LB.HL && b == LB.NU || a == LB.NU && b is LB.AL or LB.HL) return LineBreakKind.None;
            if (a == LB.PR && b is LB.ID or LB.EB or LB.EM || a is LB.ID or LB.EB or LB.EM && b == LB.PO) return LineBreakKind.None;
            if (a is LB.PR or LB.PO && b is LB.AL or LB.HL || a is LB.AL or LB.HL && b is LB.PR or LB.PO) return LineBreakKind.None;
            if (a is LB.CL or LB.CP && b is LB.PO or LB.PR) return LineBreakKind.None;
            if (a == LB.NU && b is LB.PO or LB.PR or LB.NU) return LineBreakKind.None;
            if (a is LB.PO or LB.PR && b is LB.OP or LB.NU) return LineBreakKind.None;
            if (a is LB.HY or LB.IS or LB.SY && b == LB.NU) return LineBreakKind.None;
            if (a is LB.AL or LB.HL && b is LB.AL or LB.HL) return LineBreakKind.None;
            if (a == LB.IS && b is LB.AL or LB.HL) return LineBreakKind.None;
            if (a is LB.AL or LB.HL or LB.NU && b == LB.OP || a == LB.CP && b is LB.AL or LB.HL or LB.NU) return LineBreakKind.None;
            if (a == LB.RI && b == LB.RI && riRun % 2 == 1) return LineBreakKind.None;
            if (a == LB.EB && b == LB.EM) return LineBreakKind.None;
            return LineBreakKind.Allowed;
        }
    }

    /// <summary>Greedy wrapping of <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/>, given the advance of every UTF-16 unit. Trailing spaces hang past the margin. A word wider than the line is broken at the last possible character.</summary>
    public static List<(int Start, int End)> Wrap(string text, ReadOnlySpan<double> widths, double maxWidth)
    {
        var breaks = FindBreaks(text);
        var lines = new List<(int, int)>();
        var start = 0; var lastBreak = -1; double lineW = 0, widthAtBreak = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (i > start && breaks[i] != LineBreakKind.None)
            {
                if (breaks[i] == LineBreakKind.Mandatory && i > 0 && text[i - 1] is '\n' or '\r' or '\u2028' or '\u2029') { lines.Add((start, i)); start = i; lineW = 0; lastBreak = -1; }
                else { lastBreak = i; widthAtBreak = lineW; }
            }
            var w = widths[i];
            var isSpace = text[i] == ' ';
            if (!isSpace && lineW + w > maxWidth && i > start)
            {
                var cut = lastBreak > start ? lastBreak : i;
                lines.Add((start, cut));
                start = cut; lastBreak = -1;
                lineW = 0;
                for (var j = start; j < i; j++) lineW += widths[j];
            }
            lineW += w;
        }
        if (start < text.Length || lines.Count == 0) lines.Add((start, text.Length));
        _ = widthAtBreak;
        return lines;
    }
}
