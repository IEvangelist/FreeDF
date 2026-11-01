using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

/// <summary>ISO 32000-2 8.6.5.6 DefaultGray/DefaultRGB/DefaultCMYK remapping of Device spaces.</summary>
public class DefaultColorSpaceTests
{
    const string LinearRgb = "[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [1 1 1] /Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505] >>]";
    const string CalGray = "[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 2.2 >>]";

    static int[] Render(string content, string res, string objects = "")
    {
        var pdf = TestPdf.Build(content, res, objects, brokenXref: objects.Length > 0);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var o = ((p.Height - 100) * p.Width + 100) * 4;
        return [p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]];
    }

    const string Fill = " 0 0 200 200 re f";

    [Fact]
    public void OperatorsAndNamesUseTheDefaultSpace()
    {
        var res = $"/ColorSpace << /DefaultGray {CalGray} /DefaultRGB {LinearRgb} /C /DeviceRGB >>";
        Assert.InRange(Render("0.1 g" + Fill, res)[0], 17, 21);
        Assert.Equal(Render("0.1 g" + Fill, res), Render("/CS0 cs 0.1 sc" + Fill, res.Replace("/C /DeviceRGB", "/CS0 /DeviceGray")));
        Assert.InRange(Render("0.5 0.5 0.5 rg" + Fill, res)[0], 186, 190);
        Assert.InRange(Render("/C cs 0.5 0.5 0.5 sc" + Fill, res)[0], 186, 190);
        Assert.InRange(Render("0.5 0.5 0.5 RG 40 w 0 100 m 200 100 l S", res)[0], 186, 190);
    }

    [Fact]
    public void WithoutDefaultsDeviceSpacesAreUnchanged()
        => Assert.Equal([26, 26, 26], Render("0.1 g" + Fill, "/ColorSpace << >>"));

    [Theory]
    [InlineData("/DefaultGray [/Lab << /WhitePoint [0.9642 1 0.8249] >>]")] // wrong component count
    [InlineData("/DefaultGray /Pattern")]
    [InlineData("/DefaultRGB [/Lab << /WhitePoint [0.9642 1 0.8249] >>]")]
    [InlineData("/DefaultGray [/Indexed /DeviceRGB 0 <FF0000>]")]
    [InlineData("/DefaultGray 7 0 R")]
    public void UnusableDefaultsAreIgnored(string entry)
        => Assert.Equal(entry.Contains("DefaultRGB") ? [128, 128, 128] : [26, 26, 26], Render(entry.Contains("DefaultRGB") ? "0.5 0.5 0.5 rg" + Fill : "0.1 g" + Fill, $"/ColorSpace << {entry} >>"));

    [Fact]
    public void DeviceSpaceNestedInAnotherSpaceIsNotRemapped()
    {
        var res = $"/ColorSpace << /DefaultGray {CalGray} /I [/Indexed /DeviceGray 1 <0020>] >>";
        Assert.Equal([32, 32, 32], Render("/I cs 1 sc" + Fill, res));
    }

    [Fact]
    public void ImagesUseTheDefaultSpace()
    {
        const string image = "5 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length 3 >>\nstream\n1A>\nendstream\nendobj\n";
        var plain = Render("q 200 0 0 200 0 0 cm /Im Do Q", "/XObject << /Im 5 0 R >>", image);
        var mapped = Render("q 200 0 0 200 0 0 cm /Im Do Q", $"/XObject << /Im 5 0 R >> /ColorSpace << /DefaultGray {CalGray} >>", image);
        Assert.Equal([26, 26, 26], plain);
        Assert.InRange(mapped[0], 17, 21);
    }
}
