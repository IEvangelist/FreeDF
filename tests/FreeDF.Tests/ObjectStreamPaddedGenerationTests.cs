using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamPaddedGenerationTests
{
    [Theory]
    [InlineData("000000", 0)]
    [InlineData("0000001", 1)]
    [InlineData("+0000001", 1)]
    [InlineData("-000000", 0)]
    [InlineData("0000065535", 65535)]
    [InlineData("+0000065535", 65535)]
    public void Leading_zero_padding_does_not_change_reference_generation(string generation, int expected)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = FileWithValue($"[7 {generation} R]", repair);
            Assert.Equal(new PdfRef(7, expected), Assert.Single(Assert.IsType<PdfArray>(file.GetObject(2)).Items));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Fact]
    public void Long_padding_is_parsed_without_integer_accumulation_overflow()
    {
        var file = FileWithValue($"[7 +{new string('0', 4096)}1% separator\nR]", false);
        Assert.Equal(new PdfRef(7, 1), Assert.Single(Assert.IsType<PdfArray>(file.GetObject(2)).Items));
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("0000001R")]
    [InlineData("-0000001 R")]
    [InlineData("000000.0 R")]
    [InlineData("000000x R")]
    [InlineData("00000100000 R")]
    [InlineData("99999999999999999999999999 R")]
    public void Padding_does_not_make_invalid_or_unsupported_references_valid(string suffix)
    {
        foreach (var repair in new[] { false, true })
            Assert.Throws<PdfParseException>(() => FileWithValue($"[7 {suffix}]", repair).GetObject(2));
    }

    [Fact]
    public void Default_lexer_source_width_policy_is_unchanged()
    {
        var lexer = new PdfLexer("7 0000001 R"u8.ToArray());
        Assert.Equal(7, Assert.IsType<PdfInteger>(lexer.ReadObject()).Value);
    }

    static PdfFile FileWithValue(string value, bool repair)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, value: value);
        if (repair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        return PdfFile.Load(bytes);
    }
}
