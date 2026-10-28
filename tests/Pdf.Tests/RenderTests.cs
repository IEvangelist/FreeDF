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
    public void ProducesDecodablePng()
    {
        var p = Render("1 0 0 rg 0 0 200 200 re f");
        var (w, h, rgba) = PngWriter.Decode(p.ToPng());
        Assert.Equal((200, 200), (w, h));
        Assert.Equal(255, rgba[0]);
        Assert.Equal(0, rgba[1]);
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
