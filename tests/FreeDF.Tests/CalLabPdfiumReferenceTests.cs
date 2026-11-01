using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

/// <summary>
/// CalRGB/CalGray/Lab references rendered by PDFium (pypdfium2) at 72 dpi from the same constant-colour fills. Only D65-white
/// spaces are compared: PDFium does not chromatically adapt other white points (its D50 CalRGB green is 81,255,0 where the
/// Bradford-adapted result here is 0,255,0), so those are intentionally not asserted. Within 2 levels. CalGray is not asserted: PDFium keeps gray ~identity (0.25 -> 64, 0.1 -> 26) while the spec gamma 2.2 followed by the sRGB curve gives 61 and 19.
/// </summary>
public class CalLabPdfiumReferenceTests
{
    const string CalRgb = "[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [2.2 2.2 2.2] /Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505] >>]";
    const string Lab = "[/Lab << /WhitePoint [0.9505 1 1.089] /Range [-128 127 -128 127] >>]";

    static int[] Paint(string cs, string color)
    {
        var pdf = TestPdf.Build($"/C cs {color} sc 0 0 20 20 re f", $"/ColorSpace << /C {cs} >>", "", brokenXref: false);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var o = ((p.Height - 10) * p.Width + 10) * 4;
        return [p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]];
    }

    [Theory]
    [InlineData(CalRgb, "0.2 0.6 0.9", 47, 155, 230)]
    [InlineData(CalRgb, "0.9 0.4 0.1", 230, 102, 18)]
    [InlineData(CalRgb, "0.1 0.1 0.1", 18, 18, 18)]
    [InlineData(Lab, "50 60 0", 208, 63, 121)]
    [InlineData(Lab, "50 -60 0", 0, 142, 117)]
    [InlineData(Lab, "50 0 60", 143, 116, 0)]
    [InlineData(Lab, "75 30 -40", 205, 167, 255)]
    [InlineData(Lab, "90 -20 70", 231, 234, 83)]
    public void MatchesPdfium(string cs, string color, int r, int g, int b)
    {
        var got = Paint(cs, color);
        Assert.InRange(got[0], r - 2, r + 2);
        Assert.InRange(got[1], g - 2, g + 2);
        Assert.InRange(got[2], b - 2, b + 2);
    }
}
