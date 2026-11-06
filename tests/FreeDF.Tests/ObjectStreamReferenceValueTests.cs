using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamReferenceValueTests
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
    [InlineData("1 0 R")]
    [InlineData("2 0 R")]
    [InlineData("123 0 R")]
    [InlineData("1 00000 R")]
    public void Standalone_reference_is_rejected_before_member_caching(string value)
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
    [InlineData("obj")]
    [InlineData("endobj")]
    [InlineData("stream")]
    [InlineData("endstream")]
    [InlineData("q")]
    [InlineData("True")]
    [InlineData("]")]
    [InlineData(">>")]
    [InlineData("}")]
    [InlineData(")")]
    [InlineData(" \t\r\n")]
    [InlineData("% no value\n")]
    public void Keyword_or_missing_value_is_rejected_before_member_caching(string value)
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
    [InlineData("<< /Target 1 0 R >>", typeof(PdfDictionary))]
    [InlineData("[1 0 R]", typeof(PdfArray))]
    [InlineData("(1 0 R)", typeof(PdfString))]
    [InlineData("42", typeof(PdfInteger))]
    [InlineData("null", typeof(PdfNull))]
    [InlineData("true", typeof(PdfBoolean))]
    [InlineData("false", typeof(PdfBoolean))]
    [InlineData("1.5", typeof(PdfReal))]
    [InlineData("/endobj", typeof(PdfName))]
    [InlineData("% comment\nnull % trailing comment\n", typeof(PdfNull))]
    public void Valid_values_including_nested_references_keep_cached_lookup(string value, Type expectedType)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = file.GetObject(2);
            Assert.IsType(expectedType, member);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrequested_reference_member_does_not_prevent_valid_member_lookup(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "1 0 R"));
        var member = Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.Equal("First", member.Get("Type").AsName());
        Assert.Same(member, file.GetObject(2));
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrequested_keyword_member_does_not_prevent_valid_member_lookup(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "endobj"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }
}
