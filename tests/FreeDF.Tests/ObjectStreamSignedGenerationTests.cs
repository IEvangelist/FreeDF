using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamSignedGenerationTests
{
    [Theory]
    [InlineData("+0", 0)]
    [InlineData("-0", 0)]
    [InlineData("+00000", 0)]
    [InlineData("-00000", 0)]
    [InlineData("+1", 1)]
    [InlineData("+65535", 65535)]
    public void Signed_nonnegative_integer_generations_form_references(string generation, int expected)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = FileWithValue($"<< /Ref 7 {generation} R /Array [7 {generation}% separator\nR] >>", repair);
            var value = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal(new PdfRef(7, expected), value.Get("Ref"));
            Assert.Equal(new PdfRef(7, expected), Assert.Single(Assert.IsType<PdfArray>(value.Get("Array")).Items));
            Assert.Same(value, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-65535")]
    [InlineData("++0")]
    [InlineData("+-0")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("+0.0")]
    [InlineData("+0x")]
    public void Invalid_generation_tokens_do_not_become_references(string generation)
    {
        foreach (var repair in new[] { false, true })
            Assert.Throws<PdfParseException>(() => FileWithValue($"[7 {generation} R]", repair).GetObject(2));
    }

    [Theory]
    [InlineData("+0")]
    [InlineData("-0")]
    [InlineData("+65535")]
    public void Signed_generations_still_require_the_R_separator(string generation)
    {
        foreach (var repair in new[] { false, true })
            Assert.Throws<PdfParseException>(() => FileWithValue($"[7 {generation}R]", repair).GetObject(2));
    }

    [Theory]
    [InlineData("+0")]
    [InlineData("-0")]
    [InlineData("+65535")]
    public void Default_lexer_reference_recognition_is_unchanged(string generation)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes($"7 {generation} R"));
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
