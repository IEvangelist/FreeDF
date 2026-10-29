using System.Globalization;
using System.Text;
using Pdf.Core;

namespace Pdf.Document;

/// <summary>Token-level edits of a page content stream that leave every other byte untouched.</summary>
public static class ContentRewriter
{
    static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Removes the text drawn by the given text-showing operators (counted in order over Tj, TJ, ' and ", 0-based).
    /// Each is replaced by an equivalent advance so following text and text state are unchanged.
    /// <paramref name="removals"/> maps operator index to the horizontal advance in thousandths of text space (TJ number).
    /// </summary>
    public static byte[] RemoveText(byte[] content, IReadOnlyDictionary<int, double> removals) =>
        ReplaceText(content, removals.ToDictionary(kv => kv.Key, kv => F(kv.Value)));

    /// <summary>
    /// Replaces text-showing operators (indexed as in <see cref="RemoveText"/>) by a <c>TJ</c> whose array body is the dictionary value,
    /// keeping the operator's own Tw/Tc/T* side effects.
    /// </summary>
    public static byte[] ReplaceText(byte[] content, IReadOnlyDictionary<int, string> arrayBodies)
    {
        var removals = arrayBodies;
        if (removals.Count == 0) return content;
        var lx = new PdfLexer(content);
        var ops = new List<PdfObject>();
        var output = new MemoryStream(content.Length);
        var copied = 0; var stmtStart = 0; var index = 0;
        PdfObject? o;
        while ((o = lx.ReadObject(false)) is not null)
        {
            if (o is not PdfKeyword k) { ops.Add(o); continue; }
            if (k.Value == "BI") { SkipInlineImage(lx); ops.Clear(); stmtStart = lx.Pos; continue; }
            if (k.Value is "Tj" or "TJ" or "'" or "\"")
            {
                if (removals.TryGetValue(index, out var adj))
                {
                    var sb = new StringBuilder("\n");
                    if (k.Value == "\"") sb.Append($"{F(ops.ElementAtOrDefault(0).AsNumber() ?? 0)} Tw {F(ops.ElementAtOrDefault(1).AsNumber() ?? 0)} Tc ");
                    if (k.Value is "'" or "\"") sb.Append("T* ");
                    sb.Append('[').Append(adj).Append("] TJ\n");
                    // Keep leading whitespace/comments before the operands; replace from the first operand.
                    var from = FirstOperandStart(content, stmtStart);
                    output.Write(content, copied, from - copied);
                    output.Write(Encoding.ASCII.GetBytes(sb.ToString()));
                    copied = lx.Pos;
                }
                index++;
            }
            ops.Clear(); stmtStart = lx.Pos;
        }
        output.Write(content, copied, content.Length - copied);
        return output.ToArray();
    }

    /// <summary>
    /// A replacement of one painted object's byte range: deleted, replaced by <paramref name="Replacement"/>, or wrapped in
    /// <c>q [Clip] [colours] [cm] … Q</c>. <paramref name="Clip"/> is clip-path text in the object's own user space.
    /// </summary>
    public sealed record ObjectEdit(int Start, int End, bool Delete, double[]? Matrix, double[]? Fill, double[]? Stroke, string? Clip = null, string? Replacement = null);

    /// <summary>Applies non-overlapping object edits. Offsets refer to <paramref name="content"/>.</summary>
    public static byte[] EditObjects(byte[] content, IEnumerable<ObjectEdit> edits)
    {
        var output = new MemoryStream(content.Length + 64);
        var copied = 0;
        foreach (var e in edits.OrderBy(x => x.Start))
        {
            if (e.Start < copied || e.End > content.Length) continue;
            output.Write(content, copied, e.Start - copied);
            if (e.Replacement is not null) output.Write(Encoding.ASCII.GetBytes(e.Replacement));
            else if (!e.Delete)
            {
                var sb = new StringBuilder("q ");
                if (e.Clip is not null) sb.Append(e.Clip).Append(' ');
                if (e.Matrix is { Length: 6 } m) sb.Append(string.Join(' ', m.Select(F))).Append(" cm ");
                if (e.Fill is { Length: 3 } f) sb.Append($"{F(f[0])} {F(f[1])} {F(f[2])} rg ");
                if (e.Stroke is { Length: 3 } s) sb.Append($"{F(s[0])} {F(s[1])} {F(s[2])} RG ");
                output.Write(Encoding.ASCII.GetBytes(sb.ToString()));
                output.Write(content, e.Start, e.End - e.Start);
                output.Write(Encoding.ASCII.GetBytes(" Q"));
            }
            else output.Write("\n"u8);
            copied = e.End;
        }
        output.Write(content, copied, content.Length - copied);
        return output.ToArray();
    }

    static int FirstOperandStart(byte[] d, int p)
    {
        while (p < d.Length && PdfLexer.IsWhite(d[p])) p++;
        return p;
    }

    static void SkipInlineImage(PdfLexer lx)
    {
        PdfObject? o;
        while ((o = lx.ReadObject(false)) is not null && !(o is PdfKeyword { Value: "ID" })) { }
        var d = lx.Data;
        var p = lx.Pos;
        if (p < d.Length && PdfLexer.IsWhite(d[p])) p++;
        for (; p + 1 < d.Length; p++)
            if (d[p] == 'E' && d[p + 1] == 'I' && p > 0 && PdfLexer.IsWhite(d[p - 1]) && (p + 2 >= d.Length || PdfLexer.IsWhite(d[p + 2]))) { lx.Pos = p + 2; return; }
        lx.Pos = d.Length;
    }
}
