using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamHeaderIntegerTests
{
    static PdfFile FileWithHeader(string header, bool forceRepair, string count = "1", string value = "42")
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true,
            header: header, first: header.Length.ToString(), count: count, value: value);
        if (forceRepair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        return PdfFile.Load(bytes);
    }

    [Theory]
    [InlineData("--2 0 ")]
    [InlineData("++2 0 ")]
    [InlineData("2 --0 ")]
    [InlineData("2 ++0 ")]
    [InlineData("2 +-0 ")]
    [InlineData("2 -+0 ")]
    [InlineData("2.0 0 ")]
    [InlineData("2 0.0 ")]
    public void Header_requires_integer_tokens_without_repeated_signs(string header)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithHeader(header, forceRepair);
            Assert.IsType<PdfNull>(file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("+2 +0 ")]
    [InlineData("002 000 ")]
    [InlineData("2 -0 ")]
    [InlineData("2 % object number\n 0 ")]
    public void Valid_integer_spellings_comments_and_signed_zero_remain_supported(string header)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithHeader(header, forceRepair);
            var member = Assert.IsType<PdfInteger>(file.GetObject(2));
            Assert.Equal(42, member.Value);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("--2 0 ", 1, "<< /Type /Compressed >>")]
    [InlineData("2 --0 ", 1, "<< /Type /Compressed >>")]
    [InlineData("2 0 ++5 3 ", 2, "42 43")]
    [InlineData("2 0 5 ++3 ", 2, "42 43")]
    public void Invalid_header_spelling_cannot_replace_uncompressed_or_install_partial_members(string header, int count, string value)
    {
        var file = PdfFile.Load(ObjectStreamRecoveryHeaderTests.Build(header, value, count));
        Assert.True(file.Repaired);
        Assert.False(file.Xref[2].Compressed);
        Assert.False(file.Xref.ContainsKey(5));
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Theory]
    [InlineData("2 0 ++5 3 ")]
    [InlineData("2 0 5 ++3 ")]
    public void Invalid_later_header_spelling_prevents_normal_member_installation(string header)
    {
        var file = FileWithHeader(header, false, "2", "42 43");
        Assert.IsType<PdfNull>(file.GetObject(2));
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(2));
        Assert.False(file.Xref.ContainsKey(5));
    }
}
