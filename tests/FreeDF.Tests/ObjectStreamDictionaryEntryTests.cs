using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamDictionaryEntryTests
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
    [InlineData("<< 1 2 >>")]
    [InlineData("<< q >>")]
    [InlineData("<< (Key) 2 >>")]
    [InlineData("<< [1] 2 >>")]
    [InlineData("<< null >>")]
    [InlineData("<< true >>")]
    [InlineData("<< << /Inner 1 >> 2 >>")]
    [InlineData("<< /Key >>")]
    [InlineData("<< /Key 1 /Missing >>")]
    [InlineData("<< /Key % missing\n >>")]
    [InlineData("<< /Key 1 /Key 2 >>")]
    [InlineData("<< /Key null /Key 2 >>")]
    [InlineData("<< /Key 1 /Ke#79 2 >>")]
    [InlineData("[<< /Key >>]")]
    [InlineData("<< /Outer << /Key 1 /Key 2 >> >>")]
    public void Compressed_dictionary_entries_require_name_keys_values_and_unique_keys(string value)
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
    [InlineData("<< >>")]
    [InlineData("<< /Key null >>")]
    [InlineData("<< /Key /Value >>")]
    [InlineData("<< /Key 1 0 R >>")]
    [InlineData("<< /Key 1 /Other 2 >>")]
    [InlineData("<< /Key << /Key 2 >> >>")]
    [InlineData("<< /Ke#79 1 /KEY 2 >>")]
    [InlineData("<< / 1 >>")]
    public void Complete_dictionary_entries_preserve_valid_values_and_key_scopes(string value)
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
    public void Invalid_unrequested_dictionary_preserves_valid_member(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "<< /Key >>"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Fact]
    public void Default_lexer_preserves_existing_dictionary_recovery()
    {
        var value = new PdfLexer(Encoding.Latin1.GetBytes("<< q /Missing >>")).ReadObject();
        Assert.IsType<PdfNull>(Assert.IsType<PdfDictionary>(value).Get("Missing"));
        value = new PdfLexer(Encoding.Latin1.GetBytes("<< /Key 1 /Key 2 >>")).ReadObject();
        Assert.Equal(2, Assert.IsType<PdfInteger>(Assert.IsType<PdfDictionary>(value).Get("Key")).Value);
    }
}
