using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

/// <summary>Colour-space edge cases (8.6.5-8.6.6) and inert print parameters (10.5-10.6): malformed Indexed/Lab/Cal/DeviceN input must not throw or yield NaN colours.</summary>
public class ColorPrintEdgeTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y) { var o = (y * p.Width + x) * 4; return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]); }

    static RenderedPage Render(string content, string res, string extraObjects = "")
        => DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(content, res, extraObjects, brokenXref: true)).Pages[0], 72);

    static void Near((int R, int G, int B) a, (int R, int G, int B) e, int tol = 2)
        => Assert.True(Math.Abs(a.R - e.R) <= tol && Math.Abs(a.G - e.G) <= tol && Math.Abs(a.B - e.B) <= tol, $"expected {e} got {a}");

    static (int R, int G, int B) Paint(string cs, string color)
        => Px(Render($"/C cs {color} sc 0 0 200 200 re f", $"/ColorSpace << /C {cs} >>"), 100, 100);

    static PdfObject Object(string value) => new PdfLexer(Encoding.ASCII.GetBytes(value)).ReadObject()!;

    [Theory]
    [InlineData("-1")]
    [InlineData("300")]
    public void Indexed_OutOfRangeHivalIsClampedToTheSpecRange(string hival)
        => Near(Paint($"[/Indexed /DeviceRGB {hival} <FF0000>]", "0"), (255, 0, 0));

    [Fact]
    public void Indexed_IndexBeyondShortLookupUsesZeroComponents()
        => Near(Paint("[/Indexed /DeviceRGB 3 <FF0000>]", "2"), (0, 0, 0));

    [Fact]
    public void Indexed_LabBaseUsesTheLabDefaultDecode()
        => Near(Paint("[/Indexed [/Lab << /WhitePoint [0.9505 1 1.089] /Range [-128 127 -128 127] >>] 0 <8080 80>]", "0"), (119, 119, 119), 6);

    [Fact]
    public void Indexed_LargePaletteRendersEachEntry()
    {
        var palette = new StringBuilder();
        for (var i = 0; i < 256; i++) palette.Append($"{i:X2}{255 - i:X2}00");
        var cs = $"[/Indexed /DeviceRGB 255 <{palette}>]";
        Near(Paint(cs, "200"), (200, 55, 0));
        Near(Paint(cs, "255"), (255, 0, 0));
    }

    [Fact]
    public void Lab_ZeroOrNonPositiveWhitePointFallsBackToTheDefaultWhite()
    {
        var bad = Paint("[/Lab << /WhitePoint [0 0 0] >>]", "50 0 0");
        Near(bad, (119, 119, 119), 3);
        Near(bad, Paint("[/Lab << /WhitePoint [0.9505 1 1.089] >>]", "50 0 0"), 0);
    }

    [Fact]
    public void Lab_RangeClampsAAndBAndInvertedRangeIsOrdered()
    {
        Near(Paint("[/Lab << /WhitePoint [0.9505 1 1.089] /Range [-10 10 -10 10] >>]", "50 100 100"),
             Paint("[/Lab << /WhitePoint [0.9505 1 1.089] /Range [-10 10 -10 10] >>]", "50 10 10"), 0);
        Near(Paint("[/Lab << /WhitePoint [0.9505 1 1.089] /Range [10 -10 10 -10] >>]", "50 100 100"),
             Paint("[/Lab << /WhitePoint [0.9505 1 1.089] /Range [-10 10 -10 10] >>]", "50 10 10"), 0);
    }

    [Fact]
    public void CalGrayAndCalRgb_NonPositiveGammaIsTreatedAsLinear()
    {
        Near(Paint("[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma -2 >>]", "0.5"), Paint("[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 1 >>]", "0.5"), 0);
        Near(Paint("[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [0 -1 2] >>]", "0.5 0.5 0.5"),
             Paint("[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [1 1 2] >>]", "0.5 0.5 0.5"), 0);
    }

    [Fact]
    public void CalRgb_InvalidWhitePointFallsBackToDeviceRgb()
        => Near(Paint("[/CalRGB << /WhitePoint [0 1 1] >>]", "0.2 0.4 0.6"), (51, 102, 153));

    [Fact]
    public void Separation_TintTransformReturningFewerValuesThanTheAlternateDoesNotThrow()
    {
        var cs = "[/Separation /X /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0] /C1 [1 1] /N 1 >>]";
        var page = Render($"/C cs 1 scn 0 0 200 200 re f", $"/ColorSpace << /C {cs} >>");
        Near(Px(page, 100, 100), Px(Render("1 1 0 0 k 0 0 200 200 re f", ""), 100, 100), 0);
        Assert.Empty(page.Warnings);
    }

    [Fact]
    public void DeviceN_AttributesExposeProcessColorantsAndMixingHints()
    {
        var file = PdfFile.Load(TestPdf.Build(""));
        var fn = new PdfStream((PdfDictionary)Object("<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] >>"), Encoding.ASCII.GetBytes("{ 0 0 }"));
        var attrs = Object("<< /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow /Black] >> "
            + "/Colorants << /Spot [/Separation /Spot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>] /Bad 7 >> "
            + "/MixingHints << /Solidities << /Spot 1 >> /PrintingOrder [/Spot] >> >>");
        var cs = PdfColorSpace.Parse(file, new PdfArray([new PdfName("DeviceN"), Object("[/Cyan /Spot]"), new PdfName("DeviceCMYK"), fn, attrs]));
        var a = Assert.IsType<DeviceNAttributes>(cs.Attributes);
        Assert.Equal("NChannel", a.Subtype);
        Assert.Same(PdfColorSpace.Cmyk, a.ProcessColorSpace);
        Assert.Equal(new[] { "Cyan", "Magenta", "Yellow", "Black" }, a.ProcessComponents.ToArray());
        Assert.Equal(new[] { "Spot" }, a.Colorants.Keys.ToArray());
        Assert.Equal(new[] { "PrintingOrder", "Solidities" }, a.MixingHintKeys.Order(StringComparer.Ordinal).ToArray());
        // Attributes are hints: conversion still follows the tint transform.
        Near(ToByte(cs.ToRgb([1, 0.5])), ToByte(PdfColorSpace.Cmyk.ToRgb([1, 0.5, 0, 0])), 0);
        Assert.Null(PdfColorSpace.Cmyk.Attributes);
    }

    static (int R, int G, int B) ToByte(RgbColor c) => ((int)Math.Round(c.R * 255), (int)Math.Round(c.G * 255), (int)Math.Round(c.B * 255));

    // ---- DeviceCMYK on-screen conversion: PDFium (Chrome) reference values sampled with pypdfium2, see docs/colour-conversion.md ----

    [Theory]
    [InlineData("0 0 0 0", 255, 255, 255, 0)]
    [InlineData("0 0 0 1", 35, 31, 32, 1)]
    [InlineData("1 0 0 0", 0, 174, 239, 1)]
    [InlineData("0 1 0 0", 237, 2, 140, 1)]
    [InlineData("0 0 1 0", 255, 241, 1, 1)]
    [InlineData("0 1 1 0", 238, 29, 35, 1)]
    [InlineData("1 0 1 0", 0, 165, 79, 1)]
    [InlineData("1 1 0 0", 46, 48, 146, 1)]
    [InlineData("1 1 1 1", 0, 0, 0, 1)]
    [InlineData("0 0 0 0.5", 147, 149, 152, 3)]
    [InlineData("0.1 0.6 0.9 0.2", 184, 104, 43, 6)]
    [InlineData("0.33 0.5 0.17 0.4", 117, 90, 113, 6)]
    [InlineData("0.7 0.2 0.05 0.65", 15, 77, 102, 6)]
    [InlineData("0.25 0.25 0.25 0", 192, 182, 179, 6)]
    public void DeviceCmyk_MatchesPdfiumWithinTolerance(string cmyk, int r, int g, int b, int tolerance)
        => Near(Px(Render($"{cmyk} k 0 0 200 200 re f", ""), 100, 100), (r, g, b), tolerance);

    [Fact]
    public void DeviceCmyk_IsMonotonicInEachInkAndClampsOutOfRangeInput()
    {
        for (var ink = 0; ink < 4; ink++)
        {
            var prev = 765;
            for (var i = 0; i <= 8; i++)
            {
                double[] c = [0.2, 0.2, 0.2, 0.2]; c[ink] = i / 8.0;
                var rgb = PdfColorSpace.Cmyk.ToRgb(c);
                var sum = (int)Math.Round((rgb.R + rgb.G + rgb.B) * 255);
                Assert.True(sum <= prev + 3, $"ink {ink} step {i}: {sum} > {prev}");
                prev = sum;
            }
        }
        Assert.Equal(PdfColorSpace.Cmyk.ToRgb([1, 1, 1, 1]), PdfColorSpace.Cmyk.ToRgb([7, 9, 2, double.PositiveInfinity]));
        Assert.Equal(PdfColorSpace.Cmyk.ToRgb([0, 0, 0, 0]), PdfColorSpace.Cmyk.ToRgb([-3, double.NaN, -1, 0]));
    }

    // ---- nested transparency group blending space (11.4.7, 11.7.5.3) ----

    static RenderedPage GroupPage(string groupEntries)
    {
        const string body = "0.8 0.4 0.2 rg 0 0 200 200 re f /M gs 0.3 0.9 0.5 rg 0 0 200 200 re f";
        var objects = $"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency {groupEntries} >> /Resources << /ExtGState << /M << /BM /Multiply >> >> >> /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n";
        return Render("/F Do", "/XObject << /F 5 0 R >>", objects);
    }

    [Fact]
    public void NestedIsolatedGroup_CmykBlendingSpaceMultipliesComplementedComponents()
    {
        // Backdrop (.8,.4,.2) -> CMYK (0,.4,.6,.2); source (.3,.9,.5) -> (.6,0,.4,.1); complement-multiply -> (.6,.4,.76,.28) -> RGB (.12,.32,0).
        Near(Px(GroupPage("/I true /CS /DeviceCMYK"), 100, 100), (31, 82, 0), 2);
        Near(Px(GroupPage("/I true /CS /DeviceRGB"), 100, 100), (61, 92, 26), 2);
    }

    [Fact]
    public void NestedGroup_UnsupportedColourSpaceAndNonIsolatedGroupsKeepTheInheritedBlendingSpace()
    {
        var rgb = Px(GroupPage("/I true /CS /DeviceRGB"), 100, 100);
        Near(Px(GroupPage("/I true /CS [/Lab << /WhitePoint [0.9505 1 1.089] >>]"), 100, 100), rgb, 0);
        Near(Px(GroupPage("/I false /CS /DeviceCMYK"), 100, 100), rgb, 0);
    }

    // ---- inert print parameters and transfer interactions ----

    [Fact]
    public void HalftoneTypes_BlackGenerationAndUndercolorRemoval_AreInertOnScreenOutput()
    {
        const string content = "/A gs 0.1 0.6 0.9 0.2 k 20 20 160 160 re f 0.5 g 60 60 80 80 re f";
        var plain = Render(content, "/ExtGState << /A << >> >>");
        string[] entries =
        [
            "/HT << /Type /Halftone /HalftoneType 5 /Default << /Type /Halftone /HalftoneType 1 /Frequency 60 /Angle 45 /SpotFunction /Round >> /Cyan << /Type /Halftone /HalftoneType 1 /Frequency 40 /Angle 15 /SpotFunction /Diamond >> >>",
            "/HT << /Type /Halftone /HalftoneType 6 /Width 2 /Height 2 /Length 4 >>",
            "/HT << /Type /Halftone /HalftoneType 16 /Width 2 /Height 2 >>",
            "/HTP /Foo /HT /Default",
            "/BG << /FunctionType 2 /Domain [0 1] /N 2 >> /UCR << /FunctionType 2 /Domain [0 1] /N 3 >>",
            "/BG2 /Default /UCR2 /Default",
            "/BG2 << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >> /UCR2 << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>",
            "/FL 100 /SM 1 /RI /Saturation",
        ];
        foreach (var e in entries)
        {
            var page = Render(content, $"/ExtGState << /A << {e} >> >>");
            if (!e.Contains("/RI")) Assert.Equal(plain.Rgba, page.Rgba);
            Assert.DoesNotContain(page.Warnings, w => w.Contains("HT") || w.Contains("BG") || w.Contains("UCR"));
        }
    }

    [Fact]
    public void Transfer_AppliesToImageSamplesButNeverToItsSoftMaskAlpha()
    {
        const string objects = "5 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode /SMask 6 0 R /Length 7 >>\nstream\n808080>\nendstream\nendobj\n"
            + "6 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length 3 >>\nstream\n80>\nendstream\nendobj\n";
        const string res = "/XObject << /Im 5 0 R >> /ExtGState << /A << /TR2 << /FunctionType 2 /Domain [0 1] /N 2 >> >> >>";
        var plain = Px(Render("q 200 0 0 200 0 0 cm /Im Do Q", res, objects), 100, 100);
        var tr = Px(Render("/A gs q 200 0 0 200 0 0 cm /Im Do Q", res, objects), 100, 100);
        // Sample 128 -> 64 under the squared curve; alpha 128/255 is unchanged: 64*a + 255*(1-a).
        Near(plain, (192, 192, 192), 3);
        Near(tr, (159, 159, 159), 3);
    }

    [Fact]
    public void Transfer_AppliesToStencilMaskPaintAndIndexedImageSamples()
    {
        const string res = "/ExtGState << /A << /TR2 << /FunctionType 2 /Domain [0 1] /N 2 >> >> >>";
        var stencil = Render("/A gs 0.5 g q 200 0 0 200 0 0 cm BI /W 1 /H 1 /IM true /BPC 1 /F /AHx ID 00> EI Q", res);
        Near(Px(stencil, 100, 100), (64, 64, 64), 2);
        var indexed = Render("/A gs q 200 0 0 200 0 0 cm BI /W 1 /H 1 /CS [/I /RGB 1 <000000 808080>] /BPC 8 /F /AHx ID 01> EI Q", res);
        Near(Px(indexed, 100, 100), (64, 64, 64), 3);
    }
}
