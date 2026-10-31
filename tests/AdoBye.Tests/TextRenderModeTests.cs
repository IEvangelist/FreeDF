using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

// Self-authored fixtures: Type 3 glyphs follow the invisible/clip-only text render modes (ISO 32000-2 9.3.6).
public class TextRenderModeTests
{
    static int Dark(int mode)
    {
        var res = "/Font << /F1 6 0 R >>";
        var glyph = "1000 0 d0 0 0 1000 1000 re f";
        var objs = "6 0 obj\n<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 7 0 R >> /Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] /Resources << >> >>\nendobj\n"
                 + $"7 0 obj\n<< /Length {glyph.Length} >>\nstream\n{glyph}\nendstream\nendobj\n";
        var p = DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build($"BT /F1 100 Tf {mode} Tr 0 0 Td (a) Tj ET", res, objs, brokenXref: true)).Pages[0], 72);
        return p.Rgba[(150 * p.Width + 50) * 4];
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    public void Type3_RespectsInvisibleModes(int mode, bool painted) => Assert.Equal(painted, Dark(mode) < 50);
}
