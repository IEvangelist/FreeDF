using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamReferenceGenerationRangeTests
{
    [Theory]
    [InlineData("65536")]
    [InlineData("99999")]
    [InlineData("+65536")]
    [InlineData("+99999")]
    [InlineData("0000065536")]
    [InlineData("+0000099999")]
    public void Out_of_range_reference_generations_do_not_publish_compressed_members(string generation)
    {
        foreach (var value in new[] { $"7 {generation} R", $"[7 {generation} R]",
            $"<< /Ref 7 {generation} R >>" })
            foreach (var repair in new[] { false, true })
            {
                var file = FileWithValue(value, repair);
                Assert.Throws<PdfParseException>(() => file.GetObject(2));
            }
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("65534", 65534)]
    [InlineData("65535", 65535)]
    [InlineData("+65535", 65535)]
    [InlineData("0000065535", 65535)]
    public void Supported_boundary_generations_preserve_reference_values(string generation, int expected)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = FileWithValue($"[7 {generation} R]", repair);
            var value = Assert.IsType<PdfArray>(file.GetObject(2));
            Assert.Equal(new PdfRef(7, expected), Assert.Single(value.Items));
            Assert.Same(value, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(65536)]
    [InlineData(99999)]
    public void Ordinary_lexer_generation_recovery_is_unchanged(int generation)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes($"7 {generation} R"));
        Assert.Equal(new PdfRef(7, generation), lexer.ReadObject());
    }

    static PdfFile FileWithValue(string value, bool repair)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, value: value);
        if (repair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(
                text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        return PdfFile.Load(bytes);
    }
}
