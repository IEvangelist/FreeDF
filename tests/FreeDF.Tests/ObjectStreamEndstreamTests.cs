using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamEndstreamTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\0")]
    [InlineData("\f")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\n\n")]
    [InlineData("%note")]
    public void Uncounted_padding_cannot_replace_the_optional_single_EOL(string padding)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(padding, repair));
            if (repair) Assert.IsType<PdfNull>(file.GetObject(2));
            else Assert.Equal("object stream endstream must follow Length with at most one EOL",
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
    [InlineData("\r\n")]
    [InlineData("%note")]
    public void Counted_trailing_member_trivia_remains_part_of_the_stream(string padding)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(padding, repair, counted: true));
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Exact_extent_allows_no_EOL_or_one_LF_CR_or_CRLF(string eol)
    {
        foreach (var repair in new[] { false, true })
        {
            var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, terminator: eol + "endstream");
            var file = PdfFile.Load(Repair(bytes, repair));
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("%note")]
    public void Ordinary_stream_terminator_recovery_is_explicit(string padding)
    {
        foreach (var repair in new[] { false, true })
        {
            var text = Encoding.Latin1.GetString(Build(padding, repair)).Replace("/ObjStm", "/OtherX", StringComparison.Ordinal);
            var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), Assert.IsType<PdfStream>(file.GetObject(3)).RawData);
            Assert.True(file.Repaired);
        }
    }

    [Fact]
    public void Physical_EOF_without_an_endstream_marker_cannot_publish_members()
    {
        var source = Encoding.Latin1.GetString(XrefStreamEntryRangeTests.Build(2, compressed: true));
        var bytes = Encoding.Latin1.GetBytes(source[..source.IndexOf("\nendstream", StringComparison.Ordinal)]);
        var file = PdfFile.Load(bytes);
        Assert.True(file.Repaired);
        Assert.IsType<PdfNull>(file.GetObject(2));
        Assert.False(file.Xref.ContainsKey(2));
    }

    [Theory]
    [InlineData("endstreamX")]
    [InlineData("endstream0")]
    public void Terminator_must_be_a_complete_keyword_not_a_prefix(string keyword)
    {
        foreach (var repair in new[] { false, true })
        {
            var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, terminator: "\n" + keyword);
            var file = PdfFile.Load(Repair(bytes, repair));
            if (repair) Assert.IsType<PdfNull>(file.GetObject(2));
            else Assert.Equal("object stream is missing a valid endstream marker",
                Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    static byte[] Build(string padding, bool repair, bool counted = false)
        => Repair(XrefStreamEntryRangeTests.Build(2, compressed: true,
            value: "<< /Type /Target >>" + padding, length: (counted ? 23 + padding.Length : 23).ToString()), repair);

    static byte[] Repair(byte[] source, bool repair)
    {
        if (!repair) return source;
        var text = Encoding.Latin1.GetString(source);
        return Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
    }
}
