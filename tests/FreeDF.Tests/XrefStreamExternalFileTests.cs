using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamExternalFileTests
{
    [Theory]
    [InlineData("(external-xref.bin)", null, false)]
    [InlineData("(external-xref.bin)", null, true)]
    [InlineData("(https://example.invalid/xref)", null, false)]
    [InlineData("(https://example.invalid/xref)", null, true)]
    [InlineData("<< /Type /Filespec /F (external-xref.bin) >>", null, false)]
    [InlineData("<< /Type /Filespec /F (external-xref.bin) >>", null, true)]
    [InlineData("4 0 R", "(external-xref.bin)", false)]
    [InlineData("4 0 R", "(external-xref.bin)", true)]
    [InlineData("4 0 R", "<< /Type /Filespec /F (external-xref.bin) >>", false)]
    [InlineData("4 0 R", "<< /Type /Filespec /F (external-xref.bin) >>", true)]
    [InlineData("9 0 R", null, false)]
    [InlineData("9 0 R", null, true)]
    public void Declared_external_source_does_not_publish_inline_rows_as_a_clean_index(
        string source, string? indirect, bool flate)
    {
        var file = PdfFile.Load(Build(source, indirect, flate));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preindex_unresolved_null_reference_is_explicit_recovery_not_an_external_source_verdict(bool flate)
    {
        var file = PdfFile.Load(Build("4 0 R", "null", flate));
        Assert.True(file.Repaired);
        Assert.IsType<PdfNull>(file.Resolve(new PdfRef(4, 0)));
        AssertTarget(file);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("null", false)]
    [InlineData("null", true)]
    public void Absent_or_direct_null_source_preserves_inline_index_loading(string? source, bool flate)
    {
        var file = PdfFile.Load(Build(source, null, flate));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    static byte[] Build(string? source, string? indirect, bool flate)
    {
        var bytes = XrefStreamCryptFilterTests.Build(flate ? "/FlateDecode" : "[]", flate,
            indirectObject: indirect);
        if (source is null) return bytes;
        var text = Encoding.Latin1.GetString(bytes).Replace(" /Length ", $" /F {source} /Length ", StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(text);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
