using System.Text;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;

namespace FreeDF.Tests;

/// <summary>ISO 32000-2 9.6.5 (Type 3 d0/d1) and 8.2 (BX/EX compatibility sections).</summary>
public class Type3AndCompatibilityTests
{
    static string Glyph(string content) => $"6 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n";

    const string Font = "/Font << /F0 << /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /A 6 0 R >> /Encoding << /Type /Encoding /Differences [65 /A] >> /FirstChar 65 /LastChar 65 /Widths [1000] >> >>";

    static (int R, int G, int B) Center(string glyph, string textState)
    {
        var pdf = TestPdf.Build($"{textState} BT /F0 100 Tf 50 50 Td (A) Tj ET", Font, Glyph(glyph), brokenXref: true);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var o = (100 * p.Width + 100) * 4;
        return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]);
    }

    [Fact]
    public void D1Glyph_IgnoresColourOperatorsAndUsesTextFillColour()
    {
        Assert.Equal((0, 0, 255), Center("1000 0 0 0 1000 1000 d1 1 0 0 rg 0 0 1000 1000 re f", "0 0 1 rg"));
    }

    [Fact]
    public void D1Glyph_StrokeUsesFillColour()
    {
        Assert.Equal((0, 0, 255), Center("1000 0 0 0 1000 1000 d1 0 1 0 RG 400 w 0 500 m 1000 500 l S", "0 0 1 rg"));
    }

    [Fact]
    public void D0Glyph_MayChooseItsOwnColour()
    {
        Assert.Equal((255, 0, 0), Center("1000 0 d0 1 0 0 rg 0 0 1000 1000 re f", "0 0 1 rg"));
    }

    [Fact]
    public void D1Colour_DoesNotLeakIntoFollowingContent()
    {
        var pdf = TestPdf.Build("0 0 1 rg BT /F0 100 Tf 50 50 Td (A) Tj ET 1 0 0 rg 150 150 40 40 re f", Font, Glyph("1000 0 0 0 1000 1000 d1 0 1 0 rg 0 0 1000 1000 re f"), brokenXref: true);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var o = (30 * p.Width + 170) * 4;
        Assert.Equal((255, 0, 0), (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void Type3Glyphs_ArePaintedNeitherWhenInvisibleNorClipOnly(int mode)
    {
        var pdf = TestPdf.Build($"0 0 1 rg {mode} Tr BT /F0 100 Tf 50 50 Td (A) Tj ET", Font, Glyph("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f"), brokenXref: true);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var o = (100 * p.Width + 100) * 4;
        Assert.Equal((255, 255, 255), (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]));
    }

    [Fact]
    public void Type3WithoutResources_UsesTheCurrentResources()
    {
        const string form = "7 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 1000 1000] /Length 22 >>\nstream\n0 0 1000 1000 re f    \nendstream\nendobj\n";
        var pdf = TestPdf.Build("0 0 1 rg BT /F0 100 Tf 50 50 Td (A) Tj ET", Font + " /XObject << /Fm0 7 0 R >>", Glyph("1000 0 0 0 1000 1000 d1 /Fm0 Do") + form, brokenXref: true);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var o = (100 * p.Width + 100) * 4;
        Assert.Equal((0, 0, 255), (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]));
    }

    [Fact]
    public void UnknownOperator_WarnsOutsideCompatibilitySection()
    {
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build("1 2 zz")).Pages[0], 72);
        Assert.Contains("unknown operator zz", p.Warnings);
    }

    [Fact]
    public void UnknownOperator_IsSilentlyIgnoredInsideBxEx()
    {
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build("BX 1 2 zz EX 0 0 1 rg 0 0 50 50 re f")).Pages[0], 72);
        Assert.Empty(p.Warnings);
        var o = (175 * p.Width + 10) * 4;
        Assert.Equal((0, 0, 255), (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]));
    }

    [Fact]
    public void BxSections_Nest()
    {
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build("BX BX zz EX yy EX")).Pages[0], 72);
        Assert.Empty(p.Warnings);
    }
}

