using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace FreeDF.Samples.Kit;

/// <summary>
/// Dependency-free OpenType/TrueType reader with pair-kerning lookup and glyph subsetting
/// (glyph ids retained), mirroring what the fontTools-based Python generator produced.
/// </summary>
public sealed class FontFile
{
    readonly byte[] _d;
    readonly Dictionary<string, (int Off, int Len)> _t = new(StringComparer.Ordinal);
    readonly uint _sfnt;
    Dictionary<int, int>? _cmap;
    readonly Dictionary<long, int> _kernCache = new();
    List<PairSub>? _pairs;
    Dictionary<long, int>? _legacy;
    bool _kernReady;

    public bool IsCff { get; }
    public int UnitsPerEm { get; }
    public int GlyphCount { get; }
    public string PostScriptName { get; }
    public int XMin { get; }
    public int YMin { get; }
    public int XMax { get; }
    public int YMax { get; }
    public int Ascent { get; }
    public int Descent { get; }
    public int CapHeight { get; }
    public double ItalicAngle { get; }

    FontFile(byte[] data)
    {
        _d = data;
        if (data.Length < 12 || U32(data, 0) == 0x74746366) throw new NotSupportedException("Not a single-font sfnt file.");
        _sfnt = U32(data, 0);
        int n = U16(data, 4);
        for (int i = 0; i < n; i++)
        {
            int r = 12 + 16 * i;
            _t[Encoding.ASCII.GetString(data, r, 4)] = ((int)U32(data, r + 8), (int)U32(data, r + 12));
        }
        IsCff = _t.ContainsKey("CFF ");
        int head = T("head"), hhea = T("hhea");
        UnitsPerEm = U16(data, head + 18);
        XMin = I16(data, head + 36); YMin = I16(data, head + 38); XMax = I16(data, head + 40); YMax = I16(data, head + 42);
        Ascent = I16(data, hhea + 4); Descent = I16(data, hhea + 6);
        GlyphCount = U16(data, T("maxp") + 4);
        if (_t.TryGetValue("OS/2", out var os2) && os2.Len >= 90 && U16(data, os2.Off) >= 2) CapHeight = I16(data, os2.Off + 88);
        if (_t.TryGetValue("post", out var post)) ItalicAngle = (int)U32(data, post.Off + 4) / 65536.0;
        PostScriptName = ReadNameId6();
    }

    public static FontFile Load(string path) => new(File.ReadAllBytes(path));

    // ---------------------------------------------------------------- primitives
    static int U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));
    static int I16(byte[] b, int o) => BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(o));
    static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
    int T(string tag) => _t.TryGetValue(tag, out var e) ? e.Off : throw new InvalidDataException($"Missing '{tag}' table.");
    byte[] Raw(string tag) => _t.TryGetValue(tag, out var e) ? _d[e.Off..(e.Off + e.Len)] : throw new InvalidDataException($"Missing '{tag}' table.");

    static void Put16(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o), (ushort)v);
    static void Put32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o), v);

    static byte[] Concat(params byte[][] parts)
    {
        var r = new byte[parts.Sum(p => p.Length)];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(r, o); o += p.Length; }
        return r;
    }

    static byte[] Be16(params int[] vals)
    {
        var r = new byte[vals.Length * 2];
        for (int i = 0; i < vals.Length; i++) Put16(r, i * 2, vals[i]);
        return r;
    }

    // ---------------------------------------------------------------- cmap / metrics / names
    public int GlyphForCodePoint(int cp)
    {
        _cmap ??= BuildCmap();
        return _cmap.TryGetValue(cp, out var g) ? g : 0;
    }

    Dictionary<int, int> BuildCmap()
    {
        var map = new Dictionary<int, int>();
        if (!_t.TryGetValue("cmap", out var ct)) return map;
        int best = -1, bestRank = 0;
        for (int i = 0, n = U16(_d, ct.Off + 2); i < n; i++)
        {
            int r = ct.Off + 4 + 8 * i, plat = U16(_d, r), enc = U16(_d, r + 2), off = ct.Off + (int)U32(_d, r + 4);
            int fmt = U16(_d, off);
            if (fmt is not (0 or 4 or 6 or 12)) continue;
            int rank = (plat, enc) switch
            {
                (3, 10) => 8, (0, 6) => 7, (0, 4) => 6, (3, 1) => 5, (0, 3) => 4, (0, 2) => 3, (0, 1) => 2, (0, 0) => 1, _ => 0
            };
            if (rank > bestRank) { bestRank = rank; best = off; }
        }
        if (best < 0) return map;
        int o = best;
        switch (U16(_d, o))
        {
            case 0:
                for (int c = 0; c < 256; c++) if (_d[o + 6 + c] != 0) map[c] = _d[o + 6 + c];
                break;
            case 6:
                for (int i = 0, first = U16(_d, o + 6), cnt = U16(_d, o + 8); i < cnt; i++)
                { int g = U16(_d, o + 10 + 2 * i); if (g != 0) map[first + i] = g; }
                break;
            case 12:
                for (int i = 0, n = (int)U32(_d, o + 12); i < n; i++)
                {
                    int r = o + 16 + 12 * i, s = (int)U32(_d, r), e = (int)U32(_d, r + 4), g0 = (int)U32(_d, r + 8);
                    for (int c = s; c <= e && c - s < 0x110000; c++) if (g0 + c - s != 0) map[c] = g0 + c - s;
                }
                break;
            default:
                int seg = U16(_d, o + 6) / 2, endP = o + 14, startP = endP + 2 * seg + 2, deltaP = startP + 2 * seg, rangeP = deltaP + 2 * seg;
                for (int s = 0; s < seg; s++)
                {
                    int end = U16(_d, endP + 2 * s), start = U16(_d, startP + 2 * s), delta = U16(_d, deltaP + 2 * s), ro = U16(_d, rangeP + 2 * s);
                    for (int c = start; c <= end && c != 0xFFFF; c++)
                    {
                        int g;
                        if (ro == 0) g = (c + delta) & 0xFFFF;
                        else
                        {
                            int a = rangeP + 2 * s + ro + 2 * (c - start);
                            if (a + 2 > _d.Length) continue;
                            g = U16(_d, a);
                            if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0) map[c] = g;
                    }
                }
                break;
        }
        return map;
    }

    public double AdvanceUnits(int gid)
    {
        int nh = U16(_d, T("hhea") + 34);
        if (nh == 0) return 0;
        return U16(_d, T("hmtx") + 4 * Math.Min(Math.Max(gid, 0), nh - 1));
    }

    string ReadNameId6()
    {
        if (!_t.TryGetValue("name", out var nt)) return "";
        int count = U16(_d, nt.Off + 2), so = nt.Off + U16(_d, nt.Off + 4);
        for (int i = 0; i < count; i++)
        {
            int r = nt.Off + 6 + 12 * i;
            if (U16(_d, r + 6) != 6) continue;
            int plat = U16(_d, r), len = U16(_d, r + 8), off = so + U16(_d, r + 10);
            return (plat is 0 or 3 ? Encoding.BigEndianUnicode : Encoding.Latin1).GetString(_d, off, len);
        }
        return "";
    }

    // ---------------------------------------------------------------- kerning
    sealed class PairSub
    {
        public int Off, Fmt, Vf1, Vf2;
        public Dictionary<int, int> Cov = new();
        public Dictionary<int, int>? C1, C2;
    }

    public int KernUnits(int leftGid, int rightGid)
    {
        long key = ((long)leftGid << 32) | (uint)rightGid;
        if (_kernCache.TryGetValue(key, out var cached)) return cached;
        if (!_kernReady) { LoadKern(); _kernReady = true; }
        int v = 0;
        _legacy?.TryGetValue(key, out v);
        foreach (var st in _pairs!)
        {
            if (!st.Cov.TryGetValue(leftGid, out int ci)) continue;
            int sz1 = 2 * BitOperations.PopCount((uint)st.Vf1), sz2 = 2 * BitOperations.PopCount((uint)st.Vf2);
            if (st.Fmt == 1)
            {
                int ps = st.Off + U16(_d, st.Off + 10 + 2 * ci), n = U16(_d, ps), rec = 2 + sz1 + sz2;
                bool hit = false;
                for (int i = 0; i < n; i++)
                {
                    int r = ps + 2 + i * rec;
                    if (U16(_d, r) != rightGid) continue;
                    v = XAdv(st.Vf1, r + 2);
                    hit = true;
                    break;
                }
                if (hit) break;
            }
            else
            {
                int c1 = st.C1!.GetValueOrDefault(leftGid), c2 = st.C2!.GetValueOrDefault(rightGid);
                int n1 = U16(_d, st.Off + 12), n2 = U16(_d, st.Off + 14);
                if (c1 >= n1 || c2 >= n2) continue;
                int val = XAdv(st.Vf1, st.Off + 16 + (c1 * n2 + c2) * (sz1 + sz2));
                if (val != 0) { v = val; break; }
            }
        }
        _kernCache[key] = v;
        return v;
    }

    int XAdv(int vf, int o) => (vf & 4) == 0 ? 0 : I16(_d, o + 2 * BitOperations.PopCount((uint)(vf & 3)));

    void LoadKern()
    {
        _pairs = new();
        if (_t.TryGetValue("GPOS", out var gp))
        {
            int g = gp.Off, feat = g + U16(_d, g + 6), look = g + U16(_d, g + 8);
            var idx = new SortedSet<int>();
            for (int i = 0, n = U16(_d, feat); i < n; i++)
            {
                int r = feat + 2 + 6 * i;
                if (Encoding.ASCII.GetString(_d, r, 4) != "kern") continue;
                int f = feat + U16(_d, r + 4);
                for (int j = 0, m = U16(_d, f + 2); j < m; j++) idx.Add(U16(_d, f + 4 + 2 * j));
            }
            int lc = U16(_d, look);
            foreach (int li in idx)
            {
                if (li >= lc) continue;
                int lk = look + U16(_d, look + 2 + 2 * li), type = U16(_d, lk);
                for (int s = 0, sc = U16(_d, lk + 4); s < sc; s++)
                {
                    int st = lk + U16(_d, lk + 6 + 2 * s), t = type;
                    if (type == 9) { t = U16(_d, st + 2); st += (int)U32(_d, st + 4); }
                    if (t == 2) _pairs.Add(ParsePairSub(st));
                }
            }
        }
        else if (_t.TryGetValue("kern", out var kt) && U16(_d, kt.Off) == 0 && U16(_d, kt.Off + 2) > 0)
        {
            int st = kt.Off + 4;
            if ((U16(_d, st + 4) >> 8) == 0)
            {
                _legacy = new();
                for (int i = 0, n = U16(_d, st + 6); i < n; i++)
                {
                    int r = st + 14 + 6 * i;
                    _legacy[((long)U16(_d, r) << 32) | (uint)U16(_d, r + 2)] = I16(_d, r + 4);
                }
            }
        }
    }

    PairSub ParsePairSub(int o)
    {
        var p = new PairSub { Off = o, Fmt = U16(_d, o), Vf1 = U16(_d, o + 4), Vf2 = U16(_d, o + 6) };
        int co = o + U16(_d, o + 2);
        if (U16(_d, co) == 1)
            for (int i = 0, n = U16(_d, co + 2); i < n; i++) p.Cov[U16(_d, co + 4 + 2 * i)] = i;
        else
            for (int i = 0, n = U16(_d, co + 2); i < n; i++)
            {
                int r = co + 4 + 6 * i, s = U16(_d, r), e = U16(_d, r + 2), si = U16(_d, r + 4);
                for (int g = s; g <= e; g++) p.Cov[g] = si + g - s;
            }
        if (p.Fmt == 2) { p.C1 = ClassDef(o + U16(_d, o + 8)); p.C2 = ClassDef(o + U16(_d, o + 10)); }
        return p;
    }

    Dictionary<int, int> ClassDef(int o)
    {
        var m = new Dictionary<int, int>();
        if (U16(_d, o) == 1)
            for (int i = 0, s = U16(_d, o + 2), n = U16(_d, o + 4); i < n; i++) m[s + i] = U16(_d, o + 6 + 2 * i);
        else
            for (int i = 0, n = U16(_d, o + 2); i < n; i++)
            {
                int r = o + 4 + 6 * i, s = U16(_d, r), e = U16(_d, r + 2), c = U16(_d, r + 4);
                for (int g = s; g <= e; g++) m[g] = c;
            }
        return m;
    }

    // ---------------------------------------------------------------- subsetting (shared)
    int Count(IEnumerable<int> gids) => Math.Clamp(gids.Where(g => g >= 0 && g < GlyphCount).DefaultIfEmpty(0).Max() + 1, 1, GlyphCount);

    SortedDictionary<string, byte[]> CommonTables(int count)
    {
        var t = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var head = Raw("head")[..54];
        Put32(head, 8, 0);
        t["head"] = head;
        var hhea = Raw("hhea")[..36];
        int nh = U16(hhea, 34), keepH = Math.Min(nh, count);
        Put16(hhea, 34, keepH);
        t["hhea"] = hhea;
        int hm = T("hmtx");
        t["hmtx"] = count <= nh ? _d[hm..(hm + 4 * count)] : Concat(_d[hm..(hm + 4 * nh)], _d[(hm + 4 * nh)..(hm + 4 * nh + 2 * (count - nh))]);
        var maxp = Raw("maxp");
        Put16(maxp, 4, count);
        t["maxp"] = maxp;
        t["name"] = SubsetName();
        if (_t.ContainsKey("OS/2")) t["OS/2"] = Raw("OS/2");
        var post = Raw("post")[..32];
        Put32(post, 0, 0x00030000);
        t["post"] = post;
        t["cmap"] = BuildCmap31(new SortedDictionary<int, int>());
        return t;
    }

    byte[] SubsetName()
    {
        var recs = new List<(int P, int E, int L, int Id, byte[] S)>();
        if (_t.TryGetValue("name", out var nt))
        {
            int so = nt.Off + U16(_d, nt.Off + 4);
            for (int i = 0, n = U16(_d, nt.Off + 2); i < n; i++)
            {
                int r = nt.Off + 6 + 12 * i, id = U16(_d, r + 6);
                if (id is 1 or 2 or 3 or 4 or 6)
                    recs.Add((U16(_d, r), U16(_d, r + 2), U16(_d, r + 4), id, _d[(so + U16(_d, r + 10))..(so + U16(_d, r + 10) + U16(_d, r + 8))]));
            }
        }
        recs.Sort((a, b) => (a.P, a.E, a.L, a.Id).CompareTo((b.P, b.E, b.L, b.Id)));
        var hdr = new byte[6 + 12 * recs.Count];
        Put16(hdr, 2, recs.Count); Put16(hdr, 4, hdr.Length);
        var str = new MemoryStream();
        for (int i = 0; i < recs.Count; i++)
        {
            var r = recs[i];
            int o = 6 + 12 * i;
            Put16(hdr, o, r.P); Put16(hdr, o + 2, r.E); Put16(hdr, o + 4, r.L); Put16(hdr, o + 6, r.Id);
            Put16(hdr, o + 8, r.S.Length); Put16(hdr, o + 10, (int)str.Length);
            str.Write(r.S);
        }
        return Concat(hdr, str.ToArray());
    }

    static byte[] BuildCmap31(SortedDictionary<int, int> map)
    {
        var segs = new List<(int S, int E, int D)>();
        foreach (var (cp, g) in map.Where(kv => kv.Key < 0xFFFF))
        {
            if (segs.Count > 0 && segs[^1].E + 1 == cp && (g - cp & 0xFFFF) == segs[^1].D) segs[^1] = (segs[^1].S, cp, segs[^1].D);
            else segs.Add((cp, cp, g - cp & 0xFFFF));
        }
        segs.Add((0xFFFF, 0xFFFF, 1));
        int sc = segs.Count, es = (int)Math.Log2(sc), sr = 2 << es;
        var sub = Concat(
            Be16(4, 16 + 8 * sc, 0, 2 * sc, sr, es, 2 * sc - sr),
            Be16(segs.Select(s => s.E).ToArray()), Be16(0), Be16(segs.Select(s => s.S).ToArray()),
            Be16(segs.Select(s => s.D).ToArray()), new byte[2 * sc]);
        return Concat(Be16(0, 1, 3, 1, 0, 12), sub);
    }

    static byte[] Assemble(uint sfnt, SortedDictionary<string, byte[]> tables)
    {
        int n = tables.Count, pos = 12 + 16 * n;
        var offs = new List<int>();
        foreach (var t in tables.Values) { offs.Add(pos); pos += (t.Length + 3) & ~3; }
        var f = new byte[pos];
        Put32(f, 0, sfnt);
        int es = (int)Math.Log2(n);
        Put16(f, 4, n); Put16(f, 6, 16 << es); Put16(f, 8, es); Put16(f, 10, 16 * n - (16 << es));
        int i = 0, headOff = 0;
        foreach (var (tag, data) in tables)
        {
            int r = 12 + 16 * i, o = offs[i++];
            Encoding.ASCII.GetBytes(tag, 0, 4, f, r);
            Put32(f, r + 4, Sum(data)); Put32(f, r + 8, (uint)o); Put32(f, r + 12, (uint)data.Length);
            data.CopyTo(f, o);
            if (tag == "head") headOff = o;
        }
        Put32(f, headOff + 8, unchecked(0xB1B0AFBAu - Sum(f)));
        return f;
    }

    static uint Sum(byte[] b)
    {
        uint s = 0;
        for (int i = 0; i < b.Length; i += 4)
        {
            uint w = 0;
            for (int k = 0; k < 4; k++) w = (w << 8) | (i + k < b.Length ? b[i + k] : (uint)0);
            s = unchecked(s + w);
        }
        return s;
    }

    public byte[] Subset(IEnumerable<int> gids)
    {
        var keep = new HashSet<int>(gids.Where(g => g > 0 && g < GlyphCount)) { 0 };
        if (IsCff)
        {
            int count = Count(keep);
            var t = CommonTables(count);
            t["CFF "] = SubsetCff(keep, count);
            return Assemble(_sfnt, t);
        }
        return SubsetGlyf(keep, false, null);
    }

    public byte[] SubsetForCodePoints(IEnumerable<int> codePoints)
    {
        if (IsCff) throw new NotSupportedException("SubsetForCodePoints supports TrueType (glyf) fonts only.");
        var map = new SortedDictionary<int, int>();
        foreach (int cp in codePoints) { int g = GlyphForCodePoint(cp); if (g != 0) map[cp] = g; }
        var keep = new HashSet<int>(map.Values) { 0 };
        return SubsetGlyf(keep, true, map);
    }

    // ---------------------------------------------------------------- TrueType glyf
    byte[] GlyphData(int gid, bool longLoca)
    {
        int loca = T("loca"), glyf = T("glyf");
        int a = longLoca ? (int)U32(_d, loca + 4 * gid) : 2 * U16(_d, loca + 2 * gid);
        int b = longLoca ? (int)U32(_d, loca + 4 * gid + 4) : 2 * U16(_d, loca + 2 * gid + 2);
        return b > a ? _d[(glyf + a)..(glyf + b)] : [];
    }

    static IEnumerable<int> Components(byte[] g)
    {
        if (g.Length < 10 || I16(g, 0) >= 0) yield break;
        for (int p = 10, flags = 0x20; (flags & 0x20) != 0 && p + 4 <= g.Length;)
        {
            flags = U16(g, p);
            yield return U16(g, p + 2);
            p += 4 + ((flags & 1) != 0 ? 4 : 2) + ((flags & 8) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);
        }
    }

    static byte[] StripHinting(byte[] g)
    {
        if (g.Length < 10) return g;
        int nc = I16(g, 0);
        if (nc >= 0)
        {
            int il = 10 + 2 * nc;
            if (nc == 0 || il + 2 > g.Length) return g;
            int len = U16(g, il);
            return Concat(g[..il], new byte[2], g[(il + 2 + len)..]);
        }
        int p = 10, end = 10, flags = 0x20;
        var c = (byte[])g.Clone();
        while ((flags & 0x20) != 0 && p + 4 <= c.Length)
        {
            flags = U16(c, p);
            Put16(c, p, flags & ~0x100);
            p += 4 + ((flags & 1) != 0 ? 4 : 2) + ((flags & 8) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);
            end = p;
        }
        return c[..end];
    }

    byte[] SubsetGlyf(HashSet<int> keep, bool hinted, SortedDictionary<int, int>? cmap)
    {
        bool longLoca = I16(_d, T("head") + 50) != 0;
        var stack = new Stack<int>(keep);
        var data = new Dictionary<int, byte[]>();
        while (stack.Count > 0)
        {
            int g = stack.Pop();
            if (data.ContainsKey(g)) continue;
            data[g] = GlyphData(g, longLoca);
            foreach (int c in Components(data[g])) { if (c < GlyphCount && keep.Add(c)) stack.Push(c); }
        }
        int count = Count(keep);
        var glyf = new MemoryStream();
        var offsets = new int[count + 1];
        for (int g = 0; g < count; g++)
        {
            offsets[g] = (int)glyf.Length;
            if (!data.TryGetValue(g, out var gd)) continue;
            if (!hinted) gd = StripHinting(gd);
            glyf.Write(gd);
            glyf.Write(new byte[(4 - gd.Length % 4) % 4]);
        }
        offsets[count] = (int)glyf.Length;
        bool shortLoca = glyf.Length <= 0x1FFFE;
        var loca = new byte[(count + 1) * (shortLoca ? 2 : 4)];
        for (int i = 0; i <= count; i++)
        {
            if (shortLoca) Put16(loca, 2 * i, offsets[i] / 2); else Put32(loca, 4 * i, (uint)offsets[i]);
        }
        var t = CommonTables(count);
        Put16(t["head"], 50, shortLoca ? 0 : 1);
        t["glyf"] = glyf.ToArray();
        t["loca"] = loca;
        if (hinted)
        {
            foreach (var tag in new[] { "cvt ", "fpgm", "prep", "gasp" }) if (_t.ContainsKey(tag)) t[tag] = Raw(tag);
            if (cmap != null)
            {
                t["cmap"] = Concat(Be16(0, 2, 1, 0, 0, 20, 3, 1, 0, 20 + 262), MacCmap(cmap.Values.ToHashSet()), BuildCmap31(cmap)[12..]);
            }
            var kern = FilterKern(keep);
            if (kern != null) t["kern"] = kern;
        }
        return Assemble(_sfnt, t);
    }

    // Original (1,0) byte-encoding table with unkept glyphs cleared; empty if the font lacks one.
    byte[] MacCmap(HashSet<int> keep)
    {
        var sub = new byte[262];
        Put16(sub, 2, 262);
        if (!_t.TryGetValue("cmap", out var ct)) return sub;
        for (int i = 0, n = U16(_d, ct.Off + 2); i < n; i++)
        {
            int r = ct.Off + 4 + 8 * i, off = ct.Off + (int)U32(_d, r + 4);
            if (U16(_d, r) != 1 || U16(_d, r + 2) != 0 || U16(_d, off) != 0) continue;
            for (int c = 0; c < 256; c++) if (keep.Contains(_d[off + 6 + c])) sub[6 + c] = _d[off + 6 + c];
            break;
        }
        return sub;
    }

    byte[]? FilterKern(HashSet<int> keep)
    {
        if (!_t.TryGetValue("kern", out var kt) || U16(_d, kt.Off) != 0 || U16(_d, kt.Off + 2) == 0) return null;
        int st = kt.Off + 4;
        if ((U16(_d, st + 4) >> 8) != 0) return null;
        var pairs = new List<byte[]>();
        for (int i = 0, n = U16(_d, st + 6); i < n; i++)
        {
            int r = st + 14 + 6 * i;
            if (keep.Contains(U16(_d, r)) && keep.Contains(U16(_d, r + 2))) pairs.Add(_d[r..(r + 6)]);
        }
        if (pairs.Count == 0 || 14 + 6 * pairs.Count > 0xFFFF) return null;
        int sr = 6 << (int)Math.Log2(pairs.Count), es = (int)Math.Log2(pairs.Count);
        return Concat(Be16(0, 1, 0, 14 + 6 * pairs.Count, 1, pairs.Count, sr, es, 6 * pairs.Count - sr), Concat(pairs.ToArray()));
    }

    // ---------------------------------------------------------------- CFF
    sealed record DictEntry(int Op, double[] V, int S, int E);

    static List<DictEntry> ParseDict(byte[] b, int o, int len)
    {
        var res = new List<DictEntry>();
        var ops = new List<double>();
        int p = o, s = o, end = o + len;
        while (p < end)
        {
            int b0 = b[p];
            if (b0 <= 21)
            {
                int op = b0; p++;
                if (b0 == 12) { op = 1200 + b[p]; p++; }
                res.Add(new(op, ops.ToArray(), s, p));
                ops.Clear(); s = p;
            }
            else if (b0 == 28) { ops.Add(I16(b, p + 1)); p += 3; }
            else if (b0 == 29) { ops.Add((int)U32(b, p + 1)); p += 5; }
            else if (b0 == 30)
            {
                p++;
                while (true) { int x = b[p++]; if ((x & 0xF) == 0xF || (x >> 4) == 0xF) break; }
                ops.Add(0);
            }
            else if (b0 <= 246) { ops.Add(b0 - 139); p++; }
            else if (b0 <= 250) { ops.Add((b0 - 247) * 256 + b[p + 1] + 108); p += 2; }
            else if (b0 <= 254) { ops.Add(-(b0 - 251) * 256 - b[p + 1] - 108); p += 2; }
            else p++;
        }
        return res;
    }

    static (List<(int Off, int Len)> Items, int End) ReadIndex(byte[] b, int pos)
    {
        int n = U16(b, pos);
        var items = new List<(int, int)>(n);
        if (n == 0) return (items, pos + 2);
        int os = b[pos + 2], op = pos + 3, data = op + (n + 1) * os - 1;
        int Off(int i) { int v = 0; for (int k = 0; k < os; k++) v = (v << 8) | b[op + i * os + k]; return v; }
        for (int i = 0; i < n; i++) items.Add((data + Off(i), Off(i + 1) - Off(i)));
        return (items, data + Off(n));
    }

    static byte[] WriteIndex(IReadOnlyList<byte[]> items)
    {
        if (items.Count == 0) return new byte[2];
        int total = items.Sum(i => i.Length) + 1;
        int os = total < 0x100 ? 1 : total < 0x10000 ? 2 : total < 0x1000000 ? 3 : 4;
        var r = new byte[3 + (items.Count + 1) * os + total - 1];
        Put16(r, 0, items.Count); r[2] = (byte)os;
        int off = 1, p = 3 + (items.Count + 1) * os;
        for (int i = 0; i <= items.Count; i++)
        {
            for (int k = 0; k < os; k++) r[3 + i * os + k] = (byte)(off >> (8 * (os - 1 - k)));
            if (i < items.Count) { items[i].CopyTo(r, p); p += items[i].Length; off += items[i].Length; }
        }
        return r;
    }

    static byte[] FixedInt(int v) => [29, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    static byte[] OpBytes(int op) => op >= 1200 ? [12, (byte)(op - 1200)] : [(byte)op];

    byte[] SubsetCff(HashSet<int> keep, int count)
    {
        var c = Raw("CFF ");
        int hdr = c[2];
        var (_, p1) = ReadIndex(c, hdr);
        var (tops, p2) = ReadIndex(c, p1);
        var (_, p3) = ReadIndex(c, p2);
        var (_, p4) = ReadIndex(c, p3);
        var top = ParseDict(c, tops[0].Off, tops[0].Len);
        double[]? Get(int op) => top.FirstOrDefault(e => e.Op == op)?.V;

        var csOld = ReadIndex(c, (int)Get(17)![0]).Items;

        // String INDEX pruning: only SIDs still referenced are kept, renumbered from 391.
        var sItems = ReadIndex(c, p2).Items;
        var sidOrder = new List<int>();
        var sidMap = new Dictionary<int, int>();
        int M(int s)
        {
            if (s < 391 || s - 391 >= sItems.Count) return s;
            if (!sidMap.TryGetValue(s, out int n)) { n = 391 + sidOrder.Count; sidMap[s] = n; sidOrder.Add(s); }
            return n;
        }
        byte[] SidEntry(DictEntry e)
        {
            if (e.Op is 1 or 1200) return [];
            int sids = e.Op switch { 0 or 2 or 3 or 4 or 1221 or 1222 or 1238 => 1, 1230 => 2, _ => 0 };
            if (sids == 0) return c[e.S..e.E];
            var ms = new MemoryStream();
            for (int i = 0; i < e.V.Length; i++) ms.Write(FixedInt(i < sids ? M((int)e.V[i]) : (int)e.V[i]));
            ms.Write(OpBytes(e.Op));
            return ms.ToArray();
        }

        byte[]? charset = null;
        var csetOp = Get(15);
        if (csetOp != null && csetOp[0] > 2)
        {
            int p = (int)csetOp[0], fmt = c[p++], need = csOld.Count - 1;
            var sids = new List<int>();
            if (fmt == 0) for (int i = 0; i < need; i++, p += 2) sids.Add(U16(c, p));
            else
                while (sids.Count < need)
                {
                    int first = U16(c, p), left = fmt == 1 ? c[p + 2] : U16(c, p + 2);
                    p += fmt == 1 ? 3 : 4;
                    for (int i = 0; i <= left; i++) sids.Add(first + i);
                }
            charset = Concat([0], Be16(sids.Take(count - 1).Select(M).ToArray()));
        }

        byte[]? fdSel = null, fdAll = null;
        var fdsOp = Get(1237);
        if (fdsOp != null)
        {
            int p = (int)fdsOp[0], fmt = c[p];
            var fd = fdAll = new byte[csOld.Count];
            if (fmt == 0) Array.Copy(c, p + 1, fd, 0, fd.Length);
            else
                for (int i = 0, n = U16(c, p + 1); i < n; i++)
                {
                    int s = U16(c, p + 3 + 3 * i), e = U16(c, p + 3 + 3 * (i + 1)), f = c[p + 5 + 3 * i];
                    for (int g = s; g < e && g < fd.Length; g++) fd[g] = (byte)f;
                }
            fdSel = Concat([0], fd[..count]);
        }

        // Font DICTs (CID) or the single Top DICT Private (name-keyed).
        var fdaOp = Get(1236);
        List<List<DictEntry>>? fonts = null;
        if (fdaOp != null) fonts = ReadIndex(c, (int)fdaOp[0]).Items.Select(f => ParseDict(c, f.Off, f.Len)).ToList();
        var privSources = fonts != null ? fonts.Select(f => f.FirstOrDefault(e => e.Op == 18)?.V).ToList() : [Get(18)];
        var blocks = privSources.Select(v => v == null ? [] : RebuildPrivate(c, (int)v[1], (int)v[0])).ToList();

        // Inline every subroutine call of the kept glyphs; subr INDEXes are then emitted empty.
        var gSubrs = ReadIndex(c, p3).Items;
        var localSubrs = privSources.Select(v =>
        {
            var sub = v == null ? null : ParseDict(c, (int)v[1], (int)v[0]).FirstOrDefault(e => e.Op == 19);
            return sub == null ? [] : ReadIndex(c, (int)v![1] + (int)sub.V[0]).Items;
        }).ToList();
        var cs = new List<byte[]>(count);
        for (int g = 0; g < count; g++)
        {
            if (!keep.Contains(g) || g >= csOld.Count) { cs.Add([14]); continue; }
            var ls = localSubrs[fdAll == null ? 0 : fdAll[g]];
            var o = new List<byte>();
            Desub(c, csOld[g], gSubrs, ls, o, new Desubber(), 0);
            cs.Add(o.ToArray());
        }
        var csIdx = WriteIndex(cs);

        byte[] TopDict(int cset, int fds, int cs_, int fda, int psz, int poff)
        {
            var ms = new MemoryStream();
            foreach (var e in top)
            {
                switch (e.Op)
                {
                    case 15 when charset != null: ms.Write(FixedInt(cset)); ms.Write(OpBytes(15)); continue;
                    case 16 when e.V[0] > 1: continue;
                    case 17: ms.Write(FixedInt(cs_)); ms.Write(OpBytes(17)); continue;
                    case 18: ms.Write(FixedInt(psz)); ms.Write(FixedInt(poff)); ms.Write(OpBytes(18)); continue;
                    case 1236: ms.Write(FixedInt(fda)); ms.Write(OpBytes(1236)); continue;
                    case 1237: ms.Write(FixedInt(fds)); ms.Write(OpBytes(1237)); continue;
                }
                ms.Write(SidEntry(e));
            }
            return ms.ToArray();
        }

        byte[] FontDicts(int firstPriv)
        {
            var list = new List<byte[]>();
            int pp = firstPriv;
            for (int i = 0; i < fonts!.Count; i++)
            {
                var ms = new MemoryStream();
                foreach (var e in fonts[i])
                {
                    if (e.Op == 18) { ms.Write(FixedInt(blocks[i].Length)); ms.Write(FixedInt(pp)); ms.Write(OpBytes(18)); }
                    else ms.Write(SidEntry(e));
                }
                list.Add(ms.ToArray());
                pp += blocks[i].Length;
            }
            return WriteIndex(list);
        }

        byte[] head = c[..hdr], names = c[hdr..p1], gsubrs = WriteIndex([]);
        int topSize = WriteIndex([TopDict(0, 0, 0, 0, 0, 0)]).Length;
        if (fonts != null) FontDicts(0);
        var strs = WriteIndex(sidOrder.Select(s => c[sItems[s - 391].Off..(sItems[s - 391].Off + sItems[s - 391].Len)]).ToList());
        int pos = hdr + names.Length + topSize + strs.Length + gsubrs.Length;
        int csetPos = pos; pos += charset?.Length ?? 0;
        int fdsPos = pos; pos += fdSel?.Length ?? 0;
        int csPos = pos; pos += csIdx.Length;
        int fdaPos = pos;
        byte[] fdaBytes = [];
        if (fonts != null) { fdaBytes = FontDicts(0); pos += fdaBytes.Length; fdaBytes = FontDicts(pos); }
        int privPos = pos;
        var topBytes = WriteIndex([TopDict(csetPos, fdsPos, csPos, fdaPos, fonts == null && blocks[0].Length > 0 ? blocks[0].Length : 0, privPos)]);
        return Concat(head, names, topBytes, strs, gsubrs, charset ?? [], fdSel ?? [], csIdx, fdaBytes, Concat(blocks.ToArray()));
    }

    sealed class Desubber
    {
        public readonly List<(int Pos, int Val)> Nums = new();
        public int Stems;
        public bool Done;
    }

    static void Desub(byte[] c, (int Off, int Len) cs, List<(int Off, int Len)> gs, List<(int Off, int Len)> ls, List<byte> o, Desubber s, int depth)
    {
        if (depth > 10) throw new InvalidDataException("CFF subroutine nesting too deep.");
        int p = cs.Off, end = cs.Off + cs.Len;
        while (p < end && !s.Done)
        {
            int b0 = c[p];
            if (b0 >= 32 || b0 == 28)
            {
                int len, val;
                if (b0 == 28) { len = 3; val = I16(c, p + 1); }
                else if (b0 <= 246) { len = 1; val = b0 - 139; }
                else if (b0 <= 250) { len = 2; val = (b0 - 247) * 256 + c[p + 1] + 108; }
                else if (b0 <= 254) { len = 2; val = -(b0 - 251) * 256 - c[p + 1] - 108; }
                else { len = 5; val = (int)U32(c, p + 1) >> 16; }
                s.Nums.Add((o.Count, val));
                o.AddRange(c.AsSpan(p, len));
                p += len;
                continue;
            }
            p++;
            switch (b0)
            {
                case 10 or 29:
                    var (pos, idx) = s.Nums[^1];
                    s.Nums.RemoveAt(s.Nums.Count - 1);
                    o.RemoveRange(pos, o.Count - pos);
                    var list = b0 == 10 ? ls : gs;
                    idx += list.Count < 1240 ? 107 : list.Count < 33900 ? 1131 : 32768;
                    Desub(c, list[idx], gs, ls, o, s, depth + 1);
                    break;
                case 11:
                    return;
                case 14:
                    o.Add(14);
                    s.Done = true;
                    return;
                case 19 or 20:
                    s.Stems += s.Nums.Count / 2;
                    o.Add((byte)b0);
                    int mb = (s.Stems + 7) / 8;
                    o.AddRange(c.AsSpan(p, mb));
                    p += mb;
                    s.Nums.Clear();
                    break;
                default:
                    if (b0 is 1 or 3 or 18 or 23) s.Stems += s.Nums.Count / 2;
                    o.Add((byte)b0);
                    if (b0 == 12) o.Add(c[p++]);
                    s.Nums.Clear();
                    break;
            }
        }
    }

    // Private DICT without its Subrs entry (subroutines are inlined).
    static byte[] RebuildPrivate(byte[] c, int off, int size)
    {
        var ms = new MemoryStream();
        foreach (var e in ParseDict(c, off, size)) if (e.Op != 19) ms.Write(c[e.S..e.E]);
        return ms.ToArray();
    }
}
