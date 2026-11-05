using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamOpeningEolTests
{
    [Theory]
    [InlineData("\r", "")]
    [InlineData("", " ")]
    [InlineData("", "\t")]
    [InlineData("", "\0")]
    [InlineData("", "\f")]
    [InlineData("", "%note\n")]
    public void Opening_keyword_requires_LF_or_CRLF_not_CR_or_counted_header_trivia(string opening, string prefix)
    {
        foreach (var repair in new[] { false, true })
            AssertRejected(Build(opening, prefix, repair), repair);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void LF_and_CRLF_preserve_exact_member_identity(string opening)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(opening, "", repair));
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Valid_openings_allow_empty_object_streams(string opening)
    {
        foreach (var repair in new[] { false, true })
        {
            var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true,
                header: "", value: "", count: "0", first: "0", opening: opening);
            var file = PdfFile.Load(Repair(bytes, repair));
            Assert.Empty(Assert.IsType<PdfStream>(file.GetObject(3)).RawData);
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("\r", "")]
    [InlineData("", " ")]
    [InlineData("", "%note\n")]
    public void Ordinary_stream_opening_recovery_remains_permissive(string opening, string prefix)
    {
        foreach (var repair in new[] { false, true })
        {
            var text = Encoding.Latin1.GetString(Build(opening, prefix, repair)).Replace("/ObjStm", "/OtherX", StringComparison.Ordinal);
            var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
            Assert.Equal(Encoding.Latin1.GetBytes(prefix + "2 0 << /Type /Target >>"),
                Assert.IsType<PdfStream>(file.GetObject(3)).RawData);
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Fact]
    public void Indirect_object_stream_Type_cannot_bypass_the_opening_check()
    {
        foreach (var repair in new[] { false, true })
        {
            var text = Encoding.Latin1.GetString(ObjectStreamLengthTests.Build(false, repair, indirectType: true));
            var changed = text.Replace("\nstream\n2 0 ", "\nstream\r2 0 ", StringComparison.Ordinal);
            Assert.NotEqual(text, changed);
            AssertRejected(Encoding.Latin1.GetBytes(changed), repair);
        }
    }

    static void AssertRejected(byte[] bytes, bool repair)
    {
        var file = PdfFile.Load(bytes);
        if (repair) Assert.IsType<PdfNull>(file.GetObject(2));
        else Assert.Equal("object stream keyword must be followed by LF or CRLF",
            Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(2));
        Assert.IsType<PdfNull>(file.GetObject(2));
    }

    static byte[] Build(string opening, string prefix, bool repair)
        => Repair(XrefStreamEntryRangeTests.Build(2, compressed: true,
            header: prefix + "2 0 ", first: (4 + prefix.Length).ToString(), opening: opening), repair);

    static byte[] Repair(byte[] source, bool repair)
    {
        if (!repair) return source;
        var text = Encoding.Latin1.GetString(source);
        return Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
    }
}
