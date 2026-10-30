using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.K;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

static class Gen08
{
    public const string Out = "08-colour-spaces.pdf";
    const string Wp = "/WhitePoint [0.9505 1 1.089]";

    static string Fn(double[] c0, double[] c1) =>
        $"<< /FunctionType 2 /Domain [0 1] /C0 [{Join(c0)}] /C1 [{Join(c1)}] /N 1 >>";

    static string Ramp(string cs, double[] c0, double[] c1, double x, double w) =>
        $"<< /ShadingType 2 /ColorSpace {cs} /Coords [{N(x)} 0 {N(x + w)} 0] /Function {Fn(c0, c1)} /Extend [true true] >>";

    public static void Build(string path)
    {
        var d = new Doc("Colour spaces", subject: "DeviceGray/RGB/CMYK, CalGray, CalRGB, Lab, ICCBased, Indexed, Separation, DeviceN", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["white"]);
        PageHeader(pg, t, "Colour spaces", "Colour  /  01", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Every row paints through a different PDF colour space; the reader converts to its output space.", color: P["slate"]);

        var iccRef = d.Stream("/N 3 /Alternate /DeviceRGB", Raster.SrgbIccProfile());
        var spot = d.Add("[/Separation /BrandIndigo /DeviceCMYK " + Fn([0, 0, 0, 0], [0.82, 0.78, 0, 0.08]) + "]");
        var gold = d.Add("[/Separation /Gold /DeviceRGB " + Fn([1, 1, 1], [0.85, 0.65, 0.13]) + "]");
        var dnFn = d.Stream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]",
            Latin1("{ 2 copy 0.04 mul exch 0.95 mul add 1 exch sub 3 1 roll 2 copy 0.38 mul exch 0.42 mul add 1 exch sub 3 1 roll 0.96 mul exch 0.47 mul add 1 exch sub }"), compress: false);
        var devn = d.Add($"[/DeviceN [/Teal /Amber] /DeviceRGB {dnFn} 0 R]");
        string[] pal = [P["ink"], P["indigo"], P["violet"], P["sky"], P["teal"], P["emerald"], P["amber"], P["rose"]];
        var palHex = string.Concat(pal.Select(c => c[1..]));
        const string calRgb = $"[/CalRGB << {Wp} /Gamma [2.2 2.2 2.2] /Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505] >>]";
        const string calGray = $"[/CalGray << {Wp} /Gamma 2.2 >>]";
        const string lab = $"[/Lab << {Wp} /Range [-128 127 -128 127] >>]";

        double bx = M + 190, bw = pg.W - 2 * M - 190, bh = 24;
        var y = pg.H - 150;
        (string Name, string Kind, string Desc)[] rows =
        [
            ("DeviceGray", "/G", "0 g and 1 g ramp"),
            ("DeviceRGB", "/RGB", "additive; sRGB-like"),
            ("DeviceCMYK", "/CMYK", "C, M, Y, K ramps"),
            ("CalGray", "/CAL", "gamma 2.2, D65 white"),
            ("CalRGB", "/CAL", "colorimetric RGB"),
            ("Lab", "/CAL", "L* 55, a* from -90 to +90"),
            ("ICCBased", "/ICC", "embedded sRGB profile, N = 3"),
            ("Indexed", "/IDX", "8-entry palette"),
            ("Separation", "/SEP", "spot colour BrandIndigo, alt. CMYK"),
            ("Separation", "/SEP2", "spot colour Gold, alt. RGB"),
            ("DeviceN", "/DN", "two colorants, calculator tint transform"),
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            var (name, kind, desc) = rows[i];
            var yy = y - i * 50;
            pg.Text(t.SansB, 10, M, yy + 9, name, color: P["ink"]);
            pg.Text(t.Sans, 8, M, yy - 3, desc, color: P["muted"], kern: false);
            pg.Save(); pg.RRect(bx, yy - 4, bw, bh, 4); pg.Clip();
            if (name == "DeviceGray") pg.Sh(Ramp("/DeviceGray", [0], [1], bx, bw));
            else if (name == "DeviceRGB")
                pg.Sh(Axial(bx, 0, bx + bw, 0, [(0, "#FF0000"), (0.2, "#FFFF00"), (0.4, "#00FF00"), (0.6, "#00FFFF"), (0.8, "#0000FF"), (1, "#FF00FF")]));
            else if (name == "DeviceCMYK")
            {
                double[][] ends = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]];
                for (var k = 0; k < 4; k++)
                {
                    pg.Save(); pg.Rect(bx + k * bw / 4, yy - 6, bw / 4, bh + 8); pg.Clip();
                    pg.Sh(Ramp("/DeviceCMYK", [0, 0, 0, 0], ends[k], bx + k * bw / 4, bw / 4));
                    pg.Restore();
                }
            }
            else if (name == "CalGray") pg.Sh(Ramp(calGray, [0], [1], bx, bw));
            else if (name == "CalRGB") pg.Sh(Axial(bx, 0, bx + bw, 0, [(0, "#102040"), (0.5, "#20A0E0"), (1, "#F0FFB0")], cs: calRgb));
            else if (name == "Lab") pg.Sh(Ramp(lab, [55, -90, 60], [55, 90, -60], bx, bw));
            else if (name == "ICCBased")
                pg.Sh(Axial(bx, 0, bx + bw, 0, [(0, P["indigo_d"]), (0.5, P["violet"]), (1, P["amber"])], cs: $"[/ICCBased {iccRef} 0 R]"));
            else if (name == "Indexed")
            {
                var idx = $"[/Indexed /DeviceRGB 7 <{palHex}>]";
                for (var k = 0; k < 8; k++)
                {
                    pg.Raw($"/{pg.Reg("ColorSpace", idx)} cs {k} sc");
                    pg.Rect(bx + k * bw / 8, yy - 6, bw / 8 + 0.5, bh + 8); pg.FillPath();
                }
            }
            else if (kind == "/SEP") pg.Sh(Ramp($"{spot} 0 R", [0], [1], bx, bw));
            else if (kind == "/SEP2") pg.Sh(Ramp($"{gold} 0 R", [0], [1], bx, bw));
            else
            {
                var cs = pg.Reg("ColorSpace", $"{devn} 0 R");
                for (var a = 0; a < 8; a++)
                    for (var b = 0; b < 2; b++)
                    {
                        pg.Raw($"/{cs} cs {N(a / 7.0)} {N(b)} sc");
                        pg.Rect(bx + a * bw / 8, yy - 6 + b * (bh + 8) / 2, bw / 8 + 0.5, (bh + 8) / 2 + 0.5); pg.FillPath();
                    }
            }
            pg.Restore();
            Box(pg, bx, yy - 4, bw, bh, stroke: P["mist"], r: 4);
        }

        var sy = y - 11 * 50 - 6;
        Eyebrow(pg, t, M, sy, "Spot colour tints (scn on a Separation space)");
        var scs = pg.Reg("ColorSpace", $"{spot} 0 R");
        double[] tints = [1, 0.75, 0.5, 0.25, 0.1];
        for (var k = 0; k < tints.Length; k++)
        {
            var x = M + k * 100;
            pg.Save(); pg.Raw($"/{scs} cs {N(tints[k])} scn"); pg.RRect(x, sy - 52, 88, 36, 6); pg.FillPath(); pg.Restore();
            pg.Text(t.Sans, 8, x + 4, sy - 64, $"{(int)(tints[k] * 100)}% BrandIndigo", color: P["slate"], kern: false);
        }
        d.Save(path);
    }
}
