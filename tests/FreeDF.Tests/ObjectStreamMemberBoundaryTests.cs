using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamMemberBoundaryTests
{
    [Theory]
    [InlineData("[42", "<< /Type /Second >>", typeof(PdfDictionary))]
    [InlineData("<< /Key", "<< /Type /Second >>", typeof(PdfDictionary))]
    [InlineData("(abc", "(def)", typeof(PdfString))]
    [InlineData("<41", "<42>", typeof(PdfString))]
    [InlineData("", "<< /Type /Second >>", typeof(PdfDictionary))]
    public void Requested_member_cannot_consume_the_following_member(string first, string second, Type otherType)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, first.Length + 1,
                forceRepair, firstBody: first, secondBody: second));
            Assert.IsType(otherType, file.GetObject(5));
            Assert.Equal(forceRepair, file.Repaired);
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Throws<PdfParseException>(() => file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.IsType(otherType, file.GetObject(5));
        }
    }

    [Theory]
    [InlineData("[42]", typeof(PdfArray))]
    [InlineData("<< /Key 42 >>", typeof(PdfDictionary))]
    [InlineData("(abc)", typeof(PdfString))]
    [InlineData("<41>", typeof(PdfString))]
    [InlineData("42", typeof(PdfInteger))]
    public void Complete_member_before_next_offset_keeps_cached_independent_lookup(string first, Type memberType)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0, first.Length + 1,
                forceRepair, firstBody: first));
            var member = file.GetObject(2);
            Assert.IsType(memberType, member);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal("Second", Assert.IsType<PdfDictionary>(file.GetObject(5)).Get("Type").AsName());
            Assert.Equal(forceRepair, file.Repaired);
        }
    }
}
