using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamTrailingValueTests
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
    [InlineData("42 43")]
    [InlineData("42 endobj")]
    [InlineData("null true")]
    [InlineData("/Name /Other")]
    [InlineData("(abc) (def)")]
    [InlineData("[42] ]")]
    [InlineData("<< /Type /Target >> 43")]
    [InlineData("<< /Type /Target >> endobj")]
    [InlineData("42 % comment\n43")]
    public void Trailing_noncomment_tokens_cannot_be_ignored_before_caching(string value)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            Assert.Equal(forceRepair, file.Repaired);
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Throws<PdfParseException>(() => file.GetObject(2));
            Assert.True(file.Repaired);
        }
    }

    [Theory]
    [InlineData("42 \t\r\n", typeof(PdfInteger))]
    [InlineData("42 % trailing comment", typeof(PdfInteger))]
    [InlineData("null % comment\n", typeof(PdfNull))]
    [InlineData("<< /Type /Target >> % comment\n", typeof(PdfDictionary))]
    public void Trailing_whitespace_and_comments_are_not_extra_values(string value, Type expectedType)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = file.GetObject(2);
            Assert.IsType(expectedType, member);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false, "42 43 ")]
    [InlineData(true, "42 43 ")]
    [InlineData(false, "<< /Type /Target >> endobj ")]
    [InlineData(true, "<< /Type /Target >> endobj ")]
    public void Trailing_tokens_do_not_change_next_member_identity(bool forceRepair, string first)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, first.Length + 1,
            forceRepair, firstBody: first));
        Assert.Equal("Second", Assert.IsType<PdfDictionary>(file.GetObject(5)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(2));
        Assert.Equal("Second", Assert.IsType<PdfDictionary>(file.GetObject(5)).Get("Type").AsName());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Next_member_is_not_treated_as_dictionary_trailing_data(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal("Second", Assert.IsType<PdfDictionary>(file.GetObject(5)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
    }
}
