using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;

namespace AdoBye.Tests;

/// <summary>ISO 32000-2 8.9.7 inline images: the /L length key and the /IM flag.</summary>
public class InlineImageTests
{
    static int GrayAt(string content, int x, int y)
    {
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(content)).Pages[0], 72);
        return p.Rgba[(y * p.Width + x) * 4];
    }

    // RunLength data: copy 9 bytes " EI Q 0  " (which contains a plausible EI token), no EOD.
    const string Payload = "\u0008 EI Q 0  ";

    [Fact]
    public void LengthKeyOverridesTheEiSearch()
    {
        var content = $"q 90 0 0 10 0 0 cm BI /W 9 /H 1 /BPC 8 /CS /G /F /RL /L 10 ID {Payload}\nEI Q";
        Assert.Equal(0x30, GrayAt(content, 65, 195));
    }

    [Fact]
    public void WithoutLengthKeyTheEiHeuristicTruncatesThisPayload()
    {
        var content = $"q 90 0 0 10 0 0 cm BI /W 9 /H 1 /BPC 8 /CS /G /F /RL ID {Payload}\nEI Q";
        Assert.NotEqual(0x30, GrayAt(content, 65, 195));
    }

    [Fact]
    public void ImageMaskFalse_IsNotTreatedAsAStencil()
    {
        // 2x1 gray image, unfiltered: the data length must come from 8 bits per component, not 1.
        var content = "q 20 0 0 10 0 0 cm BI /W 2 /H 1 /BPC 8 /CS /G /IM false ID \u0040\u00C0\nEI Q";
        Assert.Equal(0x40, GrayAt(content, 5, 195));
        Assert.Equal(0xC0, GrayAt(content, 15, 195));
    }
}

