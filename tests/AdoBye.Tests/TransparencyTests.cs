using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

public class TransparencyTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y)
    {
        var o = (y * p.Width + x) * 4;
        return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]);
    }

    static RenderedPage Render(string content, string res, string objs = "", string box = "[0 0 200 200]")
        => DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(content, res, objs, box, brokenXref: true)).Pages[0], 72);

    static string Stream(int n, string dict, string data)
        => $"{n} 0 obj\n<< {dict} /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n";

    static void Near((int R, int G, int B) a, (int, int, int) e, int tol = 3)
    {
        Assert.True(Math.Abs(a.R - e.Item1) <= tol && Math.Abs(a.G - e.Item2) <= tol && Math.Abs(a.B - e.Item3) <= tol, $"expected {e} got {a}");
    }

    static string GroupForm(string groupFlags, string content)
        => Stream(5, $"/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency {groupFlags} >> /Resources << /ExtGState << /M << /BM /Multiply >> /A << /ca 0.5 >> >> >>", content);

    const string RedPage = "1 0 0 rg 0 0 200 200 re f ";

    static PdfPage GroupPage(string content, string? group, string resources = "", string objects = "", string extraPage = "")
    {
        var pdf = Encoding.Latin1.GetString(TestPdf.Build(content, resources, objects, brokenXref: true));
        var attributes = group is null ? "" : $"/Group << /S /Transparency {group} >>";
        pdf = pdf.Replace("/Type /Page /Parent", $"/Type /Page {attributes} {extraPage} /Parent");
        return PdfDocument.Load(Encoding.Latin1.GetBytes(pdf)).Pages[0];
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/I false")]
    [InlineData("/I true")]
    public void OutputPage_IsIsolatedRegardlessOfPageFlag(string? flags)
    {
        // 11.4.7: Difference must not see the paper. PDFium 156 also returns red.
        var page = GroupPage("/B gs 1 0 0 rg 0 0 100 200 re f", flags, "/ExtGState << /B << /BM /Difference /ca 0.5 >> >>");
        var p = DocumentRenderer.RenderPage(page, 72);
        Near(Px(p, 50, 100), (255, 128, 128), 1);
        Near(Px(p, 150, 100), (255, 255, 255), 0);
        Assert.All(Enumerable.Range(0, p.Width * p.Height), i => Assert.Equal(255, p.Rgba[i * 4 + 3]));
        Assert.Empty(p.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/I false")]
    [InlineData("/I true")]
    public void PageGroup_GrayConvertsSourcesBeforeBlending(string flags)
    {
        const string content = RedPage + "/M gs 0 0 1 rg 0 0 200 200 re f";
        const string res = "/ExtGState << /M << /BM /Multiply >> >>";
        var gray = DocumentRenderer.RenderPage(GroupPage(content, flags + " /CS /DeviceGray", res), 72);
        var rgb = DocumentRenderer.RenderPage(GroupPage(content, flags + " /CS /DeviceRGB", res), 72);
        // 10.4.2.2, 11.6.6: gray(red) * gray(blue) = .3 * .11, not gray(red * blue).
        Near(Px(gray, 100, 100), (8, 8, 8), 1);
        Near(Px(rgb, 100, 100), (0, 0, 0), 0);
        Assert.Empty(gray.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/I false")]
    [InlineData("/I true")]
    public void PageGroup_GrayConvertsBeforeFlatteningOntoPaper(string flags)
    {
        var p = DocumentRenderer.RenderPage(GroupPage("/B gs " + RedPage, flags + " /CS /DeviceGray",
            "/ExtGState << /B << /BM /Difference /ca 0.5 >> >>"), 72);
        Near(Px(p, 100, 100), (166, 166, 166), 1);
    }

    [Theory]
    [InlineData(false, 97)]
    [InlineData(true, 142)]
    public void PageGroup_GrayPreservesKnockout(bool knockout, int expected)
    {
        var page = GroupPage("/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f",
            $"/CS /DeviceGray /K {knockout.ToString().ToLowerInvariant()}", "/ExtGState << /A << /ca 0.5 >> >>");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (expected, expected, expected), 1);
    }

    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("/I false", 0, 0, 0)]
    [InlineData("/I true", 0, 255, 255)]
    public void PlacedPage_IsolationControlsWhetherItSeesBackdrop(string flags, int r, int g, int b)
    {
        using var raster = new SoftwareRasterizer(200, 200);
        var rect = new PathData(); rect.Rect(0, 0, 200, 200);
        raster.Fill(rect, Matrix.Identity, false, new RgbColor(1, 0, 0), 1, "Normal");
        var page = GroupPage("/M gs 0 1 1 rg 0 0 200 200 re f", flags, "/ExtGState << /M << /BM /Multiply >> >>");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (r, g, b), 1);
    }

    [Theory]
    [InlineData("/I false", 0, 255, 255)]
    [InlineData("/I true", 179, 179, 179)]
    public void PlacedPage_ColorSpaceIsHonouredOnlyWhenIsolated(string flags, int r, int g, int b)
    {
        using var raster = new SoftwareRasterizer(200, 200);
        var page = GroupPage("0 1 1 rg 0 0 200 200 re f", flags + " /CS /DeviceGray");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (r, g, b), 1);
    }

    [Fact]
    public void PlacedPage_NonIsolatedInheritsGrayAndIgnoresUnsupportedCS()
    {
        using var raster = new SoftwareRasterizer(200, 200);
        raster.BeginGroup(true, false, 1, "Normal", PdfColorSpace.Gray);
        var rect = new PathData(); rect.Rect(0, 0, 200, 200);
        raster.Fill(rect, Matrix.Identity, false, new RgbColor(1, 0, 0), 1, "Normal");
        var page = GroupPage("/M gs 0 1 1 rg 0 0 200 200 re f", "/CS /DeviceCMYK",
            "/ExtGState << /M << /BM /Multiply >> >>");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        raster.EndGroup();
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (54, 54, 54), 1);
    }

    [Fact]
    public void PlacedPage_ClipsToCropBoxAndRestoresParentSpaceAndClip()
    {
        using var raster = new SoftwareRasterizer(300, 300);
        var page = GroupPage("1 0 0 rg -100 -100 400 400 re f", "/I true /CS /DeviceGray");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Translate(50, 50), asPageElement: true);
        var rect = new PathData(); rect.Rect(0, 0, 20, 20);
        raster.Fill(rect, Matrix.Identity, false, new RgbColor(1, 0, 0), 1, "Normal");
        var p = new RenderedPage(300, 300, raster.ToRgba(), []);
        Near(Px(p, 100, 100), (77, 77, 77), 1);
        Near(Px(p, 25, 100), (255, 255, 255), 0);
        Near(Px(p, 10, 10), (255, 0, 0), 0);
    }

    [Theory]
    [InlineData("/CS /Blend", "/ColorSpace << /Blend /DeviceGray >>")]
    [InlineData("/CS 5 0 R", "")]
    [InlineData("/CS /DeviceGray", "/ColorSpace << /DefaultGray /DeviceGray >>")]
    public void PageGroup_ColorSpaceResolvesNamesReferencesAndDefaults(string group, string resources)
    {
        var page = GroupPage(RedPage, group, resources, "5 0 obj /DeviceGray endobj\n");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (77, 77, 77), 1);
    }

    [Theory]
    [InlineData("[/Lab << /WhitePoint [0.9505 1 1.089] >>]")]
    [InlineData("[/Indexed /DeviceRGB 0 <FF0000>]")]
    [InlineData("[/Separation /Spot /DeviceRGB << >>]")]
    [InlineData("[/DeviceN [/Spot] /DeviceRGB << >>]")]
    [InlineData("[/CalGray << /Gamma 2 >>]")]
    [InlineData("[/CalRGB << /WhitePoint [0.9505 1 1.089] /Matrix [1 1 1 1 1 1 1 1 1] >>]")]
    [InlineData("[/ICCBased 5 0 R]")]
    [InlineData("/Pattern")]
    [InlineData("/Missing")]
    public void PageGroup_UnsupportedCSFailsInsteadOfFallingBack(string space)
    {
        // 11.4.7 limits /CS to Device*, CalGray, CalRGB and ICCBased; singular/invalid definitions cannot be inverted.
        var page = GroupPage(RedPage, "/CS " + space, objects: Stream(5, "/N 3", "invalid ICC profile"));
        var e = Assert.Throws<NotSupportedException>(() => DocumentRenderer.RenderPage(page, 72));
        Assert.Contains("Page transparency group colour space", e.Message);
    }

    [Fact]
    public void PageGroup_UnsupportedDefaultSpaceFailsInsteadOfFallingBack()
    {
        var page = GroupPage(RedPage, "/CS /DeviceRGB", "/ColorSpace << /DefaultRGB [/ICCBased 5 0 R] >>",
            Stream(5, "/N 3", "invalid ICC profile"));
        Assert.Throws<NotSupportedException>(() => DocumentRenderer.RenderPage(page, 72));
    }

    [Fact]
    public void PageGroup_CyclicColorSpaceFailsExplicitly()
    {
        var page = GroupPage(RedPage, "/CS /Loop", "/ColorSpace << /Loop /Loop >>");
        Assert.Throws<PdfParseException>(() => DocumentRenderer.RenderPage(page, 72));
    }

    [Theory]
    [InlineData("/CS null", "")]
    [InlineData("/CS 5 0 R", "")]
    [InlineData("/CS 99 0 R", "")]
    [InlineData("/CS /DeviceRGB", "/ColorSpace << /DefaultRGB 99 0 R >>")]
    public void PageGroup_NullEntriesAndReferencesToMissingObjectsAreAbsent(string group, string resources)
    {
        // 7.3.9/7.3.10: null dictionary entries and references to missing objects are absent.
        var page = GroupPage(RedPage, group, resources, "5 0 obj null endobj\n");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (255, 0, 0), 0);
    }

    [Fact]
    public void PageGroup_InvalidDefaultComponentCountFailsExplicitly()
    {
        var page = GroupPage(RedPage, "/CS /DeviceRGB", "/ColorSpace << /DefaultRGB /DeviceGray >>");
        Assert.Throws<PdfParseException>(() => DocumentRenderer.RenderPage(page, 72));
    }

    [Fact]
    public void PlacedPage_ResolvesIndirectGroupAttributes()
    {
        var page = GroupPage(RedPage, null, objects:
            "5 0 obj << /S 6 0 R /I 7 0 R /CS 8 0 R >> endobj\n" +
            "6 0 obj /Transparency endobj\n7 0 obj true endobj\n8 0 obj /DeviceGray endobj\n");
        page.Dict.Entries["Group"] = new PdfRef(5, 0);
        using var raster = new SoftwareRasterizer(200, 200);
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (77, 77, 77), 1);
    }

    [Fact]
    public void PageGroup_IgnoresAttributesOfOtherGroupSubtypes()
    {
        var page = GroupPage(RedPage, "/CS /DeviceCMYK /K true");
        ((PdfDictionary)page.Dict.Get("Group")!).Entries["S"] = new PdfName("Other");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (255, 0, 0), 0);
    }

    [Theory]
    [InlineData("q 200 0 0 200 0 0 cm /Im Do Q",
        "/XObject << /Im 5 0 R >>", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8", "\xFF\0\0")]
    [InlineData("/Sh sh",
        "/Shading << /Sh << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [1 0 0] /N 1 >> >> >>", "", "")]
    [InlineData("/F Do", "/XObject << /F 5 0 R >>",
        "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /I true >> /Resources << /ExtGState << /M << /BM /Multiply >> >> >>",
        "/M gs 1 0 0 rg 0 0 200 200 re f")]
    public void PageGroup_GrayAppliesToImagesShadersAndNestedForms(string content, string resources, string dict, string data)
    {
        var page = GroupPage(content, "/CS /DeviceGray", resources, Stream(5, dict, data));
        var p = DocumentRenderer.RenderPage(page, 72);
        Near(Px(p, 100, 100), (77, 77, 77), 1);
        Assert.Empty(p.Warnings);
    }

    [Theory]
    [InlineData("0 0 10 10 re W n")]
    [InlineData("q 0 0 10 10 re W n")]
    public void PageGroup_AnnotationsInheritPageCSButNotContentClip(string content)
    {
        var page = GroupPage(content, "/CS /DeviceGray",
            objects: Stream(5, "/Type /XObject /Subtype /Form /BBox [0 0 200 200]", RedPage),
            extraPage: "/Annots [<< /Subtype /Stamp /Rect [0 0 200 200] /AP << /N 5 0 R >> >>]");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (77, 77, 77), 1);
    }

    [Fact]
    public void TextExtraction_DoesNotRequireAGroupColorTransform()
    {
        var page = GroupPage("BT /F 12 Tf (Hello) Tj ET", "/CS /DeviceCMYK",
            "/Font << /F << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        Assert.Equal("Hello", string.Concat(DocumentRenderer.ExtractText(page).Select(s => s.Text)));
    }

    [Fact]
    public void NonIsolatedGroup_BlendsWithBackdrop()
        => Near(Px(Render(RedPage + "/F Do", "/XObject << /F 5 0 R >>", GroupForm("", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (0, 0, 0));

    [Fact]
    public void IsolatedGroup_DoesNotSeeBackdrop()
        => Near(Px(Render(RedPage + "/F Do", "/XObject << /F 5 0 R >>", GroupForm("/I true", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (0, 255, 255));

    [Fact]
    public void GroupAlpha_AppliesToWholeGroup()
    {
        const string res = "/XObject << /F 5 0 R >> /ExtGState << /A << /ca 0.5 >> >>";
        Near(Px(Render(RedPage + "/A gs /F Do", res, GroupForm("", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (127, 0, 0));
        Near(Px(Render(RedPage + "/A gs /F Do", res, GroupForm("/I true", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (127, 127, 127));
    }

    [Fact]
    public void KnockoutGroup_LaterObjectsReplaceEarlier()
    {
        const string c = "/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f";
        var ko = Render("/F Do", "/XObject << /F 5 0 R >>", GroupForm("/K true", c));
        Near(Px(ko, 100, 100), (127, 127, 255));
        Near(Px(ko, 20, 100), (255, 127, 127));
        var plain = Render("/F Do", "/XObject << /F 5 0 R >>", GroupForm("", c));
        Near(Px(plain, 100, 100), (127, 63, 191));
    }

    static double Lum(double[] c) => 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2];
    static double[] ClipColor(double[] c)
    {
        var l = Lum(c); var n = c.Min(); var x = c.Max();
        if (n < 0) c = c.Select(v => l + (v - l) * l / (l - n)).ToArray();
        if (x > 1) c = c.Select(v => l + (v - l) * (1 - l) / (x - l)).ToArray();
        return c;
    }
    static double[] SetLum(double[] c, double l) { var d = l - Lum(c); return ClipColor(c.Select(v => v + d).ToArray()); }
    static double Sat(double[] c) => c.Max() - c.Min();
    static double[] SetSat(double[] c, double s)
    {
        var idx = new[] { 0, 1, 2 }.OrderBy(i => c[i]).ToArray();
        var r = new double[3];
        var (mn, md, mx) = (idx[0], idx[1], idx[2]);
        if (c[mx] > c[mn]) { r[md] = (c[md] - c[mn]) * s / (c[mx] - c[mn]); r[mx] = s; }
        return r;
    }

    static double SepRef(string m, double cb, double cs)
    {
        double Screen(double a, double b) => a + b - a * b;
        double Hard(double b, double s) => s <= 0.5 ? b * 2 * s : Screen(b, 2 * s - 1);
        switch (m)
        {
            case "Multiply": return cb * cs;
            case "Screen": return Screen(cb, cs);
            case "Overlay": return Hard(cs, cb);
            case "Darken": return Math.Min(cb, cs);
            case "Lighten": return Math.Max(cb, cs);
            case "ColorDodge": return cb == 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs));
            case "ColorBurn": return cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs);
            case "HardLight": return Hard(cb, cs);
            case "SoftLight":
                if (cs <= 0.5) return cb - (1 - 2 * cs) * cb * (1 - cb);
                var d = cb <= 0.25 ? ((16 * cb - 12) * cb + 4) * cb : Math.Sqrt(cb);
                return cb + (2 * cs - 1) * (d - cb);
            case "Difference": return Math.Abs(cb - cs);
            case "Exclusion": return cb + cs - 2 * cb * cs;
            default: return cs;
        }
    }

    [Theory]
    [InlineData("Normal")] [InlineData("Multiply")] [InlineData("Screen")] [InlineData("Overlay")] [InlineData("Darken")]
    [InlineData("Lighten")] [InlineData("ColorDodge")] [InlineData("ColorBurn")] [InlineData("HardLight")] [InlineData("SoftLight")]
    [InlineData("Difference")] [InlineData("Exclusion")] [InlineData("Hue")] [InlineData("Saturation")] [InlineData("Color")] [InlineData("Luminosity")]
    public void BlendMode_MatchesSpecFormula(string mode)
    {
        double[] cb = [0.4, 0.6, 0.2], cs = [0.7, 0.3, 0.9];
        double[] e = mode switch
        {
            "Hue" => SetLum(SetSat(cs, Sat(cb)), Lum(cb)),
            "Saturation" => SetLum(SetSat(cb, Sat(cs)), Lum(cb)),
            "Color" => SetLum(cs, Lum(cb)),
            "Luminosity" => SetLum(cb, Lum(cs)),
            _ => [SepRef(mode, cb[0], cs[0]), SepRef(mode, cb[1], cs[1]), SepRef(mode, cb[2], cs[2])],
        };
        var p = Render("0.4 0.6 0.2 rg 0 0 200 200 re f /B gs 0.7 0.3 0.9 rg 0 0 200 200 re f", $"/ExtGState << /B << /BM /{mode} >> >>");
        Near(Px(p, 100, 100), ((int)Math.Round(e[0] * 255), (int)Math.Round(e[1] * 255), (int)Math.Round(e[2] * 255)), 2);
    }

    [Fact]
    public void BlendModeArray_UsesFirstSupported()
    {
        var p = Render("1 0 0 rg 0 0 200 200 re f /B gs 0 1 1 rg 0 0 200 200 re f", "/ExtGState << /B << /BM [/Foo /Multiply] >> >>");
        Near(Px(p, 100, 100), (0, 0, 0));
    }

    const string Inv = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";

    static string Mask(string form, string extra) => "/ExtGState << /G1 << /SMask << /Type /Mask /S /Luminosity /G 5 0 R " + extra + " >> >> >>";

    static string MaskForm(string content)
        => Stream(5, "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >>", content);

    [Fact]
    public void SoftMask_TransferFunction_Inverts()
    {
        const string fill = "/G1 gs 1 0 0 rg 0 0 200 200 re f";
        Near(Px(Render(fill, Mask("", ""), MaskForm("0.25 g 0 0 200 200 re f")), 100, 100), (255, 191, 191));
        Near(Px(Render(fill, Mask("", "/TR " + Inv), MaskForm("0.25 g 0 0 200 200 re f")), 100, 100), (255, 64, 64));
    }

    [Fact]
    public void SoftMask_TransferIdentityNameIsIgnored()
        => Near(Px(Render("/G1 gs 1 0 0 rg 0 0 200 200 re f", Mask("", "/TR /Identity"), MaskForm("0.25 g 0 0 200 200 re f")), 100, 100), (255, 191, 191));

    [Fact]
    public void SoftMask_BackdropColourFillsOutsideGroup()
    {
        const string fill = "/G1 gs 1 0 0 rg 0 0 200 200 re f";
        const string half = "1 g 0 0 100 200 re f";
        var def = Render(fill, Mask("", ""), MaskForm(half));
        Near(Px(def, 50, 100), (255, 0, 0));
        Near(Px(def, 150, 100), (255, 255, 255));
        Near(Px(Render(fill, Mask("", "/BC [0.5]"), MaskForm(half)), 150, 100), (255, 128, 128));
        Near(Px(Render(fill, Mask("", "/TR " + Inv), MaskForm(half)), 150, 100), (255, 0, 0));
    }

    // ---- page-group blending in CMYK / CalRGB / CalGray / ICCBased (ISO 32000-2 11.3.5, 11.4.7, 11.7.5.3) ----

    static double Dec(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    static double Enc(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
    static (int, int, int) To255(params double[] c) => ((int)Math.Round(c[0] * 255), (int)Math.Round(c[1] * 255), (int)Math.Round(c[2] * 255));

    static readonly string[] SeparableModes = ["Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight", "SoftLight", "Difference", "Exclusion"];
    static readonly string[] AllModes = [.. SeparableModes, "Hue", "Saturation", "Color", "Luminosity"];
    public static TheoryData<string> BlendModeNames() { var d = new TheoryData<string>(); foreach (var m in AllModes) d.Add(m); return d; }

    // R = 1 - min(1, C + K) (10.3.5) inverted with maximal black generation, then blend functions on the additive complements.
    static (int, int, int) CmykReference(string mode, double[] cb, double[] cs)
    {
        static double[] ToCmyk(double[] rgb) { var m = rgb.Max(); return [m - rgb[0], m - rgb[1], m - rgb[2], 1 - m]; }
        static double[] Add(double[] c) => [1 - c[0], 1 - c[1], 1 - c[2], 1 - c[3]];
        double[] ab = Add(ToCmyk(cb)), asrc = Add(ToCmyk(cs)), o = new double[4];
        if (Array.IndexOf(SeparableModes, mode) >= 0) for (var i = 0; i < 4; i++) o[i] = SepRef(mode, ab[i], asrc[i]);
        else
        {
            double[] b3 = ab[..3], s3 = asrc[..3];
            var rgb = mode switch
            {
                "Hue" => SetLum(SetSat(s3, Sat(b3)), Lum(b3)),
                "Saturation" => SetLum(SetSat(b3, Sat(s3)), Lum(b3)),
                "Color" => SetLum(s3, Lum(b3)),
                _ => SetLum(b3, Lum(s3)),
            };
            o = [rgb[0], rgb[1], rgb[2], mode == "Luminosity" ? asrc[3] : ab[3]];
        }
        var k = 1 - o[3];
        return To255(1 - Math.Min(1, 1 - o[0] + k), 1 - Math.Min(1, 1 - o[1] + k), 1 - Math.Min(1, 1 - o[2] + k));
    }

    const string BlendBackdrop = "0.8 0.6 0.4 rg 0 0 200 200 re f /B gs 0.5 0.7 0.9 rg 0 0 200 200 re f";

    static string BlendRes(string mode) => $"/ExtGState << /B << /BM /{mode} >> >>";

    [Theory]
    [MemberData(nameof(BlendModeNames))]
    public void PageGroup_CmykBlendsInCmykComplementSpace(string mode)
    {
        var p = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS /DeviceCMYK", BlendRes(mode)), 72);
        Near(Px(p, 100, 100), CmykReference(mode, [0.8, 0.6, 0.4], [0.5, 0.7, 0.9]), 2);
        Assert.Empty(p.Warnings);
    }

    [Fact]
    public void PageGroup_CmykDiffersFromRgbAndNormalIsUnchanged()
    {
        var cmyk = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS /DeviceCMYK", BlendRes("Multiply")), 72);
        var rgb = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS /DeviceRGB", BlendRes("Multiply")), 72);
        Near(Px(rgb, 100, 100), (102, 107, 92), 1); // component-wise product in RGB
        Near(Px(cmyk, 100, 100), (82, 92, 82), 1);  // K participates in CMYK: 1 - min(1, C + K) with complements multiplied
        var normal = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop.Replace("/B gs", ""), "/CS /DeviceCMYK"), 72);
        Near(Px(normal, 100, 100), (128, 179, 230), 1);
    }

    [Theory]
    [InlineData("Multiply")]
    [InlineData("Screen")]
    [InlineData("Difference")]
    [InlineData("SoftLight")]
    public void PageGroup_CalRgbBlendsInCalibratedGammaSpace(string mode)
    {
        // sRGB primaries/white with gamma 1.8: blend functions act on lin^(1/1.8) (8.6.5.3), then results return through lin = c^1.8.
        const string cs = "[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [1.8 1.8 1.8] /Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505] >>]";
        double[] cb = [0.8, 0.6, 0.4], cs2 = [0.5, 0.7, 0.9];
        var e = Enumerable.Range(0, 3).Select(i => Enc(Math.Pow(SepRef(mode, Math.Pow(Dec(cb[i]), 1 / 1.8), Math.Pow(Dec(cs2[i]), 1 / 1.8)), 1.8))).ToArray();
        var p = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS " + cs, BlendRes(mode)), 72);
        Near(Px(p, 100, 100), To255(e), 3);
        // Linear-light multiply is independent of the gamma, unlike an sRGB-encoded product.
        if (mode == "Multiply") Near(Px(p, 100, 100), To255(Enc(Dec(0.8) * Dec(0.5)), Enc(Dec(0.6) * Dec(0.7)), Enc(Dec(0.4) * Dec(0.9))), 3);
    }

    [Fact]
    public void PageGroup_CalRgbNormalCompositingKeepsSourceColours()
    {
        const string cs = "[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [2.2 2.2 2.2] >>]";
        Near(Px(DocumentRenderer.RenderPage(GroupPage("0.8 0.6 0.4 rg 0 0 200 200 re f", "/CS " + cs), 72), 100, 100), (204, 153, 102), 1);
    }

    [Fact]
    public void PageGroup_CalGrayProjectsToCieLuminanceAndBlendsInItsGamma()
    {
        const string cs = "[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 2.2 >>]";
        var red = DocumentRenderer.RenderPage(GroupPage("1 0 0 rg 0 0 200 200 re f", "/CS " + cs), 72);
        var y = Enc(0.2126); // CIE luminance of sRGB red
        Near(Px(red, 100, 100), To255(y, y, y), 1);
        var mul = DocumentRenderer.RenderPage(GroupPage("1 0 0 rg 0 0 200 200 re f /B gs 0 0 1 rg 0 0 200 200 re f", "/CS " + cs, BlendRes("Multiply")), 72);
        var m = Enc(0.2126 * 0.0722);
        Near(Px(mul, 100, 100), To255(m, m, m), 1);
    }

    [Fact]
    public void PageGroup_IccBasedUsesComponentCountModelAndStillValidatesProfile()
    {
        var rgbProfile = Encoding.Latin1.GetString(IccMatrixRgb());
        var cmykProfile = Encoding.Latin1.GetString(IccCmyk());
        var p3 = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS [/ICCBased 5 0 R]", BlendRes("Multiply"), Stream(5, "/N 3", rgbProfile)), 72);
        Near(Px(p3, 100, 100), (102, 107, 92), 1);
        var p4 = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS [/ICCBased 5 0 R]", BlendRes("Multiply"), Stream(5, "/N 4", cmykProfile)), 72);
        Near(Px(p4, 100, 100), CmykReference("Multiply", [0.8, 0.6, 0.4], [0.5, 0.7, 0.9]), 1);
        // A profile whose component count disagrees with /N is not a valid blending space.
        Assert.Throws<NotSupportedException>(() => DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS [/ICCBased 5 0 R]", BlendRes("Multiply"), Stream(5, "/N 3", cmykProfile)), 72));
        // DefaultCMYK remaps /DeviceCMYK to the profile's blending model.
        var def = DocumentRenderer.RenderPage(GroupPage(BlendBackdrop, "/CS /DeviceCMYK", BlendRes("Multiply") + " /ColorSpace << /DefaultCMYK [/ICCBased 5 0 R] >>", Stream(5, "/N 4", cmykProfile)), 72);
        Near(Px(def, 100, 100), CmykReference("Multiply", [0.8, 0.6, 0.4], [0.5, 0.7, 0.9]), 1);
    }

    [Fact]
    public void PageGroup_CmykKnockoutAndGroupAlphaStillApply()
    {
        var page = GroupPage("/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f", "/CS /DeviceCMYK /K true", "/ExtGState << /A << /ca 0.5 >> >>");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (127, 127, 255), 1);
    }

    // minimal synthetic ICC profiles (valid headers/tags; used only to select the group blending model)
    static void U32(List<byte> b, uint v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }
    static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    static uint Sig(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
    static byte[] IccXyz(double x, double y, double z) { var b = new List<byte>(); U32(b, Sig("XYZ ")); U32(b, 0); foreach (var v in new[] { x, y, z }) U32(b, (uint)(int)Math.Round(v * 65536)); return [.. b]; }
    static byte[] IccGamma(double g) { var b = new List<byte>(); U32(b, Sig("curv")); U32(b, 0); U32(b, 1); U16(b, (int)Math.Round(g * 256)); return [.. b]; }

    static byte[] BuildIcc(string space, string pcs, params (string Tag, byte[] Data)[] tags)
    {
        var body = new List<byte>(); var table = new List<byte>();
        U32(table, (uint)tags.Length);
        var off = 128 + 4 + tags.Length * 12;
        foreach (var (t, d) in tags)
        {
            while ((off + body.Count) % 4 != 0) body.Add(0);
            U32(table, Sig(t)); U32(table, (uint)(off + body.Count)); U32(table, (uint)d.Length);
            body.AddRange(d);
        }
        var h = new byte[128];
        Encoding.ASCII.GetBytes("mntr").CopyTo(h, 12); Encoding.ASCII.GetBytes(space).CopyTo(h, 16); Encoding.ASCII.GetBytes(pcs).CopyTo(h, 20);
        Encoding.ASCII.GetBytes("acsp").CopyTo(h, 36); h[8] = 2; h[9] = 0x40;
        var all = h.Concat(table).Concat(body).ToArray();
        all[0] = (byte)(all.Length >> 24); all[1] = (byte)(all.Length >> 16); all[2] = (byte)(all.Length >> 8); all[3] = (byte)all.Length;
        return all;
    }

    static byte[] IccMatrixRgb() => BuildIcc("RGB ", "XYZ ",
        ("rXYZ", IccXyz(0.4360747, 0.2225045, 0.0139322)), ("gXYZ", IccXyz(0.3850649, 0.7168786, 0.0971045)), ("bXYZ", IccXyz(0.1430804, 0.0606169, 0.7141733)),
        ("rTRC", IccGamma(2.2)), ("gTRC", IccGamma(2.2)), ("bTRC", IccGamma(2.2)), ("wtpt", IccXyz(0.9642, 1.0, 0.8249)));

    static byte[] IccCmyk()
    {
        var b = new List<byte>();
        U32(b, Sig("mft2")); U32(b, 0);
        b.Add(4); b.Add(3); b.Add(2); b.Add(0);
        foreach (var v in new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) U32(b, (uint)(int)Math.Round(v * 65536));
        U16(b, 2); U16(b, 2);
        for (var i = 0; i < 4; i++) { U16(b, 0); U16(b, 65535); }
        for (var n = 0; n < 16; n++) { var k = n & 1; U16(b, k == 1 ? 0 : 0xFF00); U16(b, 0x8000); U16(b, 0x8000); }
        for (var i = 0; i < 3; i++) { U16(b, 0); U16(b, 65535); }
        return BuildIcc("CMYK", "Lab ", ("A2B0", [.. b]));
    }

    // ---- per-object knockout (11.7.4.4 fill+stroke, 11.7.4.5 /TK) and /AIS (11.6.4.4) ----

    const string BoxPath = "1 0 0 rg 0 0 1 RG 40 w 60 60 80 80 re ";
    const string HalfAlpha = "/ExtGState << /A << /ca 0.5 /CA 0.5 >> >>";

    [Fact]
    public void FillAndStroke_OfOneOperatorKnockOutEachOther()
    {
        var b = Render("/A gs " + BoxPath + "B", HalfAlpha);
        Near(Px(b, 70, 100), (127, 127, 255), 2);  // stroke over the backdrop only; the fill is knocked out
        Near(Px(b, 50, 100), (127, 127, 255), 2);  // outer half of the stroke
        Near(Px(b, 100, 100), (255, 127, 127), 2); // fill only
        var separate = Render("/A gs " + BoxPath + "f 60 60 80 80 re S", HalfAlpha);
        Near(Px(separate, 70, 100), (127, 63, 191), 2); // separate operators composite the stroke over the fill
    }

    [Fact]
    public void FillAndStroke_OpaqueNormalIsUnchanged()
    {
        var b = Render(BoxPath + "B", "");
        Near(Px(b, 70, 100), (0, 0, 255), 0);
        Near(Px(b, 100, 100), (255, 0, 0), 0);
    }

    [Fact]
    public void FillAndStroke_KnockoutAppliesToBlendModes()
    {
        // Multiply stroke over the fill would give (0,0,0); knocked out it multiplies only the white backdrop.
        var b = Render("/M gs " + BoxPath + "B", "/ExtGState << /M << /BM /Multiply >> >>");
        Near(Px(b, 70, 100), (0, 0, 255), 1);
    }

    [Fact]
    public void FillAndStroke_SoftMaskIsAppliedOnceToTheKnockedOutObject()
    {
        const string mask = "/ExtGState << /G1 << /SMask << /Type /Mask /S /Luminosity /G 5 0 R >> >> >>";
        var form = Stream(5, "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >>", "0.5 g 0 0 200 200 re f");
        var b = Render("/G1 gs " + BoxPath + "B", mask, form);
        Near(Px(b, 70, 100), (128, 128, 255), 2);
    }

    [Fact]
    public void FillAndStroke_PatternFillKnockoutStillRenders()
    {
        var b = Render("/A gs /Pattern cs /P scn 0 0 1 RG 40 w 60 60 80 80 re B",
            HalfAlpha + " /Pattern << /P << /PatternType 2 /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [1 0 0] /N 1 >> >> >> >>");
        Near(Px(b, 70, 100), (127, 127, 255), 2);
    }

    const string TextRes = "/Font << /F << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> ";

    static int Darkest(RenderedPage p)
    {
        var min = 255;
        for (var i = 0; i < p.Width * p.Height; i++) min = Math.Min(min, p.Rgba[i * 4 + 1]);
        return min;
    }

    [Theory]
    [InlineData("/TK true", 128)]
    [InlineData("", 128)]   // /TK defaults to true
    [InlineData("/TK false", 64)]
    public void TextKnockout_OverlappingGlyphsOfOneTextObject(string tk, int expectedDarkest)
    {
        // Two identical glyphs at the same position with 50% black: knocked out they stay 50%, composited separately they reach 75%.
        var p = Render("/A gs 0 g BT /F 100 Tf 50 50 Td (I) Tj 0 0 Td (I) Tj ET", TextRes + $"/ExtGState << /A << /ca 0.5 {tk} >> >>");
        Assert.InRange(Darkest(p), expectedDarkest - 2, expectedDarkest + 2);
    }

    [Fact]
    public void TextKnockout_DoesNotSpanSeparateTextObjects()
    {
        var p = Render("/A gs 0 g BT /F 100 Tf 50 50 Td (I) Tj ET BT /F 100 Tf 50 50 Td (I) Tj ET", TextRes + "/ExtGState << /A << /ca 0.5 /TK true >> >>");
        Assert.InRange(Darkest(p), 62, 66);
    }

    [Fact]
    public void TextKnockout_ClosesAtEndOfStreamWithoutET()
    {
        var p = Render("/A gs 0 g BT /F 100 Tf 50 50 Td (I) Tj 0 0 Td (I) Tj", TextRes + "/ExtGState << /A << /ca 0.5 >> >>");
        Assert.InRange(Darkest(p), 126, 130);
    }

    [Theory]
    [InlineData("/AIS false", 127, 127, 255)]
    [InlineData("/AIS true", 128, 64, 191)]
    public void AlphaIsShape_ChangesKnockoutCompositing(string ais, int r, int g, int b)
    {
        // Isolated knockout page group, 50% alpha. Opacity: the blue replaces red where they overlap (blue at 50% over white).
        // Shape: red (shape .5, backdrop a=.5) then blue shape .5 => colour (1/3,0,2/3), alpha .75 over white => (.5,.25,.75) (11.4.8).
        var page = GroupPage("/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f", "/K true", $"/ExtGState << /A << /ca 0.5 {ais} >> >>");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (r, g, b), 2);
    }

    [Fact]
    public void AlphaIsShape_IsPartOfGraphicsStateAndRestoredByQ()
    {
        var page = GroupPage("q /A gs Q /B gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f", "/K true",
            "/ExtGState << /A << /AIS true >> /B << /ca 0.5 >> >>");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (127, 127, 255), 2);
    }

    // ---- soft-mask edge cases (11.6.5.2) ----

    static string AlphaMask(string extra) => "/ExtGState << /G1 << /SMask << /Type /Mask /S /Alpha /G 5 0 R " + extra + " >> >> >>";

    static string AlphaForm() => Stream(5, "/Type /XObject /Subtype /Form /BBox [0 0 100 200] /Group << /S /Transparency /CS /DeviceRGB >> /Resources << /ExtGState << /H << /ca 0.5 >> >> >>", "/H gs 0 g 0 0 100 200 re f");

    [Fact]
    public void SoftMask_AlphaUsesGroupAlphaAndIgnoresBackdropColour()
    {
        const string fill = "/G1 gs 1 0 0 rg 0 0 200 200 re f";
        var plain = Render(fill, AlphaMask(""), AlphaForm());
        Near(Px(plain, 50, 100), (255, 127, 127), 2);
        Near(Px(plain, 150, 100), (255, 255, 255), 0);
        var withBc = Render(fill, AlphaMask("/BC [1 1 1]"), AlphaForm());
        Near(Px(withBc, 50, 100), (255, 127, 127), 2);
        Near(Px(withBc, 150, 100), (255, 255, 255), 0);
    }

    [Fact]
    public void SoftMask_TransferFunctionAppliesToAlphaMasksAndOutsideTheGroup()
    {
        const string fill = "/G1 gs 1 0 0 rg 0 0 200 200 re f";
        var inv = Render(fill, AlphaMask("/TR " + Inv), AlphaForm());
        Near(Px(inv, 50, 100), (255, 127, 127), 2); // 1 - .5
        Near(Px(inv, 150, 100), (255, 0, 0), 0);    // 1 - 0 outside the group's BBox
    }

    // ---- mesh shadings 4-7 ----

    static string MeshStream(int type, string data, string extra = "")
        => Stream(5, $"/ShadingType {type} /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1 0 1 0 1] {extra}", data);

    static RenderedPage Mesh(string obj) => Render("/M sh", "/Shading << /M 5 0 R >>", obj, "[0 0 255 255]");

    static string V(int x, int y, string rgb) => $"{(char)x}{(char)y}{rgb}";
    const string R = "\xFF\0\0", G = "\0\xFF\0", B = "\0\0\xFF", Y = "\xFF\xFF\0";

    [Fact]
    public void MeshType5_Lattice_TwoAndThreeVerticesPerRow()
    {
        var two = Mesh(MeshStream(5, V(0, 0, R) + V(255, 0, G) + V(0, 255, B) + V(255, 255, Y), "/VerticesPerRow 2"));
        var (r, g, b, y) = (Px(two, 3, 252), Px(two, 252, 252), Px(two, 3, 3), Px(two, 252, 3));
        Assert.True(r.R > 230 && r.G < 25 && r.B < 25, r.ToString());
        Assert.True(g.G > 230 && g.R < 25, g.ToString());
        Assert.True(b.B > 230 && b.R < 25, b.ToString());
        Assert.True(y.R > 230 && y.G > 230 && y.B < 25, y.ToString());
        // Type 5 is Gouraud-triangulated per cell, so the centre lies on a triangle diagonal (no bilinear average).
        Assert.True(Px(two, 127, 127).R + Px(two, 127, 127).G + Px(two, 127, 127).B > 100);

        var three = Mesh(MeshStream(5, V(0, 0, R) + V(128, 0, "\0\0\0") + V(255, 0, G) + V(0, 255, B) + V(128, 255, "\0\0\0") + V(255, 255, Y), "/VerticesPerRow 3"));
        Assert.True(Px(three, 3, 252).R > 230);
        Assert.True(Px(three, 252, 252).G > 230);
        var mid = Px(three, 128, 252); // black middle vertex on the bottom row
        Assert.True(mid.R < 40 && mid.G < 40, mid.ToString());
    }

    [Fact]
    public void MeshType5_NonByteAlignedVerticesAreAlignedPerVertex()
    {
        // 12-bit coordinates and 4-bit components: a vertex is 12+12+12 = 36 bits, padded to 40.
        static string Vtx(int x, int y, int r, int g, int b)
        {
            ulong bits = ((ulong)x << 24) | ((ulong)y << 12) | (ulong)(r << 8 | g << 4 | b);
            bits <<= 4;
            var sb = new StringBuilder();
            for (var i = 4; i >= 0; i--) sb.Append((char)((bits >> (i * 8)) & 0xFF));
            return sb.ToString();
        }
        var data = Vtx(0, 0, 15, 0, 0) + Vtx(4095, 0, 0, 15, 0) + Vtx(0, 4095, 0, 0, 15) + Vtx(4095, 4095, 15, 15, 0);
        var obj = Stream(5, "/ShadingType 5 /ColorSpace /DeviceRGB /BitsPerCoordinate 12 /BitsPerComponent 4 /VerticesPerRow 2 /Decode [0 255 0 255 0 1 0 1 0 1]", data);
        var p = Mesh(obj);
        Assert.True(Px(p, 3, 252).R > 220 && Px(p, 3, 252).G < 40);
        Assert.True(Px(p, 252, 252).G > 220 && Px(p, 252, 252).R < 40);
        Assert.True(Px(p, 3, 3).B > 220);
    }

    static string PatchPoints()
    {
        (int, int)[] order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)];
        var sb = new StringBuilder();
        foreach (var (i, j) in order) sb.Append((char)(j * 85)).Append((char)(i * 85));
        return sb.ToString();
    }

    [Fact]
    public void MeshType7_TensorPatch()
    {
        var p = Mesh(MeshStream(7, "\0" + PatchPoints() + R + G + B + Y));
        Assert.True(Px(p, 3, 252).R > 230 && Px(p, 3, 252).G < 25);
        Assert.True(Px(p, 252, 252).G > 230);
        Near(Px(p, 127, 127), (127, 127, 64), 6);
    }

    [Fact]
    public void MeshType7_EdgeContinuationSharesColoursAndEdge()
    {
        // Patch 1 spans x 0..126; patch 2 (f=1) reuses its right edge and adds the far side.
        (int, int)[] order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)];
        var sb = new StringBuilder("\0");
        foreach (var (i, j) in order) sb.Append((char)(j * 42)).Append((char)(i * 85));
        sb.Append(R + G + B + Y);
        // f=1: new patch starts from edge (p03,p13,p23,p33) of the previous patch; supply the remaining 12 points and 2 colours.
        sb.Append('\x01');
        (int, int)[] rest = [(0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (1, 1), (1, 2), (2, 2)];
        foreach (var (i, j) in rest) sb.Append((char)(126 + j * 42)).Append((char)(i * 85));
        sb.Append((char)(126 + 105)).Append((char)(170)); // p21 interior point
        sb.Append("\0\xFF\xFF" + "\xFF\0\xFF");
        var p = Mesh(MeshStream(7, sb.ToString()));
        Assert.True(Px(p, 3, 252).R > 230);                     // patch 1 c00 red
        Assert.NotEqual((255, 255, 255), Px(p, 200, 128));      // patch 2 painted
        var l = Px(p, 120, 128); var r = Px(p, 132, 128);
        Assert.True(Math.Abs(l.R - r.R) < 40 && Math.Abs(l.G - r.G) < 40 && Math.Abs(l.B - r.B) < 40, $"{l} vs {r}"); // colour continuous over shared edge
    }

    [Fact]
    public void MeshType4_EdgeFlags_ShareVertices()
    {
        // Triangle A (f=0) then triangle B (f=1) reusing vb,vc of A plus a new vertex.
        var data = "\0" + V(0, 0, R) + "\0" + V(255, 0, G) + "\0" + V(0, 255, B) + "\x01" + V(255, 255, Y);
        var obj = MeshStream(4, data);
        var p = Mesh(obj);
        Assert.True(Px(p, 3, 252).R > 230);
        Assert.True(Px(p, 252, 3).R > 230 && Px(p, 252, 3).G > 230); // yellow corner from triangle B
        Assert.True(Px(p, 252, 252).G > 230);
    }

    [Fact]
    public void MeshWithFunction_MapsParameterThroughFunction()
    {
        var fn = "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";
        var data = "\0" + "\0\0\0" + "\0" + "\xFF\0\xFF" + "\0" + "\0\xFF\0";
        var obj = Stream(5, $"/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1] {fn}", data);
        var p = Mesh(obj);
        Assert.True(Px(p, 3, 252).R > 200 && Px(p, 3, 252).B < 60);
        Assert.True(Px(p, 252, 252).B > 200 && Px(p, 252, 252).R < 60);
    }
}
