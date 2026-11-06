using PortableDocument.Core;

namespace FreeDF.Tests;

public class OrdinaryDictionaryEntryTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var value in new[]
        {
            "<< 42 /Key 1 >>", "<< true /Key 1 >>", "<< (bad) /Key 1 >>",
            "<< [] /Key 1 >>", "<< << >> /Key 1 >>", "<< bogus /Key 1 >>",
            "<< /Key >>", "<< /One 1 /Key >>",
            "<< /Key 1 /Key 2 >>", "<< /Key null /Key 2 >>",
            "<< /Key 1 /Key null >>", "<< /Key 1 /K#65y 2 >>",
            "<< /K#65y 1 /Key 2 >>", "<< /#41 1 /A 2 >>",
            "[<< /Key 1 /Key 2 >>]", "<< /Outer [<< /Key >>] >>",
            "<< /Outer << 42 /Key 1 >> >>",
            "<< /Length 3 /Key 1 /Key 2 >>\nstream\nabc\nendstream",
            "<< /Length 3 /Key >>\nstream\nabc\nendstream",
            "<< /Length 3 42 /Key 1 >>\nstream\nabc\nendstream"
        })
            foreach (var repair in new[] { false, true })
                yield return [value, repair];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_dictionary_entries_require_explicit_recovery_and_preserve_the_recovered_tree(string value, bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        Assert.Equal(repair, file.Repaired);
        var target = file.GetObject(2);
        Assert.True(file.Repaired);
        var expected = new PdfLexer(System.Text.Encoding.ASCII.GetBytes(value)).ReadObject()!;
        if (target is PdfStream stream)
        {
            Assert.Equal("abc"u8.ToArray(), stream.RawData);
            Assert.Equal(Tree(expected), Tree(stream.Dict));
        }
        else Assert.Equal(Tree(expected), Tree(target));
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
    }

    public static IEnumerable<object[]> ValidCases()
    {
        foreach (var value in new[]
        {
            "<< >>", "<< /Key null >>", "<< /Key 1 /key 2 >>", "<< /Key 1 /Key2 2 >>",
            "<< /Key 1 /#4Bey2 2 >>", "<< /A#42 1 /A 2 >>",
            "<< /Key 1 % /Key 2\n /Other 3 >>",
            "<< /Key (/Key /Key) /Other /Key >>", "<< /Key << /Key 2 >> >>",
            "<< /Key [<< /Key 2 >> << /Key 3 >>] >>",
            "<< /Key 1 0 R /Other null >>", "[<< /Key 1 >> << /Key 2 >>]",
            "<< /Length 3 /Key null >>\nstream\nabc\nendstream",
            "<< /Length 3 /Key [<< /Key 1 >>] >>\nstream\nabc\nendstream"
        })
            foreach (var repair in new[] { false, true })
                yield return [value, repair];
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Valid_dictionary_names_pairs_and_nested_scopes_remain_supported(string value, bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        var target = file.GetObject(2);
        var expected = new PdfLexer(System.Text.Encoding.ASCII.GetBytes(value)).ReadObject()!;
        Assert.Equal(Tree(expected), Tree(target is PdfStream stream ? stream.Dict : target));
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(repair, file.Repaired);
    }

    [Fact]
    public void Content_lexer_retains_last_value_recovery_for_duplicate_keys()
    {
        var dictionary = Assert.IsType<PdfDictionary>(new PdfLexer("<< /Key 1 /Key 2 >>"u8.ToArray()).ReadObject());
        Assert.Equal(2, Assert.IsType<PdfInteger>(dictionary.Get("Key")).Value);
    }

    static string Tree(PdfObject value) => value switch
    {
        PdfDictionary dictionary => "{" + string.Join(",", dictionary.Entries.Select(kv => kv.Key + ":" + Tree(kv.Value))) + "}",
        PdfArray array => "[" + string.Join(",", array.Items.Select(Tree)) + "]",
        PdfString text => Convert.ToHexString(text.Bytes),
        _ => value.ToString()!
    };
}
