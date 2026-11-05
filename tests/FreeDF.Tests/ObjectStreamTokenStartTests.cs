using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamTokenStartTests
{
    [Theory]
    [InlineData("4243", 2)]
    [InlineData("+123", 2)]
    [InlineData("falsetrue", 5)]
    [InlineData("nullnull", 4)]
    [InlineData("/A42", 2)]
    [InlineData("/Atrue", 2)]
    public void Member_offset_cannot_split_a_regular_token(string body, int offset)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, offset, repair,
                firstBody: body, secondBody: ""));
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Equal("object stream member starts inside a token",
                    Assert.Throws<PdfParseException>(() => file.GetObject(5)).Message);
        }
    }

    [Theory]
    [InlineData("42 43", 3, typeof(PdfInteger), typeof(PdfInteger))]
    [InlineData("(A)42", 3, typeof(PdfString), typeof(PdfInteger))]
    [InlineData("[42]true", 4, typeof(PdfArray), typeof(PdfBoolean))]
    [InlineData("42[43]", 2, typeof(PdfInteger), typeof(PdfArray))]
    [InlineData("<< >>/Name", 5, typeof(PdfDictionary), typeof(PdfName))]
    [InlineData("42% comment\n43", 12, typeof(PdfInteger), typeof(PdfInteger))]
    public void Whitespace_comments_and_delimiters_preserve_complete_member_starts(
        string body, int offset, Type firstType, Type secondType)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, offset, repair,
                firstBody: body, secondBody: ""));
            var second = file.GetObject(5);
            Assert.Equal(secondType, second.GetType());
            Assert.Same(second, file.GetObject(5));
            Assert.Equal(firstType, file.GetObject(2).GetType());
            Assert.Equal(repair, file.Repaired);
        }
    }
}
