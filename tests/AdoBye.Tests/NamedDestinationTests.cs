using PortableDocument.Document;

namespace AdoBye.Tests;

public class NamedDestinationTests
{
    static PdfDocument Source(int depth, bool cycle)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 6 0 R >> /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [<< /Subtype /Link /Rect [0 0 10 10] /Dest (target) >>] >>",
            "<< /First 5 0 R /Last 5 0 R >>",
            "<< /Title (Deep target) /Parent 4 0 R /A << /S /GoTo /D (target) >> >>",
        };
        for (var i = 0; i < depth; i++)
        {
            var self = i + 6;
            objects.Add($"<< /Kids [{(cycle ? $"{self} 0 R " : "")}{self + 1} 0 R] >>");
        }
        objects.Add("<< /Names [(target) << /D [3 0 R /Fit] >>] >>");
        return PageTreeTests.Load(objects.ToArray());
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(20, true)]
    [InlineData(64, false)]
    public void Deep_and_cyclic_named_destination_trees_resolve_and_survive_import(int depth, bool cycle)
    {
        var source = Source(depth, cycle);
        Assert.Equal(0, Assert.Single(source.GetOutlines()).PageIndex);
        Assert.Equal(0, Assert.Single(source.Pages[0].GetAnnotations()).DestPage);
        foreach (var incremental in new[] { false, true })
        {
            var editor = PdfEditor.Open(source);
            editor.SetInfo("Title", "Preserve named destination");
            var saved = PdfDocument.Load(incremental ? editor.SaveIncremental() : editor.Save());
            Assert.Equal(0, Assert.Single(saved.GetOutlines()).PageIndex);
        }
        var merged = PdfEditor.Open(PageTreeTests.Load(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R >>"));
        merged.ImportPages(source, null, null, PdfImportOptions.Merge("source"));
        var result = PdfDocument.Load(merged.Save());
        Assert.Equal(1, Assert.Single(Assert.Single(result.GetOutlines()).Children).PageIndex);
    }
}
