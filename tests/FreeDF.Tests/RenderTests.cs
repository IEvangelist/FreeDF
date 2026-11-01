using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Content;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

public class RenderTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y)
    {
        var o = (y * p.Width + x) * 4;
        return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]);
    }

    static RenderedPage Render(string content, string res = "", double dpi = 72, bool broken = false)
        => DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(content, res, brokenXref: broken)).Pages[0], dpi);

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
        var p = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        Assert.Equal((255, 255, 255), Px(p, 20, 400));
    }

    [Fact]
    public void RotatedPageKeepsClip()
    {
        var ed = PdfEditor.Open(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "showcase.pdf")));
        ed.SetRotation(0, 90);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(ed.Save()).Pages[0], 72);
        Assert.Equal((p.Width, p.Height), (792, 612));
        Assert.Equal((255, 255, 255), Px(p, 400, 300));
    }

    [Fact]
    public void MeshShadingType4_Triangle()
    {
        var data = "\0\0\0\xFF\0\0" + "\0\xFF\0\0\xFF\0" + "\0\0\xFF\0\0\xFF";
        var obj = $"5 0 obj\n<< /ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 200 0 200 0 1 0 1 0 1] /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n";
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build("/M sh", "/Shading << /M 5 0 R >>", obj, brokenXref: true)).Pages[0], 72);
        Assert.True(Px(p, 10, 190).R > 200);
        Assert.True(Px(p, 150, 190).G > 150);
        Assert.Equal((255, 255, 255), Px(p, 150, 50));
    }

    [Fact]
    public void MeshShadingType6_CoonsPatch()
    {
        var sb = new System.Text.StringBuilder("\0");
        (int, int)[] order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0)];
        foreach (var (i, j) in order) { sb.Append((char)(j * 85)); sb.Append((char)(i * 85)); }
        sb.Append("\xFF\0\0" + "\0\xFF\0" + "\0\0\xFF" + "\xFF\xFF\0"); // c00 red, c03 green, c33 blue, c30 yellow
        var data = sb.ToString();
        var obj = $"5 0 obj\n<< /ShadingType 6 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 200 0 200 0 1 0 1 0 1] /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n";
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build("/M sh", "/Shading << /M 5 0 R >>", obj, brokenXref: true)).Pages[0], 72);
        var red = Px(p, 4, 195); var green = Px(p, 195, 195); var mid = Px(p, 100, 100);
        Assert.True(red.R > 200 && red.G < 80);
        Assert.True(green.G > 200 && green.R < 80);
        Assert.NotEqual((255, 255, 255), mid);
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
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], 72);
        Assert.Equal((255, 0, 0), Px(p, 50, 100));
        Assert.Equal((255, 255, 255), Px(p, 150, 100));
        Assert.Equal((0, 0, 255), Px(p, 195, 5)); // mask is gone after Q

        var alpha = lum.Replace("Luminosity", "Alpha");
        var p2 = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build("/G1 gs 1 0 0 rg 0 0 200 200 re f", alpha, obj, brokenXref: true)).Pages[0], 72);
        Assert.Equal((255, 0, 0), Px(p2, 50, 100));
        Assert.Equal((255, 255, 255), Px(p2, 150, 100));
    }

    static RenderedPage RenderOptionalContentExpression(string expression) =>
        DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(
            "/OC /M BDC 1 0 0 rg 0 0 200 200 re f EMC",
            "/Properties << /M 6 0 R >>",
            "5 0 obj\n<< /Type /OCG /Name (layer) >>\nendobj\n" +
            $"6 0 obj\n<< /Type /OCMD /OCGs [5 0 R] /P /AllOff /VE {expression} >>\nendobj\n",
            brokenXref: true)).Pages[0], 72);

    [Fact]
    public void Optional_content_visibility_expressions_override_legacy_policy()
    {
        var and = RenderOptionalContentExpression("[/And 5 0 R 5 0 R]");
        Assert.Equal((255, 0, 0), Px(and, 100, 100));

        var not = RenderOptionalContentExpression("[/Not 5 0 R]");
        Assert.Equal((255, 255, 255), Px(not, 100, 100));

        var nested = RenderOptionalContentExpression("[/Or [/Not 5 0 R] [/And 5 0 R 5 0 R]]");
        Assert.Equal((255, 0, 0), Px(nested, 100, 100));

        var malformed = RenderOptionalContentExpression("[/Not 5 0 R 5 0 R]");
        Assert.Equal((255, 255, 255), Px(malformed, 100, 100));
    }

    [Fact]
    public void Optional_content_named_configuration_can_be_selected_for_rendering()
    {
        const string content =
            "/OC /A BDC 1 0 0 rg 0 0 100 200 re f EMC " +
            "/OC /B BDC 0 0 1 rg 100 0 100 200 re f EMC";
        const string resources = "/Properties << /A 5 0 R /B 6 0 R >>";
        const string objects =
            "5 0 obj\n<< /Type /OCG /Name (A) >>\nendobj\n" +
            "6 0 obj\n<< /Type /OCG /Name (B) >>\nendobj\n";
        const string optionalContent =
            "/OCProperties << /OCGs [5 0 R 6 0 R] " +
            "/D << /Name (Default) /BaseState /ON /OFF [6 0 R] >> " +
            "/Configs [" +
            "<< /Name (Layer A) /BaseState /OFF /ON [5 0 R] >> " +
            "<< /Name (Layer B) /BaseState /OFF /ON [6 0 R] >> " +
            "<< /Name (Unchanged) /BaseState /Unchanged >>" +
            "] >>";
        var doc = PdfDocument.Load(TestPdf.Build(content, resources, objects, brokenXref: true, extraCatalog: optionalContent));

        var defaultView = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        Assert.Equal((255, 0, 0), Px(defaultView, 50, 100));
        Assert.Equal((255, 255, 255), Px(defaultView, 150, 100));

        var layerA = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions { OptionalContentConfigurationName = "Layer A" }, 72);
        Assert.Equal((255, 0, 0), Px(layerA, 50, 100));
        Assert.Equal((255, 255, 255), Px(layerA, 150, 100));

        var layerB = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions { OptionalContentConfigurationName = "Layer B" }, 72);
        Assert.Equal((255, 255, 255), Px(layerB, 50, 100));
        Assert.Equal((0, 0, 255), Px(layerB, 150, 100));

        var unchanged = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions { OptionalContentConfigurationName = "Unchanged" }, 72);
        Assert.Equal((255, 0, 0), Px(unchanged, 50, 100));
        Assert.Equal((255, 255, 255), Px(unchanged, 150, 100));

        Assert.Throws<ArgumentException>(() => DocumentRenderer.RenderPage(doc.Pages[0],
            new PdfRenderOptions { OptionalContentConfigurationName = "missing" }, 72));
    }

    [Fact]
    public void Optional_content_usage_applications_follow_render_event()
    {
        const string content = "/OC /M BDC 1 0 0 rg 0 0 200 200 re f EMC";
        const string resources = "/Properties << /M 6 0 R >>";
        const string objects =
            "5 0 obj\n<< /Type /OCG /Name (layer) /Usage << " +
            "/View << /ViewState /OFF >> /Print << /PrintState /ON >> /Export << /ExportState /OFF >>" +
            " >> >>\nendobj\n" +
            "6 0 obj\n<< /Type /OCMD /OCGs [5 0 R] >>\nendobj\n";
        const string optionalContent =
            "/OCProperties << /OCGs [5 0 R] /D << /BaseState /ON /AS [" +
            "<< /Event /View /Category [/View] /OCGs [5 0 R] >> " +
            "<< /Event /Print /Category [/Print] /OCGs [5 0 R] >> " +
            "<< /Event /Export /Category [/Export] /OCGs [5 0 R] >>" +
            "] >> >>";
        var doc = PdfDocument.Load(TestPdf.Build(content, resources, objects,
            brokenXref: true, extraCatalog: optionalContent));

        var view = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        Assert.Equal((255, 255, 255), Px(view, 100, 100));

        var print = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions
        {
            OptionalContentDisplayEvent = ContentInterpreter.OptionalContentEvent.Print,
        }, 72);
        Assert.Equal((255, 0, 0), Px(print, 100, 100));

        var export = DocumentRenderer.RenderPage(doc.Pages[0], new PdfRenderOptions
        {
            OptionalContentDisplayEvent = ContentInterpreter.OptionalContentEvent.Export,
        }, 72);
        Assert.Equal((255, 255, 255), Px(export, 100, 100));
    }

    [Fact]
    public void Optional_content_zoom_usage_hides_layers_outside_configured_range()
    {
        const string content = "/OC /M BDC 1 0 0 rg 0 0 200 200 re f EMC";
        const string resources = "/Properties << /M 6 0 R >>";
        const string objects =
            "5 0 obj\n<< /Type /OCG /Usage << /View << /ViewState /ON >> /Zoom << /min 0.75 /max 1.5 >> >> >>\nendobj\n" +
            "6 0 obj\n<< /Type /OCMD /OCGs [5 0 R] >>\nendobj\n";
        const string optionalContent =
            "/OCProperties << /OCGs [5 0 R] /D << /BaseState /OFF /AS [" +
            "<< /Event /View /Category [/View /Zoom] /OCGs [5 0 R] >>" +
            "] >> >>";
        var doc = PdfDocument.Load(TestPdf.Build(content, resources, objects,
            brokenXref: true, extraCatalog: optionalContent));

        var inRange = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        Assert.Equal((255, 0, 0), Px(inRange, 100, 100));

        var outOfRange = DocumentRenderer.RenderPage(doc.Pages[0], 144);
        Assert.Equal((255, 255, 255), Px(outOfRange, 100, 100));
    }

    [Fact]
    public void RotatesPage()
    {
        var bytes = TestPdf.Build("1 0 0 rg 0 0 100 100 re f", mediaBox: "[0 0 200 100]");
        var s = System.Text.Encoding.Latin1.GetString(bytes).Replace("/Type /Page /Parent", "/Type /Page /Rotate 90 /Parent");
        var page = PdfDocument.Load(System.Text.Encoding.Latin1.GetBytes(s)).Pages[0];
        var p = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal((100, 200), (p.Width, p.Height));
    }
}
