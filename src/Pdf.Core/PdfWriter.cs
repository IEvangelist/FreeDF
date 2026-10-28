using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pdf.Core;

/// <summary>Serializes an object graph to a fresh, compact PDF (classic xref). Unreachable objects are dropped and numbers are reassigned.</summary>
public static class PdfWriter
{
    public static byte[] Write(IReadOnlyDictionary<int, PdfObject> objects, int root, int? info, string version = "1.7", ISet<int>? excluded = null, byte[]? id = null)
    {
        // Breadth-first reachability from the roots gives the new numbering.
        var map = new Dictionary<int, int>();
        var order = new List<int>();
        var queue = new Queue<int>();
        void Reach(int n)
        {
            if (excluded?.Contains(n) == true || !objects.ContainsKey(n) || map.ContainsKey(n)) return;
            map[n] = order.Count + 1; order.Add(n); queue.Enqueue(n);
        }
        void Scan(PdfObject? o)
        {
            switch (o)
            {
                case PdfRef r: Reach(r.Number); break;
                case PdfArray a: foreach (var i in a.Items) Scan(i); break;
                case PdfStream s: Scan(s.Dict); break;
                case PdfDictionary d: foreach (var v in d.Entries.Values) Scan(v); break;
            }
        }
        Reach(root);
        if (info is { } inf) Reach(inf);
        while (queue.Count > 0) Scan(objects[queue.Dequeue()]);

        var ms = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        W("%PDF-" + version + "\n%\u00E2\u00E3\u00CF\u00D3\n");
        var offsets = new long[order.Count + 1];
        for (var i = 0; i < order.Count; i++)
        {
            offsets[i + 1] = ms.Position;
            W($"{i + 1} 0 obj\n");
            WriteObject(ms, objects[order[i]], map, excluded);
            W("\nendobj\n");
        }
        var xrefPos = ms.Position;
        W($"xref\n0 {order.Count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i <= order.Count; i++) W($"{offsets[i]:D10} 00000 n \n");
        id ??= RandomNumberGenerator.GetBytes(16);
        var hex = Convert.ToHexString(id);
        W($"trailer\n<< /Size {order.Count + 1} /Root {map[root]} 0 R");
        if (info is { } i2 && map.TryGetValue(i2, out var ni)) W($" /Info {ni} 0 R");
        W($" /ID [<{hex}> <{hex}>] >>\nstartxref\n{xrefPos}\n%%EOF\n");
        return ms.ToArray();
    }

    static void WriteObject(MemoryStream ms, PdfObject? o, Dictionary<int, int> map, ISet<int>? excluded)
    {
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        switch (o)
        {
            case null or PdfNull: W("null"); break;
            case PdfBoolean b: W(b.Value ? "true" : "false"); break;
            case PdfInteger i: W(i.Value.ToString(CultureInfo.InvariantCulture)); break;
            case PdfReal r: W(Real(r.Value)); break;
            case PdfString s: W("<" + Convert.ToHexString(s.Bytes) + ">"); break;
            case PdfName n: W(Name(n.Value)); break;
            case PdfRef r: W(map.TryGetValue(r.Number, out var nn) ? $"{nn} 0 R" : "null"); break;
            case PdfArray a:
                W("[");
                for (var k = 0; k < a.Items.Count; k++) { if (k > 0) W(" "); WriteObject(ms, a.Items[k], map, excluded); }
                W("]"); break;
            case PdfStream st:
                {
                    W("<<");
                    foreach (var (k, v) in st.Dict.Entries)
                    {
                        if (k == "Length") continue;
                        W(Name(k) + " "); WriteObject(ms, v, map, excluded);
                    }
                    W($" /Length {st.RawData.Length} >>\nstream\n");
                    ms.Write(st.RawData);
                    W("\nendstream");
                    break;
                }
            case PdfDictionary d:
                W("<<");
                foreach (var (k, v) in d.Entries) { W(Name(k) + " "); WriteObject(ms, v, map, excluded); }
                W(">>"); break;
            default: W("null"); break;
        }
    }

    static string Real(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
        var s = d.ToString("0.##########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    static string Name(string v)
    {
        var sb = new StringBuilder("/");
        foreach (var b in Encoding.UTF8.GetBytes(v))
        {
            if (b is < 33 or > 126 || "#()<>[]{}/%".Contains((char)b)) sb.Append('#').Append(b.ToString("X2"));
            else sb.Append((char)b);
        }
        return sb.ToString();
    }
}
