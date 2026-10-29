using System.Text;

namespace AdoBye.Tests;

/// <summary>Builds minimal valid PDFs for tests.</summary>
public static class TestPdf
{
    public static byte[] Build(string content, string extraResources = "", string extraObjects = "", string mediaBox = "[0 0 200 200]", bool brokenXref = false)
    {
        var sb = new StringBuilder("%PDF-2.0\n");
        var offs = new List<int>();
        void Obj(int n, string body) { offs.Add(sb.Length); sb.Append($"{n} 0 obj\n{body}\nendobj\n"); }
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, $"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /Contents 4 0 R /Resources << {extraResources} >> >>");
        Obj(4, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        if (extraObjects.Length > 0) sb.Append(extraObjects);
        var xref = sb.Length;
        sb.Append($"xref\n0 {offs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) sb.Append($"{(brokenXref ? 0 : o):D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {offs.Count + 1} /Root 1 0 R >>\nstartxref\n{(brokenXref ? 7 : xref)}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
