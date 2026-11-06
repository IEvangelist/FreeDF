using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class StartXrefIntegerSyntaxTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var stream in new[] { false, true })
        {
            foreach (var token in new[] { "++@", "+++@", "++000@", "--@", "+-@", "-+@", "+0+@", "@.0", "@.5", "@abc" })
                yield return [token, stream, true];
            foreach (var token in new[] { "@", "+@", "000@", "+000@" })
                yield return [token, stream, false];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Footer_offset_requires_integer_syntax_without_changing_valid_spellings(string token, bool stream, bool repaired)
    {
        var original = stream ? XrefStreamHeaderRangeTests.Build("3", "0", false)
            : XrefClassicGenerationTests.Build(0, fixedEntry: true);
        var text = Encoding.ASCII.GetString(original);
        var start = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10;
        var end = text.IndexOf('\n', start);
        var bytes = Encoding.ASCII.GetBytes(text[..start] + token.Replace("@", text[start..end], StringComparison.Ordinal) + text[end..]);
        var file = PdfFile.Load(bytes);
        Assert.Equal(repaired, file.Repaired);
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(repaired, file.Repaired);
    }

    [Theory]
    [InlineData("++17", 17)]
    [InlineData("+++17", 17)]
    [InlineData("--17", -17)]
    public void Ordinary_numeric_recovery_remains_available(string token, int value)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(token));
        Assert.Equal(value, Assert.IsType<PdfInteger>(lexer.ReadObject(false)).Value);
    }
}
