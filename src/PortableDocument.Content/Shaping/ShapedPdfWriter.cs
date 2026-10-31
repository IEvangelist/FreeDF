using System.Globalization;
using System.Text;

namespace PortableDocument.Content.Shaping;

/// <summary>
/// Writes a <see cref="ShapedRun"/> as PDF text-showing operators for an Identity-H composite font: glyph codes, TJ adjustments that
/// carry shaped advances, kerning and mark offsets, Ts rise for vertical offsets, and /ActualText marked content wherever the glyph
/// stream cannot round-trip to Unicode through ToUnicode alone (ligatures, glyph-per-cluster mismatch, right-to-left or reordered runs).
/// </summary>
public static class ShapedPdfWriter
{
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Appends the operators for <paramref name="run"/>. <paramref name="code"/> maps a glyph id to its 16-bit code,
    /// <paramref name="fontAdvance"/> gives the width the PDF font declares for a code's glyph (1/1000 em),
    /// and <paramref name="mapUnicode"/> records a glyph-to-text ToUnicode entry (empty text only marks the glyph as used), returning false when the glyph is already mapped to different text.
    /// </summary>
    public static void Write(StringBuilder sb, ShapedRun run, double fontSize, Func<int, int> code, Func<int, double> fontAdvance, Func<int, string, bool> mapUnicode)
    {
        var glyphs = run.Glyphs;
        if (glyphs.Length == 0) return;
        foreach (var g in glyphs) mapUnicode(g.GlyphId, "");

        var monotone = true;
        var lastEnd = int.MinValue;
        foreach (var g in glyphs)
        {
            if (g.TextStart < lastEnd) { monotone = false; break; }
            lastEnd = Math.Max(lastEnd, g.TextStart + g.TextLength);
        }

        if (run.RightToLeft || !monotone)
        {
            foreach (var g in glyphs) if (g.TextLength > 0) mapUnicode(g.GlyphId, run.Text.Substring(g.TextStart, g.TextLength));
            sb.Append("/Span <</ActualText ").Append(ActualText(run.Text.Substring(run.Start, run.Length))).Append(">> BDC\n");
            Tj(sb, glyphs, fontSize, code, fontAdvance);
            sb.Append("EMC\n");
            return;
        }

        var plain = new List<ShapedGlyph>();
        for (var i = 0; i < glyphs.Length;)
        {
            var j = i + 1;
            while (j < glyphs.Length && glyphs[j].TextStart == glyphs[i].TextStart && glyphs[j].TextLength == glyphs[i].TextLength) j++;
            var count = j - i;
            var text = glyphs[i].TextLength > 0 ? run.Text.Substring(glyphs[i].TextStart, glyphs[i].TextLength) : "";
            var simple = count == 1 && !glyphs[i].IsLigature && text.Length > 0 && mapUnicode(glyphs[i].GlyphId, text);
            if (count == 1 && !simple && text.Length > 0) mapUnicode(glyphs[i].GlyphId, text);
            if (simple || text.Length == 0)
            {
                for (var k = i; k < j; k++) plain.Add(glyphs[k]);
            }
            else
            {
                if (plain.Count > 0) { Tj(sb, plain, fontSize, code, fontAdvance); plain.Clear(); }
                sb.Append("/Span <</ActualText ").Append(ActualText(text)).Append(">> BDC\n");
                Tj(sb, glyphs.AsSpan(i, count).ToArray(), fontSize, code, fontAdvance);
                sb.Append("EMC\n");
            }
            i = j;
        }
        if (plain.Count > 0) Tj(sb, plain, fontSize, code, fontAdvance);
    }

    /// <summary>UTF-16BE text string with byte order mark, as a PDF hex string.</summary>
    public static string ActualText(string text)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (var c in text) sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        return sb.Append('>').ToString();
    }

    static void Tj(StringBuilder sb, IReadOnlyList<ShapedGlyph> glyphs, double size, Func<int, int> code, Func<int, double> fontAdvance)
    {
        var inArray = false; var inString = false;
        var rise = 0.0;
        double pending = 0;

        void Close() { if (inString) { sb.Append('>'); inString = false; } }
        void FlushNumber()
        {
            if (Math.Abs(pending) < 0.0005) { pending = 0; return; }
            Close();
            sb.Append(' ').Append(N(pending)).Append(' ');
            pending = 0;
        }
        void EndArray()
        {
            FlushNumber(); Close();
            if (inArray) { sb.Append("] TJ\n"); inArray = false; }
        }
        void Begin() { if (!inArray) { sb.Append('['); inArray = true; } }

        foreach (var g in glyphs)
        {
            var y = Math.Round(g.YOffset * size / 1000.0, 3);
            if (y != rise)
            {
                EndArray();
                sb.Append(N(y)).Append(" Ts\n");
                rise = y;
            }
            Begin();
            // TJ numbers move the pen left by n/1000 em: move right by the offset, show, then settle the pen on the shaped advance.
            pending -= g.XOffset;
            FlushNumber();
            if (!inString) { sb.Append('<'); inString = true; }
            sb.Append(code(g.GlyphId).ToString("X4", CultureInfo.InvariantCulture));
            pending += g.XOffset - (g.XAdvance - fontAdvance(g.GlyphId));
        }
        EndArray();
        if (rise != 0) sb.Append("0 Ts\n");
    }
}
