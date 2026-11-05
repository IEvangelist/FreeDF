using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamLengthSchemaTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var flate in new[] { false, true })
            foreach (var length in new[] { "missing", "null", "real", "fractional", "-1", "true", "/Bad", "[]", "<< >>", "9 0 R" })
                yield return [length, flate];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Missing_or_noninteger_Length_requires_explicit_xref_recovery(string length, bool flate)
    {
        var file = PdfFile.Load(Build(length, flate));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData("integer", false)]
    [InlineData("integer", true)]
    [InlineData("plus", false)]
    [InlineData("plus", true)]
    [InlineData("zero-padded", false)]
    [InlineData("zero-padded", true)]
    public void Exact_nonnegative_integer_Length_keeps_clean_loading(string length, bool flate)
    {
        var file = PdfFile.Load(Build(length, flate));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Preindex_indirect_Length_uses_explicit_recovery_not_a_reference_ban(bool flate, bool nullValue)
    {
        var file = PdfFile.Load(Build("4 0 R", flate, nullValue ? "null" : "integer"));
        Assert.True(file.Repaired);
        AssertTarget(file);
        var resolved = file.Resolve(new PdfRef(4, 0));
        if (nullValue) Assert.IsType<PdfNull>(resolved);
        else Assert.Equal(Assert.IsType<PdfStream>(file.GetObject(3)).RawData.LongLength,
            Assert.IsType<PdfInteger>(resolved).Value);
    }

    static byte[] Build(string length, bool flate, string? indirect = null)
    {
        var bytes = XrefStreamCryptFilterTests.Build(flate ? "/FlateDecode" : null, flate,
            indirectObject: indirect == "integer" ? "0000000000" : indirect);
        var text = Encoding.Latin1.GetString(bytes);
        var start = text.IndexOf("\nstream\n", StringComparison.Ordinal) + "\nstream\n".Length;
        var count = text.IndexOf("\nendstream", start, StringComparison.Ordinal) - start;
        if (indirect == "integer")
            text = text.Replace("4 0 obj\n0000000000", $"4 0 obj\n{count:D10}", StringComparison.Ordinal);
        var value = length switch
        {
            "integer" => count.ToString(), "plus" => $"+{count}", "zero-padded" => $"000{count}",
            "real" => $"{count}.0", "fractional" => $"{count}.5", _ => length
        };
        text = text.Replace($"/Length {count}", length == "missing" ? "" : $"/Length {value}", StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(text);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
