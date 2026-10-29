using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

public class CffTests
{
    static bool Dark(RenderedPage p, int x, int y) => p.Rgba[(y * p.Width + x) * 4] < 64;

    [Fact]
    public void Embedded_cff_glyphs_render_for_bare_and_opentype_wrappers()
    {
        var doc = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cff.pdf"));
        var p = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        // Font size 50: A is a 30x35 square at x 10..40, B a blob at x 45..75; baselines y=20 and y=120 (PDF), flipped to device.
        foreach (var baseline in new[] { 20, 120 })
        {
            var cy = 200 - baseline - 17;
            Assert.True(Dark(p, 25, cy), $"A missing at baseline {baseline}");
            Assert.True(Dark(p, 60, cy), $"B missing at baseline {baseline}");
            Assert.False(Dark(p, 46, 200 - baseline - 1), "blob corner should be empty");
            Assert.False(Dark(p, 100, cy));
        }
    }
}
