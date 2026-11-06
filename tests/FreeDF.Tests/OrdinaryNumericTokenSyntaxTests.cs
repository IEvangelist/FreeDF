using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class OrdinaryNumericTokenSyntaxTests
{
    public static IEnumerable<object[]> InvalidCases() => Cases(
        ["++42", "--42", "+-42", "-+42", "++1.5", "--1.5", "1.2junk", "1.2e3", "1.2.3"]);

    public static IEnumerable<object[]> ValidCases() => Cases(
        ["0", "00", "+42", "-42", "-0", ".5", "+.5", "-.5", "4.", "0.0", "-3.62"]);

    static IEnumerable<object[]> Cases(string[] tokens)
    {
        foreach (var token in tokens)
            foreach (var placement in new[] { "top", "array", "value", "stream" })
                foreach (var repair in new[] { false, true })
                    yield return [token, placement, repair];
    }

    static string Value(string token, string placement) => placement switch
    {
        "array" => $"[42 {token}]",
        "value" => $"<< /Probe [<< /Number {token} >>] >>",
        "stream" => $"<< /Length 3 /Probe {token} >>\nstream\nabc\nendstream",
        _ => token
    };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_original_numeric_tokens_require_explicit_recovery(string token, string placement, bool repair)
    {
        var value = Value(token, placement);
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        Assert.Equal(repair, file.Repaired);
        var target = file.GetObject(2);
        Assert.True(file.Repaired);
        var expected = new PdfLexer(Encoding.ASCII.GetBytes(value)).ReadObject()!;
        Assert.Equal(Tree(expected), Tree(target is PdfStream stream ? stream.Dict : target));
        if (target is PdfStream content) Assert.Equal("abc"u8.ToArray(), content.RawData);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Valid_integer_and_real_spellings_remain_supported(string token, string placement, bool repair)
    {
        var value = Value(token, placement);
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        var target = file.GetObject(2);
        var expected = new PdfLexer(Encoding.ASCII.GetBytes(value)).ReadObject()!;
        Assert.Equal(Tree(expected), Tree(target is PdfStream stream ? stream.Dict : target));
        if (target is PdfStream content) Assert.Equal("abc"u8.ToArray(), content.RawData);
        Assert.Equal(repair, file.Repaired);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
    }

    static string Tree(PdfObject value) => value switch
    {
        PdfInteger integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        PdfReal real => real.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        PdfString text => Convert.ToHexString(text.Bytes),
        PdfArray array => "[" + string.Join(",", array.Items.Select(Tree)) + "]",
        PdfDictionary dictionary => "{" + string.Join(",", dictionary.Entries.Select(kv => kv.Key + ":" + Tree(kv.Value))) + "}",
        _ => value.ToString()!
    };

    [Theory]
    [InlineData("--42")]
    [InlineData("1.2junk")]
    public void Content_lexer_retains_existing_permissive_numeric_recovery(string token)
    {
        Assert.True(new PdfLexer(Encoding.ASCII.GetBytes(token)).ReadObject() is PdfInteger or PdfReal);
    }
}
