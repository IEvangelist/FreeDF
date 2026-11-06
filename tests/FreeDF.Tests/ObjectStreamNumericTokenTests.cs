using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamNumericTokenTests
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
    [InlineData("--1")]
    [InlineData("++1")]
    [InlineData("+-1")]
    [InlineData("-+1")]
    [InlineData("--1.5")]
    [InlineData("1.2junk")]
    [InlineData("1.2e3")]
    [InlineData("1.2.3")]
    [InlineData(".")]
    [InlineData("+.")]
    [InlineData("-.")]
    [InlineData("[--1]")]
    [InlineData("<< /Key 1.2junk >>")]
    public void Compressed_numbers_reject_repeated_signs_and_partial_numeric_tokens(string value)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            Assert.Equal(forceRepair, file.Repaired);
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Contains("invalid numeric token", Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.True(file.Repaired);
        }
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("00", 0)]
    [InlineData("+42", 42)]
    [InlineData("-42", -42)]
    [InlineData("-0", 0)]
    [InlineData(".5", .5)]
    [InlineData("+.5", .5)]
    [InlineData("-.5", -.5)]
    [InlineData("4.", 4)]
    [InlineData("0.0", 0)]
    [InlineData("-3.62", -3.62)]
    public void Valid_compressed_numeric_spellings_preserve_values(string value, double expected)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = file.GetObject(2);
            var actual = member switch
            {
                PdfInteger integer => integer.Value,
                PdfReal real => real.Value,
                _ => double.NaN
            };
            Assert.Equal(expected, actual);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_unrequested_number_preserves_valid_member(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "--1"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Theory]
    [InlineData("--1")]
    [InlineData("1.2junk")]
    public void Default_lexer_preserves_existing_numeric_recovery(string value)
    {
        Assert.True(new PdfLexer(Encoding.Latin1.GetBytes(value)).ReadObject() is PdfInteger or PdfReal);
    }
}
