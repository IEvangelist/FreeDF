using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamLengthSchemaTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("22.0")]
    [InlineData("22.5")]
    [InlineData("-1")]
    [InlineData("true")]
    [InlineData("/Bad")]
    [InlineData("[]")]
    [InlineData("<< >>")]
    [InlineData("9 0 R")]
    public void Recovered_object_stream_requires_a_nonnegative_integer_Length(string length)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(length, repair));
            Assert.IsType<PdfNull>(file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("23")]
    [InlineData("+23")]
    [InlineData("00023")]
    public void Valid_integer_Length_preserves_member_identity(string length)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(length, repair));
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("22.0")]
    [InlineData("-1")]
    public void Ordinary_stream_recovery_is_not_made_strict(string length)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(length, repair, ordinary: true));
            var stream = Assert.IsType<PdfStream>(file.GetObject(3));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), stream.RawData);
            Assert.Equal(repair, file.Repaired);
        }
    }

    internal static byte[] Build(string length, bool repair, bool ordinary = false)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true,
            length: length == "missing" ? "null" : length);
        var text = Encoding.Latin1.GetString(bytes);
        if (length == "missing") text = text.Replace("/Length null", "            ", StringComparison.Ordinal);
        if (ordinary) text = text.Replace("/ObjStm", "/OtherX", StringComparison.Ordinal);
        if (repair) text = text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n";
        return Encoding.Latin1.GetBytes(text);
    }
}
