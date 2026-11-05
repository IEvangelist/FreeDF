using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamLiteralCloserTests
{
    static PdfFile FileWithValue(string value, bool forceRepair)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, value: value);
        if (forceRepair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        return PdfFile.Load(bytes);
    }

    [Theory]
    [InlineData("(")]
    [InlineData("(abc")]
    [InlineData("(a(b)")]
    [InlineData("(abc\\")]
    [InlineData("(abc\\)")]
    [InlineData("[(abc]")]
    [InlineData("<< /Key (abc >>")]
    [InlineData("(abc\\\r\n")]
    public void Compressed_literal_strings_require_unescaped_closing_parenthesis(string value)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            Assert.Equal(forceRepair, file.Repaired);
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Contains("unterminated literal string", Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.True(file.Repaired);
        }
    }

    [Theory]
    [InlineData("()", "")]
    [InlineData("(abc)", "abc")]
    [InlineData("(a(b)c)", "a(b)c")]
    [InlineData("(a\\)b)", "a)b")]
    [InlineData("(a\\(b)", "a(b")]
    [InlineData("(a\\\\b)", "a\\b")]
    [InlineData("(\\101)", "A")]
    [InlineData("(a\\\r\nb)", "ab")]
    public void Complete_strings_preserve_bytes_nesting_and_escapes(string value, string expected)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = Assert.IsType<PdfString>(file.GetObject(2));
            Assert.Equal(Encoding.Latin1.GetBytes(expected), member.Bytes);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unterminated_unrequested_string_preserves_valid_member(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "(abc"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Fact]
    public void Default_lexer_preserves_existing_unterminated_string_recovery()
    {
        var value = new PdfLexer(Encoding.Latin1.GetBytes("(abc")).ReadObject();
        Assert.Equal(Encoding.Latin1.GetBytes("abc"), Assert.IsType<PdfString>(value).Bytes);
    }
}
