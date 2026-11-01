using System.Text;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

// Self-authored structural fixtures (ISO 32000-2 7.5.8.4 hybrid xref, 7.7.3 page tree).
public class FileStructureTests
{
    [Fact]
    public void HybridXref_StreamSuppliesObjectsMarkedFreeInTable()
    {
        var sb = new StringBuilder("%PDF-1.5\n");
        var offs = new Dictionary<int, int>();
        void Obj(int n, string body) { offs[n] = sb.Length; sb.Append($"{n} 0 obj\n{body}\nendobj\n"); }
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << >> >>");
        const string content = "1 0 0 rg 0 0 200 200 re f";
        Obj(4, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");

        var xrefStmPos = sb.Length;
        var row = new StringBuilder();
        row.Append('\x01').Append((char)(offs[4] >> 24 & 255)).Append((char)(offs[4] >> 16 & 255)).Append((char)(offs[4] >> 8 & 255)).Append((char)(offs[4] & 255)).Append('\x00');
        sb.Append($"5 0 obj\n<< /Type /XRef /W [1 4 1] /Index [4 1] /Size 6 /Length {row.Length} >>\nstream\n{row}\nendstream\nendobj\n");

        var xref = sb.Length;
        sb.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var n in new[] { 1, 2, 3 }) sb.Append($"{offs[n]:D10} 00000 n \n");
        sb.Append("4 1\n0000000000 00000 f \n");
        sb.Append($"trailer\n<< /Size 6 /Root 1 0 R /XRefStm {xrefStmPos} >>\nstartxref\n{xref}\n%%EOF\n");

        var doc = PdfDocument.Load(Encoding.Latin1.GetBytes(sb.ToString()));
        var p = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        var o = (100 * p.Width + 100) * 4;
        Assert.True(p.Rgba[o] > 200 && p.Rgba[o + 1] < 50);
    }

    static string Tree(int depth, bool emptyLeafPages = false)
    {
        var sb = new StringBuilder("%PDF-1.7\n");
        for (var i = 1; i <= depth; i++) sb.Append($"{i} 0 obj\n<< /Type /Pages /Kids [{i + 1} 0 R] /Count 1 >>\nendobj\n");
        sb.Append(emptyLeafPages
            ? $"{depth + 1} 0 obj\n<< /Type /Pages /Count 0 >>\nendobj\n"
            : $"{depth + 1} 0 obj\n<< /Type /Page /MediaBox [0 0 10 10] >>\nendobj\n");
        sb.Append($"{depth + 2} 0 obj\n<< /Type /Catalog /Pages 1 0 R >>\nendobj\ntrailer\n<< /Root {depth + 2} 0 R /Size {depth + 3} >>\n%%EOF\n");
        return sb.ToString();
    }

    [Fact]
    public void PageTree_EmptyPagesNodeIsNotAPage()
        => Assert.Empty(PdfDocument.Load(Encoding.Latin1.GetBytes(Tree(2, emptyLeafPages: true))).Pages);

    [Fact]
    public void PageTree_ModerateDepthWorks()
        => Assert.Single(PdfDocument.Load(Encoding.Latin1.GetBytes(Tree(50))).Pages);

    [Fact]
    public void PageTree_HostileDepthDoesNotOverflowStack()
    {
        var doc = PdfDocument.Load(Encoding.Latin1.GetBytes(Tree(20000)));
        Assert.True(doc.Pages.Count <= 1);
    }
}
