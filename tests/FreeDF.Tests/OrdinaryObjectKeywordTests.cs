using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class OrdinaryObjectKeywordTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var value in new[]
        {
            "q", "BT", "obj", "endobj", "stream", "endstream", "R", "unknown", "]", ">>",
            "[q]", "[obj]", "[endobj]", "[true false null q]", "[>>]", "[42 R]",
            "<< /Key q >>", "<< /Key endobj >>", "[<< /Key [q] >>]",
            "<< /Key [<< /Inner endstream >>] >>", "<< /Key unknown /Valid 42 >>",
            "<< /Length 3 /Key q >>\nstream\nabc\nendstream",
            "<< /Length 3 /Key [<< /Inner BT >>] >>\nstream\nabc\nendstream"
        })
            foreach (var repair in new[] { false, true })
                yield return [value, repair];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Non_object_keywords_require_explicit_recovery_without_changing_recovered_values(string value, bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        Assert.Equal(repair, file.Repaired);
        var target = file.GetObject(2);
        Assert.True(file.Repaired);
        if (value.Contains("\nstream\n", StringComparison.Ordinal))
        {
            var stream = Assert.IsType<PdfStream>(target);
            Assert.Equal("abc"u8.ToArray(), stream.RawData);
            Assert.True(ContainsKeyword(stream.Dict));
        }
        else if (value.StartsWith('['))
            Assert.True(ContainsKeyword(Assert.IsType<PdfArray>(target)));
        else if (value.StartsWith("<<", StringComparison.Ordinal))
            Assert.True(ContainsKeyword(Assert.IsType<PdfDictionary>(target)));
        else Assert.IsType<PdfNull>(target);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
    }

    public static IEnumerable<object[]> ValidCases()
    {
        foreach (var value in new[]
        {
            "42", "1.5", "true", "false", "null", "/endobj", "(unknown q BT endstream)",
            "<4254>", "1 0 R", "[]", "<< >>",
            "[1 1.5 true false null /q (endobj)]",
            "[<< /Key [1 0 R /obj (q)] >>]",
            "<< /Key [<< /Inner 1 0 R >>] >>",
            "<< /q /obj /Value (endstream) >>",
            "<< /Type /Target /Nested [null % q endobj\n /endobj] >>",
            "<< /Length 8 /Key /q >>\nstream\nq BT Q x\nendstream",
            "<< /Length 8 /Key (BT) >>\nstream\nq BT Q x\nendstream"
        })
            foreach (var repair in new[] { false, true })
                yield return [value, repair];
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Objects_names_strings_comments_references_and_stream_operators_remain_supported(string value, bool repair)
    {
        var file = PdfFile.Load(IndirectObjectCloserTests.Build(value, "endobj\n", repair));
        var target = file.GetObject(2);
        Assert.False(ContainsKeyword(target is PdfStream stream ? stream.Dict : target));
        if (target is PdfStream content) Assert.Equal("q BT Q x"u8.ToArray(), content.RawData);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(repair, file.Repaired);
    }

    [Fact]
    public void Malformed_catalog_value_enters_repair_during_open()
    {
        var bytes = Encoding.ASCII.GetString(IndirectObjectCloserTests.Build("42", "endobj\n"))
            .Replace("/Probe 2 0 R", "/Probe bogus", StringComparison.Ordinal);
        var file = PdfFile.Load(Encoding.ASCII.GetBytes(bytes));
        Assert.True(file.Repaired);
        Assert.IsType<PdfKeyword>(file.Catalog.Get("Probe"));
        Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(2)).Value);
    }

    static bool ContainsKeyword(PdfObject value) => value switch
    {
        PdfKeyword => true,
        PdfArray array => array.Items.Any(ContainsKeyword),
        PdfDictionary dictionary => dictionary.Entries.Values.Any(ContainsKeyword),
        _ => false
    };
}
