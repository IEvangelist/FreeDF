using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamStreamValueTests
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
    [InlineData("<< /Length 1 >> stream\nX\nendstream")]
    [InlineData("<< /Length 0 >>\nstream\n\nendstream")]
    [InlineData("<< /Length 1 >> % before stream\r\nstream\r\nX\r\nendstream")]
    [InlineData("<< /Type /ObjStm /Length 1 >>\nstream\nX\nendstream")]
    public void Stream_value_cannot_be_accepted_and_cached_as_a_dictionary(string value)
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
    [InlineData("<< /Type /ObjStm >>")]
    [InlineData("<< /Name /stream >>")]
    [InlineData("<< /Value (stream) >>")]
    [InlineData("<< /Length 1 >> % stream is only a comment\n")]
    public void Dictionary_names_strings_and_comments_are_not_stream_values(string value)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrequested_stream_value_does_not_prevent_valid_dictionary_lookup(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair,
            secondBody: "<< /Length 1 >> stream\nX\nendstream"));
        var member = Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.Equal("First", member.Get("Type").AsName());
        Assert.Same(member, file.GetObject(2));
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }
}
