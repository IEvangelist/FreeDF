using PortableDocument.Core;

namespace PortableDocument.Content;

public abstract class PdfColorSpace
{
    public abstract int Components { get; }
    /// <summary>Colorant names of a Separation or DeviceN space; null for every other space.</summary>
    public virtual IReadOnlyList<string>? Colorants => null;
    public abstract RgbColor ToRgb(ReadOnlySpan<double> c);
    public virtual RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent) => ToRgb(c);
    public virtual RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent, BlackPointCompensation blackPointCompensation) => ToRgb(c, intent);
    public virtual double[] InitialColor => new double[Components];
    public virtual double[] DefaultDecode(int bpc) => Enumerable.Range(0, Components).SelectMany(_ => new double[] { 0, 1 }).ToArray();

    public static readonly PdfColorSpace Gray = new DeviceGray();
    public static readonly PdfColorSpace Rgb = new DeviceRgb();
    public static readonly PdfColorSpace Cmyk = new DeviceCmyk();

    static double C(double v) => Math.Clamp(v, 0, 1);

    sealed class DeviceGray : PdfColorSpace
    {
        public override int Components => 1;
        public override RgbColor ToRgb(ReadOnlySpan<double> c) { var g = C(c[0]); return new(g, g, g); }
    }

    sealed class DeviceRgb : PdfColorSpace
    {
        public override int Components => 3;
        public override RgbColor ToRgb(ReadOnlySpan<double> c) => new(C(c[0]), C(c[1]), C(c[2]));
    }

    sealed class DeviceCmyk : PdfColorSpace
    {
        public override int Components => 4;
        public override double[] InitialColor => [0, 0, 0, 1];

        public override RgbColor ToRgb(ReadOnlySpan<double> v)
        {
            double c = C(v[0]), m = C(v[1]), y = C(v[2]), k = C(v[3]);
            // Polynomial approximation of SWOP CMYK (as used by pdf.js) rather than naive 1-min(1,c+k).
            var r = 255 + c * (-4.387332384609988 * c + 54.48615194189176 * m + 18.82290502165302 * y + 212.25662451639585 * k + -285.2331026137004)
                + m * (1.7149763477362134 * m - 5.6096736904047315 * y + -17.873870861415444 * k - 5.497006427196366)
                + y * (-2.5217340131683033 * y - 21.248923337353073 * k + 17.5119270841813)
                + k * (-21.86122147463605 * k - 189.48180835922747);
            var g = 255 + c * (8.841041422036149 * c + 60.118027045597366 * m + 6.871425592049007 * y + 31.159100130055922 * k - 79.2970844816548)
                + m * (-15.310361306967817 * m + 17.575251261109482 * y + 131.35250912493976 * k - 190.9453302588951)
                + y * (4.444339102852739 * y + 9.8632861493405 * k - 24.86741582555878)
                + k * (-20.737325471181034 * k - 187.80453709719578);
            var b = 255 + c * (0.8842522430003296 * c + 8.078677503112928 * m + 30.89978309703729 * y - 0.23883238689178934 * k + -14.183576799673286)
                + m * (10.49593273432072 * m + 63.02378494754052 * y + 50.606957656360734 * k - 112.23884253719248)
                + y * (0.03296041114873217 * y + 115.60384449646641 * k + -193.58209356861505)
                + k * (-22.33816807309886 * k - 180.12613974708367);
            return new(C(r / 255), C(g / 255), C(b / 255));
        }
    }

    sealed record IccResult(PdfColorSpace? Space);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfStream, IccResult> IccCache = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IccProfile?> ProfilesByHash = new();

    static PdfColorSpace Device(int n) => n switch { 1 => Gray, 4 => Cmyk, _ => Rgb };

    /// <summary>Caches only the profile result; an Alternate may depend on the current resources.</summary>
    static IccResult BuildIcc(PdfFile f, PdfStream s, int n)
    {
        byte[] data;
        try { data = f.DecodeStream(s); } catch (Exception) { return new(null); }
        var key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(data));
        var prof = ProfilesByHash.GetOrAdd(key, _ => IccProfile.TryParse(data));
        if (prof is null || prof.Components != n) return new(null);
        // Keep the profile: the approximate sRGB shortcut must not discard explicit BPC context.
        return new(new IccBased(prof));
    }

    sealed class IccBased(IccProfile profile) : PdfColorSpace
    {
        public override int Components => profile.Components;
        public override RgbColor ToRgb(ReadOnlySpan<double> c) => profile.ToSrgb(c);
        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent) => profile.ToSrgb(c, intent);
        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent, BlackPointCompensation blackPointCompensation) => profile.ToSrgb(c, intent, blackPointCompensation);
    }

    /// <summary>CIE XYZ (relative to a given white point) to sRGB: Bradford adaptation to D65, linear sRGB matrix, sRGB transfer curve.</summary>
    sealed class XyzToSrgb
    {
        readonly double[] _m = new double[9];

        public XyzToSrgb(double xw, double yw, double zw)
        {
            double[] br = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];
            double[] brInv = Inv(br);
            double[] d65 = [0.95047, 1, 1.08883];
            var src = Mul(br, [xw, yw, zw]); var dst = Mul(br, d65);
            double[] scale = [dst[0] / src[0], dst[1] / src[1], dst[2] / src[2]];
            double[] sc = new double[9];
            for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) sc[i * 3 + j] = brInv[i * 3 + j] * scale[j];
            var adapt = Mul3(sc, br);
            double[] srgb = [3.2404542, -1.5371385, -0.4985314, -0.9692660, 1.8760108, 0.0415560, 0.0556434, -0.2040259, 1.0572252];
            Array.Copy(Mul3(srgb, adapt), _m, 9);
        }

        static double[] Mul(double[] m, double[] v) => [m[0] * v[0] + m[1] * v[1] + m[2] * v[2], m[3] * v[0] + m[4] * v[1] + m[5] * v[2], m[6] * v[0] + m[7] * v[1] + m[8] * v[2]];
        static double[] Mul3(double[] a, double[] b)
        {
            var r = new double[9];
            for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) for (var k = 0; k < 3; k++) r[i * 3 + j] += a[i * 3 + k] * b[k * 3 + j];
            return r;
        }
        static double[] Inv(double[] m)
        {
            var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
            return [(m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
                    (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
                    (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det];
        }

        static double Enc(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

        public RgbColor Convert(double x, double y, double z) => new(
            C(Enc(_m[0] * x + _m[1] * y + _m[2] * z)), C(Enc(_m[3] * x + _m[4] * y + _m[5] * z)), C(Enc(_m[6] * x + _m[7] * y + _m[8] * z)));
    }

    static double[] Nums(PdfFile f, PdfObject? o, int n, double[] def)
    {
        if (f.Resolve(o) is not PdfArray a || a.Count < n) return def;
        var r = new double[n];
        for (var i = 0; i < n; i++) r[i] = f.Resolve(a[i]).AsNumber() ?? def[i];
        return r;
    }

    sealed class CalGray(double[] white, double gamma) : PdfColorSpace
    {
        readonly XyzToSrgb _x = new(white[0], white[1], white[2]);
        public override int Components => 1;
        public override RgbColor ToRgb(ReadOnlySpan<double> c)
        {
            var y = Math.Pow(C(c[0]), gamma);
            return _x.Convert(white[0] * y, white[1] * y, white[2] * y);
        }
    }

    sealed class CalRgb(double[] white, double[] gamma, double[] matrix) : PdfColorSpace
    {
        readonly XyzToSrgb _x = new(white[0], white[1], white[2]);
        public override int Components => 3;
        public override RgbColor ToRgb(ReadOnlySpan<double> c)
        {
            double a = Math.Pow(C(c[0]), gamma[0]), b = Math.Pow(C(c[1]), gamma[1]), cc = Math.Pow(C(c[2]), gamma[2]);
            return _x.Convert(matrix[0] * a + matrix[3] * b + matrix[6] * cc, matrix[1] * a + matrix[4] * b + matrix[7] * cc, matrix[2] * a + matrix[5] * b + matrix[8] * cc);
        }
    }

    static PdfColorSpace BuildCal(PdfFile f, PdfObject? dictObj, bool rgb)
    {
        if (f.Resolve(dictObj) is not PdfDictionary d) return rgb ? Rgb : Gray;
        var white = Nums(f, d.Get("WhitePoint"), 3, [0.9505, 1, 1.089]);
        if (white[1] <= 0 || white[0] <= 0 || white[2] <= 0) return rgb ? Rgb : Gray;
        if (!rgb) return new CalGray(white, f.Resolve(d.Get("Gamma")).AsNumber() ?? 1);
        return new CalRgb(white, Nums(f, d.Get("Gamma"), 3, [1, 1, 1]), Nums(f, d.Get("Matrix"), 9, [1, 0, 0, 0, 1, 0, 0, 0, 1]));
    }

    sealed class Lab : PdfColorSpace
    {
        public double[] White = [0.9505, 1, 1.089], Range = [-100, 100, -100, 100];
        XyzToSrgb? _x;
        public override int Components => 3;
        public override double[] DefaultDecode(int bpc) => [0, 100, Range[0], Range[1], Range[2], Range[3]];

        public override RgbColor ToRgb(ReadOnlySpan<double> c)
        {
            _x ??= new XyzToSrgb(White[0], White[1], White[2]);
            var ls = Math.Clamp(c[0], 0, 100);
            static double Cl(double v, double a, double b) => Math.Clamp(v, Math.Min(a, b), Math.Max(a, b));
            var m = (ls + 16) / 116; var l = m + Cl(c[1], Range[0], Range[1]) / 500; var n = m - Cl(c[2], Range[2], Range[3]) / 200;
            static double G(double x) => x >= 6.0 / 29 ? x * x * x : 108.0 / 841 * (x - 4.0 / 29);
            return _x.Convert(White[0] * G(l), White[1] * G(m), White[2] * G(n));
        }
    }

    sealed class Indexed(PdfColorSpace baseCs, byte[] lookup, int hival) : PdfColorSpace
    {
        readonly double[] _decode = baseCs.DefaultDecode(8);
        public override int Components => 1;
        public override double[] DefaultDecode(int bpc) => [0, (1 << bpc) - 1];

        public override RgbColor ToRgb(ReadOnlySpan<double> c) => ToRgb(c, RenderingIntent.RelativeColorimetric);

        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent)
            => ToRgb(c, intent, BlackPointCompensation.Default);

        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent, BlackPointCompensation blackPointCompensation)
        {
            var i = Math.Clamp((int)Math.Round(c[0]), 0, hival);
            var n = baseCs.Components;
            Span<double> comps = n <= 16 ? stackalloc double[n] : new double[n];
            var dec = _decode;
            for (var k = 0; k < n; k++)
            {
                var o = i * n + k;
                var v = o < lookup.Length ? lookup[o] / 255.0 : 0;
                comps[k] = dec[2 * k] + v * (dec[2 * k + 1] - dec[2 * k]);
            }
            return baseCs.ToRgb(comps, intent, blackPointCompensation);
        }
    }

    sealed class Tint(PdfColorSpace alt, PdfFunction fn, int n, bool allNone, string[] names) : PdfColorSpace
    {
        public override int Components => n;
        public override IReadOnlyList<string>? Colorants => names;
        public override double[] InitialColor => Enumerable.Repeat(1.0, n).ToArray();

        public override RgbColor ToRgb(ReadOnlySpan<double> c) => ToRgb(c, RenderingIntent.RelativeColorimetric);

        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent)
            => ToRgb(c, intent, BlackPointCompensation.Default);

        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent, BlackPointCompensation blackPointCompensation)
        {
            if (allNone) return new(1, 1, 1);
            return alt.ToRgb(fn.Evaluate(c.ToArray()), intent, blackPointCompensation);
        }
    }

    /// <summary>Pattern colour space; underlying colour space used for uncoloured tiling patterns.</summary>
    public sealed class PatternCs(PdfColorSpace? under) : PdfColorSpace
    {
        public PdfColorSpace? Underlying => under;
        public override int Components => under?.Components ?? 0;
        public override RgbColor ToRgb(ReadOnlySpan<double> c) => under is null ? RgbColor.Black : under.ToRgb(c);
        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent) => under is null ? RgbColor.Black : under.ToRgb(c, intent);
        public override RgbColor ToRgb(ReadOnlySpan<double> c, RenderingIntent intent, BlackPointCompensation blackPointCompensation) => under is null ? RgbColor.Black : under.ToRgb(c, intent, blackPointCompensation);
    }

    public static PdfColorSpace Parse(PdfFile f, PdfObject? obj, PdfDictionary? resources = null, int depth = 0)
        => TryParse(f, obj, resources, depth) ?? Gray;

    static PdfColorSpace? TryParse(PdfFile f, PdfObject? obj, PdfDictionary? resources, int depth)
    {
        obj = f.Resolve(obj);
        if (depth > 8) return null;
        if (obj is PdfName n)
        {
            switch (n.Value)
            {
                case "DeviceGray" or "G" or "CalGray": return Gray;
                case "DeviceRGB" or "RGB" or "CalRGB": return Rgb;
                case "DeviceCMYK" or "CMYK": return Cmyk;
                case "Pattern": return new PatternCs(null);
            }
            if (resources is not null && f.Resolve(resources.Get("ColorSpace")) is PdfDictionary csd && csd.Get(n.Value) is { } named)
                return TryParse(f, named, resources, depth + 1);
            return null;
        }
        if (obj is not PdfArray a || a.Count == 0) return null;
        var kind = f.Resolve(a[0]).AsName();
        switch (kind)
        {
            case "DeviceGray" or "G": return Gray;
            case "CalGray": return BuildCal(f, a.Count > 1 ? a[1] : null, false);
            case "DeviceRGB" or "RGB": return Rgb;
            case "CalRGB": return BuildCal(f, a.Count > 1 ? a[1] : null, true);
            case "DeviceCMYK" or "CMYK": return Cmyk;
            case "Lab":
                {
                    var lab = new Lab();
                    if (a.Count > 1 && f.Resolve(a[1]) is PdfDictionary d)
                    {
                        if (f.Resolve(d.Get("WhitePoint")) is PdfArray wp && wp.Count >= 3) lab.White = wp.Items.Select(x => f.Resolve(x).AsNumber() ?? 1).ToArray();
                        if (f.Resolve(d.Get("Range")) is PdfArray r && r.Count >= 4) lab.Range = r.Items.Select(x => f.Resolve(x).AsNumber() ?? 0).ToArray();
                    }
                    return lab;
                }
            case "ICCBased":
                {
                    if (a.Count > 1 && f.Resolve(a[1]) is PdfStream s)
                    {
                        var n2 = (int)(f.Resolve(s.Dict.Get("N")).AsNumber() ?? 3);
                        var profile = IccCache.GetValue(s, _ => BuildIcc(f, s, n2)).Space;
                        if (profile is not null) return profile;
                        var alternate = TryParse(f, s.Dict.Get("Alternate"), resources, depth + 1);
                        return alternate is not null and not PatternCs && alternate.Components == n2 ? alternate : Device(n2);
                    }
                    return Rgb;
                }
            case "Indexed" or "I":
                {
                    if (a.Count < 4) return null;
                    var bcs = Parse(f, a[1], resources, depth + 1);
                    var hival = (int)(f.Resolve(a[2]).AsNumber() ?? 255);
                    var lookup = f.Resolve(a[3]) switch { PdfString ps => ps.Bytes, PdfStream st => f.DecodeStream(st), _ => [] };
                    return new Indexed(bcs, lookup, hival);
                }
            case "Separation" or "DeviceN":
                {
                    if (a.Count < 4) return null;
                    var names = kind == "Separation" ? [f.Resolve(a[1]).AsName() ?? ""] : (f.Resolve(a[1]) as PdfArray)?.Items.Select(x => f.Resolve(x).AsName() ?? "").ToArray() ?? [];
                    var alt = Parse(f, a[2], resources, depth + 1);
                    var fn = PdfFunction.Parse(f, a[3]);
                    if (fn is null) return null;
                    return new Tint(alt, fn, Math.Max(1, names.Length), names.Length > 0 && names.All(x => x == "None"), names);
                }
            case "Pattern":
                return new PatternCs(a.Count > 1 ? Parse(f, a[1], resources, depth + 1) : null);
        }
        return null;
    }
}
