using System.IO.Compression;
using Pdf.Core;

namespace Pdf.Content;

/// <summary>
/// Character-code to CID mapping (ISO 32000-2 9.7.5): codespace ranges plus cidrange/cidchar mappings, with usecmap chaining.
/// Predefined CJK CMaps are loaded lazily from the embedded <c>predefined-cmaps.bin</c> resource (see tools/cmapgen).
/// </summary>
public sealed class CMap
{
    sealed class Table
    {
        public uint[] Lo = [], Hi = [];
        public int[] Cid = [];
    }

    readonly List<(int N, uint Lo, uint Hi)> _codespace = [];
    readonly Table?[] _tables = new Table?[5];
    public string Name { get; private set; } = "";
    public int WMode { get; private set; }
    public CMap? Parent { get; private set; }
    public bool IsIdentity { get; private init; }

    public static CMap Identity(bool vertical) => new() { Name = vertical ? "Identity-V" : "Identity-H", WMode = vertical ? 1 : 0, IsIdentity = true };

    // ---------------------------------------------------------------- predefined resources

    static readonly object Gate = new();
    static byte[]? _blob;
    static Dictionary<string, (int Off, int Len, int Raw)>? _index;
    static readonly Dictionary<string, CMap?> Cache = [];

    static void LoadIndex()
    {
        if (_index is not null) return;
        using var s = typeof(CMap).Assembly.GetManifestResourceStream("Pdf.Content.Resources.predefined-cmaps.bin");
        var ms = new MemoryStream();
        s?.CopyTo(ms);
        _blob = ms.ToArray();
        var idx = new Dictionary<string, (int, int, int)>();
        if (_blob.Length > 6 && _blob[0] == 'P' && _blob[1] == 'C' && _blob[2] == 'M' && _blob[3] == '1')
        {
            int n = _blob[4] | _blob[5] << 8, p = 6;
            for (var i = 0; i < n; i++)
            {
                int nl = _blob[p++];
                var name = System.Text.Encoding.ASCII.GetString(_blob, p, nl); p += nl;
                idx[name] = (BitConverter.ToInt32(_blob, p), BitConverter.ToInt32(_blob, p + 4), BitConverter.ToInt32(_blob, p + 8)); p += 12;
            }
        }
        _index = idx;
    }

    public static IReadOnlyCollection<string> PredefinedNames { get { lock (Gate) { LoadIndex(); return _index!.Keys.ToArray(); } } }

    /// <summary>Returns the named predefined CMap (Identity-H/V included), or null when unknown.</summary>
    public static CMap? Predefined(string name)
    {
        if (name == "Identity-H") return Identity(false);
        if (name == "Identity-V") return Identity(true);
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out var c)) return c;
            LoadIndex();
            CMap? m = null;
            if (_index!.TryGetValue(name, out var e))
            {
                var raw = new byte[e.Raw];
                using var ds = new DeflateStream(new MemoryStream(_blob!, e.Off, e.Len), CompressionMode.Decompress);
                ds.ReadExactly(raw);
                m = FromBinary(name, raw);
            }
            return Cache[name] = m;
        }
    }

    static CMap FromBinary(string name, byte[] d)
    {
        var p = 0;
        uint Var() { uint v = 0; var sh = 0; while (true) { var b = d[p++]; v |= (uint)(b & 0x7F) << sh; if (b < 0x80) return v; sh += 7; } }
        var m = new CMap { Name = name };
        int ul = d[p++];
        if (ul > 0) m.Parent = Predefined(System.Text.Encoding.ASCII.GetString(d, p, ul));
        p += ul;
        m.WMode = d[p++];
        var ncs = (int)Var();
        for (var i = 0; i < ncs; i++) { int n = d[p++]; var lo = Var(); var hi = Var(); m._codespace.Add((n, lo, hi)); }
        for (var n = 1; n <= 4; n++)
        {
            var cnt = (int)Var();
            var t = new Table { Lo = new uint[cnt], Hi = new uint[cnt], Cid = new int[cnt] };
            long prevHi = -1, prevCid = 0;
            for (var i = 0; i < cnt; i++)
            {
                var lo = (uint)(prevHi + 1 + Var());
                var hi = lo + Var();
                var z = Var();
                var cid = prevCid + ((long)(z >> 1) ^ -(long)(z & 1));
                t.Lo[i] = lo; t.Hi[i] = hi; t.Cid[i] = (int)cid;
                prevHi = hi; prevCid = cid + (hi - lo) + 1;
            }
            m._tables[n] = t;
        }
        return m;
    }

    // ---------------------------------------------------------------- embedded CMap streams

    /// <summary>Parses an embedded CMap program. <paramref name="useCMap"/> supplies the /UseCMap dictionary entry parent, if any.</summary>
    public static CMap Parse(byte[] data, CMap? useCMap = null)
    {
        var m = new CMap { Parent = useCMap };
        var lists = new List<(uint Lo, uint Hi, int Cid)>?[5];
        var lx = new PdfLexer(data);
        var stack = new List<PdfObject>();
        PdfObject? o, prev = null;
        while ((o = lx.ReadObject(false)) is not null)
        {
            if (o is PdfKeyword k)
            {
                switch (k.Value)
                {
                    case "usecmap": if (prev is PdfName un) m.Parent = Predefined(un.Value) ?? m.Parent; break;
                    case "def":
                        if (stack.Count >= 2 && stack[^2] is PdfName wn && wn.Value == "WMode" && stack[^1] is PdfInteger wi) m.WMode = (int)wi.Value;
                        if (stack.Count >= 2 && stack[^2] is PdfName cn && cn.Value == "CMapName" && stack[^1] is PdfName cv) m.Name = cv.Value;
                        break;
                    case "endcodespacerange":
                        for (var i = 0; i + 1 < stack.Count; i += 2)
                            if (stack[i] is PdfString lo && stack[i + 1] is PdfString hi && lo.Bytes.Length is >= 1 and <= 4) m._codespace.Add((lo.Bytes.Length, Be(lo.Bytes), Be(hi.Bytes)));
                        break;
                    case "endcidrange":
                        for (var i = 0; i + 2 < stack.Count; i += 3)
                            if (stack[i] is PdfString lo && stack[i + 1] is PdfString hi && stack[i + 2] is PdfInteger ci && lo.Bytes.Length is >= 1 and <= 4)
                                (lists[lo.Bytes.Length] ??= []).Add((Be(lo.Bytes), Be(hi.Bytes), (int)ci.Value));
                        break;
                    case "endcidchar":
                        for (var i = 0; i + 1 < stack.Count; i += 2)
                            if (stack[i] is PdfString c && stack[i + 1] is PdfInteger ci && c.Bytes.Length is >= 1 and <= 4)
                                (lists[c.Bytes.Length] ??= []).Add((Be(c.Bytes), Be(c.Bytes), (int)ci.Value));
                        break;
                }
                if (k.Value.StartsWith("end") || k.Value.StartsWith("begin") || k.Value is "def" or "usecmap") stack.Clear();
            }
            else stack.Add(o);
            prev = o;
        }
        for (var n = 1; n <= 4; n++)
        {
            if (lists[n] is not { } l) continue;
            l.Sort((a, b) => a.Lo.CompareTo(b.Lo));
            m._tables[n] = new Table { Lo = l.Select(x => x.Lo).ToArray(), Hi = l.Select(x => x.Hi).ToArray(), Cid = l.Select(x => x.Cid).ToArray() };
        }
        return m;
    }

    static uint Be(byte[] b) { uint v = 0; foreach (var x in b) v = v << 8 | x; return v; }

    // ---------------------------------------------------------------- decoding

    IEnumerable<(int N, uint Lo, uint Hi)> AllCodespace()
    {
        for (var m = this; m is not null; m = m.Parent)
            foreach (var c in m._codespace) yield return c;
    }

    /// <summary>Reads one character code at <paramref name="pos"/> using the codespace ranges; always consumes at least one byte.</summary>
    public int ReadCode(ReadOnlySpan<byte> s, int pos, out int code)
    {
        if (IsIdentity) { code = pos + 1 < s.Length ? s[pos] << 8 | s[pos + 1] : s[pos]; return pos + 1 < s.Length ? 2 : 1; }
        var avail = Math.Min(4, s.Length - pos);
        var minLen = 4; var any = false;
        for (var m = this; m is not null; m = m.Parent)
            foreach (var (n, lo, hi) in m._codespace)
            {
                any = true;
                minLen = Math.Min(minLen, n);
                if (n > avail) continue;
                var ok = true;
                for (var i = 0; i < n && ok; i++)
                {
                    var sh = 8 * (n - 1 - i);
                    var b = s[pos + i]; var l = lo >> sh & 0xFF; var h = hi >> sh & 0xFF;
                    ok = b >= l && b <= h;
                }
                if (ok) { code = 0; for (var i = 0; i < n; i++) code = code << 8 | s[pos + i]; return n; }
            }
        var len = Math.Min(any ? minLen : 1, avail);
        code = 0; for (var i = 0; i < len; i++) code = code << 8 | s[pos + i];
        return len;
    }

    /// <summary>Maps a character code of <paramref name="len"/> bytes to a CID (0 when unmapped).</summary>
    public int ToCid(int code, int len)
    {
        if (IsIdentity) return code;
        for (var m = this; m is not null; m = m.Parent)
        {
            if (len is < 1 or > 4 || m._tables[len] is not { } t) continue;
            var c = (uint)code;
            int lo = 0, hi = t.Lo.Length - 1, found = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (t.Lo[mid] <= c) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (found >= 0 && c <= t.Hi[found]) return t.Cid[found] + (int)(c - t.Lo[found]);
        }
        return 0;
    }

    /// <summary>Enumerates (code, byteLength, cid) for every mapped range entry, expanding ranges; used to invert Unicode CMaps.</summary>
    public IEnumerable<(uint Code, int Len, int Cid)> Entries()
    {
        for (var m = this; m is not null; m = m.Parent)
            for (var n = 1; n <= 4; n++)
                if (m._tables[n] is { } t)
                    for (var i = 0; i < t.Lo.Length; i++)
                        for (var c = t.Lo[i]; c <= t.Hi[i]; c++)
                            yield return (c, n, t.Cid[i] + (int)(c - t.Lo[i]));
    }
}
