using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

public class GraphicsGapTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y) { var o = (y * p.Width + x) * 4; return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]); }

    static RenderedPage Render(string content, string res, string objs = "", Func<string, string>? patch = null)
    {
        var bytes = TestPdf.Build(content, res, objs, brokenXref: true);
        if (patch is not null) bytes = System.Text.Encoding.Latin1.GetBytes(patch(System.Text.Encoding.Latin1.GetString(bytes)));
        return DocumentRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], 72);
    }

    static void Near((int R, int G, int B) a, (int, int, int) e, int tol = 4)
        => Assert.True(Math.Abs(a.R - e.Item1) <= tol && Math.Abs(a.G - e.Item2) <= tol && Math.Abs(a.B - e.Item3) <= tol, $"expected {e} got {a}");

    const string D65 = "[0.9505 1 1.089]";

    static (int, int, int) Fill(string cs, string color) => Px(Render($"/C cs {color} sc 0 0 200 200 re f", $"/ColorSpace << /C {cs} >>"), 100, 100);

    [Fact]
    public void CalGray_AppliesGamma()
    {
        Near(Fill($"[/CalGray << /WhitePoint {D65} /Gamma 1 >>]", "0.5"), (188, 188, 188));
        Near(Fill($"[/CalGray << /WhitePoint {D65} /Gamma 2.2 >>]", "0.5"), (128, 128, 128));
    }

    [Fact]
    public void CalRgb_GammaAndMatrix()
    {
        const string srgbM = "[0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505]";
        Near(Fill($"[/CalRGB << /WhitePoint {D65} /Gamma [2.2 2.2 2.2] /Matrix {srgbM} >>]", "0.5 0.5 0.5"), (128, 128, 128));
        Near(Fill($"[/CalRGB << /WhitePoint {D65} /Gamma [2.2 2.2 2.2] /Matrix {srgbM} >>]", "1 0 0"), (255, 0, 0));
        Near(Fill($"[/CalRGB << /WhitePoint {D65} /Gamma [1 1 1] /Matrix {srgbM} >>]", "0.5 0.5 0.5"), (188, 188, 188));
    }

    [Fact]
    public void CalRgb_WhitePointIsMappedToWhite_AndBadDictFallsBack()
    {
        Near(Fill("[/CalRGB << /WhitePoint [0.9642 1 0.8249] /Gamma [1.8 1.8 1.8] /Matrix [0.4361 0.2225 0.0139 0.3851 0.7169 0.0971 0.1431 0.0606 0.7141] >>]", "1 1 1"), (255, 255, 255), 2);
        Near(Fill("[/CalRGB << /WhitePoint [0 0 0] >>]", "1 0 0"), (255, 0, 0), 0);
        Near(Fill("[/CalRGB]", "0 1 0"), (0, 255, 0), 0);
    }

    [Fact]
    public void Lab_UsesWhitePointAndRange()
    {
        const string d50 = "[0.9642 1 0.8249]";
        Near(Fill($"[/Lab << /WhitePoint {d50} /Range [-128 127 -128 127] >>]", "100 0 0"), (255, 255, 255), 2);
        Near(Fill($"[/Lab << /WhitePoint {d50} /Range [-128 127 -128 127] >>]", "50 0 0"), (119, 119, 119));
        Near(Fill($"[/Lab << /WhitePoint {d50} /Range [-128 127 -128 127] >>]", "54.29 80.80 69.89"), (255, 0, 0), 6);
        Near(Fill($"[/Lab << /WhitePoint {D65} /Range [-128 127 -128 127] >>]", "53.24 80.09 67.2"), (255, 0, 0), 6);
        Near(Fill($"[/Lab << /WhitePoint {D65} /Range [-10 10 -10 10] >>]", "50 100 0"), Fill($"[/Lab << /WhitePoint {D65} /Range [-10 10 -10 10] >>]", "50 10 0"), 0);
    }

    const string TwoSquares = "/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f";

    [Fact]
    public void PageGroupKnockout_TopLevelObjectsKnockOut()
    {
        const string res = "/ExtGState << /A << /ca 0.5 >> >>";
        var ko = Render(TwoSquares, res, patch: s => s.Replace("/Type /Page /Parent", "/Type /Page /Group << /S /Transparency /CS /DeviceRGB /K true >> /Parent"));
        Near(Px(ko, 100, 100), (127, 127, 255));
        Near(Px(ko, 20, 100), (255, 127, 127));
        var plain = Render(TwoSquares, res, patch: s => s.Replace("/Type /Page /Parent", "/Type /Page /Group << /S /Transparency /CS /DeviceRGB /I true >> /Parent"));
        Near(Px(plain, 100, 100), (127, 63, 191));
    }

    static string Mask() => "/ExtGState << /G1 << /SMask << /Type /Mask /S /Luminosity /G 5 0 R >> >> /G0 << /SMask /None >> >>";
    const string MaskForm = "5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >> /Length 28 >>\nstream\n1 g 0 0 200 200 re f      \nendstream\nendobj\n";

    [Fact]
    public void ClipSetWhileMaskActive_SurvivesClearingTheMask()
    {
        var rect = Render("q /G1 gs 0 0 50 200 re W n /G0 gs 1 0 0 rg 0 0 200 200 re f Q", Mask(), MaskForm);
        Near(Px(rect, 25, 100), (255, 0, 0), 0);
        Near(Px(rect, 150, 100), (255, 255, 255), 0);
        var tri = Render("q /G1 gs 0 0 m 100 0 l 0 200 l h W n /G0 gs 1 0 0 rg 0 0 200 200 re f Q", Mask(), MaskForm);
        Near(Px(tri, 10, 150), (255, 0, 0), 0);
        Near(Px(tri, 150, 150), (255, 255, 255), 0);
        // and the mask itself still applies while active, then restores after Q
        var after = Render("q /G1 gs 0 0 50 200 re W n Q 0 0 1 rg 150 0 50 50 re f", Mask(), MaskForm);
        Near(Px(after, 175, 175), (0, 0, 255), 0);
    }
}
