using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>PDF functions types 0, 2, 3, 4 (ISO 32000-2 §7.10).</summary>
public abstract class PdfFunction
{
    public double[] Domain = [0, 1];
    public double[]? Range;
    public int Outputs { get; protected set; }

    public abstract double[] Evaluate(double[] input);

    protected double[] ClipIn(double[] x)
    {
        // A short input is completed with the lower domain bound; extra input components are ignored.
        var m = Math.Max(1, Domain.Length / 2);
        var r = new double[Math.Max(m, 0)];
        for (var i = 0; i < r.Length; i++)
        {
            var v = i < x.Length ? x[i] : Domain[2 * i];
            r[i] = double.IsNaN(v) ? Domain[2 * i] : Math.Clamp(v, Math.Min(Domain[2 * i], Domain[2 * i + 1]), Math.Max(Domain[2 * i], Domain[2 * i + 1]));
        }
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
        // A fractional exponent of a negative value (or 0 to a negative power) is undefined; treat as 0.
        var p = _n == 1 ? x : Math.Pow(x, _n);
        if (!double.IsFinite(p)) p = 0;
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
        if (fns.Length == 0 || fns.Any(x => x is null)) return null;
        double[] N(string k) => f.Resolve(d.Get(k)) is PdfArray a ? a.Items.Select(x => f.Resolve(x).AsNumber() ?? 0).ToArray() : [];
        return new StitchingFunction { _fns = fns!, _bounds = N("Bounds"), _encode = N("Encode"), Outputs = fns[0]!.Outputs };
    }

    public override double[] Evaluate(double[] input)
    {
        var x = ClipIn(input)[0];
        var k = _fns.Length;
        // Subdomain i is [Bounds[i-1], Bounds[i]); the last one is closed at Domain1. A missing bound is Domain1.
        double Bound(int j) => j < _bounds.Length ? _bounds[j] : Domain[1];
        var i = 0;
        while (i < k - 1 && x >= Bound(i)) i++;
        var lo = i == 0 ? Domain[0] : Bound(i - 1);
        var hi = i == k - 1 ? Domain[1] : Bound(i);
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
        if (domain.Length < 2 || range.Length < 2 || size.Length == 0 || size.Length < domain.Length / 2 || size.Any(x => x < 1)) return null;
        var bps = (int)(f.Resolve(s.Dict.Get("BitsPerSample")).AsNumber() ?? 8);
        if (bps is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32)) return null;
        var fn = new SampledFunction
        {
            _m = domain.Length / 2, _n = range.Length / 2, _size = size,
            _bps = bps, _data = f.DecodeStream(s)
        };
        fn._encode = N("Encode") is { Length: > 0 } e && e.Length >= domain.Length ? e : size.Take(fn._m).SelectMany(x => new double[] { 0, x - 1 }).ToArray();
        fn._decode = N("Decode") is { Length: > 0 } dc && dc.Length >= range.Length ? dc : range;
        fn._maxSample = Math.Pow(2, bps) - 1;
        fn.Outputs = fn._n;
        return fn;
    }

    double _maxSample;

    double Sample(int[] idx, int j)
    {
        long off = 0, stride = 1;
        for (var i = 0; i < _m; i++) { off += idx[i] * stride; stride *= _size[i]; }
        var bit = (off * _n + j) * _bps;
        long v = 0;
        if ((bit & 7) == 0 && _bps >= 8)
        {
            var by = (int)(bit >> 3);
            for (var b = 0; b < _bps / 8; b++) v = (v << 8) | (by + b < _data.Length ? (long)_data[by + b] : 0L);
        }
        else
            for (var b = 0; b < _bps; b++)
            {
                var bi = bit + b; var by = (int)(bi >> 3);
                if (by >= _data.Length) break;
                v = (v << 1) | (long)((_data[by] >> (7 - (int)(bi & 7))) & 1);
            }
        return v / _maxSample;
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
        // Multilinear interpolation (7.10.2): only dimensions with a fractional part contribute a second corner.
        var idx = new int[_m]; var frac = new double[_m]; var active = new int[_m]; var k = 0;
        for (var i = 0; i < _m; i++)
        {
            idx[i] = (int)Math.Floor(e[i]); frac[i] = e[i] - idx[i];
            if (frac[i] > 0 && idx[i] + 1 < _size[i]) active[k++] = i;
        }
        for (var corner = 0; corner < (1 << k); corner++)
        {
            double w = 1;
            for (var a = 0; a < k; a++)
            {
                var d = active[a];
                if ((corner >> a & 1) == 1) { idx[d]++; w *= frac[d]; }
                else w *= 1 - frac[d];
            }
            for (var j = 0; j < _n; j++) res[j] += w * Sample(idx, j);
            for (var a = 0; a < k; a++) if ((corner >> a & 1) == 1) idx[active[a]]--;
        }
        for (var j = 0; j < _n; j++) res[j] = _decode[2 * j] + res[j] * (_decode[2 * j + 1] - _decode[2 * j]);
        return ClipOut(res);
    }
}

