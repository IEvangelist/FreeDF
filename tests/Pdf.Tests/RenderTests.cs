using Pdf.Document;
using Pdf.Render;
using Xunit;

namespace Pdf.Tests;

public class RenderTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y)
    {
        var o = (y * p.Width + x) * 4;
        return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]);
    }

    static RenderedPage Render(string content, string res = "", double dpi = 72, bool broken = false)
        => PdfRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(content, res, brokenXref: broken)).Pages[0], dpi);

    [Fact]
    public void ParsesPageTree()
    {
        var doc = PdfDocument.Load(TestPdf.Build("q Q"));
        Assert.Single(doc.Pages);
        Assert.Equal(200, doc.Pages[0].MediaBox.Width);
    }

    [Fact]
    public void FillsRectangle_YAxisFlipped()
    {
        var p = Render("1 0 0 rg 0 0 100 100 re f");
        Assert.Equal(200, p.Width);
        Assert.Equal((255, 0, 0), Px(p, 50, 150));   // bottom-left in PDF space => lower in image
        Assert.Equal((255, 255, 255), Px(p, 50, 50));
        Assert.Equal((255, 255, 255), Px(p, 150, 150));
    }

    [Fact]
    public void RepairsBrokenXref()
    {
        var p = Render("0 0 1 rg 0 0 200 200 re f", broken: true);
        Assert.Equal((0, 0, 255), Px(p, 100, 100));
    }

    [Fact]
    public void StrokesLine()
    {
        var p = Render("0 G 10 w 0 100 m 200 100 l S");
        Assert.Equal((0, 0, 0), Px(p, 100, 100));
        Assert.Equal((255, 255, 255), Px(p, 100, 120));
    }

    [Fact]
    public void ClipRestrictsFill()
    {
        var p = Render("q 0 0 50 50 re W n 0 1 0 rg 0 0 200 200 re f Q");
        Assert.Equal((0, 255, 0), Px(p, 25, 175));
        Assert.Equal((255, 255, 255), Px(p, 100, 100));
    }

    [Fact]
    public void AxialShadingPaints()
    {
        var res = "/Shading << /S1 << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >> >>";
        var p = Render("/S1 sh", res);
        var l = Px(p, 5, 100); var r = Px(p, 195, 100);
        Assert.True(l.R > 200 && r.B > 200);
    }

    [Fact]
    public void ShadingHonoursRectClip()
    {
        var res = "/Shading << /S1 << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >> >>";
        var p = Render("q\nn 0 100 200 100 re W* n\n/S1 sh\nQ", res);
        Assert.NotEqual((255, 255, 255), Px(p, 100, 50));
        Assert.Equal((255, 255, 255), Px(p, 100, 150));
    }

    [Fact]
    public void ShowcaseGradientIsClippedToHeader()
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "showcase.pdf")));
        var p = PdfRenderer.RenderPage(doc.Pages[0], 72);
        Assert.Equal((255, 255, 255), Px(p, 20, 400));
    }

    [Fact]
    public void RotatedPageKeepsClip()
    {
        var ed = PdfEditor.Open(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "showcase.pdf")));
        ed.SetRotation(0, 90);
        var p = PdfRenderer.RenderPage(PdfDocument.Load(ed.Save()).Pages[0], 72);
        Assert.Equal((p.Width, p.Height), (792, 612));
        Assert.Equal((255, 255, 255), Px(p, 400, 300));
    }

    [Fact]
    public void ProducesDecodablePng()
    {
        var p = Render("1 0 0 rg 0 0 200 200 re f");
        var (w, h, rgba) = PngWriter.Decode(p.ToPng());
        Assert.Equal((200, 200), (w, h));
        Assert.Equal(255, rgba[0]);
        Assert.Equal(0, rgba[1]);
    }

    [Fact]
    public void SoftMask_Luminosity_And_Alpha()
    {
        const string form = "1 g 0 0 100 200 re f";
        var obj = $"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Length {form.Length} >>\nstream\n{form}\nendstream\nendobj\n";
        var lum = "/ExtGState << /G1 << /SMask << /Type /Mask /S /Luminosity /G 5 0 R >> >> /G0 << /SMask /None >> >>";
        var bytes = TestPdf.Build("q /G1 gs 1 0 0 rg 0 0 200 200 re f Q 0 0 1 rg 190 190 10 10 re f", lum, obj, brokenXref: true);
        var p = PdfRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], 72);
        Assert.Equal((255, 0, 0), Px(p, 50, 100));
        Assert.Equal((255, 255, 255), Px(p, 150, 100));
        Assert.Equal((0, 0, 255), Px(p, 195, 5)); // mask is gone after Q

        var alpha = lum.Replace("Luminosity", "Alpha");
        var p2 = PdfRenderer.RenderPage(PdfDocument.Load(TestPdf.Build("/G1 gs 1 0 0 rg 0 0 200 200 re f", alpha, obj, brokenXref: true)).Pages[0], 72);
        Assert.Equal((255, 0, 0), Px(p2, 50, 100));
        Assert.Equal((255, 255, 255), Px(p2, 150, 100));
    }

    [Fact]
    public void RotatesPage()
    {
        var bytes = TestPdf.Build("1 0 0 rg 0 0 100 100 re f", mediaBox: "[0 0 200 100]");
        var s = System.Text.Encoding.Latin1.GetString(bytes).Replace("/Type /Page /Parent", "/Type /Page /Rotate 90 /Parent");
        var page = PdfDocument.Load(System.Text.Encoding.Latin1.GetBytes(s)).Pages[0];
        var p = PdfRenderer.RenderPage(page, 72);
        Assert.Equal((100, 200), (p.Width, p.Height));
    }
}
