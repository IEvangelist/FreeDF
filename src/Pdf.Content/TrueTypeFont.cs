namespace Pdf.Content;

/// <summary>Minimal TrueType/OpenType-glyf reader: outlines, cmap (0, 4, 6, 12), hmtx, post names.</summary>
public sealed class TrueTypeFont
{
    readonly byte[] _d;
    readonly Dictionary<string, (int Off, int Len)> _tables = [];
    public int UnitsPerEm { get; private set; } = 1000;
    int _locaFormat, _numGlyphs, _numHMetrics;
    int _loca, _glyf, _hmtx;
    public bool HasGlyf => _glyf > 0 && _loca > 0;
    public bool IsCff => _tables.ContainsKey("CFF ");
    readonly List<(int Platform, int Encoding, int Off)> _cmaps = [];
    Dictionary<string, int>? _postNames;

    public TrueTypeFont(byte[] data)
    {
        _d = data;
        var start = 0;
        if (U32(0) == 0x74746366) start = (int)U32(12);
        var n = U16(start + 4);
        for (var i = 0; i < n; i++)
        {
            var p = start + 12 + i * 16;
            _tables[System.Text.Encoding.ASCII.GetString(_d, p, 4)] = ((int)U32(p + 8), (int)U32(p + 12));
        }
        if (_tables.TryGetValue("head", out var h)) { UnitsPerEm = Math.Max(16, U16(h.Off + 18)); _locaFormat = S16(h.Off + 50); }
        if (_tables.TryGetValue("maxp", out var mp)) _numGlyphs = U16(mp.Off + 4);
        if (_tables.TryGetValue("hhea", out var hh)) _numHMetrics = U16(hh.Off + 34);
        if (_tables.TryGetValue("hmtx", out var hm)) _hmtx = hm.Off;
        if (_tables.TryGetValue("loca", out var lo)) _loca = lo.Off;
        if (_tables.TryGetValue("glyf", out var gl)) _glyf = gl.Off;
        if (_tables.TryGetValue("cmap", out var cm))
        {
            var cnt = U16(cm.Off + 2);
            for (var i = 0; i < cnt; i++) _cmaps.Add((U16(cm.Off + 4 + i * 8), U16(cm.Off + 6 + i * 8), cm.Off + (int)U32(cm.Off + 8 + i * 8)));
        }
    }

    public int NumGlyphs => _numGlyphs;
    uint U32(int o) => o + 4 <= _d.Length ? (uint)(_d[o] << 24 | _d[o + 1] << 16 | _d[o + 2] << 8 | _d[o + 3]) : 0;
    int U16(int o) => o + 2 <= _d.Length ? _d[o] << 8 | _d[o + 1] : 0;
    int S16(int o) => (short)U16(o);
    public bool HasCmap(int platform, int enc) => _cmaps.Any(c => c.Platform == platform && c.Encoding == enc);

    public double Advance(int gid)
    {
        if (_hmtx == 0 || _numHMetrics == 0) return UnitsPerEm;
        return U16(_hmtx + Math.Min(gid, _numHMetrics - 1) * 4) * 1000.0 / UnitsPerEm;
    }

    public int Lookup(int platform, int enc, int code)
    {
        foreach (var c in _cmaps)
            if (c.Platform == platform && c.Encoding == enc) return LookupSub(c.Off, code);
        return 0;
    }

    public int LookupAny(int code)
    {
        foreach (var (pl, en) in new[] { (3, 10), (3, 1), (0, 4), (0, 3), (0, 6), (0, 0), (0, 1), (0, 2) })
            if (HasCmap(pl, en)) { var g = Lookup(pl, en, code); if (g != 0) return g; }
        return 0;
    }

    int LookupSub(int off, int code)
    {
        var fmt = U16(off);
        switch (fmt)
        {
            case 0: return code is >= 0 and < 256 ? _d[off + 6 + code] : 0;
            case 4:
                {
                    var segX2 = U16(off + 6);
                    var ends = off + 14; var starts = ends + segX2 + 2; var deltas = starts + segX2; var ranges = deltas + segX2;
                    for (var i = 0; i < segX2 / 2; i++)
                    {
                        if (code > U16(ends + i * 2)) continue;
                        var s = U16(starts + i * 2);
                        if (code < s) return 0;
                        var ro = U16(ranges + i * 2);
                        if (ro == 0) return (code + S16(deltas + i * 2)) & 0xFFFF;
                        var g = U16(ranges + i * 2 + ro + (code - s) * 2);
                        return g == 0 ? 0 : (g + S16(deltas + i * 2)) & 0xFFFF;
                    }
                    return 0;
                }
            case 6:
                {
                    var first = U16(off + 6); var cnt = U16(off + 8);
                    return code >= first && code < first + cnt ? U16(off + 10 + (code - first) * 2) : 0;
                }
            case 12:
                {
                    var n = (int)U32(off + 12);
                    for (var i = 0; i < n; i++)
                    {
                        var g = off + 16 + i * 12;
                        uint s = U32(g), e = U32(g + 4);
                        if (code >= s && code <= e) return (int)(U32(g + 8) + (uint)code - s);
                    }
                    return 0;
                }
        }
        return 0;
    }

    public int GlyphByName(string name)
    {
        if (_postNames is null)
        {
            _postNames = [];
            if (_tables.TryGetValue("post", out var p) && U32(p.Off) == 0x00020000)
            {
                var n = U16(p.Off + 32);
                var idxOff = p.Off + 34; var strOff = idxOff + n * 2;
                var strs = new List<string>();
                for (var o = strOff; o < p.Off + p.Len;) { var l = _d[o]; strs.Add(System.Text.Encoding.Latin1.GetString(_d, o + 1, Math.Min(l, _d.Length - o - 1))); o += l + 1; }
                for (var g = 0; g < n; g++)
                {
                    var idx = U16(idxOff + g * 2);
                    if (idx >= 258 && idx - 258 < strs.Count) _postNames.TryAdd(strs[idx - 258], g);
                    else if (idx < 258) _postNames.TryAdd(MacGlyphNames.Get(idx), g);
                }
            }
        }
        return _postNames.GetValueOrDefault(name);
    }

    /// <summary>Returns outline in font units scaled to 1/1000 em, or null when the glyph has no contours.</summary>
    public PathData? GetOutline(int gid, int depth = 0)
    {
        if (!HasGlyf || gid < 0 || gid >= _numGlyphs || depth > 6) return null;
        int a, b;
        if (_locaFormat == 0) { a = U16(_loca + gid * 2) * 2; b = U16(_loca + gid * 2 + 2) * 2; }
        else { a = (int)U32(_loca + gid * 4); b = (int)U32(_loca + gid * 4 + 4); }
        if (b <= a) return null;
        var p = _glyf + a;
        var nc = S16(p);
        var scale = 1000.0 / UnitsPerEm;
        var path = new PathData();
        if (nc >= 0)
        {
            var ends = new int[nc];
            for (var i = 0; i < nc; i++) ends[i] = U16(p + 10 + i * 2);
            var np = nc == 0 ? 0 : ends[^1] + 1;
            var insLen = U16(p + 10 + nc * 2);
            var q = p + 12 + nc * 2 + insLen;
            var flags = new byte[np];
            for (var i = 0; i < np;)
            {
                var f = _d[q++]; flags[i++] = f;
                if ((f & 8) != 0) { var r = _d[q++]; while (r-- > 0 && i < np) flags[i++] = f; }
            }
            var xs = new int[np]; var ys = new int[np];
            var v = 0;
            for (var i = 0; i < np; i++)
            {
                if ((flags[i] & 2) != 0) { var dx = _d[q++]; v += (flags[i] & 16) != 0 ? dx : -dx; }
                else if ((flags[i] & 16) == 0) { v += S16(q); q += 2; }
                xs[i] = v;
            }
            v = 0;
            for (var i = 0; i < np; i++)
            {
                if ((flags[i] & 4) != 0) { var dy = _d[q++]; v += (flags[i] & 32) != 0 ? dy : -dy; }
                else if ((flags[i] & 32) == 0) { v += S16(q); q += 2; }
                ys[i] = v;
            }
            var s = 0;
            foreach (var e in ends) { Contour(path, xs, ys, flags, s, e, scale); s = e + 1; }
        }
        else
        {
            var q = p + 10;
            while (true)
            {
                var fl = U16(q); var gi = U16(q + 2); q += 4;
                double dx, dy;
                if ((fl & 1) != 0) { dx = S16(q); dy = S16(q + 2); q += 4; } else { dx = (sbyte)_d[q]; dy = (sbyte)_d[q + 1]; q += 2; }
                if ((fl & 2) == 0) { dx = 0; dy = 0; }
                double ma = 1, mb = 0, mc = 0, md = 1;
                if ((fl & 8) != 0) { ma = md = S16(q) / 16384.0; q += 2; }
                else if ((fl & 0x40) != 0) { ma = S16(q) / 16384.0; md = S16(q + 2) / 16384.0; q += 4; }
                else if ((fl & 0x80) != 0) { ma = S16(q) / 16384.0; mb = S16(q + 2) / 16384.0; mc = S16(q + 4) / 16384.0; md = S16(q + 6) / 16384.0; q += 8; }
                var sub = GetOutline(gi, depth + 1);
                if (sub is not null) path.Append(sub.Transform(new Matrix(ma, mb, mc, md, dx * scale, dy * scale)));
                if ((fl & 0x20) == 0) break;
            }
        }
        return path.IsEmpty ? null : path;
    }

    static void Contour(PathData path, int[] xs, int[] ys, byte[] fl, int s, int e, double k)
    {
        var n = e - s + 1;
        if (n < 2) return;
        (double X, double Y, bool On) P(int i) { var j = s + ((i % n) + n) % n; return (xs[j] * k, ys[j] * k, (fl[j] & 1) != 0); }
        var first = 0;
        for (var i = 0; i < n; i++) if (P(i).On) { first = i; break; }
        var f = P(first);
        (double X, double Y) start;
        if (f.On) start = (f.X, f.Y);
        else { var l = P(first - 1); start = l.On ? (l.X, l.Y) : ((l.X + f.X) / 2, (l.Y + f.Y) / 2); }
        path.MoveTo(start.X, start.Y);
        var cur = start;
        var begin = f.On ? first + 1 : first;
        var count = f.On ? n : n + 1;
        (double X, double Y)? ctl = null;
        for (var i = begin; i < first + count; i++)
        {
            var pt = P(i);
            if (pt.On)
            {
                if (ctl is { } c) { Quad(path, cur, c, (pt.X, pt.Y)); ctl = null; } else path.LineTo(pt.X, pt.Y);
                cur = (pt.X, pt.Y);
            }
            else
            {
                if (ctl is { } c) { var mid = ((c.X + pt.X) / 2, (c.Y + pt.Y) / 2); Quad(path, cur, c, mid); cur = mid; }
                ctl = (pt.X, pt.Y);
            }
        }
        if (ctl is { } cc) Quad(path, cur, cc, start);
        path.Close();
    }

    static void Quad(PathData p, (double X, double Y) a, (double X, double Y) c, (double X, double Y) b) =>
        p.CurveTo(a.X + 2.0 / 3 * (c.X - a.X), a.Y + 2.0 / 3 * (c.Y - a.Y), b.X + 2.0 / 3 * (c.X - b.X), b.Y + 2.0 / 3 * (c.Y - b.Y), b.X, b.Y);
}

static class MacGlyphNames
{
    static readonly string[] Names =
    [".notdef", ".null", "nonmarkingreturn", "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand", "quotesingle", "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash",
     "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less", "equal", "greater", "question", "at",
     "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
     "bracketleft", "backslash", "bracketright", "asciicircum", "underscore", "grave",
     "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
     "braceleft", "bar", "braceright", "asciitilde"];
    public static string Get(int i) => i < Names.Length ? Names[i] : "";
}
