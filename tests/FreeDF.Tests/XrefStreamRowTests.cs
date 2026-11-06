using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamRowTests
{
    internal static byte[] Build(int removed, bool split = false, bool extra = false)
    {
        var text = Encoding.Latin1.GetString(XrefStreamWidthTests.Build("[1 4 2]"));
        var start = text.IndexOf("\nstream\n", StringComparison.Ordinal) + "\nstream\n".Length;
        var end = text.IndexOf("\nendstream", start, StringComparison.Ordinal);
        var length = end - start;
        text = text[..(end - removed)] + (extra ? "extra" : "") + text[end..];
        text = text.Replace($"/Length {length}", $"/Length {length - removed + (extra ? 5 : 0)}", StringComparison.Ordinal);
        if (split) text = text.Replace("/Index [1 3]", "/Index [1 2 3 1]", StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(text);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(14, false)]
    [InlineData(20, false)]
    [InlineData(21, false)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    public void Missing_declared_rows_repair_the_whole_section(int removed, bool split)
    {
        var file = PdfFile.Load(Build(removed, split));
        Assert.True(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.IsType<PdfStream>(file.GetObject(new PdfRef(3, 0)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Complete_rows_preserve_subsections_but_surplus_bytes_require_repair(bool split, bool extra)
    {
        var file = PdfFile.Load(Build(0, split, extra));
        Assert.Equal(extra, file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.IsType<PdfStream>(file.GetObject(new PdfRef(3, 0)));
        Assert.Equal(extra, file.Repaired);
    }
}
