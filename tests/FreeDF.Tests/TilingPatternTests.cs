using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

// Self-authored fixtures: expectations derive from ISO 32000-2 8.7.3 (tiling patterns).
public class TilingPatternTests
{
    static RenderedPage Render(string patDict)
    {
        const string content = "/Pattern cs /P scn 0 0 200 200 re f";
        var body = "1 0 0 rg 0 0 10 10 re f";
        var objs = $"5 0 obj\n<< /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /Resources << >> {patDict} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n";
        var bytes = TestPdf.Build(content, "/Pattern << /P 5 0 R >>", objs, brokenXref: true);
        return DocumentRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], 72);
    }

    [Fact]
    public void NegativeSteps_MatchPositiveSteps()
    {
        var pos = Render("/XStep 20 /YStep 20");
        var neg = Render("/XStep -20 /YStep -20");
        Assert.Equal(pos.Rgba, neg.Rgba);
        // Cell at origin is red, gap is paper.
        var o = (190 * pos.Width + 5) * 4;
        Assert.True(pos.Rgba[o] > 200 && pos.Rgba[o + 1] < 50);
        o = (190 * pos.Width + 15) * 4;
        Assert.True(pos.Rgba[o + 1] > 200);
    }

    [Fact]
    public void HugeMatrix_DoesNotThrowOrHang()
    {
        _ = Render("/XStep 20 /YStep 20 /Matrix [1e30 0 0 1e30 0 0]");
        _ = Render("/XStep 1e-12 /YStep 1e-12");
        _ = Render("/XStep 1e300 /YStep 1e300");
    }
}
