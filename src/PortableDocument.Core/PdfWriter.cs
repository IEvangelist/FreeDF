using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PortableDocument.Core;

public sealed class PdfWriteOptions
{
    /// <summary>Flate-compress streams that have no filter.</summary>
    public bool Compress { get; init; }
    /// <summary>Pack non-stream objects into object streams and write a cross-reference stream (PDF 1.5+).</summary>
    public bool ObjectStreams { get; init; }
    /// <summary>Encryption (AES-256 by default; AES-128, RC4 or public-key via <see cref="PdfEncryption"/>); the document key is generated fresh on every write.</summary>
    public PdfEncryption? Encryption { get; init; }
    /// <summary>When set, receives the mapping from input object numbers to the numbers used in the written file.</summary>
    public Dictionary<int, int>? ObjectMapOut { get; init; }
    /// <summary>The 16-byte file identifier to write (default: random). Set it for reproducible output; the array is copied.</summary>
    public byte[]? FileId
    {
        get => _fileId?.ToArray();
        init => _fileId = value is null ? null : value.Length == 16 ? value.ToArray() : throw new ArgumentException("FileId must be exactly 16 bytes", nameof(value));
    }

    byte[]? _fileId;
}

/// <summary>Serializes an object graph to a fresh PDF (classic xref or xref/object streams, optionally compressed and encrypted), or appends an incremental update. Unreachable objects are dropped and numbers are reassigned on full writes.</summary>
public static class PdfWriter
{
    sealed class Ctx(Dictionary<int, int>? map, PdfEncryptor? enc, bool compress)
    {
        public readonly Dictionary<int, int>? Map = map;
        public readonly PdfEncryptor? Enc = enc;
        public readonly bool Compress = compress;
        public PdfDecryptor? Dec;
        public int Num;
    }

    public static byte[] Write(IReadOnlyDictionary<int, PdfObject> objects, int root, int? info, string version = "1.7", ISet<int>? excluded = null, byte[]? id = null, PdfWriteOptions? options = null)
    {
        options ??= new();
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
        if (options.ObjectMapOut is { } mapOut) foreach (var (k, v) in map) mapOut[k] = v;

        if (options.ObjectStreams && string.Compare(version, "1.5", StringComparison.Ordinal) < 0) version = "1.5";
        id ??= options.FileId ?? RandomNumberGenerator.GetBytes(16);
        var enc = options.Encryption is null ? null : new PdfEncryptor(options.Encryption, id);
        var ms = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        W("%PDF-" + version + "\n%\u00E2\u00E3\u00CF\u00D3\n");

        var total = order.Count;
        var ctx = new Ctx(map, enc, options.Compress);
        var plain = new Ctx(map, null, options.Compress);
        var encNum = 0;
        if (enc is not null) encNum = ++total;

        // Object stream packing: every non-stream object goes into a chunk of up to 100.
        var packed = new Dictionary<int, (int Stm, int Index)>();
        var chunks = new List<(int Num, List<int> Members)>();
        if (options.ObjectStreams)
        {
            List<int>? cur = null;
            for (var i = 0; i < order.Count; i++)
            {
                if (objects[order[i]] is PdfStream) continue;
                if (cur is null || cur.Count >= 100) { cur = []; chunks.Add((++total, cur)); }
                packed[i + 1] = (total, cur.Count);
                cur.Add(i + 1);
            }
        }

        var offsets = new long[total + 2];
        void Emit(int num, Action body)
        {
            offsets[num] = ms.Position;
            W($"{num} 0 obj\n");
            body();
            W("\nendobj\n");
        }
        for (var i = 0; i < order.Count; i++)
        {
            if (packed.ContainsKey(i + 1)) continue;
            var o = objects[order[i]];
            Emit(i + 1, () => { ctx.Num = i + 1; WriteObject(ms, o, ctx); });
        }
        if (enc is not null) Emit(encNum, () => WriteObject(ms, enc.Dict, new Ctx(map, null, false)));
        foreach (var (num, members) in chunks)
        {
            var header = new StringBuilder();
            var body = new MemoryStream();
            foreach (var n in members)
            {
                header.Append(n).Append(' ').Append(body.Position).Append(' ');
                WriteObject(body, objects[order[n - 1]], plain);
                body.WriteByte((byte)'\n');
            }
            var hb = Encoding.Latin1.GetBytes(header.ToString());
            var st = new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("ObjStm"), ["N"] = new PdfInteger(members.Count), ["First"] = new PdfInteger(hb.Length),
            }), [.. hb, .. body.ToArray()]);
            Emit(num, () => WriteObject(ms, st, new Ctx(map, enc, true) { Num = num }));
        }

        var hex = Convert.ToHexString(id);
        var tail = new StringBuilder($" /Root {map[root]} 0 R");
        if (info is { } i2 && map.TryGetValue(i2, out var ni)) tail.Append($" /Info {ni} 0 R");
        if (enc is not null) tail.Append($" /Encrypt {encNum} 0 R");
        tail.Append($" /ID [<{hex}> <{hex}>]");

        var xrefPos = ms.Position;
        if (options.ObjectStreams)
        {
            var xn = total + 1;
            var rows = new byte[(xn + 1) * 7];
            rows[5] = 0xFF; rows[6] = 0xFF;
            for (var n = 1; n <= xn; n++)
            {
                var o = n * 7;
                if (n == xn) { rows[o] = 1; WriteBE(rows, o + 1, xrefPos, 4); }
                else if (packed.TryGetValue(n, out var p)) { rows[o] = 2; WriteBE(rows, o + 1, p.Stm, 4); WriteBE(rows, o + 5, p.Index, 2); }
                else if (chunks.Any(c => c.Num == n) || n <= order.Count || n == encNum) { rows[o] = 1; WriteBE(rows, o + 1, offsets[n], 4); }
            }
            var data = Deflate(rows);
            W($"{xn} 0 obj\n<< /Type /XRef /Size {xn + 1} /W [1 4 2] /Filter /FlateDecode{tail} /Length {data.Length} >>\nstream\n");
            ms.Write(data);
            W($"\nendstream\nendobj\nstartxref\n{xrefPos}\n%%EOF\n");
        }
        else
        {
            W($"xref\n0 {total + 1}\n0000000000 65535 f \n");
            for (var n = 1; n <= total; n++) W($"{offsets[n]:D10} 00000 n \n");
            W($"trailer\n<< /Size {total + 1}{tail} >>\nstartxref\n{xrefPos}\n%%EOF\n");
        }
        return ms.ToArray();
    }

    static void WriteBE(byte[] b, int o, long v, int n) { for (var i = n - 1; i >= 0; i--) { b[o + i] = (byte)v; v >>= 8; } }

    static byte[] Deflate(byte[] data)
    {
        using var o = new MemoryStream();
        using (var z = new ZLibStream(o, CompressionLevel.Optimal, true)) z.Write(data);
        return o.ToArray();
    }

    /// <summary>
    /// Appends an incremental update (7.5.6) to <paramref name="original"/>: only <paramref name="changed"/> objects are written, under their
    /// existing numbers, followed by a new cross-reference section chained with /Prev. The original bytes are preserved verbatim.
    /// </summary>
    public static byte[] WriteIncremental(byte[] original, long prevStartXref, IReadOnlyDictionary<int, PdfObject> changed, int size, int root, int? info, PdfObject? id, bool xrefStream, PdfDecryptor? decryptor = null, PdfObject? encrypt = null)
    {
        var ms = new MemoryStream();
        ms.Write(original);
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        if (original.Length > 0 && original[^1] != (byte)'\n') W("\n");
        var ctx = new Ctx(null, null, false) { Dec = decryptor };
        var offs = new SortedDictionary<int, long>();
        foreach (var (num, obj) in changed.OrderBy(k => k.Key))
        {
            offs[num] = ms.Position;
            ctx.Num = num;
            W($"{num} 0 obj\n");
            WriteObject(ms, obj, ctx);
            W("\nendobj\n");
        }
        ctx = new Ctx(null, null, false);
        var tail = new StringBuilder($" /Root {root} 0 R");
        if (info is { } inf) tail.Append($" /Info {inf} 0 R");
        if (encrypt is not null)
        {
            var tmp = new MemoryStream();
            WriteObject(tmp, encrypt, ctx);
            tail.Append(" /Encrypt ").Append(Encoding.Latin1.GetString(tmp.ToArray()));
        }
        if (id is PdfArray { Count: >= 1 } ia)
        {
            var tmp = new MemoryStream();
            WriteObject(tmp, ia, ctx);
            tail.Append(" /ID ").Append(Encoding.Latin1.GetString(tmp.ToArray()));
        }
        var maxNum = offs.Count == 0 ? 0 : offs.Keys.Max();
        var xrefPos = ms.Position;
        if (xrefStream)
        {
            var xn = Math.Max(size, maxNum + 1);
            offs[xn] = xrefPos;
            var rows = new List<byte>();
            var index = new StringBuilder();
            var keys = offs.Keys.ToList();
            for (var i = 0; i < keys.Count;)
            {
                var j = i;
                while (j + 1 < keys.Count && keys[j + 1] == keys[j] + 1) j++;
                index.Append(keys[i]).Append(' ').Append(j - i + 1).Append(' ');
                for (var k = i; k <= j; k++) { var r = new byte[7]; r[0] = 1; WriteBE(r, 1, offs[keys[k]], 4); rows.AddRange(r); }
                i = j + 1;
            }
            var data = Deflate([.. rows]);
            W($"{xn} 0 obj\n<< /Type /XRef /Size {xn + 1} /W [1 4 2] /Index [{index.ToString().TrimEnd()}] /Filter /FlateDecode{tail} /Prev {prevStartXref} /Length {data.Length} >>\nstream\n");
            ms.Write(data);
            W($"\nendstream\nendobj\nstartxref\n{xrefPos}\n%%EOF\n");
        }
        else
        {
            W("xref\n");
            var keys = offs.Keys.ToList();
            for (var i = 0; i < keys.Count;)
            {
                var j = i;
                while (j + 1 < keys.Count && keys[j + 1] == keys[j] + 1) j++;
                W($"{keys[i]} {j - i + 1}\n");
                for (var k = i; k <= j; k++) W($"{offs[keys[k]]:D10} 00000 n \n");
                i = j + 1;
            }
            W($"trailer\n<< /Size {Math.Max(size, maxNum + 1)}{tail} /Prev {prevStartXref} >>\nstartxref\n{xrefPos}\n%%EOF\n");
        }
        return ms.ToArray();
    }

    static void WriteObject(MemoryStream ms, PdfObject? o, Ctx ctx)
    {
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        switch (o)
        {
            case null or PdfNull: W("null"); break;
            case PdfBoolean b: W(b.Value ? "true" : "false"); break;
            case PdfInteger i: W(i.Value.ToString(CultureInfo.InvariantCulture)); break;
            case PdfReal r: W(Real(r.Value)); break;
            case PdfString s: W("<" + Convert.ToHexString(ctx.Dec is not null ? ctx.Dec.SealString(s.Bytes, ctx.Num) : ctx.Enc is null || ctx.Enc.EmbeddedFilesOnly ? s.Bytes : ctx.Enc.Encrypt(s.Bytes, ctx.Num)) + ">"); break;
            case PdfName n: W(Name(n.Value)); break;
            case PdfRef r: W(ctx.Map is null ? $"{r.Number} 0 R" : ctx.Map.TryGetValue(r.Number, out var nn) ? $"{nn} 0 R" : "null"); break;
            case PdfArray a:
                W("[");
                for (var k = 0; k < a.Items.Count; k++) { if (k > 0) W(" "); WriteObject(ms, a.Items[k], ctx); }
                W("]"); break;
            case PdfStream st:
                {
                    var type = (st.Dict.Get("Type") as PdfName)?.Value;
                    var data = st.RawData;
                    var addFlate = ctx.Compress && !st.Dict.Entries.ContainsKey("Filter") && data.Length >= 32;
                    if (addFlate) data = Deflate(data);
                    if (ctx.Enc is not null && !(type == "Metadata" && !ctx.Enc.EncryptMetadata) && (!ctx.Enc.EmbeddedFilesOnly || type == "EmbeddedFile")) data = ctx.Enc.Encrypt(data, ctx.Num);
                    if (ctx.Dec is not null) data = ctx.Dec.SealStream(st, ctx.Num);
                    W("<<");
                    foreach (var (k, v) in st.Dict.Entries)
                    {
                        if (k == "Length") continue;
                        W(Name(k) + " "); WriteObject(ms, v, ctx);
                    }
                    if (addFlate) W(" /Filter /FlateDecode");
                    W($" /Length {data.Length} >>\nstream\n");
                    ms.Write(data);
                    W("\nendstream");
                    break;
                }
            case PdfDictionary d:
                W("<<");
                foreach (var (k, v) in d.Entries)
                {
                    W(Name(k) + " ");
                    // A signature's /Contents is never encrypted (7.6.2); the hash covers its raw bytes.
                    if (ctx.Dec is not null && k == "Contents" && v is PdfString cs && d.Entries.ContainsKey("ByteRange")) W("<" + Convert.ToHexString(cs.Bytes) + ">");
                    else WriteObject(ms, v, ctx);
                }
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
