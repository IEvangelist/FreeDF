using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamMemberCommentTests
{
    const string First = "<< /Type /First /Note (% literal) >>";
    const string Second = "<< /Type /Second >>\n";

    [Theory]
    [InlineData("%")]
    [InlineData("% comment")]
    [InlineData(" % comment\t\f\0")]
    [InlineData("% ) >> [ /text")]
    [InlineData("% comment\n% unfinished")]
    [InlineData("\r\n% unfinished")]
    public void Trailing_comment_cannot_consume_the_following_member(string suffix)
    {
        foreach (var repair in new[] { false, true })
        {
            var firstBody = First + suffix;
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, firstBody.Length + 1,
                repair, firstBody: firstBody, secondBody: Second));
            Assert.Equal("object stream member comment crosses its boundary",
                Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.Equal("Second", Assert.IsType<PdfDictionary>(file.GetObject(5)).Get("Type").AsName());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0\t\r\n\f ")]
    [InlineData("% complete\n")]
    [InlineData("% complete\r")]
    [InlineData("% complete\r\n")]
    [InlineData("% first\n \t% second\r\n ")]
    public void Complete_comments_and_whitespace_stop_at_the_next_member(string suffix)
    {
        foreach (var repair in new[] { false, true })
        {
            var firstBody = First + suffix;
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, firstBody.Length + 1,
                repair, firstBody: firstBody, secondBody: Second));
            var first = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("First", first.Get("Type").AsName());
            Assert.Equal("% literal", Assert.IsType<PdfString>(first.Get("Note")).Text);
            Assert.Equal("Second", Assert.IsType<PdfDictionary>(file.GetObject(5)).Get("Type").AsName());
            Assert.Same(first, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("%")]
    [InlineData("% final comment")]
    [InlineData("\n% first\n% final comment")]
    public void Final_member_retains_existing_end_of_stream_comment_support(string suffix)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, First.Length + 1,
                repair, firstBody: First, secondBody: Second + suffix));
            Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
            Assert.Equal("Second", Assert.IsType<PdfDictionary>(file.GetObject(5)).Get("Type").AsName());
            Assert.Equal(repair, file.Repaired);
        }
    }
}
