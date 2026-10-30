namespace PortableDocument.Content;

/// <summary>
/// Blending space of a transparency group (ISO 32000-2 11.4.7, 11.7.5.3) for spaces other than DeviceGray/DeviceRGB.
/// The rasterizer keeps layers as output sRGB; blend functions are evaluated on these components instead (11.3.5), which is exact
/// for the blend step and approximate only for Normal compositing of partial alpha in a non-linear space.
/// </summary>
public abstract class GroupBlendSpace
{
    public abstract int Components { get; }
    /// <summary>True for CMYK-like spaces whose components are complemented before the (additive) blend functions run (11.3.5.2).</summary>
    public virtual bool Subtractive => false;
    /// <summary>True when the space has fewer than three components, so colours must be projected into it before compositing (10.4.2.2).</summary>
    public virtual bool Lossy => false;
    public abstract void FromRgb(float r, float g, float b, Span<float> c);
    public abstract void ToRgb(ReadOnlySpan<float> c, out float r, out float g, out float b);

    protected static float Dec(float v) => v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    protected static float Enc(float v) => v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1 / 2.4f) - 0.055f;
    protected static float Clamp(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    /// <summary>DeviceCMYK / ICC CMYK: the spec's DeviceCMYK to DeviceRGB formula R = 1 - min(1, C + K) (10.3.5), inverted with maximal black generation.</summary>
    public static GroupBlendSpace Cmyk { get; } = new CmykSpace();

    public static GroupBlendSpace CalGray(double gamma) => new CalGraySpace(gamma);

    /// <summary>Returns null when the matrix is singular and the space cannot be inverted.</summary>
    public static GroupBlendSpace? CalRgb(double[] white, double[] gamma, double[] matrix) => CalRgbSpace.TryCreate(white, gamma, matrix);

    sealed class CmykSpace : GroupBlendSpace
    {
        public override int Components => 4;
        public override bool Subtractive => true;

        public override void FromRgb(float r, float g, float b, Span<float> c)
        {
            var m = MathF.Max(r, MathF.Max(g, b));
            c[0] = m - r; c[1] = m - g; c[2] = m - b; c[3] = 1f - m;
        }

        public override void ToRgb(ReadOnlySpan<float> c, out float r, out float g, out float b)
        {
            var k = Clamp(c[3]);
            r = 1f - MathF.Min(1f, Clamp(c[0]) + k); g = 1f - MathF.Min(1f, Clamp(c[1]) + k); b = 1f - MathF.Min(1f, Clamp(c[2]) + k);
        }
    }

    sealed class CalGraySpace(double gamma) : GroupBlendSpace
    {
        readonly float _g = (float)(gamma > 0 ? gamma : 1);
        public override int Components => 1;
        public override bool Lossy => true;

        // CIE luminance of the sRGB colour; Y^(1/gamma) is the CalGray component (8.6.5.2).
        public override void FromRgb(float r, float g, float b, Span<float> c)
            => c[0] = Clamp(MathF.Pow(Clamp(0.2126f * Dec(r) + 0.7152f * Dec(g) + 0.0722f * Dec(b)), 1f / _g));

        public override void ToRgb(ReadOnlySpan<float> c, out float r, out float g, out float b)
            => r = g = b = Clamp(Enc(MathF.Pow(Clamp(c[0]), _g)));
    }

    sealed class CalRgbSpace : GroupBlendSpace
    {
        readonly float[] _toLinear = new float[9]; // CalRGB (A^g, B^g, C^g) -> linear sRGB
        readonly float[] _fromLinear = new float[9];
        readonly float[] _gamma = new float[3];

        CalRgbSpace() { }

        public override int Components => 3;

        public static CalRgbSpace? TryCreate(double[] white, double[] gamma, double[] matrix)
        {
            if (white.Length < 3 || gamma.Length < 3 || matrix.Length < 9 || white[0] <= 0 || white[1] <= 0 || white[2] <= 0) return null;
            if (gamma.Any(g => !(g > 0))) return null;
            // XYZ(white) -> linear sRGB: Bradford adaptation to D65 followed by the sRGB matrix, as in the forward conversion.
            double[] br = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];
            var brInv = Inv(br);
            if (brInv is null) return null;
            var src = Mul(br, [white[0], white[1], white[2]]); var dst = Mul(br, [0.95047, 1, 1.08883]);
            var sc = new double[9];
            for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) sc[i * 3 + j] = brInv[i * 3 + j] * (dst[j] / src[j]);
            var adapt = Mul3(sc, br);
            double[] srgb = [3.2404542, -1.5371385, -0.4985314, -0.9692660, 1.8760108, 0.0415560, 0.0556434, -0.2040259, 1.0572252];
            var s = Mul3(srgb, adapt);
            // CalRGB Matrix is [XA YA ZA XB YB ZB XC YC ZC]: XYZ = column vectors A, B, C scaled by the linearised components.
            double[] m = [matrix[0], matrix[3], matrix[6], matrix[1], matrix[4], matrix[7], matrix[2], matrix[5], matrix[8]];
            var lin = Mul3(s, m);
            var inv = Inv(lin);
            if (inv is null) return null;
            var r = new CalRgbSpace();
            for (var i = 0; i < 9; i++) { r._toLinear[i] = (float)lin[i]; r._fromLinear[i] = (float)inv[i]; }
            for (var i = 0; i < 3; i++) r._gamma[i] = (float)gamma[i];
            return r;
        }

        public override void FromRgb(float r, float g, float b, Span<float> c)
        {
            float lr = Dec(r), lg = Dec(g), lb = Dec(b);
            var f = _fromLinear;
            for (var i = 0; i < 3; i++)
                c[i] = Clamp(MathF.Pow(Clamp(f[i * 3] * lr + f[i * 3 + 1] * lg + f[i * 3 + 2] * lb), 1f / _gamma[i]));
        }

        public override void ToRgb(ReadOnlySpan<float> c, out float r, out float g, out float b)
        {
            float a = MathF.Pow(Clamp(c[0]), _gamma[0]), bb = MathF.Pow(Clamp(c[1]), _gamma[1]), cc = MathF.Pow(Clamp(c[2]), _gamma[2]);
            var t = _toLinear;
            r = Clamp(Enc(Clamp(t[0] * a + t[1] * bb + t[2] * cc)));
            g = Clamp(Enc(Clamp(t[3] * a + t[4] * bb + t[5] * cc)));
            b = Clamp(Enc(Clamp(t[6] * a + t[7] * bb + t[8] * cc)));
        }

        static double[] Mul(double[] m, double[] v) => [m[0] * v[0] + m[1] * v[1] + m[2] * v[2], m[3] * v[0] + m[4] * v[1] + m[5] * v[2], m[6] * v[0] + m[7] * v[1] + m[8] * v[2]];

        static double[] Mul3(double[] a, double[] b)
        {
            var r = new double[9];
            for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) for (var k = 0; k < 3; k++) r[i * 3 + j] += a[i * 3 + k] * b[k * 3 + j];
            return r;
        }

        static double[]? Inv(double[] m)
        {
            var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
            if (Math.Abs(det) < 1e-9) return null;
            return [(m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
                    (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
                    (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det];
        }
    }
}

/// <summary>Carries a <see cref="GroupBlendSpace"/> through <see cref="IRenderBackend.BeginGroup(bool, bool, double, string, PdfColorSpace?)"/>.</summary>
public sealed class GroupBlendColorSpace(GroupBlendSpace space) : PdfColorSpace
{
    public GroupBlendSpace Space { get; } = space;
    public override int Components => Space.Components;

    public override RgbColor ToRgb(ReadOnlySpan<double> c)
    {
        Span<float> v = stackalloc float[4];
        for (var i = 0; i < Components; i++) v[i] = (float)c[i];
        Space.ToRgb(v, out var r, out var g, out var b);
        return new(r, g, b);
    }
}
