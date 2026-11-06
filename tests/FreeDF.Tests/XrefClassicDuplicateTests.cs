using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefClassicDuplicateTests
{
    internal static byte[] Build(string shape, bool fixedEntries)
    {
        var original = XrefClassicGenerationTests.Build(0, fixedEntry: true);
        var text = new StringBuilder(Encoding.ASCII.GetString(original));
        var root = text.ToString().IndexOf("1 0 obj", StringComparison.Ordinal);
        var target = text.ToString().IndexOf("2 0 obj", StringComparison.Ordinal);
        var oldXref = text.ToString().IndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var xref = text.Length;
        string Entry(int number, bool free = false)
        {
            var offset = free ? 0 : number == 1 ? root : target;
            var generation = free ? 1 : 0;
            var kind = free ? "f" : "n";
            return fixedEntries ? $"{offset:D10} {generation:D5} {kind} \n" : $"{offset} {generation} {kind}\n";
        }
        text.Append("xref\n");
        text.Append(shape switch
        {
            "duplicate-free" => $"2 1\n{Entry(2, true)}2 1\n{Entry(2)}",
            "duplicate-inuse" => $"2 1\n{Entry(2)}2 1\n{Entry(2, true)}",
            "overlap" => $"1 2\n{Entry(1)}{Entry(2, true)}2 1\n{Entry(2)}",
            "disjoint" => $"2 1\n{Entry(2)}1 1\n{Entry(1)}",
            "free-update" => $"2 1\n{Entry(2, true)}",
            _ => throw new ArgumentException("Unknown test subsection shape", nameof(shape))
        });
        text.Append($"trailer\n<< /Size 3 /Root 1 0 R /Prev {oldXref} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    [Theory]
    [InlineData("duplicate-free", true)]
    [InlineData("duplicate-free", false)]
    [InlineData("duplicate-inuse", true)]
    [InlineData("duplicate-inuse", false)]
    [InlineData("overlap", true)]
    [InlineData("overlap", false)]
    public void Repeated_object_numbers_within_one_section_use_explicit_recovery(string shape, bool fixedEntries)
    {
        var file = PdfFile.Load(Build(shape, fixedEntries));
        Assert.True(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Theory]
    [InlineData("disjoint", true)]
    [InlineData("disjoint", false)]
    [InlineData("free-update", true)]
    [InlineData("free-update", false)]
    public void Disjoint_out_of_order_subsections_and_repeated_revision_numbers_remain_supported(string shape, bool fixedEntries)
    {
        var file = PdfFile.Load(Build(shape, fixedEntries));
        Assert.False(file.Repaired);
        if (shape == "free-update")
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
        else
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.False(file.Repaired);
    }
}
