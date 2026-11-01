using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;

namespace FreeDF.Tests;

/// <summary>ISO 32000-2 11.6.5.3: an SMask /Matte means the image samples are pre-blended with the matte colour.</summary>
public class SoftMaskMatteTests
{
    static string Image(int n, string dict, string hex) => $"{n} 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 {dict} /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream\nendobj\n";

    static (int R, int G, int B) Render(string matte)
    {
        var objects = Image(5, "/ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R", "404040>")
            + Image(6, "/ColorSpace /DeviceGray /BitsPerComponent 8 " + matte, "80>");
        var pdf = TestPdf.Build("200 0 0 200 0 0 cm /Im0 Do", "/XObject << /Im0 5 0 R >>", objects, brokenXref: true);
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
        var o = (100 * p.Width + 100) * 4;
        return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]);
    }

    [Fact]
    public void WithoutMatte_SamplesAreUsedAsIs()
    {
        // 0x40 at alpha 128/255 over white.
        var (r, _, _) = Render("");
        Assert.InRange(r, 157, 161);
    }

    [Fact]
    public void BlackMatte_UnPremultipliesBeforeCompositing()
    {
        // c = 0x40 * 255 / 128 = 127.5; over white at alpha 128/255 -> about 191.
        var (r, g, b) = Render("/Matte [0 0 0]");
        Assert.InRange(r, 188, 194);
        Assert.Equal(r, g);
        Assert.Equal(r, b);
    }

    [Fact]
    public void MatteEqualToTheSampleLeavesItUnchanged()
    {
        var (r, _, _) = Render("/Matte [0.25098 0.25098 0.25098]");
        Assert.InRange(r, 157, 161);
    }
}

