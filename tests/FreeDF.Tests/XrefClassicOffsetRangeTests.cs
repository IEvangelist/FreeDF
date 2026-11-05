using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefClassicOffsetRangeTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    [InlineData(-4294967296, false)]
    [InlineData(-4294967296, true)]
    [InlineData(long.MinValue, false)]
    [InlineData(long.MinValue, true)]
    public void Negative_offset_or_next_free_number_requires_immediate_recovery(long offset, bool free)
    {
        var file = PdfFile.Load(Build(offset.ToString(System.Globalization.CultureInfo.InvariantCulture), free));
        Assert.True(file.Repaired);
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.DoesNotContain(file.Xref.Values, entry => !entry.Compressed && entry.OffsetOrStream < 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Nonnegative_indexed_fields_keep_existing_target_disposition(bool free, bool fixedEntry)
    {
        var file = PdfFile.Load(XrefClassicGenerationTests.Build(0, free: free, fixedEntry: fixedEntry));
        Assert.False(file.Repaired);
        var target = file.Resolve(file.Catalog.Get("Probe"));
        if (free) Assert.IsType<PdfNull>(target);
        else Assert.Equal("Target", Assert.IsType<PdfDictionary>(target).Get("Type").AsName());
        Assert.False(file.Repaired);
    }

    internal static byte[] Build(string field, bool free)
    {
        var text = Encoding.ASCII.GetString(XrefClassicGenerationTests.Build(0, free: free));
        var end = text.IndexOf("trailer\n", StringComparison.Ordinal);
        var start = text.LastIndexOf('\n', end - 2) + 1;
        var row = $"{field} 0 {(free ? "f" : "n")}\n";
        return Encoding.ASCII.GetBytes(text[..start] + row + text[end..]);
    }
}
