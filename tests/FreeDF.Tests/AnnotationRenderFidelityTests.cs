using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

/// <summary>
/// Viewer-generated annotation appearances (annotations without /AP) and annotation flag / optional-content handling at render time
/// (ISO 32000-2 12.5.2-12.5.6). Fixtures are self-authored; the independent reference for the matrix is PDFium via tools/make_annot_matrix.py.
/// </summary>
public class AnnotationRenderFidelityTests
{
    const int Size = 200;

    static byte[] Pdf(string annot, string pageExtra = "", string catalogExtra = "", string extraObjects = "")
    {
        var s = "%PDF-2.0\n" +
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Size} {Size}] /Annots [4 0 R] {pageExtra} >>\nendobj\n" +
            $"4 0 obj\n<< /Type /Annot {annot} >>\nendobj\n" + extraObjects +
            "trailer\n<< /Root 1 0 R /Size 9 >>\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(s);
    }

    static byte[] Render(byte[] pdf, ContentInterpreter.OptionalContentEvent e = ContentInterpreter.OptionalContentEvent.View) =>
        DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], new PdfRenderOptions { OptionalContentDisplayEvent = e }, 72).Rgba;

    static bool IsWhite(byte[] px, int x, int y) { var o = (y * Size + x) * 4; return px[o] > 245 && px[o + 1] > 245 && px[o + 2] > 245; }
    static bool IsDark(byte[] px, int x, int y) { var o = (y * Size + x) * 4; return px[o] < 80 && px[o + 1] < 80 && px[o + 2] < 80; }
    static int Ink(byte[] px) { var n = 0; for (var i = 0; i < px.Length; i += 4) if (px[i] < 250 || px[i + 1] < 250 || px[i + 2] < 250) n++; return n; }

    const string FilledSquare = "/Subtype /Square /Rect [20 20 100 60] /IC [0 0 0] /BS << /W 0 >>";

    [Fact]
    public void Print_event_requires_print_flag_and_screen_honours_noview()
    {
        var print = ContentInterpreter.OptionalContentEvent.Print;
        Assert.True(IsDark(Render(Pdf(FilledSquare + " /F 4", ""), print), 60, 160));
        Assert.True(IsWhite(Render(Pdf(FilledSquare + " /F 0"), print), 60, 160));
        Assert.True(IsWhite(Render(Pdf(FilledSquare + " /F 6"), print), 60, 160));
        Assert.True(IsDark(Render(Pdf(FilledSquare + " /F 0")), 60, 160));
        Assert.True(IsWhite(Render(Pdf(FilledSquare + " /F 32")), 60, 160));
        Assert.True(IsDark(Render(Pdf(FilledSquare + " /F 36"), print), 60, 160));
    }

    [Fact]
    public void Annotation_optional_content_is_applied()
    {
        const string ocg = "5 0 obj\n<< /Type /OCG /Name (L) >>\nendobj\n";
        string Doc(string state) => $"/OCProperties << /OCGs [5 0 R] /D << {state} >> >>";
        Assert.True(IsWhite(Render(Pdf(FilledSquare + " /OC 5 0 R", catalogExtra: Doc("/OFF [5 0 R]"), extraObjects: ocg)), 60, 160));
        Assert.True(IsDark(Render(Pdf(FilledSquare + " /OC 5 0 R", catalogExtra: Doc("/ON [5 0 R]"), extraObjects: ocg)), 60, 160));
    }

    [Fact]
    public void NoRotate_keeps_annotation_upright_on_a_rotated_page()
    {
        var upright = Render(Pdf(FilledSquare + " /F 16", "/Rotate 90"));
        var turned = Render(Pdf(FilledSquare, "/Rotate 90"));
        // Rect [20 20 100 60] on a page rotated 90 degrees clockwise: the turned box is 40 wide x 80 tall at (20,20); the upright one 80 x 40 anchored at (60,20).
        Assert.True(IsDark(turned, 40, 80)); Assert.True(IsWhite(turned, 100, 40));
        Assert.True(IsDark(upright, 100, 40)); Assert.True(IsWhite(upright, 40, 80));
    }

    [Fact]
    public void Square_RD_insets_the_drawn_shape()
    {
        var px = Render(Pdf("/Subtype /Square /Rect [20 20 100 80] /RD [10 10 10 10] /IC [0 0 0] /BS << /W 0 >>"));
        Assert.True(IsWhite(px, 24, 175)); Assert.True(IsDark(px, 60, 140));
    }

    [Fact]
    public void Cloudy_border_differs_from_plain_rectangle()
    {
        var plain = Render(Pdf("/Subtype /Square /Rect [20 20 120 100] /C [0 0 1] /BS << /W 1 >>"));
        var cloud = Render(Pdf("/Subtype /Square /Rect [20 20 120 100] /C [0 0 1] /BS << /W 1 >> /BE << /S /C /I 1 >>"));
        Assert.True(Ink(cloud) > Ink(plain) * 1.5);
    }

    [Fact]
    public void Dash_array_from_BS_and_Border_is_honoured()
    {
        // Bottom edge is stroked first from the left corner: dash 6 on / 3 off.
        foreach (var d in new[] { "/BS << /W 2 /S /D /D [6 3] >>", "/Border [0 0 2 [6 3]]" })
        {
            var px = Render(Pdf("/Subtype /Square /Rect [20 20 100 80] /C [0 0 0] " + d));
            Assert.True(IsDark(px, 23, 178), d); Assert.True(!IsDark(px, 28, 178), d); Assert.True(IsDark(px, 32, 178), d);
        }
    }

    [Fact]
    public void Text_annotation_without_AP_draws_a_note_icon()
    {
        var px = Render(Pdf("/Subtype /Text /Rect [50 50 68 68] /Name /Note /Contents (x)"));
        Assert.True(Ink(px) > 100);
    }

    [Fact]
    public void Line_endings_are_drawn_for_Line_without_AP()
    {
        var plain = Render(Pdf("/Subtype /Line /Rect [10 10 190 190] /L [30 100 170 100] /C [0 0 0] /BS << /W 2 >>"));
        var arrow = Render(Pdf("/Subtype /Line /Rect [10 10 190 190] /L [30 100 170 100] /LE [/None /ClosedArrow] /IC [0 0.8 0] /C [0 0 0] /BS << /W 2 >>"));
        Assert.True(Ink(arrow) > Ink(plain));
    }

    [Fact]
    public void FreeText_background_comes_from_C_and_the_border_from_DA()
    {
        var px = Render(Pdf("/Subtype /FreeText /Rect [20 100 180 180] /Contents (x) /DA (0 0 1 rg /Helv 14 Tf) /C [1 1 0] /BS << /W 2 >>"));
        var o = (60 * Size + 100) * 4; // inside the box, away from the text
        Assert.True(px[o] > 240 && px[o + 1] > 240 && px[o + 2] < 40);
        o = (60 * Size + 20) * 4; // left border
        Assert.True(px[o + 2] > 200 && px[o] < 60);
    }
}
