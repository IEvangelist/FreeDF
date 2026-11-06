using PortableDocument.Core;

namespace FreeDF.Tests;

public class OrdinaryHexStringSyntaxTests
{
    public static IEnumerable<object[]> InvalidCases() => Cases(
        ["<4G>", "< 4 2 Z >", "<0x42>", "<#42>", "[<4G>]", "<< /Key <4G> >>",
         "<< /Length 3 /Key <4G> >>\nstream\nabc\nendstream"]);

    public static IEnumerable<object[]> ValidCases() => Cases(
        ["<>", "<42>", "<4 2>", "<4\n2>", "<ABC>", "<F>", "<< /Key <4 2> >>",
         "[<42> (not hex)]", "<< /Key (text <G>) >>", "<4\0 2>"]);

    static IEnumerable<object[]> Cases(string[] values)
    {
        foreach (var item in values)
            foreach (var repair in new[] { false, true })
                yield return [item, repair];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_hex_string_syntax_requires_explicit_recovery(string value, bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        Assert.Equal(repair, file.Repaired);
        var target = file.GetObject(2);
        Assert.True(file.Repaired);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Valid_hex_syntax_and_odd_final_digit_remain_supported(string value, bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        var target = file.GetObject(2);
        var expected = new PdfLexer(System.Text.Encoding.ASCII.GetBytes(value)).ReadObject()!;
        Assert.Equal(Tree(expected), Tree(target));
        Assert.Equal(repair, file.Repaired);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
    }

    static string Tree(PdfObject value) => value switch
    {
        PdfString text => Convert.ToHexString(text.Bytes),
        PdfArray array => "[" + string.Join(",", array.Items.Select(Tree)) + "]",
        PdfDictionary dictionary => "{" + string.Join(",", dictionary.Entries.Select(kv => kv.Key + ":" + Tree(kv.Value))) + "}",
        _ => value.ToString()!
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_stream_bytes_are_not_parsed_as_hex_strings(bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(
            "<< /Length 4 >>\nstream\n<4G>\nendstream", "endobj\n", repair));
        Assert.Equal(repair, file.Repaired);
        Assert.Equal("<4G>"u8.ToArray(), Assert.IsType<PdfStream>(file.GetObject(2)).RawData);
    }

    [Fact]
    public void Content_lexer_retains_existing_invalid_hex_recovery()
    {
        Assert.Equal([0x40], Assert.IsType<PdfString>(new PdfLexer("<4G>"u8.ToArray()).ReadObject()).Bytes);
    }
}
