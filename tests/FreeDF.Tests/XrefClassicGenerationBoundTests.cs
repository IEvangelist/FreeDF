using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefClassicGenerationBoundTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var fixedEntry in new[] { false, true })
            foreach (var free in new[] { false, true })
                foreach (var generation in new[] { 65536, 99999, int.MaxValue })
                    if (!fixedEntry || generation <= 99999)
                        yield return [generation, free, fixedEntry];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Oversized_generation_requires_repair_without_losing_recoverable_target(int generation, bool free, bool fixedEntry)
    {
        var file = PdfFile.Load(XrefClassicGenerationTests.Build(generation, free: free, fixedEntry: fixedEntry));
        Assert.True(file.Repaired);
        Assert.Equal(0, file.Xref[2].GenOrIndex);
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.True(file.Repaired);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(65534, false, false)]
    [InlineData(65535, false, false)]
    [InlineData(0, false, true)]
    [InlineData(65534, false, true)]
    [InlineData(65535, false, true)]
    [InlineData(0, true, false)]
    [InlineData(65534, true, false)]
    [InlineData(65535, true, false)]
    [InlineData(0, true, true)]
    [InlineData(65534, true, true)]
    [InlineData(65535, true, true)]
    public void Representable_generation_keeps_existing_indexed_disposition(int generation, bool free, bool fixedEntry)
    {
        var file = PdfFile.Load(XrefClassicGenerationTests.Build(generation, generation, free, fixedEntry));
        Assert.False(file.Repaired);
        Assert.Equal(generation, file.Xref[2].GenOrIndex);
        Assert.Equal(!free, file.Xref[2].InUse);
        var target = file.GetObject(new PdfRef(2, generation));
        if (free) Assert.IsType<PdfNull>(target);
        else Assert.Equal("Target", Assert.IsType<PdfDictionary>(target).Get("Type").AsName());
        Assert.False(file.Repaired);
    }
}
