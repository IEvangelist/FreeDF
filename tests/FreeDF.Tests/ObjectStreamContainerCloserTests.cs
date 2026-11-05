using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamContainerCloserTests
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
    [InlineData("[")]
    [InlineData("[1")]
    [InlineData("[1 % missing closer\n")]
    [InlineData("[[1]")]
    [InlineData("<<")]
    [InlineData("<< /Key")]
    [InlineData("<< /Key 1")]
    [InlineData("<< /Key 1 % missing closer\n")]
    [InlineData("<< /Key << /Inner 1 >>")]
    [InlineData("[<< /Key [1] >>")]
    public void Compressed_containers_require_closing_delimiters(string value)
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
    [InlineData("[]")]
    [InlineData("[1 % comment\n]")]
    [InlineData("[[1]]")]
    [InlineData("<< >>")]
    [InlineData("<< /Key 1 % comment\n >>")]
    [InlineData("<< /Key << /Inner 1 >> >>")]
    [InlineData("[<< /Key [1 0 R /endobj (>>)] >>]")]
    public void Complete_compressed_containers_remain_supported(string value)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = file.GetObject(2);
            Assert.True(member is PdfArray or PdfDictionary);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrequested_unterminated_container_preserves_valid_member(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "[1"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Theory]
    [InlineData("[1")]
    [InlineData("<< /Key 1")]
    public void Default_lexer_retains_existing_recovery_behavior(string value)
    {
        var member = new PdfLexer(Encoding.Latin1.GetBytes(value)).ReadObject();
        Assert.True(member is PdfArray or PdfDictionary);
    }
}
