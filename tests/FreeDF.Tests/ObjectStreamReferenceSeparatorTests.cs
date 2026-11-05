using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamReferenceSeparatorTests
{
    [Theory]
    [InlineData("[7 0R]")]
    [InlineData("[7 00000R]")]
    [InlineData("[7% first\n0R]")]
    [InlineData("<< /Ref 7 0R >>")]
    [InlineData("<< /Array [7 0R] >>")]
    [InlineData("[<< /Ref 7 0R >>]")]
    public void Compressed_references_require_a_separator_before_R(string value)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = FileWithValue(value, repair);
            Assert.Equal("indirect reference requires white space in object stream member",
                Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
        }
    }

    [Theory]
    [InlineData("7 0 R", 0)]
    [InlineData("7\0\t0\fR", 0)]
    [InlineData("7% first\r\n0% second\nR", 0)]
    [InlineData("7 00000 R", 0)]
    [InlineData("7 65535 R", 65535)]
    public void Complete_integer_and_comment_separators_preserve_reference_values(string reference, int generation)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = FileWithValue($"[{reference}]", repair);
            var array = Assert.IsType<PdfArray>(file.GetObject(2));
            Assert.Equal(new PdfRef(7, generation), Assert.Single(array.Items));
            Assert.Same(array, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("7 0R")]
    [InlineData("7 00000R")]
    [InlineData("7% first\n0R")]
    public void Default_lexer_retains_existing_reference_recovery(string source)
    {
        Assert.Equal(new PdfRef(7, 0), new PdfLexer(Encoding.ASCII.GetBytes(source)).ReadObject());
    }

    static PdfFile FileWithValue(string value, bool repair)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, value: value);
        if (repair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        return PdfFile.Load(bytes);
    }
}
