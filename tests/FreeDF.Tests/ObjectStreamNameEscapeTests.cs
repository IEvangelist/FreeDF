using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamNameEscapeTests
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
    [InlineData("/#")]
    [InlineData("/A#")]
    [InlineData("/#0")]
    [InlineData("/#GG")]
    [InlineData("/#0G")]
    [InlineData("/#G0")]
    [InlineData("/#00")]
    [InlineData("/A#00B")]
    [InlineData("[/#]")]
    [InlineData("<< /Key /#00 >>")]
    [InlineData("<< /A# 1 >>")]
    public void Compressed_names_require_valid_hex_escapes_and_exclude_null(string value)
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
    [InlineData("/", "")]
    [InlineData("/Name", "Name")]
    [InlineData("/#23", "#")]
    [InlineData("/A#20B", "A B")]
    [InlineData("/#2F", "/")]
    [InlineData("/#28#29", "()")]
    [InlineData("/#ff", "\u00ff")]
    [InlineData("/A#42", "AB")]
    public void Valid_compressed_name_escapes_preserve_decoded_names(string value, string expected)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = Assert.IsType<PdfName>(file.GetObject(2));
            Assert.Equal(expected, member.Value);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_unrequested_name_preserves_valid_member(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "/#00"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Theory]
    [InlineData("/#", "#")]
    [InlineData("/#00", "\0")]
    public void Default_lexer_preserves_existing_name_recovery(string value, string expected)
    {
        Assert.Equal(expected, Assert.IsType<PdfName>(new PdfLexer(Encoding.Latin1.GetBytes(value)).ReadObject()).Value);
    }
}
