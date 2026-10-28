using Pdf.Core;

namespace Pdf.Content;

public abstract class PdfColorSpace
{
    public abstract int Components { get; }
    public abstract RgbColor ToRgb(ReadOnlySpan<double> c);
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

    sealed class Lab : PdfColorSpace
    {
        public double[] White = [0.9505, 1, 1.089], Range = [-100, 100, -100, 100];
        public override int Components => 3;
        public override double[] DefaultDecode(int bpc) => [0, 100, Range[0], Range[1], Range[2], Range[3]];

        public override RgbColor ToRgb(ReadOnlySpan<double> c)
        {
            var m = (c[0] + 16) / 116; var l = m + c[1] / 500; var n = m - c[2] / 200;
            static double G(double x) => x >= 6.0 / 29 ? x * x * x : 108.0 / 841 * (x - 4.0 / 29);
            double X = White[0] * G(l), Y = White[1] * G(m), Z = White[2] * G(n);
            // D50 -> sRGB via Bradford-adapted matrix.
            var r = 3.1339 * X - 1.6169 * Y - 0.4906 * Z;
            var g = -0.9785 * X + 1.9160 * Y + 0.0333 * Z;
            var b = 0.0720 * X - 0.2290 * Y + 1.4057 * Z;
            static double Gm(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return new(C(Gm(r)), C(Gm(g)), C(Gm(b)));
        }
    }

    sealed class Indexed(PdfColorSpace baseCs, byte[] lookup, int hival) : PdfColorSpace
    {
        public override int Components => 1;
        public override double[] DefaultDecode(int bpc) => [0, (1 << bpc) - 1];

        public override RgbColor ToRgb(ReadOnlySpan<double> c)
        {
            var i = Math.Clamp((int)Math.Round(c[0]), 0, hival);
            var n = baseCs.Components;
            var comps = new double[n];
            var dec = baseCs.DefaultDecode(8);
            for (var k = 0; k < n; k++)
            {
                var o = i * n + k;
                var v = o < lookup.Length ? lookup[o] / 255.0 : 0;
                comps[k] = dec[2 * k] + v * (dec[2 * k + 1] - dec[2 * k]);
            }
            return baseCs.ToRgb(comps);
        }
    }

    sealed class Tint(PdfColorSpace alt, PdfFunction fn, int n, bool allNone) : PdfColorSpace
    {
        public override int Components => n;
        public override double[] InitialColor => Enumerable.Repeat(1.0, n).ToArray();

        public override RgbColor ToRgb(ReadOnlySpan<double> c)
        {
            if (allNone) return new(1, 1, 1);
            return alt.ToRgb(fn.Evaluate(c.ToArray()));
        }
    }

    /// <summary>Pattern colour space; underlying colour space used for uncoloured tiling patterns.</summary>
    public sealed class PatternCs(PdfColorSpace? under) : PdfColorSpace
    {
        public PdfColorSpace? Underlying => under;
        public override int Components => under?.Components ?? 0;
        public override RgbColor ToRgb(ReadOnlySpan<double> c) => under is null ? RgbColor.Black : under.ToRgb(c);
    }

    public static PdfColorSpace Parse(PdfFile f, PdfObject? obj, PdfDictionary? resources = null, int depth = 0)
    {
        obj = f.Resolve(obj);
        if (depth > 8) return Gray;
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
                return Parse(f, named, resources, depth + 1);
            return Gray;
        }
        if (obj is not PdfArray a || a.Count == 0) return Gray;
        var kind = f.Resolve(a[0]).AsName();
        switch (kind)
        {
            case "DeviceGray" or "G" or "CalGray": return Gray;
            case "DeviceRGB" or "RGB" or "CalRGB": return Rgb;
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
                        // ICC profile transforms are not applied; the component count selects the matching device space.
                        return n2 switch { 1 => Gray, 4 => Cmyk, _ => Rgb };
                    }
                    return Rgb;
                }
            case "Indexed" or "I":
                {
                    if (a.Count < 4) return Gray;
                    var bcs = Parse(f, a[1], resources, depth + 1);
                    var hival = (int)(f.Resolve(a[2]).AsNumber() ?? 255);
                    var lookup = f.Resolve(a[3]) switch { PdfString ps => ps.Bytes, PdfStream st => f.DecodeStream(st), _ => [] };
                    return new Indexed(bcs, lookup, hival);
                }
            case "Separation" or "DeviceN":
                {
                    if (a.Count < 4) return Gray;
                    var names = kind == "Separation" ? [f.Resolve(a[1]).AsName() ?? ""] : (f.Resolve(a[1]) as PdfArray)?.Items.Select(x => f.Resolve(x).AsName() ?? "").ToArray() ?? [];
                    var alt = Parse(f, a[2], resources, depth + 1);
                    var fn = PdfFunction.Parse(f, a[3]);
                    if (fn is null) return Gray;
                    return new Tint(alt, fn, Math.Max(1, names.Length), names.Length > 0 && names.All(x => x == "None"));
                }
            case "Pattern":
                return new PatternCs(a.Count > 1 ? Parse(f, a[1], resources, depth + 1) : null);
        }
        return Gray;
    }
}
