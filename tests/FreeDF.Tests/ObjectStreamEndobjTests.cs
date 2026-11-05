using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamEndobjTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\n42\nendobj\n")]
    [InlineData("\nnull\nendobj\n")]
    [InlineData("\n[]\nendobj\n")]
    [InlineData("\n<< >>\nendobj\n")]
    [InlineData("\nendobjX\n")]
    [InlineData("\nendobj0\n")]
    [InlineData("\nendstream\nendobj\n")]
    public void Stream_value_requires_endobj_without_intervening_values(string closing)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(closing, repair));
            if (repair) Assert.IsType<PdfNull>(file.GetObject(2));
            else Assert.Equal("object stream endstream must be followed by endobj",
                Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.IsType<PdfNull>(file.GetObject(2));
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\0")]
    [InlineData("\f")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("%comment\n")]
    [InlineData("%comment\r\n \t")]
    public void PDF_whitespace_and_complete_comments_preserve_the_closing_keyword(string trivia)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(trivia + "endobj\n", repair));
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\n42\nendobj\n")]
    [InlineData("\nendobjX\n")]
    public void Ordinary_stream_closing_recovery_remains_permissive(string closing)
    {
        foreach (var repair in new[] { false, true })
        {
            var text = Encoding.Latin1.GetString(Build(closing, repair)).Replace("/ObjStm", "/OtherX", StringComparison.Ordinal);
            var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), Assert.IsType<PdfStream>(file.GetObject(3)).RawData);
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n%endobj")]
    public void EOF_or_an_unterminated_comment_cannot_supply_endobj(string closing)
    {
        var source = Encoding.Latin1.GetString(Build(closing, false));
        var end = source.IndexOf("\nendstream", StringComparison.Ordinal) + "\nendstream".Length;
        var file = PdfFile.Load(Encoding.Latin1.GetBytes(source[..end] + closing));
        Assert.True(file.Repaired);
        Assert.IsType<PdfNull>(file.GetObject(2));
        Assert.False(file.Xref.ContainsKey(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Indirect_Type_cannot_bypass_the_required_endobj(bool repair)
    {
        var source = Encoding.Latin1.GetString(ObjectStreamTypeTests.Build("/ObjStm", repair, indirect: true));
        var changed = source.Replace("endstream\nendobj", "endstream\nendobX", StringComparison.Ordinal);
        Assert.NotEqual(source, changed);
        var file = PdfFile.Load(Encoding.Latin1.GetBytes(changed));
        if (repair) Assert.IsType<PdfNull>(file.GetObject(2));
        else Assert.Equal("object stream endstream must be followed by endobj",
            Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(2));
    }

    static byte[] Build(string closing, bool repair)
    {
        var source = XrefStreamEntryRangeTests.Build(2, compressed: true, closing: closing);
        if (!repair) return source;
        var text = Encoding.Latin1.GetString(source);
        return Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
    }
}
