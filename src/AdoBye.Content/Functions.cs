using AdoBye.Core;

namespace AdoBye.Content;

/// <summary>PDF functions types 0, 2, 3, 4 (ISO 32000-2 §7.10).</summary>
public abstract class PdfFunction
{
    public double[] Domain = [0, 1];
    public double[]? Range;
    public int Outputs { get; protected set; }

    public abstract double[] Evaluate(double[] input);

    protected double[] ClipIn(double[] x)
    {
        var r = (double[])x.Clone();
        for (var i = 0; i < r.Length && 2 * i + 1 < Domain.Length; i++) r[i] = Math.Clamp(r[i], Domain[2 * i], Domain[2 * i + 1]);
        return r;
    }

    protected double[] ClipOut(double[] y)
    {
        if (Range is null) return y;
        for (var i = 0; i < y.Length && 2 * i + 1 < Range.Length; i++) y[i] = Math.Clamp(y[i], Range[2 * i], Range[2 * i + 1]);
        return y;
    }

    static double[] Nums(PdfFile f, PdfObject? o) => f.Resolve(o) is PdfArray a ? a.Items.Select(x => f.Resolve(x).AsNumber() ?? 0).ToArray() : [];

    public static PdfFunction? Parse(PdfFile f, PdfObject? obj)
    {
        obj = f.Resolve(obj);
        var d = obj as PdfDictionary ?? (obj as PdfStream)?.Dict;
        if (d is null) return null;
        var type = (int)(f.Resolve(d.Get("FunctionType")).AsNumber() ?? -1);
        PdfFunction? fn = type switch
        {
            0 => obj is PdfStream s ? SampledFunction.Create(f, s) : null,
            2 => new ExponentialFunction(Nums(f, d.Get("C0")) is { Length: > 0 } c0 ? c0 : [0], Nums(f, d.Get("C1")) is { Length: > 0 } c1 ? c1 : [1], f.Resolve(d.Get("N")).AsNumber() ?? 1),
            3 => StitchingFunction.Create(f, d),
            4 => obj is PdfStream ps ? new PostScriptFunction(f.DecodeStream(ps)) : null,
            _ => null
        };
        if (fn is null) return null;
        var dom = Nums(f, d.Get("Domain"));
        if (dom.Length >= 2) fn.Domain = dom;
        var rng = Nums(f, d.Get("Range"));
        if (rng.Length >= 2) fn.Range = rng;
        if (type is 0 or 4 && rng.Length >= 2) fn.Outputs = rng.Length / 2;
        return fn;
    }
}

sealed class ExponentialFunction : PdfFunction
{
    readonly double[] _c0, _c1; readonly double _n;
    public ExponentialFunction(double[] c0, double[] c1, double n) { _c0 = c0; _c1 = c1; _n = n; Outputs = c0.Length; }

    public override double[] Evaluate(double[] input)
    {
        var x = ClipIn(input)[0];
        var p = _n == 1 ? x : Math.Pow(x, _n);
        var r = new double[_c0.Length];
        for (var i = 0; i < r.Length; i++) r[i] = _c0[i] + p * ((i < _c1.Length ? _c1[i] : _c0[i]) - _c0[i]);
        return ClipOut(r);
    }
}

sealed class StitchingFunction : PdfFunction
{
    PdfFunction[] _fns = []; double[] _bounds = [], _encode = [];

    public static StitchingFunction? Create(PdfFile f, PdfDictionary d)
    {
        var fnsObj = f.Resolve(d.Get("Functions")) as PdfArray;
        if (fnsObj is null) return null;
        var fns = fnsObj.Items.Select(x => Parse(f, x)).ToArray();
        if (fns.Any(x => x is null)) return null;
        double[] N(string k) => f.Resolve(d.Get(k)) is PdfArray a ? a.Items.Select(x => f.Resolve(x).AsNumber() ?? 0).ToArray() : [];
        return new StitchingFunction { _fns = fns!, _bounds = N("Bounds"), _encode = N("Encode"), Outputs = fns[0]!.Outputs };
    }

    public override double[] Evaluate(double[] input)
    {
        var x = ClipIn(input)[0];
        var k = _fns.Length;
        var i = 0;
        while (i < k - 1 && x >= _bounds[i]) i++;
        var lo = i == 0 ? Domain[0] : _bounds[i - 1];
        var hi = i == k - 1 ? Domain[1] : _bounds[i];
        double e0 = _encode.Length > 2 * i + 1 ? _encode[2 * i] : 0, e1 = _encode.Length > 2 * i + 1 ? _encode[2 * i + 1] : 1;
        var t = hi == lo ? e0 : e0 + (x - lo) * (e1 - e0) / (hi - lo);
        return ClipOut(_fns[i].Evaluate([t]));
    }
}

sealed class SampledFunction : PdfFunction
{
    int[] _size = []; int _bps; double[] _encode = [], _decode = []; byte[] _data = []; int _m, _n;

    public static SampledFunction? Create(PdfFile f, PdfStream s)
    {
        double[] N(string k) => f.Resolve(s.Dict.Get(k)) is PdfArray a ? a.Items.Select(x => f.Resolve(x).AsNumber() ?? 0).ToArray() : [];
        var domain = N("Domain"); var range = N("Range"); var size = N("Size").Select(x => (int)x).ToArray();
        if (domain.Length < 2 || range.Length < 2 || size.Length == 0) return null;
        var fn = new SampledFunction
        {
            _m = domain.Length / 2, _n = range.Length / 2, _size = size,
            _bps = (int)(f.Resolve(s.Dict.Get("BitsPerSample")).AsNumber() ?? 8), _data = f.DecodeStream(s)
        };
        fn._encode = N("Encode") is { Length: > 0 } e ? e : size.SelectMany(x => new double[] { 0, x - 1 }).ToArray();
        fn._decode = N("Decode") is { Length: > 0 } dc ? dc : range;
        fn.Outputs = fn._n;
        return fn;
    }

    double Sample(int[] idx, int j)
    {
        long off = 0, stride = 1;
        for (var i = 0; i < _m; i++) { off += idx[i] * stride; stride *= _size[i]; }
        var bit = (off * _n + j) * _bps;
        long v = 0;
        for (var b = 0; b < _bps; b++)
        {
            var bi = bit + b; var by = (int)(bi >> 3);
            if (by >= _data.Length) break;
            v = (v << 1) | (long)((_data[by] >> (7 - (int)(bi & 7))) & 1);
        }
        return v / (Math.Pow(2, _bps) - 1);
    }

    public override double[] Evaluate(double[] input)
    {
        var x = ClipIn(input);
        var e = new double[_m];
        for (var i = 0; i < _m; i++)
        {
            double d0 = Domain[2 * i], d1 = Domain[2 * i + 1];
            var v = d1 == d0 ? _encode[2 * i] : _encode[2 * i] + (x[i] - d0) * (_encode[2 * i + 1] - _encode[2 * i]) / (d1 - d0);
            e[i] = Math.Clamp(v, 0, _size[i] - 1);
        }
        var res = new double[_n];
        // Multilinear interpolation over 2^m corners.
        for (var corner = 0; corner < (1 << _m); corner++)
        {
            double w = 1; var idx = new int[_m];
            for (var i = 0; i < _m; i++)
            {
                var fl = (int)Math.Floor(e[i]); var fr = e[i] - fl;
                if ((corner >> i & 1) == 1) { idx[i] = Math.Min(fl + 1, _size[i] - 1); w *= fr; }
                else { idx[i] = fl; w *= 1 - fr; }
            }
            if (w == 0) continue;
            for (var j = 0; j < _n; j++) res[j] += w * Sample(idx, j);
        }
        for (var j = 0; j < _n; j++) res[j] = _decode[2 * j] + res[j] * (_decode[2 * j + 1] - _decode[2 * j]);
        return ClipOut(res);
    }
}

sealed class PostScriptFunction : PdfFunction
{
    readonly List<object> _prog;
    public PostScriptFunction(byte[] code)
    {
        var tokens = System.Text.Encoding.Latin1.GetString(code).Replace("{", " { ").Replace("}", " } ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var i = 0;
        while (i < tokens.Length && tokens[i] != "{") i++;
        i++;
        _prog = Block(tokens, ref i);
    }

    static List<object> Block(string[] t, ref int i)
    {
        var l = new List<object>();
        while (i < t.Length)
        {
            var s = t[i++];
            if (s == "}") break;
            if (s == "{") l.Add(Block(t, ref i));
            else if (double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d)) l.Add(d);
            else l.Add(s);
        }
        return l;
    }

    public override double[] Evaluate(double[] input)
    {
        var st = new List<double>(ClipIn(input));
        try { Run(_prog, st); } catch (Exception e) when (e is InvalidOperationException or ArgumentOutOfRangeException) { }
        var n = Outputs > 0 ? Outputs : st.Count;
        var r = st.Count >= n ? st.GetRange(st.Count - n, n).ToArray() : [.. st, .. new double[n - st.Count]];
        return ClipOut(r);
    }

    static double Pop(List<double> s) { var v = s[^1]; s.RemoveAt(s.Count - 1); return v; }

    static void Run(List<object> prog, List<double> s)
    {
        for (var pc = 0; pc < prog.Count; pc++)
        {
            var o = prog[pc];
            if (o is double d) { s.Add(d); continue; }
            if (o is List<object> blk)
            {
                // Block followed by "if" or by another block and "ifelse".
                if (pc + 1 < prog.Count && prog[pc + 1] is "if") { pc++; if (Pop(s) != 0) Run(blk, s); }
                else if (pc + 2 < prog.Count && prog[pc + 1] is List<object> els && prog[pc + 2] is "ifelse") { pc += 2; if (Pop(s) != 0) Run(blk, s); else Run(els, s); }
                continue;
            }
            double a, b;
            switch ((string)o)
            {
                case "add": b = Pop(s); a = Pop(s); s.Add(a + b); break;
                case "sub": b = Pop(s); a = Pop(s); s.Add(a - b); break;
                case "mul": b = Pop(s); a = Pop(s); s.Add(a * b); break;
                case "div": b = Pop(s); a = Pop(s); s.Add(b == 0 ? 0 : a / b); break;
                case "idiv": b = Pop(s); a = Pop(s); s.Add(b == 0 ? 0 : Math.Truncate(a / b)); break;
                case "mod": b = Pop(s); a = Pop(s); s.Add(b == 0 ? 0 : a % b); break;
                case "neg": s.Add(-Pop(s)); break;
                case "abs": s.Add(Math.Abs(Pop(s))); break;
                case "ceiling": s.Add(Math.Ceiling(Pop(s))); break;
                case "floor": s.Add(Math.Floor(Pop(s))); break;
                case "round": s.Add(Math.Floor(Pop(s) + 0.5)); break;
                case "truncate": s.Add(Math.Truncate(Pop(s))); break;
                case "sqrt": s.Add(Math.Sqrt(Pop(s))); break;
                case "sin": s.Add(Math.Sin(Pop(s) * Math.PI / 180)); break;
                case "cos": s.Add(Math.Cos(Pop(s) * Math.PI / 180)); break;
                case "atan": b = Pop(s); a = Pop(s); var ang = Math.Atan2(a, b) * 180 / Math.PI; s.Add(ang < 0 ? ang + 360 : ang); break;
                case "exp": b = Pop(s); a = Pop(s); s.Add(Math.Pow(a, b)); break;
                case "ln": s.Add(Math.Log(Pop(s))); break;
                case "log": s.Add(Math.Log10(Pop(s))); break;
                case "cvi": s.Add(Math.Truncate(Pop(s))); break;
                case "cvr": break;
                case "eq": b = Pop(s); a = Pop(s); s.Add(a == b ? 1 : 0); break;
                case "ne": b = Pop(s); a = Pop(s); s.Add(a != b ? 1 : 0); break;
                case "gt": b = Pop(s); a = Pop(s); s.Add(a > b ? 1 : 0); break;
                case "ge": b = Pop(s); a = Pop(s); s.Add(a >= b ? 1 : 0); break;
                case "lt": b = Pop(s); a = Pop(s); s.Add(a < b ? 1 : 0); break;
                case "le": b = Pop(s); a = Pop(s); s.Add(a <= b ? 1 : 0); break;
                case "and": b = Pop(s); a = Pop(s); s.Add((long)a & (long)b); break;
                case "or": b = Pop(s); a = Pop(s); s.Add((long)a | (long)b); break;
                case "xor": b = Pop(s); a = Pop(s); s.Add((long)a ^ (long)b); break;
                case "not": a = Pop(s); s.Add(a is 0 or 1 ? 1 - a : ~(long)a); break;
                case "bitshift": b = Pop(s); a = Pop(s); s.Add(b >= 0 ? (long)a << (int)b : (long)a >> (int)-b); break;
                case "true": s.Add(1); break;
                case "false": s.Add(0); break;
                case "pop": Pop(s); break;
                case "exch": b = Pop(s); a = Pop(s); s.Add(b); s.Add(a); break;
                case "dup": s.Add(s[^1]); break;
                case "copy": { var n = (int)Pop(s); s.AddRange(s.GetRange(s.Count - n, n)); break; }
                case "index": { var n = (int)Pop(s); s.Add(s[s.Count - 1 - n]); break; }
                case "roll":
                    {
                        var j = (int)Pop(s); var n = (int)Pop(s);
                        if (n <= 0) break;
                        var seg = s.GetRange(s.Count - n, n); s.RemoveRange(s.Count - n, n);
                        j = ((j % n) + n) % n;
                        for (var i = 0; i < n; i++) s.Add(seg[(i - j + n) % n]);
                        break;
                    }
            }
        }
    }
}
