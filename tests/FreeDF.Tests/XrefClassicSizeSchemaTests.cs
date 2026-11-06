using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefClassicSizeSchemaTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var fixedEntry in new[] { false, true })
            foreach (var value in new[] { "", "null", "-1", "0", "3.0", "3.5", "/Three", "(3)", "true", "[3]", "<< /Value 3 >>", "2 0 R", "3 0 R" })
                yield return [value, fixedEntry];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Missing_indirect_or_invalid_size_requires_explicit_recovery(string value, bool fixedEntry)
    {
        var file = PdfFile.Load(Build(value, fixedEntry));
        Assert.True(file.Repaired);
        AssertTarget(file);
        if (value == "3 0 R")
            Assert.Equal(4, Assert.IsType<PdfInteger>(file.GetObject(new PdfRef(3, 0))).Value);
    }

    [Theory]
    [InlineData("3", false)]
    [InlineData("+3", false)]
    [InlineData("0003", false)]
    [InlineData("3", true)]
    [InlineData("+3", true)]
    [InlineData("0003", true)]
    public void Direct_positive_integer_spellings_preserve_indexed_loading(string value, bool fixedEntry)
    {
        var file = PdfFile.Load(Build(value, fixedEntry));
        Assert.False(file.Repaired);
        Assert.Equal(3, Assert.IsType<PdfInteger>(file.Trailer.Get("Size")).Value);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("3.0", true)]
    [InlineData("2 0 R", true)]
    [InlineData("", false)]
    public void Invalid_older_section_is_not_hidden_by_valid_latest_size(string value, bool fixedEntry)
    {
        var file = PdfFile.Load(Build(value, fixedEntry, incremental: true));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_incremental_sections_keep_precedence(bool fixedEntry)
    {
        var file = PdfFile.Load(Build("3", fixedEntry, incremental: true));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    internal static byte[] Build(string value, bool fixedEntry, bool incremental = false)
    {
        var text = Encoding.ASCII.GetString(XrefClassicGenerationTests.Build(0, fixedEntry: fixedEntry));
        text = text.Replace("/Size 3", value.Length == 0 ? "" : $"/Size {value}", StringComparison.Ordinal);
        if (value == "3 0 R")
        {
            var old = text.IndexOf("xref\n", StringComparison.Ordinal);
            var extra = "3 0 obj\n4\nendobj\n";
            text = text[..old] + extra + text[old..];
            var trailer = text.IndexOf("trailer\n", StringComparison.Ordinal);
            text = text[..trailer] + $"3 1\n{old:D10} 00000 n \n" + text[trailer..];
            var footer = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
            text = text[..footer] + $"startxref\n{old + extra.Length}\n%%EOF\n";
        }
        if (incremental)
        {
            var old = text.IndexOf("xref\n", StringComparison.Ordinal);
            var root = text.IndexOf("1 0 obj", StringComparison.Ordinal);
            var offset = text.Length;
            text += $"xref\n1 1\n{root:D10} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R /Prev {old} >>\nstartxref\n{offset}\n%%EOF\n";
        }
        return Encoding.ASCII.GetBytes(text);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
    }
}
