using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Content;

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
    double[]? _weights;
    string[] _axisTypes = [];
    (double Design, double Blend)[][] _designMap = [];
    double[][] _designPositions = [];
    public bool HasBuiltInEncoding { get; private set; }

    const int MaxMasters = 16, MaxAxes = 4, MaxMapPoints = 12, MaxKeywordTokens = 2048;
    // OtherSubr 18 with 16 masters takes 6 * 16 operands plus the count and entry number.
    const int StackSize = 6 * MaxMasters + 32;

    /// <summary>True when the font dictionary carries a valid /WeightVector (Adobe TN #5015 section 3.2: 2 to 16 masters summing to 1.0).</summary>
    public bool IsMultipleMaster => _weights is not null;
    public int MasterCount => _weights?.Length ?? 0;
    /// <summary>The instance's blend weights, used by charstring OtherSubrs 14-18; empty for non-MM fonts.</summary>
    public IReadOnlyList<double> WeightVector => _weights ?? [];
    /// <summary>/BlendAxisTypes names (Weight, Width, OpticalSize...) for up to four axes.</summary>
    public IReadOnlyList<string> BlendAxisTypes => _axisTypes;
    /// <summary>/BlendDesignPositions: one blend-space location (0..1 per axis) per master.</summary>
    public IReadOnlyList<IReadOnlyList<double>> BlendDesignPositions => _designPositions;
    /// <summary>/BlendDesignMap: per axis, the piecewise-linear design-to-blend coordinate points.</summary>
    public IReadOnlyList<IReadOnlyList<(double Design, double Blend)>> BlendDesignMap => _designMap;

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
        ParseBlend(d);
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
        if (first == "ExpertEncoding")
        {
            HasBuiltInEncoding = true;
            for (var i = 0; i < 256; i++) _encoding[i] = CffStandardStrings.Strings[CffStandardStrings.ExpertEncoding[i]] is { } n && n != ".notdef" ? n : null;
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

    // Adobe TN #5015 section 3.3/3.5: WeightVector is in the font dictionary, the Blend* keywords in the FontInfo dictionary;
    // both are clear text. Every array is size- and token-bounded; invalid values leave the font as a non-MM (default master) font.
    void ParseBlend(byte[] d)
    {
        if (ReadKeyword(d, "/WeightVector") is { } wv && wv.Count is >= 2 and <= MaxMasters && Numbers(wv) is { } w && Math.Abs(w.Sum() - 1) <= 0.001)
            _weights = w;
        if (ReadKeyword(d, "/BlendAxisTypes") is { } at && at.Count is >= 1 and <= MaxAxes && at.All(o => o is string))
            _axisTypes = [.. at.Cast<string>()];
        if (ReadKeyword(d, "/BlendDesignPositions") is { } dp && dp.Count is >= 2 and <= MaxMasters)
        {
            var rows = dp.Select(o => o is List<object> r && r.Count is >= 1 and <= MaxAxes ? Numbers(r) : null).ToArray();
            if (rows.All(r => r is not null && r.Length == rows[0]!.Length)) _designPositions = [.. rows.Select(r => r!)];
        }
        if (ReadKeyword(d, "/BlendDesignMap") is { } dm && dm.Count is >= 1 and <= MaxAxes)
        {
            List<(double, double)[]>? axes = [];
            foreach (var axis in dm)
            {
                if (axis is not List<object> pts || pts.Count is < 2 or > MaxMapPoints) { axes = null; break; }
                List<(double, double)>? map = [];
                foreach (var pt in pts)
                {
                    if (pt is not List<object> pair || Numbers(pair) is not { Length: 2 } v || map.Count > 0 && v[0] <= map[^1].Item1) { map = null; break; }
                    map.Add((v[0], v[1]));
                }
                if (map is null) { axes = null; break; }
                axes.Add([.. map]);
            }
            if (axes is not null) _designMap = [.. axes];
        }
    }

    static double[]? Numbers(List<object> l)
    {
        var r = new double[l.Count];
        for (var i = 0; i < r.Length; i++)
        {
            if (l[i] is not double v || !double.IsFinite(v)) return null;
            r[i] = v;
        }
        return r;
    }

    static List<object>? ReadKeyword(byte[] d, string key)
    {
        for (var from = 0; (from = IndexOf(d, key, from)) >= 0; from += key.Length)
        {
            var p = from + key.Length;
            if (p < d.Length && !Space(d[p]) && !Delim(d[p])) continue;
            var budget = MaxKeywordTokens;
            if (ReadArray(d, ref p, 0, ref budget) is { } arr) return arr;
        }
        return null;
    }

    static List<object>? ReadArray(byte[] d, ref int p, int depth, ref int budget)
    {
        if (depth > 3 || Next(d, ref p) is not ("[" or "{")) return null;
        var items = new List<object>();
        while (budget-- > 0)
        {
            var t = Next(d, ref p);
            if (t is null) return null;
            if (t is "]" or "}") return items;
            if (t is "[" or "{")
            {
                p -= 1;
                if (ReadArray(d, ref p, depth + 1, ref budget) is not { } sub) return null;
                items.Add(sub);
            }
            else if (t[0] == '/') items.Add(t[1..]);
            else if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) items.Add(v);
            else return null;
        }
        return null;
    }

    /// <summary>Selects another instance by replacing the WeightVector (length must equal <see cref="MasterCount"/>, finite, summing to 1 within 0.001).</summary>
    public bool TrySetWeightVector(ReadOnlySpan<double> weights)
    {
        if (_weights is null || weights.Length != _weights.Length) return false;
        double sum = 0;
        foreach (var w in weights)
        {
            if (!double.IsFinite(w)) return false;
            sum += w;
        }
        if (Math.Abs(sum - 1) > 0.001) return false;
        _weights = weights.ToArray();
        _advance.Clear();
        return true;
    }

    /// <summary>
    /// Maps design coordinates (one per axis) through /BlendDesignMap to blend coordinates and then to a WeightVector by multilinear
    /// interpolation over /BlendDesignPositions. TN #5015 leaves ConvertDesignVector to the font's PostScript code; this reproduces its
    /// conventional form and only supports masters at 0/1 corners (no intermediate designs). Returns null when the data does not fit.
    /// </summary>
    public double[]? ConvertDesignVector(ReadOnlySpan<double> design)
    {
        var n = _designMap.Length;
        if (_weights is null || n == 0 || design.Length != n || _designPositions.Length != _weights.Length) return null;
        var blend = new double[n];
        for (var a = 0; a < n; a++)
        {
            var map = _designMap[a];
            var x = design[a];
            if (!double.IsFinite(x)) return null;
            if (x <= map[0].Design) blend[a] = map[0].Blend;
            else if (x >= map[^1].Design) blend[a] = map[^1].Blend;
            else
            {
                var i = 1;
                while (map[i].Design < x) i++;
                var (d0, b0) = map[i - 1]; var (d1, b1) = map[i];
                blend[a] = b0 + (x - d0) / (d1 - d0) * (b1 - b0);
            }
        }
        var w = new double[_weights.Length];
        for (var m = 0; m < w.Length; m++)
        {
            var pos = _designPositions[m];
            if (pos.Length != n) return null;
            double v = 1;
            for (var a = 0; a < n; a++)
            {
                if (pos[a] == 1) v *= blend[a];
                else if (pos[a] == 0) v *= 1 - blend[a];
                else return null;
            }
            w[m] = v;
        }
        return w;
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

    // TN #5015 section 3.12: the n = points * k arguments are the first master's values followed by each value's k-1 deltas
    // (the "a2-a1 ... ak-a1" runs); result = a1 + sum(W[i] * delta[i]) for i = 1..k-1. Results are queued so the first `pop` yields the first value.
    // Without a valid WeightVector, or when n is not points * k, the first master's values are used.
    void Blend(double[] st, int at, int n, int points, List<double> ps)
    {
        ps.Clear();
        if (n < points) return;
        var w = _weights;
        var blend = w is not null && n == points * w.Length;
        for (var i = points - 1; i >= 0; i--)
        {
            var v = st[at + i];
            if (blend)
                for (var m = 1; m < w!.Length; m++) v += w[m] * st[at + points + i * (w.Length - 1) + m - 1];
            ps.Add(v);
        }
    }

    double Run(string name, PathData? path, double ox, double oy, int depth)
    {
        var st = new double[StackSize];
        var overflow = false;
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
                    if (sp < st.Length) st[sp++] = v; else { overflow = true; done = true; }
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
                                else if (os is >= 14 and <= 18)
                                {
                                    Blend(st, sp - n, n, os == 18 ? 6 : os - 13, ps);
                                    sp -= n;
                                }
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
        if (overflow)
        {
            // An operand-stack overflow means the charstring is malformed (or needs more than 16 masters): drop the glyph rather than draw a wrong one.
            path?.Ops.Clear(); path?.Pts.Clear();
            return 0;
        }
        if (open) path?.Close();
        return wx * scale;
    }
}
