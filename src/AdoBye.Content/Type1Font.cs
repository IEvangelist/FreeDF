using System.Text;

namespace AdoBye.Content;

/// <summary>
/// Legacy Adobe Type 1 font program (PFA/PFB or the raw FontFile stream): eexec and charstring decryption, a Type 1 charstring
/// interpreter (hints skipped, flex, hint replacement, seac, div, sbw) and glyph outlines in 1/1000 em.
/// </summary>
public sealed class Type1Font
{
    readonly Dictionary<string, byte[]> _glyphs = [];
    readonly List<byte[]?> _subrs = [];
    readonly string?[] _encoding = new string?[256];
    readonly Dictionary<string, double> _advance = [];
    readonly double[] _matrix = [0.001, 0, 0, 0.001, 0, 0];
    int _lenIV = 4;
    public bool HasBuiltInEncoding { get; private set; }

    public int GlyphCount => _glyphs.Count;
    public IEnumerable<string> GlyphNames => _glyphs.Keys;
    public bool HasGlyph(string name) => _glyphs.ContainsKey(name);

    /// <summary>True when the data looks like a Type 1 program (%! header or PFB marker).</summary>
    public static bool Looks(byte[] d) => d.Length > 4 && (d[0] == 0x80 && d[1] is 1 or 2 || d[0] == '%' && d[1] == '!');

    public string? EncodingName(int code) => code is >= 0 and < 256 ? _encoding[code] : null;

    public Type1Font(byte[] data)
    {
        var (clear, cipher) = Split(data);
        ParseClear(clear);
        if (cipher.Length == 0) throw new InvalidOperationException("Type 1: no eexec section");
        var plain = Decrypt(cipher, 55665, 4);
        ParsePrivate(plain);
        if (_glyphs.Count == 0) throw new InvalidOperationException("Type 1: no CharStrings");
    }

    // ------------------------------------------------------------ container

    static (byte[] Clear, byte[] Cipher) Split(byte[] d)
    {
        if (d[0] == 0x80)
        {
            var clear = new MemoryStream(); var bin = new MemoryStream();
            var p = 0;
            while (p + 6 <= d.Length && d[p] == 0x80 && d[p + 1] is 1 or 2)
            {
                var len = BitConverter.ToInt32(d, p + 2);
                len = Math.Clamp(len, 0, d.Length - p - 6);
                (d[p + 1] == 1 ? (bin.Length == 0 ? clear : null) : bin)?.Write(d, p + 6, len);
                p += 6 + len;
            }
            return (clear.ToArray(), bin.ToArray());
        }
        var at = IndexOf(d, "eexec");
        if (at < 0) return (d, []);
        var s = at + 5;
        while (s < d.Length && d[s] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') s++;
        var cl = d.AsSpan(0, at + 5).ToArray();
        var rest = d.AsSpan(s);
        var isHex = rest.Length >= 4;
        for (var i = 0; i < 4 && isHex; i++) isHex = Uri.IsHexDigit((char)rest[i]);
        if (!isHex) return (cl, rest.ToArray());
        var outp = new List<byte>(rest.Length / 2);
        int hi = -1;
        foreach (var b in rest)
        {
            if (b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') continue;
            if (!Uri.IsHexDigit((char)b)) break;
            var v = Convert.ToInt32(((char)b).ToString(), 16);
            if (hi < 0) hi = v; else { outp.Add((byte)(hi << 4 | v)); hi = -1; }
        }
        return (cl, outp.ToArray());
    }

    static int IndexOf(byte[] d, string s, int from = 0)
    {
        var pat = Encoding.ASCII.GetBytes(s);
        return from >= d.Length ? -1 : d.AsSpan(from).IndexOf(pat) is var i and >= 0 ? i + from : -1;
    }

    static byte[] Decrypt(ReadOnlySpan<byte> c, int r, int skip)
    {
        var o = new byte[Math.Max(0, c.Length - skip)];
        for (var i = 0; i < c.Length; i++)
        {
            var b = c[i];
            var p = (byte)(b ^ r >> 8);
            r = (b + r) * 52845 + 22719 & 0xFFFF;
            if (i >= skip) o[i - skip] = p;
        }
        return o;
    }

    // ------------------------------------------------------------ tokenizer

    static bool Delim(byte b) => b is (byte)'/' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'%';
    static bool Space(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0;

    static string? Next(byte[] d, ref int p)
    {
        while (p < d.Length)
        {
            if (Space(d[p])) p++;
            else if (d[p] == '%') { while (p < d.Length && d[p] != '\n' && d[p] != '\r') p++; }
            else break;
        }
        if (p >= d.Length) return null;
        var s = p;
        if (d[p] == '/') { p++; while (p < d.Length && !Space(d[p]) && !Delim(d[p])) p++; }
        else if (Delim(d[p])) p++;
        else while (p < d.Length && !Space(d[p]) && !Delim(d[p])) p++;
        return Encoding.Latin1.GetString(d, s, p - s);
    }

    // ------------------------------------------------------------ parsing

    void ParseClear(byte[] d)
    {
        var fm = IndexOf(d, "/FontMatrix");
        if (fm >= 0)
        {
            var p = fm + 11; var vals = new List<double>();
            for (var t = Next(d, ref p); t is not null && vals.Count < 6; t = Next(d, ref p))
            {
                if (t is "[" or "{") continue;
                if (double.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var v)) vals.Add(v); else break;
            }
            if (vals.Count == 6 && vals[0] != 0) vals.CopyTo(_matrix);
        }
        var en = IndexOf(d, "/Encoding");
        if (en < 0) return;
        var q = en + 9;
        var first = Next(d, ref q);
        if (first == "StandardEncoding")
        {
            for (var i = 0; i < 256; i++) _encoding[i] = CffStandardStrings.StandardEncoding[i] is { } n && n != ".notdef" ? n : null;
            return;
        }
        HasBuiltInEncoding = true;
        while (true)
        {
            var t = Next(d, ref q);
            if (t is null || t == "def" || t == "readonly") break;
            if (t != "dup") continue;
            var cs = Next(d, ref q); var nm = Next(d, ref q);
            if (cs is null || nm is null || !nm.StartsWith('/')) continue;
            int code;
            var hash = cs.IndexOf('#');
            var ok = hash > 0 ? TryRadix(cs, hash, out code) : int.TryParse(cs, out code);
            if (ok && code is >= 0 and < 256) _encoding[code] = nm[1..];
        }
    }

    static bool TryRadix(string s, int hash, out int code)
    {
        code = 0;
        if (hash <= 0 || !int.TryParse(s[..hash], out var radix) || radix is < 2 or > 36) return false;
        foreach (var ch in s[(hash + 1)..])
        {
            var v = char.IsDigit(ch) ? ch - '0' : char.ToLowerInvariant(ch) - 'a' + 10;
            if (v < 0 || v >= radix) return false;
            code = code * radix + v;
        }
        return true;
    }

    void ParsePrivate(byte[] d)
    {
        var li = IndexOf(d, "/lenIV");
        if (li >= 0) { var p = li + 6; if (int.TryParse(Next(d, ref p), out var v) && v >= 0) _lenIV = v; }

        var sb = IndexOf(d, "/Subrs");
        if (sb >= 0)
        {
            var p = sb + 6;
            Next(d, ref p); // count
            while (true)
            {
                var t = Next(d, ref p);
                if (t is null || t is "ND" or "|-" or "def" || t == "/CharStrings") break;
                if (t != "dup") continue;
                if (!int.TryParse(Next(d, ref p), out var idx) || !int.TryParse(Next(d, ref p), out var len)) break;
                Next(d, ref p);
                p++;
                if (len < 0 || p + len > d.Length || idx is < 0 or > 65535) break;
                while (_subrs.Count <= idx) _subrs.Add(null);
                _subrs[idx] = Decrypt(d.AsSpan(p, len), 4330, _lenIV);
                p += len;
            }
        }

        var cp = IndexOf(d, "/CharStrings");
        if (cp < 0) return;
        var q = cp + 12;
        while (true)
        {
            var t = Next(d, ref q);
            if (t is null || t == "end") break;
            if (!t.StartsWith('/') || t.Length < 2) continue;
            if (!int.TryParse(Next(d, ref q), out var len)) continue;
            Next(d, ref q);
            q++;
            if (len < 0 || q + len > d.Length) break;
            _glyphs[t[1..]] = Decrypt(d.AsSpan(q, len), 4330, _lenIV);
            q += len;
        }
    }

    // ------------------------------------------------------------ outlines

    /// <summary>Advance width in 1/1000 em, or 0 when the glyph is missing.</summary>
    public double Advance(string name)
    {
        if (_advance.TryGetValue(name, out var w)) return w;
        if (!_glyphs.ContainsKey(name)) return 0;
        return _advance[name] = Run(name, null, 0, 0, 0);
    }

    public PathData? GetOutline(string name)
    {
        if (!_glyphs.ContainsKey(name)) return null;
        var p = new PathData();
        _advance[name] = Run(name, p, 0, 0, 0);
        return p.Ops.Count == 0 ? null : p;
    }

    double Run(string name, PathData? path, double ox, double oy, int depth)
    {
        var st = new double[64];
        var sp = 0;
        var ps = new List<double>();
        double x = ox, y = oy, sbx = 0, wx = 0;
        var scale = _matrix[0] * 1000;
        var flex = false;
        var flexPts = new List<(double X, double Y)>();
        var open = false;
        var done = false;

        void MoveTo(double dx, double dy)
        {
            x += dx; y += dy;
            if (flex) { flexPts.Add((x, y)); return; }
            if (open) path?.Close();
            open = true;
            path?.MoveTo(x * scale, y * scale);
        }
        void LineTo(double dx, double dy)
        {
            if (!open) { open = true; path?.MoveTo(x * scale, y * scale); }
            x += dx; y += dy; path?.LineTo(x * scale, y * scale);
        }
        void Curve(double a, double b, double c, double d, double e, double f)
        {
            if (!open) { open = true; path?.MoveTo(x * scale, y * scale); }
            var x1 = x + a; var y1 = y + b; var x2 = x1 + c; var y2 = y1 + d; x = x2 + e; y = y2 + f;
            path?.CurveTo(x1 * scale, y1 * scale, x2 * scale, y2 * scale, x * scale, y * scale);
        }

        void Exec(byte[] cs, int level)
        {
            var p = 0;
            while (p < cs.Length && !done)
            {
                int b = cs[p++];
                if (b >= 32)
                {
                    double v;
                    if (b <= 246) v = b - 139;
                    else if (b <= 250) v = (b - 247) * 256 + cs[p++] + 108;
                    else if (b <= 254) v = -(b - 251) * 256 - cs[p++] - 108;
                    else { v = cs[p] << 24 | cs[p + 1] << 16 | cs[p + 2] << 8 | cs[p + 3]; p += 4; }
                    if (sp < st.Length) st[sp++] = v;
                    continue;
                }
                switch (b)
                {
                    case 1: case 3: sp = 0; break;
                    case 4: if (sp >= 1) MoveTo(0, st[sp - 1]); sp = 0; break;
                    case 5: if (sp >= 2) LineTo(st[0], st[1]); sp = 0; break;
                    case 6: if (sp >= 1) LineTo(st[0], 0); sp = 0; break;
                    case 7: if (sp >= 1) LineTo(0, st[0]); sp = 0; break;
                    case 8: if (sp >= 6) Curve(st[0], st[1], st[2], st[3], st[4], st[5]); sp = 0; break;
                    case 9: if (open) { path?.Close(); open = false; } sp = 0; break;
                    case 10:
                    {
                        if (sp == 0) break;
                        var idx = (int)st[--sp];
                        if (level < 12 && (uint)idx < (uint)_subrs.Count && _subrs[idx] is { } sub) Exec(sub, level + 1);
                        break;
                    }
                    case 11: return;
                    case 13: if (sp >= 2) { sbx = st[0]; wx = st[1]; x = ox + sbx; y = oy; } sp = 0; break;
                    case 14: done = true; break;
                    case 21: if (sp >= 2) MoveTo(st[sp - 2], st[sp - 1]); sp = 0; break;
                    case 22: if (sp >= 1) MoveTo(st[sp - 1], 0); sp = 0; break;
                    case 30: if (sp >= 4) Curve(0, st[0], st[1], st[2], st[3], 0); sp = 0; break;
                    case 31: if (sp >= 4) Curve(st[0], 0, st[1], st[2], 0, st[3]); sp = 0; break;
                    case 12:
                    {
                        int b2 = cs[p++];
                        switch (b2)
                        {
                            case 0: case 1: case 2: sp = 0; break;
                            case 6:
                                if (sp >= 5 && depth == 0)
                                {
                                    double asb = st[0], adx = st[1], ady = st[2];
                                    int bchar = (int)st[3], achar = (int)st[4];
                                    if (open) { path?.Close(); open = false; }
                                    var bn = bchar is >= 0 and < 256 ? CffStandardStrings.StandardEncoding[bchar] : ".notdef";
                                    var an = achar is >= 0 and < 256 ? CffStandardStrings.StandardEncoding[achar] : ".notdef";
                                    if (_glyphs.ContainsKey(bn)) Run(bn, path, ox, oy, 1);
                                    if (_glyphs.ContainsKey(an)) Run(an, path, ox + sbx - asb + adx, oy + ady, 1);
                                }
                                sp = 0; done = true; break;
                            case 7: if (sp >= 4) { sbx = st[0]; wx = st[2]; x = ox + sbx; y = oy + st[1]; } sp = 0; break;
                            case 12: if (sp >= 2) { st[sp - 2] = st[sp - 1] == 0 ? 0 : st[sp - 2] / st[sp - 1]; sp--; } break;
                            case 16:
                            {
                                if (sp < 2) { sp = 0; break; }
                                var os = (int)st[--sp]; var n = Math.Clamp((int)st[--sp], 0, sp);
                                if (os == 1) { flex = true; flexPts.Clear(); sp -= n; }
                                else if (os == 2) sp -= n;
                                else if (os == 0)
                                {
                                    flex = false;
                                    if (flexPts.Count >= 7)
                                    {
                                        var f = flexPts;
                                        if (!open) { open = true; path?.MoveTo(f[0].X * scale, f[0].Y * scale); }
                                        path?.CurveTo(f[1].X * scale, f[1].Y * scale, f[2].X * scale, f[2].Y * scale, f[3].X * scale, f[3].Y * scale);
                                        path?.CurveTo(f[4].X * scale, f[4].Y * scale, f[5].X * scale, f[5].Y * scale, f[6].X * scale, f[6].Y * scale);
                                        x = f[6].X; y = f[6].Y;
                                    }
                                    sp -= n;
                                    ps.Clear(); ps.Add(y); ps.Add(x); // popped back as x then y
                                }
                                else
                                {
                                    ps.Clear();
                                    for (var i = 0; i < n; i++) ps.Add(st[sp - n + i]);
                                    sp -= n;
                                }
                                break;
                            }
                            case 17: if (ps.Count > 0 && sp < st.Length) { st[sp++] = ps[^1]; ps.RemoveAt(ps.Count - 1); } break;
                            case 33: sp = 0; break;
                            default: sp = 0; break;
                        }
                        break;
                    }
                    default: sp = 0; break;
                }
            }
        }

        try { Exec(_glyphs[name], 0); }
        catch (IndexOutOfRangeException) { }
        if (open) path?.Close();
        return wx * scale;
    }
}
