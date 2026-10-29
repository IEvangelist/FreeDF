using System.Buffers.Binary;
using AdoBye.Core;

namespace AdoBye.Content;

/// <summary>
/// ICC profile reader/evaluator (ICC.1 v2/v4) converting device values to sRGB. Supports matrix/TRC RGB, gray TRC, and
/// LUT profiles (lut8, lut16, lutAToB) for RGB/CMYK via the A2B0/A2B1 tag. PCS is D50 XYZ or Lab; output is sRGB (Bradford D50 → D65).
/// Evaluation is allocation-free; CMYK/RGB LUT profiles bake a 17-point grid after enough calls to amortise the cost.
/// </summary>
public sealed class IccProfile
{
    public int Components { get; }
    /// <summary>True when the profile is (numerically) sRGB, so colours can pass through unchanged.</summary>
    public bool IsSrgb { get; private set; }

    enum Kind { Gray, Matrix, Lut }
    readonly Kind _kind;
    Curve[] _trc = [];
    float[] _m = new float[9]; // profile RGB -> linear sRGB
    Lut? _lut;
    float[]? _grid; int _gridN; int _calls;
    const int GridPoints = 17;

    // ---------------------------------------------------------------- curves

    sealed class Curve
    {
        public float[] T = [0f, 1f];
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public float Eval(float x)
        {
            var t = T;
            if (!(x > 0f)) return t[0];
            if (x >= 1f) return t[^1];
            var p = x * (t.Length - 1); var i = (int)p; var f = p - i;
            return t[i] + (t[i + 1] - t[i]) * f;
        }
    }

    static Curve Identity() => new();

    static Curve Sample(Func<double, double> fn, int n = 4096)
    {
        var c = new Curve { T = new float[n] };
        for (var i = 0; i < n; i++) c.T[i] = (float)Math.Clamp(fn((double)i / (n - 1)), 0, 1);
        return c;
    }

    static float S15(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadInt32BigEndian(d[o..]) / 65536f;

    /// <summary>Parses a 'curv' or 'para' element; returns its byte length (unpadded) or -1 if invalid.</summary>
    static int ReadCurve(ReadOnlySpan<byte> d, int o, out Curve? c)
    {
        c = null;
        if (o < 0 || o + 12 > d.Length) return -1;
        var sig = BinaryPrimitives.ReadUInt32BigEndian(d[o..]);
        if (sig == 0x63757276) // curv
        {
            var n = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(o + 8)..]);
            if (n < 0 || n > 65536 || o + 12 + 2 * n > d.Length) return -1;
            if (n == 0) c = Identity();
            else if (n == 1)
            {
                var g = BinaryPrimitives.ReadUInt16BigEndian(d[(o + 12)..]) / 256.0;
                c = Math.Abs(g - 1) < 1e-6 ? Identity() : Sample(x => Math.Pow(x, g));
            }
            else
            {
                c = new Curve { T = new float[n] };
                for (var i = 0; i < n; i++) c.T[i] = BinaryPrimitives.ReadUInt16BigEndian(d[(o + 12 + 2 * i)..]) / 65535f;
            }
            return 12 + 2 * n;
        }
        if (sig == 0x70617261) // para
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(d[(o + 8)..]);
            int[] np = [1, 3, 4, 5, 7];
            if (type > 4 || o + 12 + 4 * np[type] > d.Length) return -1;
            var p = new double[7];
            for (var i = 0; i < np[type]; i++) p[i] = S15(d, o + 12 + 4 * i);
            double g = p[0], a = p[1], b = p[2], cc = p[3], dd = p[4], e = p[5], f = p[6];
            Func<double, double> fn = type switch
            {
                0 => x => Math.Pow(x, g),
                1 => x => a != 0 && x >= -b / a ? Math.Pow(Math.Max(0, a * x + b), g) : 0,
                2 => x => a != 0 && x >= -b / a ? Math.Pow(Math.Max(0, a * x + b), g) + cc : cc,
                3 => x => x >= dd ? Math.Pow(Math.Max(0, a * x + b), g) : cc * x,
                _ => x => x >= dd ? Math.Pow(Math.Max(0, a * x + b), g) + e : cc * x + f,
            };
            c = Sample(fn);
            return 12 + 4 * np[type];
        }
        return -1;
    }

    // ---------------------------------------------------------------- LUT pipeline (A -> CLUT -> M -> matrix -> B)

    enum Pcs { Xyz, LabV2Legacy16, LabV2Lut8, LabV4 }

    sealed class Lut
    {
        public int In, Out;
        public Curve[]? A, M, B;
        public float[]? Clut; public int[] Grid = [];
        public float[]? Mat; // 3x3 + 3 offsets
        public Pcs Pcs;
        int[] _stride = [];

        public void Prepare()
        {
            _stride = new int[In];
            if (Clut is null) return;
            var s = Out;
            for (var d = In - 1; d >= 0; d--) { _stride[d] = s; s *= Grid[d]; }
        }

        public void Eval(ReadOnlySpan<double> inp, Span<float> o3)
        {
            Span<float> x = stackalloc float[4];
            for (var i = 0; i < In; i++)
            {
                var v = (float)Math.Clamp(inp[i], 0, 1);
                x[i] = A is null ? v : A[i].Eval(v);
            }
            Span<float> y = stackalloc float[4];
            if (Clut is null) { for (var i = 0; i < Math.Min(In, 3); i++) y[i] = x[i]; }
            else Interp(x, y);
            if (M is not null) for (var i = 0; i < 3; i++) y[i] = M[i].Eval(y[i]);
            if (Mat is not null)
            {
                float a = y[0], b = y[1], c = y[2];
                y[0] = Mat[0] * a + Mat[1] * b + Mat[2] * c + Mat[9];
                y[1] = Mat[3] * a + Mat[4] * b + Mat[5] * c + Mat[10];
                y[2] = Mat[6] * a + Mat[7] * b + Mat[8] * c + Mat[11];
            }
            for (var i = 0; i < 3; i++) o3[i] = B is null ? y[i] : B[i].Eval(Math.Clamp(y[i], 0f, 1f));
        }

        void Interp(ReadOnlySpan<float> x, Span<float> y)
        {
            Span<int> i0 = stackalloc int[4]; Span<float> fr = stackalloc float[4];
            var clut = Clut!;
            for (var d = 0; d < In; d++)
            {
                var g = Grid[d];
                var p = Math.Clamp(x[d], 0f, 1f) * (g - 1);
                var i = Math.Min((int)p, Math.Max(g - 2, 0));
                i0[d] = i; fr[d] = g > 1 ? p - i : 0;
            }
            y[0] = y[1] = y[2] = 0;
            for (var corner = 0; corner < 1 << In; corner++)
            {
                float w = 1; var idx = 0;
                for (var d = 0; d < In; d++)
                {
                    var bit = (corner >> d) & 1;
                    w *= bit == 1 ? fr[d] : 1 - fr[d];
                    idx += (i0[d] + (bit == 1 && Grid[d] > 1 ? 1 : 0)) * _stride[d];
                }
                if (w == 0) continue;
                y[0] += w * clut[idx]; y[1] += w * clut[idx + 1]; y[2] += w * clut[idx + 2];
            }
        }
    }

    // ---------------------------------------------------------------- parsing

    IccProfile(int comps, Kind kind) { Components = comps; _kind = kind; }

    public static IccProfile? TryParse(byte[] d)
    {
        try { return Parse(d); } catch (Exception) { return null; }
    }

    static IccProfile? Parse(byte[] data)
    {
        if (data.Length < 132) return null;
        ReadOnlySpan<byte> d = data;
        if (BinaryPrimitives.ReadUInt32BigEndian(d[36..]) != 0x61637370) return null; // 'acsp'
        var cls = BinaryPrimitives.ReadUInt32BigEndian(d[12..]);
        if (cls is 0x6C696E6B or 0x61627374 or 0x6E6D636C) return null; // link, abstract, named colour
        var space = BinaryPrimitives.ReadUInt32BigEndian(d[16..]);
        var pcsSig = BinaryPrimitives.ReadUInt32BigEndian(d[20..]);
        var major = d[8];
        int n = space switch { 0x52474220 => 3, 0x47524159 => 1, 0x434D594B => 4, _ => 0 };
        if (n == 0 || (pcsSig != 0x58595A20 && pcsSig != 0x4C616220)) return null;
        var pcsLab = pcsSig == 0x4C616220;

        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(d[128..]);
        if (count < 0 || count > 1000 || 132 + count * 12 > d.Length) return null;
        var tags = new Dictionary<uint, (int Off, int Size)>();
        for (var i = 0; i < count; i++)
        {
            var e = 132 + i * 12;
            var off = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(e + 4)..]); var size = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(e + 8)..]);
            if (off < 0 || size < 8 || (long)off + size > d.Length) continue;
            tags.TryAdd(BinaryPrimitives.ReadUInt32BigEndian(d[e..]), (off, size));
        }
        static uint Sig(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);

        // LUT-based transform takes priority (as lcms does); required for CMYK.
        foreach (var t in new[] { "A2B0", "A2B1" })
            if (tags.TryGetValue(Sig(t), out var tg) && ReadLut(data, tg.Off, tg.Size, n, pcsLab, major) is { } lut)
            {
                var p = new IccProfile(n, Kind.Lut) { _lut = lut };
                p.DetectSrgb();
                return p;
            }
        if (n == 4) return null;

        if (n == 1)
        {
            if (!tags.TryGetValue(Sig("kTRC"), out var k) || ReadCurve(d, k.Off, out var kc) < 0) return null;
            var g = new IccProfile(1, Kind.Gray) { _trc = [kc!] };
            g.DetectSrgb();
            return g;
        }

        var curves = new Curve[3];
        var xyz = new float[9];
        string[] cn = ["rTRC", "gTRC", "bTRC"], xn = ["rXYZ", "gXYZ", "bXYZ"];
        for (var i = 0; i < 3; i++)
        {
            if (!tags.TryGetValue(Sig(cn[i]), out var c) || ReadCurve(d, c.Off, out var cv) < 0) return null;
            curves[i] = cv!;
            if (!tags.TryGetValue(Sig(xn[i]), out var x) || x.Size < 20 || BinaryPrimitives.ReadUInt32BigEndian(d[x.Off..]) != 0x58595A20) return null;
            for (var j = 0; j < 3; j++) xyz[j * 3 + i] = S15(d, x.Off + 8 + 4 * j); // column i = colorant i
        }
        var prof = new IccProfile(3, Kind.Matrix) { _trc = curves };
        // linear sRGB = XYZ(D50)->sRGB * profile matrix
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                prof._m[r * 3 + c] = (float)(XyzToRgb[r * 3] * xyz[c] + XyzToRgb[r * 3 + 1] * xyz[3 + c] + XyzToRgb[r * 3 + 2] * xyz[6 + c]);
        prof.DetectSrgb();
        return prof;
    }

    static Lut? ReadLut(byte[] data, int o, int size, int nIn, bool pcsLab, int major)
    {
        ReadOnlySpan<byte> d = data;
        var sig = BinaryPrimitives.ReadUInt32BigEndian(d[o..]);
        var lut = new Lut();
        if (sig is 0x6D667431 or 0x6D667432) // mft1 / mft2
        {
            var is16 = sig == 0x6D667432;
            int i = d[o + 8], oc = d[o + 9], g = d[o + 10];
            if (i != nIn || oc != 3 || g < 1) return null;
            int nin = 256, nout = 256, p = o + 48;
            if (is16) { nin = BinaryPrimitives.ReadUInt16BigEndian(d[(o + 48)..]); nout = BinaryPrimitives.ReadUInt16BigEndian(d[(o + 50)..]); p = o + 52; }
            if (nin < 2 || nout < 2) return null;
            var bps = is16 ? 2 : 1;
            long clutLen = oc;
            for (var k = 0; k < i; k++) clutLen *= g;
            if (clutLen > 50_000_000 || p + (long)i * nin * bps + clutLen * bps + (long)oc * nout * bps > d.Length) return null;
            float Rd(int off) => is16 ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(off)) / 65535f : data[off] / 255f;
            Curve[] Tbl(int cnt, int len, ref int pos)
            {
                var r = new Curve[cnt];
                for (var c = 0; c < cnt; c++)
                {
                    r[c] = new Curve { T = new float[len] };
                    for (var k = 0; k < len; k++) { r[c].T[k] = Rd(pos); pos += bps; }
                }
                return r;
            }
            lut.In = i; lut.Out = oc;
            lut.A = Tbl(i, nin, ref p);
            lut.Clut = new float[clutLen];
            for (var k = 0; k < clutLen; k++) { lut.Clut[k] = Rd(p); p += bps; }
            lut.Grid = Enumerable.Repeat(g, i).ToArray();
            lut.B = Tbl(oc, nout, ref p);
            lut.Pcs = !pcsLab ? Pcs.Xyz : is16 ? Pcs.LabV2Legacy16 : Pcs.LabV2Lut8;
            lut.Prepare();
            return lut;
        }
        if (sig == 0x6D414220) // mAB
        {
            int i = d[o + 8], oc = d[o + 9];
            if (i != nIn || oc != 3) return null;
            int oB = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(o + 12)..]), oMat = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(o + 16)..]),
                oM = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(o + 20)..]), oC = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(o + 24)..]),
                oA = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(o + 28)..]);
            Curve[]? Seq(int off, int cnt)
            {
                if (off == 0) return null;
                var r = new Curve[cnt]; var p = o + off;
                for (var k = 0; k < cnt; k++)
                {
                    var len = ReadCurve(data, p, out var c);
                    if (len < 0) throw new InvalidDataException();
                    r[k] = c!; p += (len + 3) & ~3;
                }
                return r;
            }
            lut.In = i; lut.Out = oc;
            lut.B = Seq(oB, oc); lut.M = Seq(oM, oc); lut.A = Seq(oA, i);
            if (oMat != 0)
            {
                lut.Mat = new float[12];
                for (var k = 0; k < 12; k++) lut.Mat[k] = S15(d, o + oMat + 4 * k);
            }
            if (oC != 0)
            {
                var gp = o + oC;
                lut.Grid = new int[i]; long len = oc;
                for (var k = 0; k < i; k++) { lut.Grid[k] = d[gp + k]; len *= lut.Grid[k]; if (lut.Grid[k] < 1) return null; }
                if (len > 50_000_000) return null;
                var prec = d[gp + 16]; var dp = gp + 20;
                if (dp + len * prec > d.Length || prec is not (1 or 2)) return null;
                lut.Clut = new float[len];
                for (var k = 0; k < len; k++) lut.Clut[k] = prec == 2 ? BinaryPrimitives.ReadUInt16BigEndian(d[(dp + 2 * k)..]) / 65535f : d[dp + k] / 255f;
            }
            else if (i != 3) return null;
            lut.Pcs = pcsLab ? Pcs.LabV4 : Pcs.Xyz;
            lut.Prepare();
            return lut;
        }
        return null;
    }

    // ---------------------------------------------------------------- conversion to sRGB

    // Bradford-adapted D50 XYZ -> linear sRGB.
    static readonly double[] XyzToRgb = [3.1338561, -1.6168667, -0.4906146, -0.9787684, 1.9161415, 0.0334540, 0.0719453, -0.2289914, 1.4052427];

    static readonly float[] EncodeTable = BuildEncode();
    static float[] BuildEncode()
    {
        var t = new float[4097];
        for (var i = 0; i <= 4096; i++)
        {
            double v = i / 4096.0;
            t[i] = (float)(v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055);
        }
        return t;
    }

    static float Encode(float lin)
    {
        if (!(lin > 0f)) return 0f;
        if (lin >= 1f) return 1f;
        var p = lin * 4096f; var i = (int)p;
        return EncodeTable[i] + (EncodeTable[i + 1] - EncodeTable[i]) * (p - i);
    }

    static void PcsToSrgb(Pcs pcs, float p0, float p1, float p2, Span<float> rgb)
    {
        double X, Y, Z;
        if (pcs == Pcs.Xyz) { const double s = 65535.0 / 32768.0; X = p0 * s; Y = p1 * s; Z = p2 * s; }
        else
        {
            double L, a, b;
            switch (pcs)
            {
                case Pcs.LabV2Legacy16: L = p0 * 65535.0 / 65280.0 * 100; a = p1 * 65535.0 / 256 - 128; b = p2 * 65535.0 / 256 - 128; break;
                default: L = p0 * 100; a = p1 * 255 - 128; b = p2 * 255 - 128; break;
            }
            var fy = (L + 16) / 116; var fx = fy + a / 500; var fz = fy - b / 200;
            static double Inv(double t) => t > 6.0 / 29 ? t * t * t : 3 * (6.0 / 29) * (6.0 / 29) * (t - 4.0 / 29);
            X = 0.9642 * Inv(fx); Y = Inv(fy); Z = 0.8249 * Inv(fz);
        }
        var r = XyzToRgb[0] * X + XyzToRgb[1] * Y + XyzToRgb[2] * Z;
        var g = XyzToRgb[3] * X + XyzToRgb[4] * Y + XyzToRgb[5] * Z;
        var bl = XyzToRgb[6] * X + XyzToRgb[7] * Y + XyzToRgb[8] * Z;
        rgb[0] = Encode((float)r); rgb[1] = Encode((float)g); rgb[2] = Encode((float)bl);
    }

    void EvalExact(ReadOnlySpan<double> c, Span<float> rgb)
    {
        switch (_kind)
        {
            case Kind.Gray:
                {
                    var y = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1));
                    var e = Encode(y);
                    rgb[0] = rgb[1] = rgb[2] = e; break;
                }
            case Kind.Matrix:
                {
                    float r = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1)), g = _trc[1].Eval((float)Math.Clamp(c[1], 0, 1)), b = _trc[2].Eval((float)Math.Clamp(c[2], 0, 1));
                    rgb[0] = Encode(_m[0] * r + _m[1] * g + _m[2] * b);
                    rgb[1] = Encode(_m[3] * r + _m[4] * g + _m[5] * b);
                    rgb[2] = Encode(_m[6] * r + _m[7] * g + _m[8] * b);
                    break;
                }
            default:
                {
                    Span<float> p = stackalloc float[3];
                    _lut!.Eval(c, p);
                    PcsToSrgb(_lut.Pcs, p[0], p[1], p[2], rgb);
                    break;
                }
        }
    }

    void DetectSrgb()
    {
        if (Components == 1 || Components == 4) { IsSrgb = false; return; }
        Span<float> o = stackalloc float[3];
        var ok = true;
        double[][] samples = [[0, 0, 0], [1, 1, 1], [0.5, 0.5, 0.5], [1, 0, 0], [0, 1, 0], [0, 0, 1], [0.2, 0.6, 0.9], [0.9, 0.4, 0.1]];
        foreach (var s in samples)
        {
            EvalExact(s, o);
            for (var k = 0; k < 3; k++) if (Math.Abs(o[k] - s[k]) > 0.006) ok = false;
        }
        IsSrgb = ok;
    }

    public RgbColor ToSrgb(ReadOnlySpan<double> c)
    {
        if (IsSrgb) return new(Math.Clamp(c[0], 0, 1), Math.Clamp(c[1], 0, 1), Math.Clamp(c[2], 0, 1));
        Span<float> o = stackalloc float[3];
        if (_kind == Kind.Lut && Components >= 3)
        {
            var grid = _grid;
            if (grid is null && ++_calls > 4096) { BuildGrid(); grid = _grid; }
            if (grid is not null) { Lookup(grid, c, o); return new(o[0], o[1], o[2]); }
        }
        EvalExact(c, o);
        return new(o[0], o[1], o[2]);
    }

    void BuildGrid()
    {
        var n = GridPoints; var comps = Components;
        var total = 1; for (var i = 0; i < comps; i++) total *= n;
        var g = new float[total * 3];
        var inp = new double[comps]; Span<float> o = stackalloc float[3];
        for (var idx = 0; idx < total; idx++)
        {
            var t = idx;
            for (var d = comps - 1; d >= 0; d--) { inp[d] = (t % n) / (double)(n - 1); t /= n; }
            EvalExact(inp, o);
            g[idx * 3] = o[0]; g[idx * 3 + 1] = o[1]; g[idx * 3 + 2] = o[2];
        }
        _gridN = n; _grid = g;
    }

    void Lookup(float[] grid, ReadOnlySpan<double> c, Span<float> o)
    {
        var comps = Components; var n = _gridN;
        Span<int> i0 = stackalloc int[4]; Span<float> fr = stackalloc float[4]; Span<int> stride = stackalloc int[4];
        var s = 1;
        for (var d = comps - 1; d >= 0; d--) { stride[d] = s; s *= n; }
        for (var d = 0; d < comps; d++)
        {
            var p = (float)Math.Clamp(c[d], 0, 1) * (n - 1);
            var i = Math.Min((int)p, n - 2);
            i0[d] = i; fr[d] = p - i;
        }
        float r = 0, g = 0, b = 0;
        for (var corner = 0; corner < 1 << comps; corner++)
        {
            float w = 1; var idx = 0;
            for (var d = 0; d < comps; d++)
            {
                var bit = (corner >> d) & 1;
                w *= bit == 1 ? fr[d] : 1 - fr[d];
                idx += (i0[d] + bit) * stride[d];
            }
            if (w == 0) continue;
            idx *= 3;
            r += w * grid[idx]; g += w * grid[idx + 1]; b += w * grid[idx + 2];
        }
        o[0] = r; o[1] = g; o[2] = b;
    }
}
