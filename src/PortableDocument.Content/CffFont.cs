using System.Buffers.Binary;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>Compact Font Format (CFF) parser with a Type 2 charstring interpreter. Outlines are returned in 1/1000 em.</summary>
public sealed class CffFont
{
    readonly byte[] _d;
    readonly int[][] _charStrings = [];   // [start, end) per glyph
    readonly int[] _gsubrs = [];
    readonly int[] _gsubrOffsets = [];
    readonly string?[] _strings = [];
    readonly int[] _charset = [];         // gid -> SID (or CID)
    readonly Private[] _privates = [];
    readonly byte[]? _fdSelect;
    readonly int[]? _fdSelectRanges;
    readonly double[] _matrix = [0.001, 0, 0, 0.001, 0, 0];
    readonly int[] _encoding = new int[256]; // code -> gid
    Dictionary<string, int>? _byName;
    Dictionary<int, int>? _byCid;

    sealed class Private
    {
        public double DefaultWidth, NominalWidth;
        public int[] SubrStart = [], SubrEnd = [];
    }

    public int GlyphCount => _charStrings.Length;
    public bool IsCid { get; }

    public CffFont(byte[] data)
    {
        _d = Unwrap(data);
        if (_d.Length < 4 || _d[0] != 1) throw new InvalidOperationException("not CFF");
        int pos = _d[2];
        var names = ReadIndex(ref pos);
        var tops = ReadIndex(ref pos);
        var strs = ReadIndex(ref pos);
        var gs = ReadIndex(ref pos);
        if (tops.Length == 0) throw new InvalidOperationException("no top dict");
        _gsubrs = gs.Select(x => x.Start).ToArray();
        _gsubrOffsets = gs.Select(x => x.End).ToArray();
        _strings = strs.Select(x => Encoding.Latin1.GetString(_d, x.Start, x.End - x.Start)).ToArray();

        var top = ParseDict(tops[0].Start, tops[0].End);
        if (top.TryGetValue(1207, out var fm) && fm.Count == 6) _matrix = fm.ToArray();
        IsCid = top.ContainsKey(1230);
        var csOff = (int)top[17][0];
        var cs = ReadIndexAt(csOff);
        _charStrings = cs.Select(x => new[] { x.Start, x.End }).ToArray();
        var n = _charStrings.Length;

        _charset = ReadCharset(top.TryGetValue(15, out var cso) ? (int)cso[0] : 0, n);

        if (IsCid && top.TryGetValue(1236, out var fda))
        {
            var fds = ReadIndexAt((int)fda[0]);
            _privates = fds.Select(x => ReadPrivate(ParseDict(x.Start, x.End))).ToArray();
            if (top.TryGetValue(1237, out var fsel)) ReadFdSelect((int)fsel[0], n, out _fdSelect, out _fdSelectRanges);
        }
        else
        {
            _privates = [ReadPrivate(top)];
            if (!IsCid) ReadEncoding(top.TryGetValue(16, out var eo) ? (int)eo[0] : 0);
        }
    }

    static byte[] Unwrap(byte[] data)
    {
        if (data.Length > 12 && data[0] == 'O' && data[1] == 'T' && data[2] == 'T' && data[3] == 'O')
        {
            int num = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
            for (var i = 0; i < num; i++)
            {
                var r = 12 + i * 16;
                if (r + 16 > data.Length) break;
                if (data[r] == 'C' && data[r + 1] == 'F' && data[r + 2] == 'F' && data[r + 3] == ' ')
                {
                    int off = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(r + 8)), len = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(r + 12));
                    return data.AsSpan(off, Math.Min(len, data.Length - off)).ToArray();
                }
            }
            throw new InvalidOperationException("OpenType without CFF table");
        }
        return data;
    }

    // ------------------------------------------------------------ structures

    (int Start, int End)[] ReadIndex(ref int pos)
    {
        var r = ReadIndexAt(pos, out var end);
        pos = end;
        return r;
    }

    (int Start, int End)[] ReadIndexAt(int pos) => ReadIndexAt(pos, out _);

    (int Start, int End)[] ReadIndexAt(int pos, out int end)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(_d.AsSpan(pos));
        if (count == 0) { end = pos + 2; return []; }
        int offSize = _d[pos + 2];
        var offs = pos + 3;
        var dataStart = offs + (count + 1) * offSize - 1;
        int Off(int i) { var v = 0; for (var k = 0; k < offSize; k++) v = v << 8 | _d[offs + i * offSize + k]; return v; }
        var res = new (int, int)[count];
        for (var i = 0; i < count; i++) res[i] = (dataStart + Off(i), dataStart + Off(i + 1));
        end = dataStart + Off(count);
        return res;
    }

    // Keys: operator, or 1200+b for two-byte (12 b) operators.
    Dictionary<int, List<double>> ParseDict(int start, int end)
    {
        var res = new Dictionary<int, List<double>>();
        var ops = new List<double>();
        var p = start;
        while (p < end)
        {
            int b = _d[p++];
            if (b <= 21)
            {
                var key = b;
                if (b == 12) key = 1200 + _d[p++];
                res[key] = ops; ops = [];
            }
            else if (b == 28) { ops.Add((short)(_d[p] << 8 | _d[p + 1])); p += 2; }
            else if (b == 29) { ops.Add(BinaryPrimitives.ReadInt32BigEndian(_d.AsSpan(p))); p += 4; }
            else if (b == 30)
            {
                var sb = new StringBuilder();
                var done = false;
                while (!done && p < end)
                {
                    var v = _d[p++];
                    foreach (var nib in new[] { v >> 4, v & 15 })
                    {
                        if (nib == 15) { done = true; break; }
                        if (nib <= 9) sb.Append((char)('0' + nib));
                        else if (nib == 10) sb.Append('.');
                        else if (nib == 11) sb.Append('E');
                        else if (nib == 12) sb.Append("E-");
                        else if (nib == 14) sb.Append('-');
                    }
                }
                ops.Add(double.TryParse(sb.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : 0);
            }
            else if (b >= 32 && b <= 246) ops.Add(b - 139);
            else if (b >= 247 && b <= 250) ops.Add((b - 247) * 256 + _d[p++] + 108);
            else if (b >= 251 && b <= 254) ops.Add(-(b - 251) * 256 - _d[p++] - 108);
        }
        return res;
    }

    Private ReadPrivate(Dictionary<int, List<double>> dict)
    {
        var pr = new Private();
        if (!dict.TryGetValue(18, out var pv) || pv.Count < 2) return pr;
        int size = (int)pv[0], off = (int)pv[1];
        var pd = ParseDict(off, off + size);
        if (pd.TryGetValue(20, out var dw) && dw.Count > 0) pr.DefaultWidth = dw[0];
        if (pd.TryGetValue(21, out var nw) && nw.Count > 0) pr.NominalWidth = nw[0];
        if (pd.TryGetValue(19, out var so) && so.Count > 0)
        {
            var subrs = ReadIndexAt(off + (int)so[0]);
            pr.SubrStart = subrs.Select(x => x.Start).ToArray();
            pr.SubrEnd = subrs.Select(x => x.End).ToArray();
        }
        return pr;
    }

    int[] ReadCharset(int off, int n)
    {
        var cs = new int[n];
        if (off is >= 0 and <= 2)
        {
            var predefined = off switch
            {
                0 => CffStandardStrings.ISOAdobeCharset,
                1 => CffStandardStrings.ExpertCharset,
                _ => CffStandardStrings.ExpertSubsetCharset,
            };
            Array.Copy(predefined, cs, Math.Min(n, predefined.Length));
            return cs;
        }
        var p = off;
        var fmt = _d[p++];
        var g = 1;
        if (fmt == 0) for (; g < n && p + 1 < _d.Length; g++, p += 2) cs[g] = _d[p] << 8 | _d[p + 1];
        else
        {
            while (g < n && p + 2 < _d.Length)
            {
                int first = _d[p] << 8 | _d[p + 1]; p += 2;
                int left;
                if (fmt == 1) left = _d[p++]; else { left = _d[p] << 8 | _d[p + 1]; p += 2; }
                for (var k = 0; k <= left && g < n; k++) cs[g++] = first + k;
            }
        }
        return cs;
    }

    void ReadFdSelect(int off, int n, out byte[]? sel, out int[]? ranges)
    {
        sel = null; ranges = null;
        var fmt = _d[off];
        if (fmt == 0) { sel = _d.AsSpan(off + 1, n).ToArray(); return; }
        int nr = _d[off + 1] << 8 | _d[off + 2];
        var s = new byte[n];
        var p = off + 3;
        for (var i = 0; i < nr; i++)
        {
            int first = _d[p] << 8 | _d[p + 1]; var fd = _d[p + 2];
            int next = _d[p + 3] << 8 | _d[p + 4];
            for (var g = first; g < next && g < n; g++) s[g] = fd;
            p += 3;
        }
        sel = s;
    }

    void ReadEncoding(int off)
    {
        if (off == 0)
        {
            for (var c = 0; c < 256; c++) _encoding[c] = StdGlyph(c);
            return;
        }
        if (off == 1)
        {
            for (var c = 0; c < 256; c++)
                _encoding[c] = GlyphByName(CffStandardStrings.Strings[CffStandardStrings.ExpertEncoding[c]]);
            return;
        }
        var p = off;
        var fmt = _d[p++] & 0x7F;
        if (fmt == 0)
        {
            int nc = _d[p++];
            for (var i = 0; i < nc; i++) _encoding[_d[p++]] = i + 1;
        }
        else if (fmt == 1)
        {
            int nr = _d[p++]; var g = 1;
            for (var i = 0; i < nr; i++)
            {
                int first = _d[p++], left = _d[p++];
                for (var k = 0; k <= left; k++) { if (first + k < 256) _encoding[first + k] = g; g++; }
            }
        }
    }

    // ------------------------------------------------------------ lookups

    public string GlyphName(int gid)
    {
        if (gid < 0 || gid >= _charset.Length || IsCid) return "";
        var sid = _charset[gid];
        if (sid < CffStandardStrings.Strings.Length) return CffStandardStrings.Strings[sid];
        var i = sid - 391;
        return i < _strings.Length ? _strings[i]! : "";
    }

    internal string GlyphNameByCode(int code) => GlyphName(GlyphByCode(code)) is { Length: > 0 } n ? n : ".notdef";

    public int GlyphByName(string name)
    {
        if (_byName is null)
        {
            _byName = new Dictionary<string, int>();
            for (var g = _charset.Length - 1; g >= 0; g--) _byName[GlyphName(g)] = g;
        }
        return _byName.GetValueOrDefault(name, 0);
    }

    public int GlyphByCid(int cid)
    {
        if (!IsCid) return cid < GlyphCount ? cid : 0;
        if (_byCid is null)
        {
            _byCid = new Dictionary<int, int>();
            for (var g = _charset.Length - 1; g >= 0; g--) _byCid[_charset[g]] = g;
        }
        return _byCid.GetValueOrDefault(cid, 0);
    }

    /// <summary>Glyph from the font's built-in encoding (custom encodings only).</summary>
    public int GlyphByCode(int code) => code is >= 0 and < 256 ? _encoding[code] : 0;

    // ------------------------------------------------------------ charstrings

    public double Advance(int gid) => Run(gid, null);

    public PathData? GetOutline(int gid)
    {
        if (gid < 0 || gid >= _charStrings.Length) return null;
        var p = new PathData();
        Run(gid, p);
        return p.Ops.Count == 0 ? null : p;
    }

    int StdGlyph(int code) => code is >= 0 and < 256 ? GlyphByName(CffStandardStrings.StandardEncoding[code]) : 0;

    Private PrivateFor(int gid) => _fdSelect is not null && _fdSelect[gid] < _privates.Length ? _privates[_fdSelect[gid]] : _privates[0];

    /// <summary>Executes a charstring. Returns the advance width in 1/1000 em; fills <paramref name="path"/> when non-null.</summary>
    double Run(int gid, PathData? path, double ox = 0, double oy = 0, int seacDepth = 0)
    {
        var pr = PrivateFor(gid);
        var st = new double[96];
        var sp = 0;
        double x = ox, y = oy;
        var trans = new double[32];
        var nStems = 0;
        double width = pr.DefaultWidth;
        var haveWidth = false;
        var open = false;
        var done = false;
        var scale = _matrix[0] * 1000;

        void Width(bool extra)
        {
            if (haveWidth) return;
            haveWidth = true;
            if (extra) { width = pr.NominalWidth + st[0]; Array.Copy(st, 1, st, 0, --sp); }
        }
        void MoveTo(double dx, double dy)
        {
            if (open) path?.Close();
            x += dx; y += dy; open = true;
            path?.MoveTo(x * scale, y * scale);
        }
        void LineTo(double dx, double dy) { x += dx; y += dy; path?.LineTo(x * scale, y * scale); }
        void Curve(double a, double b, double c, double d, double e, double f)
        {
            var x1 = x + a; var y1 = y + b; var x2 = x1 + c; var y2 = y1 + d; x = x2 + e; y = y2 + f;
            path?.CurveTo(x1 * scale, y1 * scale, x2 * scale, y2 * scale, x * scale, y * scale);
        }

        void Exec(int start, int end, int depth)
        {
            var p = start;
            while (p < end && !done)
            {
                int b = _d[p++];
                if (b >= 32 || b == 28)
                {
                    double v;
                    if (b == 28) { v = (short)(_d[p] << 8 | _d[p + 1]); p += 2; }
                    else if (b <= 246) v = b - 139;
                    else if (b <= 250) v = (b - 247) * 256 + _d[p++] + 108;
                    else if (b <= 254) v = -(b - 251) * 256 - _d[p++] - 108;
                    else { v = BinaryPrimitives.ReadInt32BigEndian(_d.AsSpan(p)) / 65536.0; p += 4; }
                    if (sp < st.Length) st[sp++] = v;
                    continue;
                }
                switch (b)
                {
                    case 1: case 3: case 18: case 23:
                        Width(sp % 2 == 1); nStems += sp / 2; sp = 0; break;
                    case 19: case 20:
                        Width(sp % 2 == 1); nStems += sp / 2; sp = 0; p += (nStems + 7) / 8; break;
                    case 21: Width(sp > 2); if (sp >= 2) MoveTo(st[sp - 2], st[sp - 1]); sp = 0; break;
                    case 22: Width(sp > 1); if (sp >= 1) MoveTo(st[sp - 1], 0); sp = 0; break;
                    case 4: Width(sp > 1); if (sp >= 1) MoveTo(0, st[sp - 1]); sp = 0; break;
                    case 5: for (var i = 0; i + 1 < sp; i += 2) LineTo(st[i], st[i + 1]); sp = 0; break;
                    case 6: case 7:
                    {
                        var horiz = b == 6;
                        for (var i = 0; i < sp; i++) { if (horiz) LineTo(st[i], 0); else LineTo(0, st[i]); horiz = !horiz; }
                        sp = 0; break;
                    }
                    case 8: for (var i = 0; i + 5 < sp; i += 6) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]); sp = 0; break;
                    case 24:
                    {
                        var i = 0;
                        for (; i + 7 < sp; i += 6) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                        if (i + 1 < sp) LineTo(st[i], st[i + 1]);
                        sp = 0; break;
                    }
                    case 25:
                    {
                        var i = 0;
                        for (; i + 7 < sp; i += 2) LineTo(st[i], st[i + 1]);
                        if (i + 5 < sp) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                        sp = 0; break;
                    }
                    case 26:
                    {
                        var i = 0; double dx1 = 0;
                        if (sp % 4 == 1) dx1 = st[i++];
                        for (; i + 3 < sp; i += 4) { Curve(dx1, st[i], st[i + 1], st[i + 2], 0, st[i + 3]); dx1 = 0; }
                        sp = 0; break;
                    }
                    case 27:
                    {
                        var i = 0; double dy1 = 0;
                        if (sp % 4 == 1) dy1 = st[i++];
                        for (; i + 3 < sp; i += 4) { Curve(st[i], dy1, st[i + 1], st[i + 2], st[i + 3], 0); dy1 = 0; }
                        sp = 0; break;
                    }
                    case 30: case 31:
                    {
                        var horiz = b == 31;
                        var i = 0;
                        while (i + 3 < sp)
                        {
                            var last = sp - i == 5 ? st[i + 4] : 0;
                            if (horiz) Curve(st[i], 0, st[i + 1], st[i + 2], last, st[i + 3]);
                            else Curve(0, st[i], st[i + 1], st[i + 2], st[i + 3], last);
                            horiz = !horiz; i += 4;
                        }
                        sp = 0; break;
                    }
                    case 10:
                    case 29:
                    {
                        if (sp == 0 || depth > 10) break;
                        var idx = (int)st[--sp];
                        int[] ss, se;
                        if (b == 10) { ss = pr.SubrStart; se = pr.SubrEnd; } else { ss = _gsubrs; se = _gsubrOffsets; }
                        idx += ss.Length < 1240 ? 107 : ss.Length < 33900 ? 1131 : 32768;
                        if ((uint)idx < (uint)ss.Length) Exec(ss[idx], se[idx], depth + 1);
                        break;
                    }
                    case 11: return;
                    case 14:
                        Width(sp is 1 or 5);
                        if (sp >= 4 && seacDepth == 0 && !IsCid)
                        {
                            // Deprecated seac form: adx ady bchar achar compose two StandardEncoding glyphs.
                            double adx = st[sp - 4], ady = st[sp - 3];
                            int bchar = (int)st[sp - 2], achar = (int)st[sp - 1];
                            if (open) path?.Close();
                            open = false;
                            var bg = StdGlyph(bchar); var ag = StdGlyph(achar);
                            if (bg > 0) Run(bg, path, ox, oy, 1);
                            if (ag > 0) Run(ag, path, ox + adx, oy + ady, 1);
                        }
                        done = true; break;
                    case 12:
                    {
                        int b2 = _d[p++];
                        switch (b2)
                        {
                            case 34: if (sp >= 7) { Curve(st[0], 0, st[1], st[2], st[3], 0); Curve(st[4], 0, st[5], -st[2], st[6], 0); } sp = 0; break;
                            case 35: if (sp >= 12) { Curve(st[0], st[1], st[2], st[3], st[4], st[5]); Curve(st[6], st[7], st[8], st[9], st[10], st[11]); } sp = 0; break;
                            case 36: if (sp >= 9) { Curve(st[0], st[1], st[2], st[3], st[4], 0); Curve(st[5], 0, st[6], st[7], st[8], -(st[1] + st[3] + st[7])); } sp = 0; break;
                            case 37:
                                if (sp >= 11)
                                {
                                    double dx = st[0] + st[2] + st[4] + st[6] + st[8], dy = st[1] + st[3] + st[5] + st[7] + st[9];
                                    Curve(st[0], st[1], st[2], st[3], st[4], st[5]);
                                    if (Math.Abs(dx) > Math.Abs(dy)) Curve(st[6], st[7], st[8], st[9], st[10], -dy);
                                    else Curve(st[6], st[7], st[8], st[9], -dx, st[10]);
                                }
                                sp = 0; break;
                            case 3: if (sp >= 2) { st[sp - 2] = st[sp - 2] != 0 && st[sp - 1] != 0 ? 1 : 0; sp--; } break;
                            case 4: if (sp >= 2) { st[sp - 2] = st[sp - 2] != 0 || st[sp - 1] != 0 ? 1 : 0; sp--; } break;
                            case 5: if (sp >= 1) st[sp - 1] = st[sp - 1] == 0 ? 1 : 0; break;
                            case 9: if (sp >= 1) st[sp - 1] = Math.Abs(st[sp - 1]); break;
                            case 10: if (sp >= 2) { st[sp - 2] += st[sp - 1]; sp--; } break;
                            case 11: if (sp >= 2) { st[sp - 2] -= st[sp - 1]; sp--; } break;
                            case 12: if (sp >= 2) { st[sp - 2] = st[sp - 1] == 0 ? 0 : st[sp - 2] / st[sp - 1]; sp--; } break;
                            case 14: if (sp >= 1) st[sp - 1] = -st[sp - 1]; break;
                            case 15: if (sp >= 2) { st[sp - 2] = st[sp - 2] == st[sp - 1] ? 1 : 0; sp--; } break;
                            case 18: if (sp >= 1) sp--; break;
                            case 20: if (sp >= 2) { var ti = (int)st[sp - 1]; if ((uint)ti < (uint)trans.Length) trans[ti] = st[sp - 2]; sp -= 2; } break;
                            case 21: if (sp >= 1) { var ti = (int)st[sp - 1]; st[sp - 1] = (uint)ti < (uint)trans.Length ? trans[ti] : 0; } break;
                            case 22: if (sp >= 4) { st[sp - 4] = st[sp - 2] <= st[sp - 1] ? st[sp - 4] : st[sp - 3]; sp -= 3; } break;
                            case 23: if (sp < st.Length) st[sp++] = 0.5; break;
                            case 24: if (sp >= 2) { st[sp - 2] *= st[sp - 1]; sp--; } break;
                            case 26: if (sp >= 1) st[sp - 1] = Math.Sqrt(Math.Max(0, st[sp - 1])); break;
                            case 27: if (sp >= 1 && sp < st.Length) { st[sp] = st[sp - 1]; sp++; } break;
                            case 28: if (sp >= 2) (st[sp - 1], st[sp - 2]) = (st[sp - 2], st[sp - 1]); break;
                            case 29:
                                if (sp >= 1)
                                {
                                    var ix = (int)st[sp - 1]; if (ix < 0) ix = 0;
                                    if (sp - 2 - ix >= 0) st[sp - 1] = st[sp - 2 - ix];
                                }
                                break;
                            case 30:
                                if (sp >= 2)
                                {
                                    int j = (int)st[sp - 1], n = (int)st[sp - 2];
                                    sp -= 2;
                                    if (n > 0 && n <= sp)
                                    {
                                        var seg = st.AsSpan(sp - n, n);
                                        j = (j % n + n) % n;
                                        var tmp = seg.ToArray();
                                        for (var q = 0; q < n; q++) seg[(q + j) % n] = tmp[q];
                                    }
                                }
                                break;
                            default: sp = 0; break;
                        }
                        break;
                    }
                    default: sp = 0; break;
                }
            }
        }

        try { Exec(_charStrings[gid][0], _charStrings[gid][1], 0); }
        catch (IndexOutOfRangeException) { }
        if (open) path?.Close();
        return width * scale;
    }
}
