using System.Buffers.Binary;
using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>
/// ICC profile reader/evaluator (ICC.1 v2/v4) converting device values to sRGB. Supports matrix/TRC RGB, gray TRC, and
/// LUT profiles (lut8, lut16, lutAToB) for RGB/CMYK via A2B0/A2B1/A2B2. PCS is D50 XYZ or Lab; output is sRGB (Bradford D50 → D65).
/// Evaluation is allocation-free; CMYK/RGB LUT profiles bake a 17-point grid after enough calls to amortise the cost.
/// Perceptual PCS black (ICC.1:2022 6.3.4.3, Table 16): v4 A2B0 tables are mapped from the perceptual reference
/// black to the analytic sRGB black (XYZ 0) by the white-preserving affine XYZ map; v2 perceptual tables are
/// treated as having black 0 (6.3.4.3 pre-v4 adjustment followed by the same map is the identity), so they are unchanged.
/// Optional BPC uses a parse-time prepared XYZ affine map for the matrix/gray relative-colorimetric subset, and for
/// LUT profiles the Little CMS algorithm (cmsDetectBlackPoint: v4 perceptual/saturation reference black; CMYK output
/// relative via the perceptual A2B0/B2A0 round trip; otherwise the darkest-colorant Y, clamped to L* 50, made neutral).
/// It is not ISO 18619 black-point estimation. <see cref="TryConvert"/> converts between profiles with B2A tables (or
/// matrix/gray inverses); its destination BPC black uses the Adobe/Little CMS L* round-trip fit (cmsDetectDestinationBlackPoint).
/// Named-colour ('nmcl'/'ncl2') and N-colour profiles are rejected here: ISO 32000-2 ICCBased accepts only N = 1, 3 or 4 and
/// non-named classes (see <see cref="IccNamedColorProfile"/> for a standalone ncl2 reader).
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
    float[] _xyz = new float[9]; // profile RGB -> D50 XYZ (matrix profiles)
    double[]? _xyzInverse;
    readonly Transform[] _transforms = [new(), new(), new(), new()];
    readonly Transform[] _bpcTransforms = [new(), new(), new(), new()];
    readonly Lut?[] _b2a = new Lut?[4];
    double[] _whiteScale = [1, 1, 1];
    float[] _absoluteM = new float[9];
    float[]? _blackPointM;
    float? _grayBlack;
    int _major;
    uint _class;
    double? _perceptualRoundTripY;
    const float MaxEstimatedBlack = 0.1841865f; // Neutral L* = 50; higher endpoints are not estimated.
    const int GridPoints = 17;
    static readonly double[] D50 = [0.9642, 1, 0.8249];
    // ICC.1:2022 Table 16 (perceptual reference medium black), PCSXYZ.
    static readonly double[] PerceptualBlack = [0.003357, 0.003479, 0.002869];
    const uint OutputClass = 0x70727472; // 'prtr'

    sealed class Transform
    {
        public Lut? Lut;
        public float[]? Grid;
        public int Calls;
        public double[]? Map; // PCS XYZ affine map: scale[0..2], offset[3..5]
        public double[]? Black;
        public double[]? DestinationBlack;
        public int BpcState; // 0 unknown, 1 map available, 2 no-op/unavailable
        public readonly object Gate = new();
    }

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

        public bool HasMonotoneUnitWhite()
        {
            if (T[^1] != 1f || !(T[0] >= 0f && T[0] < 1f)) return false;
            for (var i = 1; i < T.Length; i++)
                if (!float.IsFinite(T[i]) || T[i] < T[i - 1]) return false;
            return true;
        }

        /// <summary>Inverse of a nondecreasing sampled curve (binary search + linear interpolation).</summary>
        public float Invert(double y)
        {
            var t = T;
            if (!(y > t[0])) return 0f;
            if (y >= t[^1]) return 1f;
            int lo = 0, hi = t.Length - 1;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (t[mid] <= y) lo = mid; else hi = mid;
            }
            var span = t[hi] - t[lo];
            var f = span > 0 ? (float)((y - t[lo]) / span) : 0f;
            return (lo + f) / (t.Length - 1);
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
        public bool Reverse; // mBA / B2A: B -> matrix -> M -> CLUT -> A
        public Curve[]? A, M, B;
        public float[]? Clut; public int[] Grid = [];
        public float[]? Mat; // 3x3 + 3 offsets
        public float[]? PreMat; // lut8/lut16 3x3 applied first when PCS is XYZ (B2A only)
        public Pcs Pcs;
        int[] _stride = [];

        public void Prepare()
        {
            _stride = new int[In];
            if (Clut is null) return;
            var s = Out;
            for (var d = In - 1; d >= 0; d--) { _stride[d] = s; s *= Grid[d]; }
        }

        /// <summary>Evaluates to <see cref="Out"/> channels (A2B: 3 PCS channels; B2A: device channels, normalised 0..1).</summary>
        public void Eval(ReadOnlySpan<double> inp, Span<float> o3)
        {
            Span<float> x = stackalloc float[4];
            Span<float> y = stackalloc float[4];
            if (Reverse) { EvalReverse(inp, o3, x, y); return; }
            for (var i = 0; i < In; i++)
            {
                var v = (float)Math.Clamp(inp[i], 0, 1);
                x[i] = A is null ? v : A[i].Eval(v);
            }
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

        void EvalReverse(ReadOnlySpan<double> inp, Span<float> o, Span<float> x, Span<float> y)
        {
            for (var i = 0; i < 3; i++) x[i] = (float)Math.Clamp(inp[i], 0, 1);
            if (PreMat is { } pm)
            {
                float a = x[0], b = x[1], c = x[2];
                x[0] = Math.Clamp(pm[0] * a + pm[1] * b + pm[2] * c, 0f, 1f);
                x[1] = Math.Clamp(pm[3] * a + pm[4] * b + pm[5] * c, 0f, 1f);
                x[2] = Math.Clamp(pm[6] * a + pm[7] * b + pm[8] * c, 0f, 1f);
            }
            if (B is not null) for (var i = 0; i < 3; i++) x[i] = B[i].Eval(x[i]);
            if (Mat is not null)
            {
                float a = x[0], b = x[1], c = x[2];
                x[0] = Mat[0] * a + Mat[1] * b + Mat[2] * c + Mat[9];
                x[1] = Mat[3] * a + Mat[4] * b + Mat[5] * c + Mat[10];
                x[2] = Mat[6] * a + Mat[7] * b + Mat[8] * c + Mat[11];
            }
            if (M is not null) for (var i = 0; i < 3; i++) x[i] = M[i].Eval(Math.Clamp(x[i], 0f, 1f));
            if (Clut is null) for (var i = 0; i < Math.Min(Out, 3); i++) y[i] = x[i];
            else Interp(x, y);
            for (var i = 0; i < Out; i++) o[i] = A is null ? y[i] : A[i].Eval(Math.Clamp(y[i], 0f, 1f));
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
            var outs = Out;
            for (var k = 0; k < outs; k++) y[k] = 0;
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
                if (outs == 3) { y[0] += w * clut[idx]; y[1] += w * clut[idx + 1]; y[2] += w * clut[idx + 2]; }
                else for (var k = 0; k < outs; k++) y[k] += w * clut[idx + k];
            }
        }
    }

    // ---------------------------------------------------------------- parsing

    IccProfile(int comps, Kind kind) { Components = comps; _kind = kind; }

    public static IccProfile? TryParse(byte[] d) => TryParse(d, false);

    /// <summary>
    /// Parses a profile. <paramref name="includeDestinationTables"/> also loads B2A0/B2A1/B2A2 so the profile can be the
    /// destination of <see cref="TryConvert"/>; the rendering path leaves it off to avoid the extra tables.
    /// </summary>
    public static IccProfile? TryParse(byte[] d, bool includeDestinationTables)
    {
        try { return Parse(d, includeDestinationTables); } catch (Exception) { return null; }
    }

    static IccProfile? Parse(byte[] data, bool includeDestination)
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

        // ICC.1:2022 8.10.2: requested A2B table, then A2B0, then matrix/TRC.
        Lut? ReadTag(string name, int nIn, int nOut, bool toDevice = false) => tags.TryGetValue(Sig(name), out var tag)
            ? ReadLut(data, tag.Off, tag.Size, nIn, nOut, pcsLab, major, toDevice) : null;
        var perceptual = ReadTag("A2B0", n, 3);
        var relative = ReadTag("A2B1", n, 3) ?? perceptual;
        var saturation = ReadTag("A2B2", n, 3) ?? perceptual;
        IccProfile? Configure(IccProfile p)
        {
            p._major = major; p._class = cls;
            p._transforms[(int)RenderingIntent.Perceptual].Lut = perceptual;
            p._transforms[(int)RenderingIntent.RelativeColorimetric].Lut = relative;
            p._transforms[(int)RenderingIntent.Saturation].Lut = saturation;
            p._transforms[(int)RenderingIntent.AbsoluteColorimetric].Lut = relative;
            // 6.3.4.3 Table 16: v4 perceptual PCS black maps to the (analytic) destination black.
            if (major >= 4 && perceptual is not null)
                p._transforms[(int)RenderingIntent.Perceptual].Map = BlackMap(PerceptualBlack, [0, 0, 0]);
            if (includeDestination)
            {
                var b0 = ReadTag("B2A0", 3, n, true);
                p._b2a[(int)RenderingIntent.Perceptual] = b0;
                p._b2a[(int)RenderingIntent.RelativeColorimetric] = ReadTag("B2A1", 3, n, true) ?? b0;
                p._b2a[(int)RenderingIntent.Saturation] = ReadTag("B2A2", 3, n, true) ?? b0;
                p._b2a[(int)RenderingIntent.AbsoluteColorimetric] = p._b2a[(int)RenderingIntent.RelativeColorimetric];
            }
            if (cls == OutputClass && n == 4 && perceptual is not null && tags.ContainsKey(Sig("B2A0")))
            {
                // Little CMS BlackPointUsingPerceptualBlack: PCS black -> B2A0 -> A2B0 (perceptual round trip).
                var b0 = includeDestination ? p._b2a[(int)RenderingIntent.Perceptual] : ReadTag("B2A0", 3, n, true);
                if (b0 is not null) p._perceptualRoundTripY = RoundTripBlackY(b0, perceptual, major >= 4 ? PerceptualBlack : [0, 0, 0]);
            }
            // ICC.1:2022 6.3.2.2 (4)-(6): source media white / destination D50 media white.
            // As in lcms _cmsReadMediaWhitePoint, v2 display profiles use D50.
            if (!(major < 4 && cls == 0x6D6E7472) && tags.TryGetValue(Sig("wtpt"), out var wp)
                && wp.Size >= 20 && BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(wp.Off)) == 0x58595A20)
            {
                double[] d50 = [0.9642, 1, 0.8249];
                for (var i = 0; i < 3; i++)
                {
                    var white = S15(data, wp.Off + 8 + 4 * i);
                    if (!(white > 0)) return null;
                    p._whiteScale[i] = white / d50[i];
                }
            }
            p.DetectSrgb();
            if ((major is 2 or 4) && (cls is 0x73636E72 or 0x6D6E7472) && !pcsLab
                && relative is null && p._kind == Kind.Gray && p._trc[0].HasMonotoneUnitWhite()
                && p._trc[0].T[0] <= MaxEstimatedBlack)
                p._grayBlack = p._trc[0].T[0];
            return p;
        }
        IccProfile? LutOnly() => relative is not null ? Configure(new IccProfile(n, Kind.Lut)) : null;
        if (perceptual is not null && relative is not null && saturation is not null)
            return Configure(new IccProfile(n, Kind.Lut));
        if (n == 4) return LutOnly();

        if (n == 1)
        {
            if (!tags.TryGetValue(Sig("kTRC"), out var k) || ReadCurve(d, k.Off, out var kc) < 0) return LutOnly();
            var g = new IccProfile(1, Kind.Gray) { _trc = [kc!] };
            return Configure(g);
        }

        var curves = new Curve[3];
        var xyz = new float[9];
        string[] cn = ["rTRC", "gTRC", "bTRC"], xn = ["rXYZ", "gXYZ", "bXYZ"];
        for (var i = 0; i < 3; i++)
        {
            if (!tags.TryGetValue(Sig(cn[i]), out var c) || ReadCurve(d, c.Off, out var cv) < 0) return LutOnly();
            curves[i] = cv!;
            if (!tags.TryGetValue(Sig(xn[i]), out var x) || x.Size < 20 || BinaryPrimitives.ReadUInt32BigEndian(d[x.Off..]) != 0x58595A20) return LutOnly();
            for (var j = 0; j < 3; j++) xyz[j * 3 + i] = S15(d, x.Off + 8 + 4 * j); // column i = colorant i
        }
        var prof = new IccProfile(3, Kind.Matrix) { _trc = curves, _xyz = xyz };
        // linear sRGB = XYZ(D50)->sRGB * profile matrix
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                prof._m[r * 3 + c] = (float)(XyzToRgb[r * 3] * xyz[c] + XyzToRgb[r * 3 + 1] * xyz[3 + c] + XyzToRgb[r * 3 + 2] * xyz[6 + c]);
        if (Configure(prof) is null) return null;
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                prof._absoluteM[r * 3 + c] = (float)(XyzToRgb[r * 3] * prof._whiteScale[0] * xyz[c]
                    + XyzToRgb[r * 3 + 1] * prof._whiteScale[1] * xyz[3 + c]
                    + XyzToRgb[r * 3 + 2] * prof._whiteScale[2] * xyz[6 + c]);
        if ((major is 2 or 4) && (cls is 0x73636E72 or 0x6D6E7472) && !pcsLab && relative is null)
            prof.PrepareBlackPoint(xyz);
        return prof;
    }

    void PrepareBlackPoint(float[] xyz)
    {
        if (_trc.Any(c => !c.HasMonotoneUnitWhite()) || xyz.Any(v => v < 0 || !float.IsFinite(v)))
            return;
        var black = _trc[0].T[0];
        if (black > MaxEstimatedBlack) return;
        if (_trc.Any(c => Math.Abs(c.T[0] - black) > 0.000001)) return;
        double[] white = [0.9642, 1, 0.8249];
        var scale = new double[3];
        var offset = new double[3];
        for (var r = 0; r < 3; r++)
        {
            var sum = (double)xyz[r * 3] + xyz[r * 3 + 1] + xyz[r * 3 + 2];
            if (Math.Abs(sum - white[r]) > 0.0001) return;
            var sourceBlack = (double)xyz[r * 3] * _trc[0].T[0]
                + xyz[r * 3 + 1] * _trc[1].T[0] + xyz[r * 3 + 2] * _trc[2].T[0];
            if (!(sourceBlack < white[r])) return;
            // XYZ' = (W - Bd)/(W - Bs) * XYZ + W*(Bd - Bs)/(W - Bs).
            // The analytic sRGB destination has Bd = 0; W is PCS D50.
            scale[r] = white[r] / (white[r] - sourceBlack);
            offset[r] = -scale[r] * sourceBlack;
        }
        var matrix = new float[12];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
                for (var k = 0; k < 3; k++)
                    matrix[r * 3 + c] += (float)(XyzToRgb[r * 3 + k] * scale[k] * xyz[k * 3 + c]);
            for (var k = 0; k < 3; k++)
                matrix[9 + r] += (float)(XyzToRgb[r * 3 + k] * offset[k]);
        }
        _blackPointM = matrix;
    }

    /// <summary>
    /// Whether black-point compensation changes this intent. Matrix/gray profiles use the bounded prepared
    /// relative-colorimetric estimate (v2/v4 input/display XYZ matrix/TRC RGB with nonnegative colorants, D50 white and
    /// equal black endpoints, or gray TRC; monotone curves with unit white and black no higher than neutral L* 50; D50
    /// row-sum tolerance 0.0001, endpoint tolerance 0.000001). LUT profiles use <see cref="BlackXyz"/>. Absolute
    /// intent always disables BPC; unsupported estimation preserves ordinary conversion.
    /// </summary>
    public bool SupportsBlackPointCompensation(RenderingIntent intent) =>
        SupportsMatrixBlackPointCompensation(intent) || EnsureLutBpc(intent);

    bool SupportsMatrixBlackPointCompensation(RenderingIntent intent) =>
        intent == RenderingIntent.RelativeColorimetric && (_grayBlack.HasValue || _blackPointM is not null);

    static double[]? BlackMap(ReadOnlySpan<double> sourceBlack, ReadOnlySpan<double> destinationBlack)
    {
        // Little CMS ComputeBlackPointCompensation: X' = ax*X + bx, mapping source black to destination black, D50 fixed.
        var map = new double[6];
        var changed = false;
        for (var i = 0; i < 3; i++)
        {
            var t = sourceBlack[i] - D50[i];
            if (!(t < 0) || !double.IsFinite(sourceBlack[i]) || !double.IsFinite(destinationBlack[i])) return null;
            map[i] = (destinationBlack[i] - D50[i]) / t;
            map[3 + i] = -D50[i] * (destinationBlack[i] - sourceBlack[i]) / t;
            if (Math.Abs(sourceBlack[i] - destinationBlack[i]) > 1e-9) changed = true;
        }
        return changed ? map : null;
    }

    bool EnsureLutBpc(RenderingIntent intent)
    {
        if ((uint)intent > (uint)RenderingIntent.Saturation) return false;
        var t = _bpcTransforms[(int)intent];
        if (System.Threading.Volatile.Read(ref t.BpcState) == 0)
        {
            lock (t.Gate)
            {
                if (t.BpcState == 0)
                {
                    var source = _transforms[(int)intent];
                    var map = source.Lut is null ? null : BlackMap(BlackXyz(intent), [0, 0, 0]);
                    t.Lut = source.Lut; t.Map = map;
                    System.Threading.Volatile.Write(ref t.BpcState, map is null ? 2 : 1);
                }
            }
        }
        return t.BpcState == 1;
    }

    /// <summary>
    /// PCS XYZ (D50-relative) black point estimated for <paramref name="intent"/> (Little CMS cmsDetectBlackPoint):
    /// v4 perceptual/saturation LUTs use the Table 16 reference black; CMYK output-class relative uses the perceptual
    /// A2B0/B2A0 round trip; everything else is the darkest-colorant Y (RGB/gray 0, CMYK 100% inks) forced neutral and
    /// clamped to L* 50. Not ISO 18619.
    /// </summary>
    internal double[] BlackXyz(RenderingIntent intent)
    {
        var t = _transforms[(int)intent];
        var cached = System.Threading.Volatile.Read(ref t.Black);
        if (cached is not null) return cached;
        double[] black;
        if (_major >= 4 && intent is RenderingIntent.Perceptual or RenderingIntent.Saturation && t.Lut is not null)
            black = PerceptualBlack;
        else if (intent == RenderingIntent.RelativeColorimetric && _class == OutputClass && Components == 4 && _perceptualRoundTripY is { } py)
            black = NeutralBlack(py);
        else
        {
            Span<double> dev = stackalloc double[4];
            for (var i = 0; i < 4; i++) dev[i] = Components == 4 ? 1 : 0;
            Span<double> xyz = stackalloc double[3];
            black = ToPcsXyz(dev[..Components], intent, xyz) ? NeutralBlack(xyz[1]) : [0, 0, 0];
        }
        System.Threading.Volatile.Write(ref t.Black, black);
        return black;
    }

    static double[] NeutralBlack(double y)
    {
        y = Math.Clamp(y, 0, MaxEstimatedBlack);
        return [D50[0] * y, y, D50[2] * y];
    }

    static double? RoundTripBlackY(Lut toDevice, Lut fromDevice, ReadOnlySpan<double> startXyz)
    {
        if (toDevice.In != 3 || fromDevice.Out != 3 || toDevice.Out != fromDevice.In) return null;
        return RoundTripY(toDevice, fromDevice, startXyz[0], startXyz[1], startXyz[2]);
    }

    static double RoundTripY(Lut toDevice, Lut fromDevice, double x, double y, double z)
    {
        Span<double> pcs = stackalloc double[3]; Span<float> dev = stackalloc float[4];
        Span<double> devD = stackalloc double[4]; Span<float> back = stackalloc float[4];
        EncodePcs(toDevice.Pcs, x, y, z, pcs);
        toDevice.Eval(pcs, dev);
        for (var i = 0; i < 4; i++) devD[i] = dev[i];
        fromDevice.Eval(devD, back);
        DecodePcs(fromDevice.Pcs, back[0], back[1], back[2], out _, out var yo, out _);
        return yo;
    }

    static double LFromY(double y) => y > Cube(6.0 / 29) ? 116 * Math.Cbrt(y) - 16 : 116 * (y / (3 * (6.0 / 29) * (6.0 / 29)) + 4.0 / 29) - 16;

    static double YFromL(double l)
    {
        var f = (l + 16) / 116;
        return f > 6.0 / 29 ? Cube(f) : 3 * (6.0 / 29) * (6.0 / 29) * (f - 4.0 / 29);
    }

    /// <summary>
    /// Destination black for BPC (Little CMS cmsDetectDestinationBlackPoint, after Adobe's BPC paper 7.2): for LUT
    /// gray/RGB/CMYK profiles with a B2A table, a 256-step L* round trip (B2A then A2B) is checked for a straight mid-range
    /// (relative) or fitted with a least-squares quadratic; otherwise this is <see cref="BlackXyz"/>.
    /// </summary>
    internal double[] DestinationBlackXyz(RenderingIntent intent)
    {
        if ((uint)intent > (uint)RenderingIntent.Saturation) return [0, 0, 0];
        var t = _transforms[(int)intent];
        if (_major >= 4 && intent is RenderingIntent.Perceptual or RenderingIntent.Saturation
            || _b2a[(int)intent] is not { } toDevice || t.Lut is not { } fromDevice || toDevice.In != 3 || toDevice.Out != fromDevice.In)
            return BlackXyz(intent);
        var cached = System.Threading.Volatile.Read(ref t.DestinationBlack);
        if (cached is not null) return cached;
        double[] black;
        var relative = intent == RenderingIntent.RelativeColorimetric;
        var initial = relative ? BlackXyz(intent) : [0, 0, 0];
        var initialL = LFromY(initial[1]);
        Span<double> inRamp = stackalloc double[256]; Span<double> outRamp = stackalloc double[256];
        for (var l = 0; l < 256; l++)
        {
            inRamp[l] = l * 100.0 / 255.0;
            var y = YFromL(inRamp[l]);
            outRamp[l] = LFromY(RoundTripY(toDevice, fromDevice, D50[0] * y, y, D50[2] * y));
        }
        for (var l = 254; l > 0; --l) outRamp[l] = Math.Min(outRamp[l], outRamp[l + 1]);
        double min = outRamp[0], max = outRamp[255];
        if (!(min < max)) black = [0, 0, 0];
        else
        {
            var straight = relative;
            if (relative)
                for (var l = 0; l < 256; l++)
                    if (!(inRamp[l] <= min + 0.2 * (max - min) || Math.Abs(inRamp[l] - outRamp[l]) < 4.0)) straight = false;
            if (straight) black = initial;
            else
            {
                double lo = relative ? 0.1 : 0.03, hi = relative ? 0.5 : 0.25;
                Span<double> x = stackalloc double[256]; Span<double> y = stackalloc double[256];
                var n = 0;
                for (var l = 0; l < 256; l++)
                {
                    var ff = (outRamp[l] - min) / (max - min);
                    if (ff >= lo && ff < hi) { x[n] = inRamp[l]; y[n] = ff; n++; }
                }
                if (n < 3) black = [0, 0, 0];
                else
                {
                    var yb = YFromL(Math.Max(0, QuadraticFitRoot(x[..n], y[..n])));
                    black = [D50[0] * yb, yb, D50[2] * yb];
                }
            }
        }
        System.Threading.Volatile.Write(ref t.DestinationBlack, black);
        return black;
    }

    // Mirrors lcms RootOfLeastSquaresFitQuadraticCurve, including its clamping.
    static double QuadraticFitRoot(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        var n = x.Length;
        if (n < 4) return 0;
        double sx = 0, sx2 = 0, sx3 = 0, sx4 = 0, sy = 0, syx = 0, syx2 = 0;
        for (var i = 0; i < n; i++)
        {
            double xn = x[i], yn = y[i];
            sx += xn; sx2 += xn * xn; sx3 += xn * xn * xn; sx4 += xn * xn * xn * xn;
            sy += yn; syx += yn * xn; syx2 += yn * xn * xn;
        }
        double[] m = [n, sx, sx2, sx, sx2, sx3, sx2, sx3, sx4];
        double det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (!(Math.Abs(det) > 1e-12)) return 0;
        double[] v3 = [sy, syx, syx2];
        double Det3(double[] a) => a[0] * (a[4] * a[8] - a[5] * a[7]) - a[1] * (a[3] * a[8] - a[5] * a[6]) + a[2] * (a[3] * a[7] - a[4] * a[6]);
        double[] col(int k) { var c = (double[])m.Clone(); for (var r = 0; r < 3; r++) c[r * 3 + k] = v3[r]; return c; }
        double c0 = Det3(col(0)) / det, b = Det3(col(1)) / det, a = Det3(col(2)) / det;
        if (Math.Abs(a) < 1e-10) return Math.Abs(b) < 1e-10 ? 0 : Math.Min(0, Math.Max(50, -c0 / b));
        var d = b * b - 4 * a * c0;
        if (d <= 0) return 0;
        return Math.Max(0, Math.Min(50, (-b + Math.Sqrt(d)) / (2 * a)));
    }

    /// <summary>Device values to D50 PCS XYZ for the intent's A2B table or matrix/gray model (no BPC, no absolute scaling).</summary>
    bool ToPcsXyz(ReadOnlySpan<double> c, RenderingIntent intent, Span<double> xyz)
    {
        if (_transforms[(int)intent].Lut is { } lut)
        {
            Span<float> p = stackalloc float[4];
            lut.Eval(c, p);
            DecodePcs(lut.Pcs, p[0], p[1], p[2], out var x, out var y, out var z);
            xyz[0] = x; xyz[1] = y; xyz[2] = z;
            return true;
        }
        switch (_kind)
        {
            case Kind.Gray:
                {
                    var y = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1));
                    xyz[0] = D50[0] * y; xyz[1] = y; xyz[2] = D50[2] * y;
                    return true;
                }
            case Kind.Matrix:
                {
                    double r = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1)), g = _trc[1].Eval((float)Math.Clamp(c[1], 0, 1)), b = _trc[2].Eval((float)Math.Clamp(c[2], 0, 1));
                    for (var j = 0; j < 3; j++) xyz[j] = _xyz[j * 3] * r + _xyz[j * 3 + 1] * g + _xyz[j * 3 + 2] * b;
                    return true;
                }
            default: return false;
        }
    }

    /// <summary>D50 PCS XYZ to device values (0..1) through B2A (when parsed with destination tables) or the matrix/gray model.</summary>
    bool FromPcsXyz(ReadOnlySpan<double> xyz, RenderingIntent intent, Span<double> device)
    {
        if (_b2a[(int)intent] is { } lut)
        {
            Span<double> e = stackalloc double[3]; Span<float> o = stackalloc float[4];
            EncodePcs(lut.Pcs, xyz[0], xyz[1], xyz[2], e);
            lut.Eval(e, o);
            for (var i = 0; i < Components; i++) device[i] = Math.Clamp(o[i], 0f, 1f);
            return true;
        }
        for (var i = 0; i < _transforms.Length; i++)
            if (_transforms[i].Lut is not null) return false; // LUT profile without destination tables
        switch (_kind)
        {
            case Kind.Gray:
                device[0] = _trc[0].Invert(xyz[1]);
                return true;
            case Kind.Matrix:
                {
                    var inv = _xyzInverse ??= Invert3(_xyz);
                    if (inv is null) return false;
                    for (var i = 0; i < 3; i++)
                        device[i] = _trc[i].Invert(inv[i * 3] * xyz[0] + inv[i * 3 + 1] * xyz[1] + inv[i * 3 + 2] * xyz[2]);
                    return true;
                }
            default: return false;
        }
    }

    static double[]? Invert3(float[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
        var det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (!(Math.Abs(det) > 1e-12)) return null;
        return [(e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det,
            (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det,
            (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det];
    }

    /// <summary>PCS black used by <see cref="TryConvert"/> for one side of the conversion.</summary>
    double[] PcsBlackFor(RenderingIntent intent, bool bpc, bool destination)
    {
        if (bpc) return destination ? DestinationBlackXyz(intent) : BlackXyz(intent);
        // 6.3.4.3: v4 perceptual PCS black is Table 16; pre-v4 perceptual transforms are taken to have black 0.
        return intent == RenderingIntent.Perceptual && _major >= 4 && _transforms[(int)intent].Lut is not null
            ? PerceptualBlack : ZeroBlack;
    }

    static readonly double[] ZeroBlack = [0, 0, 0];

    /// <summary>
    /// Converts <paramref name="input"/> device values of <paramref name="source"/> to 0..1 device values of
    /// <paramref name="destination"/> through the D50 PCS XYZ: source A2B (or matrix/gray) then destination B2A
    /// (parse the destination with <c>includeDestinationTables</c>) or inverse matrix/TRC. Perceptual intent maps between the
    /// profiles' PCS blacks (Table 16 for v4, 0 for pre-v4 per the 6.3.4.3 adjustment); BPC (On) maps the estimated
    /// <see cref="BlackXyz"/> of each side; absolute intent scales by the media-white ratio and disables BPC.
    /// Returns false when a table or inverse is unavailable. Allocation-free.
    /// </summary>
    public static bool TryConvert(IccProfile source, ReadOnlySpan<double> input, IccProfile destination, RenderingIntent intent,
        BlackPointCompensation blackPointCompensation, Span<double> output)
    {
        if (input.Length < source.Components || output.Length < destination.Components) return false;
        if ((uint)intent > (uint)RenderingIntent.AbsoluteColorimetric) intent = RenderingIntent.RelativeColorimetric;
        Span<double> xyz = stackalloc double[3];
        if (!source.ToPcsXyz(input, intent, xyz)) return false;
        if (intent == RenderingIntent.AbsoluteColorimetric)
        {
            for (var i = 0; i < 3; i++) xyz[i] *= source._whiteScale[i] / destination._whiteScale[i];
        }
        else
        {
            var bpc = blackPointCompensation == BlackPointCompensation.On;
            var from = source.PcsBlackFor(intent, bpc, false);
            var to = destination.PcsBlackFor(intent, bpc, true);
            for (var i = 0; i < 3; i++)
                if (Math.Abs(from[i] - to[i]) > 1e-9)
                    xyz[i] = (xyz[i] - from[i]) * (D50[i] - to[i]) / (D50[i] - from[i]) + to[i];
        }
        return destination.FromPcsXyz(xyz, intent, output);
    }

    static Lut? ReadLut(byte[] data, int o, int size, int nIn, int nOut, bool pcsLab, int major, bool toDevice = false)
    {
        ReadOnlySpan<byte> d = data;
        var sig = BinaryPrimitives.ReadUInt32BigEndian(d[o..]);
        var lut = new Lut { Reverse = toDevice };
        if (sig is 0x6D667431 or 0x6D667432) // mft1 / mft2
        {
            var is16 = sig == 0x6D667432;
            int i = d[o + 8], oc = d[o + 9], g = d[o + 10];
            if (i != nIn || oc != nOut || g < 1 || i > 4 || oc > 4) return null;
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
            var inTables = Tbl(i, nin, ref p);
            lut.Clut = new float[clutLen];
            for (var k = 0; k < clutLen; k++) { lut.Clut[k] = Rd(p); p += bps; }
            lut.Grid = Enumerable.Repeat(g, i).ToArray();
            var outTables = Tbl(oc, nout, ref p);
            // The mAB/mBA names are used internally: A2B = A (input) .. B (output); B2A = B (PCS input) .. A (device output).
            if (toDevice) { lut.B = inTables; lut.A = outTables; } else { lut.A = inTables; lut.B = outTables; }
            lut.Pcs = !pcsLab ? Pcs.Xyz : is16 ? Pcs.LabV2Legacy16 : Pcs.LabV2Lut8;
            if (toDevice && !pcsLab)
            {
                // lut8/lut16 B2A with PCSXYZ input applies the 3x3 matrix first (identity unless the profile says otherwise).
                var mat = new float[9]; var identity = true;
                for (var k = 0; k < 9; k++) { mat[k] = S15(d, o + 12 + 4 * k); if (Math.Abs(mat[k] - (k % 4 == 0 ? 1f : 0f)) > 1e-4f) identity = false; }
                if (!identity) lut.PreMat = mat;
            }
            lut.Prepare();
            return lut;
        }
        if (sig is 0x6D414220 or 0x6D424120) // mAB / mBA
        {
            if ((sig == 0x6D424120) != toDevice) return null;
            int i = d[o + 8], oc = d[o + 9];
            if (i != nIn || oc != nOut || i > 4 || oc > 4) return null;
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
            lut.B = Seq(oB, 3); lut.M = Seq(oM, 3); lut.A = Seq(oA, toDevice ? oc : i);
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
            else if (i != oc) return null;
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

    static void DecodePcs(Pcs pcs, float p0, float p1, float p2, out double X, out double Y, out double Z)
    {
        if (pcs == Pcs.Xyz) { const double s = 65535.0 / 32768.0; X = p0 * s; Y = p1 * s; Z = p2 * s; return; }
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

    /// <summary>Inverse of <see cref="DecodePcs"/>: PCS XYZ to the encoded 0..1 LUT input values.</summary>
    static void EncodePcs(Pcs pcs, double X, double Y, double Z, Span<double> e)
    {
        if (pcs == Pcs.Xyz) { const double s = 32768.0 / 65535.0; e[0] = X * s; e[1] = Y * s; e[2] = Z * s; return; }
        static double F(double t) => t > Cube(6.0 / 29) ? Math.Cbrt(t) : t / (3 * (6.0 / 29) * (6.0 / 29)) + 4.0 / 29;
        double fx = F(Math.Max(X, 0) / 0.9642), fy = F(Math.Max(Y, 0)), fz = F(Math.Max(Z, 0) / 0.8249);
        double L = 116 * fy - 16, a = 500 * (fx - fy), b = 200 * (fy - fz);
        if (pcs == Pcs.LabV2Legacy16) { e[0] = L / 100 * 65280.0 / 65535.0; e[1] = (a + 128) * 256 / 65535.0; e[2] = (b + 128) * 256 / 65535.0; }
        else { e[0] = L / 100; e[1] = (a + 128) / 255; e[2] = (b + 128) / 255; }
    }

    static double Cube(double v) => v * v * v;

    static void PcsToSrgb(Pcs pcs, float p0, float p1, float p2, Span<float> rgb, RenderingIntent intent, double[]? map, double[] whiteScale)
    {
        DecodePcs(pcs, p0, p1, p2, out var X, out var Y, out var Z);
        if (map is not null) { X = X * map[0] + map[3]; Y = Y * map[1] + map[4]; Z = Z * map[2] + map[5]; }
        XyzToSrgb(X, Y, Z, rgb, intent, whiteScale);
    }

    static void XyzToSrgb(double X, double Y, double Z, Span<float> rgb, RenderingIntent intent, double[] whiteScale)
    {
        if (intent == RenderingIntent.AbsoluteColorimetric)
        {
            X *= whiteScale[0]; Y *= whiteScale[1]; Z *= whiteScale[2];
        }
        var r = XyzToRgb[0] * X + XyzToRgb[1] * Y + XyzToRgb[2] * Z;
        var g = XyzToRgb[3] * X + XyzToRgb[4] * Y + XyzToRgb[5] * Z;
        var bl = XyzToRgb[6] * X + XyzToRgb[7] * Y + XyzToRgb[8] * Z;
        rgb[0] = Encode((float)r); rgb[1] = Encode((float)g); rgb[2] = Encode((float)bl);
    }

    /// <summary>D50 PCS XYZ (relative colorimetric) to sRGB 0..1 using the same adaptation and encoding as profile conversion.</summary>
    internal static RgbColor XyzD50ToSrgb(double X, double Y, double Z)
    {
        Span<float> rgb = stackalloc float[3];
        XyzToSrgb(X, Y, Z, rgb, RenderingIntent.RelativeColorimetric, D50);
        return new(rgb[0], rgb[1], rgb[2]);
    }

    void EvalExact(ReadOnlySpan<double> c, Span<float> rgb, RenderingIntent intent = RenderingIntent.RelativeColorimetric, bool bpc = false)
    {
        var transform = bpc ? _bpcTransforms[(int)intent] : _transforms[(int)intent];
        if (transform.Lut is { } lut)
        {
            Span<float> p = stackalloc float[4];
            lut.Eval(c, p);
            PcsToSrgb(lut.Pcs, p[0], p[1], p[2], rgb, intent, transform.Map, _whiteScale);
            return;
        }
        switch (_kind)
        {
            case Kind.Gray:
                {
                    var y = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1));
                    if (intent == RenderingIntent.AbsoluteColorimetric)
                    {
                        XyzToSrgb(0.9642 * y, y, 0.8249 * y, rgb, intent, _whiteScale);
                        break;
                    }
                    var e = Encode(y);
                    rgb[0] = rgb[1] = rgb[2] = e; break;
                }
            case Kind.Matrix:
                {
                    float r = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1)), g = _trc[1].Eval((float)Math.Clamp(c[1], 0, 1)), b = _trc[2].Eval((float)Math.Clamp(c[2], 0, 1));
                    var m = intent == RenderingIntent.AbsoluteColorimetric ? _absoluteM : _m;
                    rgb[0] = Encode(m[0] * r + m[1] * g + m[2] * b);
                    rgb[1] = Encode(m[3] * r + m[4] * g + m[5] * b);
                    rgb[2] = Encode(m[6] * r + m[7] * g + m[8] * b);
                    break;
                }
            default:
                {
                    throw new NotSupportedException($"ICC profile has no transform for {intent}.");
                }
        }
    }

    void DetectSrgb()
    {
        if (_kind != Kind.Matrix || _transforms.Any(t => t.Lut is not null)
            || _whiteScale.Any(s => Math.Abs(s - 1) > 0.0001)) { IsSrgb = false; return; }
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
        => ToSrgb(c, RenderingIntent.RelativeColorimetric);

    public RgbColor ToSrgb(ReadOnlySpan<double> c, RenderingIntent intent)
        => ToSrgb(c, intent, BlackPointCompensation.Default);

    /// <summary>Converts to analytic sRGB (destination black XYZ zero); Default is Off.</summary>
    public RgbColor ToSrgb(ReadOnlySpan<double> c, RenderingIntent intent, BlackPointCompensation blackPointCompensation)
    {
        if ((uint)intent > (uint)RenderingIntent.AbsoluteColorimetric) intent = RenderingIntent.RelativeColorimetric;
        if (blackPointCompensation == BlackPointCompensation.On && SupportsMatrixBlackPointCompensation(intent))
        {
            if (_grayBlack is { } black)
            {
                var y = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1));
                var e = Encode((y - black) / (1 - black));
                return new(e, e, e);
            }
            var m = _blackPointM!;
            var r = _trc[0].Eval((float)Math.Clamp(c[0], 0, 1));
            var g = _trc[1].Eval((float)Math.Clamp(c[1], 0, 1));
            var b = _trc[2].Eval((float)Math.Clamp(c[2], 0, 1));
            return new(Encode(m[0] * r + m[1] * g + m[2] * b + m[9]),
                Encode(m[3] * r + m[4] * g + m[5] * b + m[10]),
                Encode(m[6] * r + m[7] * g + m[8] * b + m[11]));
        }
        var lutBpc = blackPointCompensation == BlackPointCompensation.On && EnsureLutBpc(intent);
        if (IsSrgb) return new(Math.Clamp(c[0], 0, 1), Math.Clamp(c[1], 0, 1), Math.Clamp(c[2], 0, 1));
        Span<float> o = stackalloc float[3];
        var transform = lutBpc ? _bpcTransforms[(int)intent] : _transforms[(int)intent];
        if (transform.Lut is not null && Components >= 3)
        {
            var grid = System.Threading.Volatile.Read(ref transform.Grid);
            if (grid is null && System.Threading.Interlocked.Increment(ref transform.Calls) > 4096)
            {
                lock (transform.Gate)
                {
                    grid = transform.Grid;
                    if (grid is null)
                    {
                        grid = BuildGrid(intent, lutBpc);
                        System.Threading.Volatile.Write(ref transform.Grid, grid);
                    }
                }
            }
            if (grid is not null) { Lookup(grid, c, o); return new(o[0], o[1], o[2]); }
        }
        EvalExact(c, o, intent, lutBpc);
        return new(o[0], o[1], o[2]);
    }

    float[] BuildGrid(RenderingIntent intent, bool bpc)
    {
        var n = GridPoints; var comps = Components;
        var total = 1; for (var i = 0; i < comps; i++) total *= n;
        var g = new float[total * 3];
        Span<double> inp = stackalloc double[comps]; Span<float> o = stackalloc float[3];
        for (var idx = 0; idx < total; idx++)
        {
            var t = idx;
            for (var d = comps - 1; d >= 0; d--) { inp[d] = (t % n) / (double)(n - 1); t /= n; }
            EvalExact(inp, o, intent, bpc);
            g[idx * 3] = o[0]; g[idx * 3 + 1] = o[1]; g[idx * 3 + 2] = o[2];
        }
        return g;
    }

    void Lookup(float[] grid, ReadOnlySpan<double> c, Span<float> o)
    {
        var comps = Components; var n = GridPoints;
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
