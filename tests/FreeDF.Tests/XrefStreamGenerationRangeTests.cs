using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamGenerationRangeTests
{
    [Theory]
    [InlineData(0UL, 65536UL)]
    [InlineData(1UL, 65536UL)]
    [InlineData(0UL, 99999UL)]
    [InlineData(1UL, 99999UL)]
    [InlineData(0UL, 2147483647UL)]
    [InlineData(1UL, 2147483647UL)]
    public void Generation_above_65535_requires_explicit_section_recovery(ulong type, ulong generation)
    {
        var file = PdfFile.Load(XrefStreamEntryRangeTests.Build(type, third: generation));
        Assert.True(file.Repaired);
        Assert.Equal(0, file.Xref[2].GenOrIndex);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(1UL, 0UL)]
    [InlineData(0UL, 65534UL)]
    [InlineData(1UL, 65534UL)]
    [InlineData(0UL, 65535UL)]
    [InlineData(1UL, 65535UL)]
    public void Representable_generation_boundaries_preserve_indexed_identity(ulong type, ulong generation)
    {
        var file = PdfFile.Load(XrefStreamEntryRangeTests.Build(type, third: generation, generation: (int)generation));
        Assert.False(file.Repaired);
        Assert.Equal((int)generation, file.Xref[2].GenOrIndex);
        var target = file.GetObject(new PdfRef(2, (int)generation));
        if (type == 0) Assert.IsType<PdfNull>(target);
        else Assert.Equal("Target", Assert.IsType<PdfDictionary>(target).Get("Type").AsName());
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData(65536UL)]
    [InlineData(99999UL)]
    [InlineData(2147483647UL)]
    public void Compressed_member_index_is_not_a_generation_number(ulong index)
    {
        var file = PdfFile.Load(XrefStreamEntryRangeTests.Build(2, second: 3, third: index));
        Assert.False(file.Repaired);
        Assert.True(file.Xref[2].Compressed);
        Assert.Equal((int)index, file.Xref[2].GenOrIndex);
    }
}
