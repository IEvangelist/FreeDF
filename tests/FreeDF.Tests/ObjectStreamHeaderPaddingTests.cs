using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamHeaderPaddingTests
{
    static PdfFile FileWithPadding(string padding, bool forceRepair)
    {
        var header = "2 0 " + padding;
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true,
            header: header, first: header.Length.ToString(), value: "<< /Type /Target >>");
        if (forceRepair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        return PdfFile.Load(bytes);
    }

    [Theory]
    [InlineData("null ")]
    [InlineData("42 ")]
    [InlineData("5 0 ")]
    [InlineData("q ")]
    [InlineData("/Ignored ")]
    [InlineData("[] ")]
    [InlineData("<< >> ")]
    [InlineData("% unterminated header comment ")]
    [InlineData(" % comment\n true ")]
    public void Header_padding_cannot_hide_extra_tokens_or_comment_out_First(string padding)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithPadding(padding, forceRepair);
            Assert.IsType<PdfNull>(file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0\t\r\n\f ")]
    [InlineData("% comment\n")]
    [InlineData("% comment\r")]
    [InlineData("% comment\r\n\t% another\n ")]
    public void Header_padding_accepts_pdf_whitespace_and_complete_comments(string padding)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithPadding(padding, forceRepair);
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("null ")]
    [InlineData("% unterminated ")]
    public void Invalid_header_padding_cannot_replace_uncompressed_identity(string padding)
    {
        var file = PdfFile.Load(ObjectStreamRecoveryHeaderTests.Build("2 0 " + padding, "<< /Type /Compressed >>"));
        Assert.True(file.Repaired);
        Assert.False(file.Xref[2].Compressed);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }
}
