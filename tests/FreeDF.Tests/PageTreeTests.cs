using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PageTreeTests
{
    internal static PdfDocument Load(params string[] objects)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(text.Length);
            text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = text.Length;
        text.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Root 1 0 R /Size {offsets.Count} >>\nstartxref\n{xref}\n%%EOF\n");
        return PdfDocument.Load(Encoding.ASCII.GetBytes(text.ToString()));
    }

    [Fact]
    public void Branch_overrides_do_not_leak_to_siblings_or_replace_leaf_attributes()
    {
        var doc = Load(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /MediaBox [0 0 600 800] /Rotate 90 /Resources << /Marker /Root >> >>",
            "<< /Type /Pages /Kids [6 0 R 7 0 R] /Rotate 180 /Resources << /Marker /Branch >> >>",
            "<< /Type /Page /Parent 2 0 R >>",
            "<< /Type /Pages /Kids [8 0 R] >>",
            "<< /Type /Page /Parent 3 0 R /CropBox [10 20 590 780] /Rotate 270 >>",
            "<< /Type /Page /Parent 3 0 R >>",
            "<< /Type /Page /Parent 5 0 R >>");
        Assert.Equal(4, doc.Pages.Count);
        Assert.Equal([270, 180, 90, 90], doc.Pages.Select(p => p.Rotate));
        Assert.Equal(["Branch", "Branch", "Root", "Root"], doc.Pages.Select(p => p.Resources.Get("Marker").AsName()));
        Assert.All(doc.Pages, p => Assert.Equal(new PdfRect(0, 0, 600, 800), p.MediaBox));
        Assert.Equal(new PdfRect(10, 20, 590, 780), doc.Pages[0].CropBox);
        Assert.Equal(doc.Pages[1].MediaBox, doc.Pages[1].CropBox);
        var saved = PdfDocument.Load(PdfEditor.Open(doc).Save());
        Assert.Equal(doc.Pages.Select(p => p.Rotate), saved.Pages.Select(p => p.Rotate));
        Assert.Equal(doc.Pages.Select(p => p.CropBox), saved.Pages.Select(p => p.CropBox));
    }

    [Fact]
    public void Cycles_and_duplicate_kids_still_visit_each_leaf_once()
    {
        var doc = Load(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [2 0 R 3 0 R 3 0 R] /MediaBox [0 0 400 500] >>",
            "<< /Type /Page /Parent 2 0 R >>");
        Assert.Equal(new PdfRect(0, 0, 400, 500), Assert.Single(doc.Pages).MediaBox);
    }
}
