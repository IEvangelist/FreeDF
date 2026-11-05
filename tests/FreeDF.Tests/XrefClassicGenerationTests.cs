using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefClassicGenerationTests
{
    internal static byte[] Build(long indexedGeneration, int actualGeneration = 0, bool free = false, bool fixedEntry = false)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var root = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = text.Length;
        text.Append($"2 {actualGeneration} obj\n<< /Type /Target >>\nendobj\n");
        var xref = text.Length;
        text.Append($"xref\n0 3\n0000000000 65535 f \n{root:D10} 00000 n \n");
        var offset = free ? 0 : target;
        text.Append(fixedEntry
            ? $"{offset:D10} {indexedGeneration:D5} {(free ? "f" : "n")} \n"
            : $"{offset} {indexedGeneration} {(free ? "f" : "n")}\n");
        text.Append($"trailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-4294967296)]
    [InlineData(2147483648)]
    [InlineData(4294967296)]
    [InlineData(long.MaxValue)]
    public void Unsupported_generation_values_repair_in_use_and_free_entries(long generation)
    {
        foreach (var free in new[] { false, true })
        {
            var file = PdfFile.Load(Build(generation, free: free));
            Assert.True(file.Repaired);
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
            Assert.DoesNotContain(file.Xref.Values, e => e.GenOrIndex < 0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    [InlineData(99999)]
    public void Fixed_and_fallback_lossless_compatibility_remains_unchanged(int generation)
    {
        foreach (var fixedEntry in new[] { false, true })
        {
            var file = PdfFile.Load(Build(generation, generation, fixedEntry: fixedEntry));
            Assert.False(file.Repaired);
            Assert.Equal(generation, file.Xref[2].GenOrIndex);
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(new PdfRef(2, generation))).Get("Type").AsName());
            Assert.False(file.Repaired);
        }
    }

    [Fact]
    public void Fallback_int_generation_boundary_is_preserved_without_narrowing()
    {
        var file = PdfFile.Load(Build(int.MaxValue, int.MaxValue));
        Assert.False(file.Repaired);
        Assert.Equal(int.MaxValue, file.Xref[2].GenOrIndex);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(new PdfRef(2, int.MaxValue))).Get("Type").AsName());
        Assert.False(file.Repaired);
    }
}
