using PortableDocument.Core;

namespace FreeDF.Tests;

public class OrdinaryNameSyntaxTests
{
    public static IEnumerable<object[]> InvalidCases() => Cases(
        ["/#", "/#0", "/#GG", "/A#4ZB", "/A#00B", "/#000", "/A#x0"]);

    public static IEnumerable<object[]> ValidCases() => Cases(
        ["/", "/Name", "/#23", "/A#20B", "/A#2fB", "/#28#29", "/#ff", "/#01",
         "/#2300", "/A;B_With-Various***Characters?"]);

    static IEnumerable<object[]> Cases(string[] names)
    {
        foreach (var name in names)
            foreach (var placement in new[] { "top", "array", "key", "value", "stream" })
                foreach (var repair in new[] { false, true })
                    yield return [name, placement, repair];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_original_names_require_explicit_recovery(string name, string placement, bool repair)
        => Check(name, placement, repair, invalid: true);

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Valid_names_and_escaped_bytes_remain_supported(string name, string placement, bool repair)
        => Check(name, placement, repair, invalid: false);

    static void Check(string name, string placement, bool repair, bool invalid)
    {
        var value = placement switch
        {
            "array" => $"[42 {name}]",
            "key" => $"<< {name} 42 >>",
            "value" => $"<< /Probe [<< /Name {name} >>] >>",
            "stream" => $"<< /Length 3 /Probe {name} >>\nstream\nabc\nendstream",
            _ => name
        };
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        Assert.Equal(repair, file.Repaired);
        var target = file.GetObject(2);
        Assert.Equal(repair || invalid, file.Repaired);
        var expectedName = Assert.IsType<PdfName>(new PdfLexer(System.Text.Encoding.ASCII.GetBytes(name)).ReadObject()).Value;
        var recoveredName = placement switch
        {
            "array" => Assert.IsType<PdfName>(Assert.IsType<PdfArray>(target).Items[1]).Value,
            "key" => Assert.Single(Assert.IsType<PdfDictionary>(target).Entries).Key,
            "value" => Assert.IsType<PdfName>(Assert.IsType<PdfDictionary>(
                Assert.Single(Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(target).Get("Probe")).Items)).Get("Name")).Value,
            "stream" => Assert.IsType<PdfName>(Assert.IsType<PdfStream>(target).Dict.Get("Probe")).Value,
            _ => Assert.IsType<PdfName>(target).Value
        };
        Assert.Equal(expectedName, recoveredName);
        if (target is PdfStream stream) Assert.Equal("abc"u8.ToArray(), stream.RawData);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_stream_bytes_and_comments_are_not_name_tokens(bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(
            "<< /Length 8 % /#00 /#G\n >>\nstream\n/#00 /#G\nendstream", "endobj\n", repair));
        var stream = Assert.IsType<PdfStream>(file.GetObject(2));
        Assert.Equal("/#00 /#G"u8.ToArray(), stream.RawData);
        Assert.Equal(repair, file.Repaired);
    }

    [Fact]
    public void Direct_content_lexer_retains_malformed_name_recovery()
    {
        Assert.Equal("#", Assert.IsType<PdfName>(new PdfLexer("/#"u8.ToArray()).ReadObject()).Value);
        Assert.Equal("\0", Assert.IsType<PdfName>(new PdfLexer("/#00"u8.ToArray()).ReadObject()).Value);
    }
}
