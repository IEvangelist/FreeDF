using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamBoundedLexerTests
{
    [Theory]
    [InlineData("[42", "]", "unterminated array in object stream member")]
    [InlineData("<< /Type /First", ">>", "unterminated dictionary in object stream member")]
    [InlineData("(abc", ")", "unterminated literal string in object stream member")]
    [InlineData("<4", ">", "unterminated hexadecimal string in object stream member")]
    public void Member_cannot_borrow_a_closing_delimiter_from_the_next_member(string first, string second, string message)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, first.Length + 1,
                repair, firstBody: first, secondBody: second));
            Assert.Equal(message, Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
        }
    }

    [Theory]
    [InlineData("42", typeof(PdfInteger))]
    [InlineData("true", typeof(PdfBoolean))]
    [InlineData("/Alpha", typeof(PdfName))]
    [InlineData("(abc)", typeof(PdfString))]
    [InlineData("<41>", typeof(PdfString))]
    [InlineData("[42]", typeof(PdfArray))]
    [InlineData("<< /Type /First >>", typeof(PdfDictionary))]
    public void Complete_values_remain_isolated_from_a_malformed_following_member(string first, Type type)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, first.Length + 1,
                repair, firstBody: first, secondBody: "["));
            var value = file.GetObject(2);
            Assert.Equal(type, value.GetType());
            Assert.Same(value, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
            Assert.Throws<PdfParseException>(() => file.GetObject(5));
            var reloaded = file.GetObject(2);
            Assert.Equal(type, reloaded.GetType());
            Assert.Same(reloaded, file.GetObject(2));
        }
    }

    [Theory]
    [InlineData("4243", 2)]
    [InlineData("+123", 2)]
    [InlineData("truefalse", 4)]
    [InlineData("nulltrue", 4)]
    [InlineData("/AlphaBeta", 6)]
    [InlineData("/A#41", 4)]
    public void A_declared_boundary_cannot_make_a_token_prefix_valid(string first, int end)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, end,
                repair, firstBody: first));
            Assert.Throws<PdfParseException>(() => file.GetObject(2));
        }
    }

    [Theory]
    [InlineData("(abc ", ")")]
    [InlineData("<41 ", ">")]
    [InlineData("[42 ", "]")]
    [InlineData("<< /A 42 ", ">>")]
    public void Bounded_lexer_stops_before_a_large_unrelated_tail(string prefix, string closer)
    {
        var data = Encoding.ASCII.GetBytes(prefix + new string(' ', 1024 * 1024) + closer);
        var lexer = new PdfLexer(data, 0, objectStreamMember: true, end: prefix.Length);
        Assert.Throws<PdfParseException>(() => lexer.ReadObject());
        Assert.Equal(prefix.Length, lexer.Pos);
        Assert.Equal(prefix.Length, lexer.Length);
        Assert.Same(data, lexer.Data);
        Assert.NotNull(new PdfLexer(data).ReadObject());
    }
}
