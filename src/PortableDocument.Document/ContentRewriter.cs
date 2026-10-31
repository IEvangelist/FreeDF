using System.Globalization;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

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
    public sealed record ObjectEdit(int Start, int End, bool Delete, double[]? Matrix, double[]? Fill, double[]? Stroke, string? Clip = null, string? Replacement = null,
        double? LineWidth = null, double[]? Dash = null, int? MoveTo = null, string? State = null);

    /// <summary>Applies object edits. Offsets refer to <paramref name="content"/>; ranges must not overlap. <see cref="ObjectEdit.MoveTo"/> re-emits the edited object at that offset (a z-order change).</summary>
    public static byte[] EditObjects(byte[] content, IEnumerable<ObjectEdit> edits)
    {
        var output = new MemoryStream(content.Length + 64);
        var copied = 0;
        var inserts = new List<(int At, int Order, byte[] Bytes)>();
        var ordered = edits.OrderBy(x => x.Start).ToList();
        var replaced = new List<(int Start, int End, byte[] Bytes)>();
        foreach (var e in ordered)
        {
            if (e.End > content.Length) continue;
            byte[] bytes;
            if (e.Replacement is not null) bytes = Encoding.ASCII.GetBytes(e.Replacement);
            else if (!e.Delete)
            {
                var sb = new StringBuilder("q ");
                if (e.Clip is not null) sb.Append(e.Clip).Append(' ');
                if (e.Matrix is { Length: 6 } m) sb.Append(string.Join(' ', m.Select(F))).Append(" cm ");
                if (e.Fill is { Length: 3 } f) sb.Append($"{F(f[0])} {F(f[1])} {F(f[2])} rg ");
                if (e.Stroke is { Length: 3 } s) sb.Append($"{F(s[0])} {F(s[1])} {F(s[2])} RG ");
                if (e.State is not null) sb.Append(e.State).Append(' ');
                var scale = e.Matrix is { Length: 6 } sm ? Math.Sqrt(Math.Abs(sm[0] * sm[3] - sm[1] * sm[2])) : 1;
                if (scale <= 1e-9) scale = 1;
                if (e.LineWidth is { } w) sb.Append(F(w / scale)).Append(" w ");
                if (e.Dash is { } d) sb.Append('[').Append(string.Join(' ', d.Select(v => F(v / scale)))).Append("] 0 d ");
                var head = Encoding.ASCII.GetBytes(sb.ToString());
                bytes = [.. head, .. content.AsSpan(e.Start, e.End - e.Start), .. " Q"u8];
            }
            else bytes = "\n"u8.ToArray();
            if (e.MoveTo is { } at && !e.Delete && e.Replacement is null)
            {
                inserts.Add((at, e.Start, [.. "\n"u8, .. bytes, .. "\n"u8]));
                bytes = "\n"u8.ToArray();
            }
            replaced.Add((e.Start, e.End, bytes));
        }
        var steps = replaced.Select(r => (Kind: 1, Order: r.Start, r.Start, r.End, r.Bytes))
            .Concat(inserts.Select(i => (Kind: 0, i.Order, Start: i.At, End: i.At, i.Bytes)))
            .OrderBy(x => x.Start).ThenBy(x => x.Kind).ThenBy(x => x.Order).ToList();
        foreach (var step in steps)
        {
            if (step.Start < copied) continue;
            output.Write(content, copied, step.Start - copied);
            output.Write(step.Bytes);
            copied = step.End;
        }
        output.Write(content, copied, content.Length - copied);
        return output.ToArray();
    }

    sealed record Stmt(string Op, int Start, int End);

    static readonly HashSet<string> Construction = ["m", "l", "c", "v", "y", "h", "re"];
    static readonly HashSet<string> PaintEnds = ["S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n", "sh", "Do", "BI"];
    static bool IsPaintOp(string op) => Construction.Contains(op) || PaintEnds.Contains(op);
    static readonly HashSet<string> TextOps = ["Tf", "Td", "TD", "Tm", "T*", "Tj", "TJ", "'", "\"", "Tc", "Tw", "Tz", "TL", "Tr", "Ts"];

    static List<Stmt> Statements(byte[] content)
    {
        var lx = new PdfLexer(content);
        var list = new List<Stmt>();
        var begin = 0;
        PdfObject? o;
        while ((o = lx.ReadObject(false)) is not null)
        {
            if (o is not PdfKeyword k) continue;
            var start = FirstOperandStart(content, begin);
            if (k.Value == "BI") SkipInlineImage(lx);
            list.Add(new(k.Value, start, lx.Pos));
            begin = lx.Pos;
        }
        return list;
    }

    /// <summary>
    /// Finds where an object's byte range can be re-emitted to move it toward the front (end of its enclosing scope) or the back (start of its
    /// scope) without changing any graphics state it depends on: it may only hop path/painting statements, balanced q…Q blocks and text
    /// blocks that set no graphics state. Returns null when no movement is possible.
    /// </summary>
    public sealed record ZMove(int Position, bool StateHopped);

    static readonly HashSet<string> StateOps = ["rg", "RG", "g", "G", "k", "K", "w", "J", "j", "M", "d"];

    /// <param name="allowStateHops">When true the object may also hop colour and line-style operators; the caller must then re-establish that state explicitly (<see cref="ZMove.StateHopped"/>).</param>
    public static ZMove? ZTarget(byte[] content, int start, int end, bool front, bool allowStateHops = false)
    {
        var all = Statements(content);
        var inside = all.Select((s, i) => (s, i)).Where(x => x.s.End > start && x.s.Start < end).ToList();
        if (inside.Count == 0 || inside.Any(x => x.s.Start < start - 1 || x.s.End > end + 1) || inside.Any(x => !IsPaintOp(x.s.Op))) return null;
        int? target = null; var hopped = false; var pending = false;
        if (front)
        {
            var j = inside[^1].i + 1;
            while (j < all.Count)
            {
                var op = all[j].Op;
                if (Construction.Contains(op)) j++;
                else if (PaintEnds.Contains(op)) { target = all[j].End; hopped |= pending; j++; }
                else if (allowStateHops && StateOps.Contains(op)) { pending = true; j++; }
                else if (op == "q" && Match(all, j, 1) is { } close) { target = all[close].End; hopped |= pending; j = close + 1; }
                else if (op == "BT" && TextBlock(all, j) is { } et) { target = all[et].End; hopped |= pending; j = et + 1; }
                else break;
            }
        }
        else
        {
            var j = inside[0].i - 1;
            while (j >= 0)
            {
                var op = all[j].Op;
                if (PaintEnds.Contains(op))
                {
                    var k = j - 1;
                    while (k >= 0 && Construction.Contains(all[k].Op)) k--;
                    target = all[k + 1].Start; hopped |= pending; j = k;
                }
                else if (allowStateHops && StateOps.Contains(op)) { pending = true; j--; }
                else if (op == "Q" && Match(all, j, -1) is { } open) { target = all[open].Start; hopped |= pending; j = open - 1; }
                else if (op == "ET" && TextBlockBack(all, j) is { } bt) { target = all[bt].Start; hopped |= pending; j = bt - 1; }
                else break;
            }
        }
        return target is { } position ? new ZMove(position, hopped) : null;
    }

    static int? Match(List<Stmt> all, int from, int step)
    {
        var depth = 0;
        for (var j = from; j >= 0 && j < all.Count; j += step)
        {
            if (all[j].Op == "q") depth += step;
            else if (all[j].Op == "Q") depth -= step;
            if (depth == 0) return j;
            if (depth < 0) return null;
        }
        return null;
    }

    static int? TextBlock(List<Stmt> all, int bt)
    {
        for (var j = bt + 1; j < all.Count; j++)
        {
            if (all[j].Op == "ET") return j;
            if (!TextOps.Contains(all[j].Op)) return null;
        }
        return null;
    }

    static int? TextBlockBack(List<Stmt> all, int et)
    {
        for (var j = et - 1; j >= 0; j--)
        {
            if (all[j].Op == "BT") return j;
            if (!TextOps.Contains(all[j].Op)) return null;
        }
        return null;
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
