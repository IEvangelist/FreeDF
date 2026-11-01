using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

/// <summary>ISO 32000-2 clause 10 rendering parameters: transfer functions (10.4, 11.7.5.2), overprint (8.6.7, 11.7.4.3) and inert halftone/flatness/smoothness.</summary>
public class OverprintTransferTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y) { var o = (y * p.Width + x) * 4; return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]); }

    static RenderedPage Render(string content, string res, bool overprint = false)
    {
        var bytes = TestPdf.Build(content, res, "", brokenXref: true);
        return DocumentRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], new PdfRenderOptions { OverprintSimulation = overprint }, 72);
    }

    static void Near((int R, int G, int B) a, (int R, int G, int B) e, int tol = 2)
        => Assert.True(Math.Abs(a.R - e.R) <= tol && Math.Abs(a.G - e.G) <= tol && Math.Abs(a.B - e.B) <= tol, $"expected {e} got {a}");

    const string Square = "50 50 100 100 re f";
    const string Gamma2 = "<< /FunctionType 2 /Domain [0 1] /N 2 >>";
    const string Invert = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";

    static (int R, int G, int B) Gray(string gs, string paint = "0.5 g")
        => Px(Render($"/A gs {paint} {Square}", $"/ExtGState << /A << {gs} >> >>"), 100, 100);

    // ---- transfer functions ----

    [Fact]
    public void Transfer_SingleFunctionAppliesToEveryComponent()
    {
        Near(Gray($"/TR2 {Gamma2}"), (64, 64, 64));
        Near(Gray($"/TR {Gamma2}", "0.8 0.5 0.2 rg"), (163, 64, 10));
    }

    [Fact]
    public void Transfer_IdentityAndDefaultNamesLeaveColoursUntouched()
    {
        Near(Gray("/TR2 /Identity"), (128, 128, 128));
        Near(Gray("/TR /Identity"), (128, 128, 128));
        Near(Gray("/TR2 /Default"), (128, 128, 128));
    }

    [Fact]
    public void Transfer_Tr2TakesPrecedenceOverTr_AndDefaultResetsTheState()
    {
        Near(Gray($"/TR {Invert} /TR2 /Identity"), (128, 128, 128));
        var reset = Px(Render($"/A gs /B gs 0.5 g {Square}", $"/ExtGState << /A << /TR2 {Gamma2} >> /B << /TR2 /Default >> >>"), 100, 100);
        Near(reset, (128, 128, 128));
    }

    [Fact]
    public void Transfer_ArrayOfFourUsesTheFirstThreeForRgb()
    {
        Near(Gray($"/TR2 [{Invert} /Identity /Identity {Gamma2}]", "0.2 0.5 0.7 rg"), (204, 128, 179));
    }

    [Fact]
    public void Transfer_InvalidValueWarnsAndKeepsTheCurrentTransfer()
    {
        var bytes = TestPdf.Build($"/A gs 0.5 g {Square}", "/ExtGState << /A << /TR2 [1 2] >> >>", "", brokenXref: true);
        var page = DocumentRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], 72);
        Near(Px(page, 100, 100), (128, 128, 128));
        Assert.Contains(page.Warnings, w => w.Contains("/TR2"));
    }

    [Fact]
    public void Transfer_IsGraphicsStateAndRestoredByQ()
    {
        var p = Render($"q /A gs 0.5 g 0 0 100 100 re f Q 0.5 g 100 100 100 100 re f", $"/ExtGState << /A << /TR2 {Gamma2} >> >>");
        Near(Px(p, 50, 150), (64, 64, 64));
        Near(Px(p, 150, 50), (128, 128, 128));
    }

    [Fact]
    public void Transfer_OnlyAppliesToFullyOpaqueObjects()
    {
        // 11.7.5.2: a partially transparent topmost object uses the page default (identity) transfer.
        Near(Gray($"/TR2 {Invert} /ca 0.5", "0 g"), (128, 128, 128));
        Near(Gray($"/TR2 {Invert} /BM /Multiply", "0.5 g"), (128, 128, 128));
        Near(Gray($"/TR2 {Invert}", "0 g"), (255, 255, 255));
    }

    [Fact]
    public void Transfer_AppliesToStrokesTextFreeImagesAndShadings()
    {
        var stroke = Render("/A gs 0.5 G 20 w 100 0 m 100 200 l S", $"/ExtGState << /A << /TR2 {Gamma2} >> >>");
        Near(Px(stroke, 100, 100), (64, 64, 64));
        var image = Render("/A gs q 200 0 0 200 0 0 cm BI /W 1 /H 1 /CS /RGB /BPC 8 /F /AHx ID 808080> EI Q", $"/ExtGState << /A << /TR2 {Gamma2} >> >>");
        Near(Px(image, 100, 100), (64, 64, 64), 3);
        var shading = Render("/A gs /S sh", $"/ExtGState << /A << /TR2 {Gamma2} >> >> /Shading << /S << /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0.5] /C1 [0.5] /N 1 >> /Extend [true true] >> >>");
        Near(Px(shading, 100, 100), (64, 64, 64), 3);
    }

    // ---- overprint ----

    const string Ops = "/ExtGState << /K0 << /OP false >> /O0 << /OP true /OPM 0 >> /O1 << /OP true /OPM 1 >> /O1fill << /OP true /op false /OPM 1 >> /Sep << /OP true >> >> /ColorSpace << /Spot [/Separation /Spot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>] /None [/Separation /None /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>] >>";

    static string Page(string gs, string source) => $"1 0 0 0 k 0 0 200 200 re f /{gs} gs {source} 50 50 100 100 re f";

    [Fact]
    public void Overprint_IsIgnoredByDefaultOnRgbOutput()
    {
        // 8.6.7: overprint is device-dependent and nonzero mode does not apply to a non-CMYK device.
        var off = Render(Page("O1", "0 1 0 0 k"), Ops);
        var knockout = Render(Page("K0", "0 1 0 0 k"), Ops);
        Assert.Equal(knockout.Rgba, off.Rgba);
    }

    [Fact]
    public void Overprint_Mode0PaintsAllProcessColorantsLikeKnockout()
    {
        var o0 = Render(Page("O0", "0 1 0 0 k"), Ops, overprint: true);
        var knockout = Render(Page("K0", "0 1 0 0 k"), Ops, overprint: true);
        Assert.Equal(knockout.Rgba, o0.Rgba);
    }

    // Consistency check only: the expectation reuses the naive-complement model of the simulation, so it does not prove ISO conformance.
    // Overprint on RGB output is an approximation; see the independent analytic case below.
    [Fact]
    public void Overprint_OverBlankBackdropEqualsSourceExactly()
    {
        // Hand-derived from Table 148/149 (OPM 1): unpainted colorants keep the backdrop's 0.0, so over white the result is the source colour.
        var p = Render("/O1 gs 0 1 0 0 k 50 50 100 100 re f", Ops, overprint: true);
        var source = Px(Render("0 1 0 0 k 50 50 100 100 re f", Ops), 100, 100);
        Near(Px(p, 100, 100), source, 0);
    }

    [Fact]
    public void Overprint_NonzeroModeKeepsBackdropWhereComponentsAreZero()
    {
        var p = Render(Page("O1", "0 1 0 0 k"), Ops, overprint: true);
        var backdrop = Px(Render("1 0 0 0 k 0 0 200 200 re f", Ops), 100, 100);
        var source = Px(Render("0 1 0 0 k 0 0 200 200 re f", Ops), 100, 100);
        var got = Px(p, 100, 100);
        Near(Px(p, 10, 10), backdrop, 0);
        // Cyan retained under the new magenta: expected = source + naive(C=b.c, M=1) - naive(M=1) using the complement model.
        var (c, m, y, k) = Decompose(backdrop);
        double[] t = [c, 1, y, k];
        var exp = (R: source.R / 255.0 + (1 - t[0]) * (1 - t[3]) - 1.0, G: source.G / 255.0 + (1 - t[1]) * (1 - t[3]) - 0.0, B: source.B / 255.0 + (1 - t[2]) * (1 - t[3]) - 1.0);
        Near(got, (Clamp(exp.R), Clamp(exp.G), Clamp(exp.B)), 3);
        Assert.NotEqual(source, got);
    }

    static (double C, double M, double Y, double K) Decompose((int R, int G, int B) c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var k = 1 - Math.Max(r, Math.Max(g, b));
        if (k >= 0.9999) return (0, 0, 0, k);
        return ((1 - r - k) / (1 - k), (1 - g - k) / (1 - k), (1 - b - k) / (1 - k), k);
    }

    static int Clamp(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    [Fact]
    public void Overprint_NonzeroModePaintsNothingForAllZeroCmyk()
    {
        var p = Render(Page("O1", "0 0 0 0 k"), Ops, overprint: true);
        var backdrop = Px(Render("1 0 0 0 k 0 0 200 200 re f", Ops), 100, 100);
        Near(Px(p, 100, 100), backdrop, 0);
        var knock = Render(Page("O0", "0 0 0 0 k"), Ops, overprint: true);
        Near(Px(knock, 100, 100), (255, 255, 255), 0);
    }

    [Fact]
    public void Overprint_SpotColorantAccumulatesLikeMultiply()
    {
        var p = Render("1 0 0 0 k 0 0 200 200 re f /Sep gs /Spot cs 1 scn 50 50 100 100 re f", Ops, overprint: true);
        var backdrop = Px(Render("1 0 0 0 k 0 0 200 200 re f", Ops), 100, 100);
        var source = Px(Render("/Spot cs 1 scn 0 0 200 200 re f", Ops), 100, 100);
        Near(Px(p, 100, 100), (backdrop.R * source.R / 255, backdrop.G * source.G / 255, backdrop.B * source.B / 255), 2);
        var knock = Render("1 0 0 0 k 0 0 200 200 re f /K0 gs /Spot cs 1 scn 50 50 100 100 re f", Ops, overprint: true);
        Near(Px(knock, 100, 100), source, 0);
    }

    [Fact]
    public void Overprint_SeparationNoneMarksNothing()
    {
        var p = Render("1 0 0 0 k 0 0 200 200 re f /Sep gs /None cs 1 scn 50 50 100 100 re f", Ops, overprint: true);
        var backdrop = Px(Render("1 0 0 0 k 0 0 200 200 re f", Ops), 100, 100);
        Near(Px(p, 100, 100), backdrop, 0);
    }

    [Fact]
    public void Overprint_OpEntryOverridesOpForNonStrokingOnly()
    {
        // /OP true /op false: strokes overprint (all-zero CMYK marks nothing in mode 1) while fills knock out.
        var p = Render("1 0 0 0 k 0 0 200 200 re f /O1fill gs 0 0 0 0 k 0 0 0 0 K 20 w 0 0 100 100 re f 150 0 m 150 200 l S", Ops, overprint: true);
        var backdrop = Px(Render("1 0 0 0 k 0 0 200 200 re f", Ops), 100, 100);
        Near(Px(p, 50, 150), (255, 255, 255), 0);
        Near(Px(p, 150, 100), backdrop, 0);
    }

    [Fact]
    public void Overprint_NonNormalBlendModeIgnoresSimulation()
    {
        const string res = "/ExtGState << /B << /OP true /OPM 1 /BM /Multiply >> >>";
        var a = Render("1 0 0 0 k 0 0 200 200 re f /B gs 0 0 0 0 k 50 50 100 100 re f", res, overprint: true);
        var b = Render("1 0 0 0 k 0 0 200 200 re f /B gs 0 0 0 0 k 50 50 100 100 re f", res, overprint: false);
        Assert.Equal(b.Rgba, a.Rgba);
    }

    // ---- halftones, flatness, smoothness ----

    [Fact]
    public void HalftoneFlatnessAndSmoothness_AreInertOnScreenOutput()
    {
        // 10.5, 10.6.2 and 10.6.3 describe device-dependent quality/screening parameters; RGB screen output ignores them.
        const string content = "/A gs 0.3 0.6 0.9 rg 0 0 m 200 0 l 100 200 l h f 3 i 1 0 0 RG 10 w 20 20 160 160 re S";
        var plain = Render(content, "/ExtGState << /A << >> >>");
        var tuned = Render(content, "/ExtGState << /A << /HT << /Type /Halftone /HalftoneType 1 /Frequency 60 /Angle 45 /SpotFunction /Round >> /FL 50 /SM 0.9 >> >>");
        var named = Render(content, "/ExtGState << /A << /HT /Default /FL 0.5 /SM 0 >> >>");
        Assert.Equal(plain.Rgba, tuned.Rgba);
        Assert.Equal(plain.Rgba, named.Rgba);
    }
}
