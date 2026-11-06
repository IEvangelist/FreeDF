using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class IndexedObjectHeaderSyntaxTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var value in new[] { "42", "<< /Type /Target >>", "<< /Length 3 >>\nstream\nabc\nendstream" })
        {
            foreach (var number in new[] { "++2", "+++2", "++++0002" })
                yield return [number, "0", value, true];
            foreach (var generation in new[] { "++0", "+-0", "--0", "-+0", "+++0000" })
                yield return ["2", generation, value, false];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Repeated_header_signs_enter_explicit_recovery(string number, string generation, string value, bool recoverable)
    {
        var file = PdfFile.Load(Build(number, generation, value));
        Assert.False(file.Repaired);
        var target = file.GetObject(new PdfRef(2, 0));
        Assert.True(file.Repaired);
        if (recoverable) AssertValue(target, value);
        else Assert.IsType<PdfNull>(target);
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
    }

    public static IEnumerable<object[]> ValidCases()
    {
        foreach (var value in new[] { "42", "<< /Type /Target >>", "<< /Length 3 >>\nstream\nabc\nendstream" })
            foreach (var pair in new[] { ("2", "0"), ("+2", "+0"), ("0002", "00000"), ("+0002", "-0"), ("2", "7"), ("2", "65535") })
                foreach (var separator in new[] { " ", "%comment\r\n", "\0\t\f\r\n" })
                    yield return [pair.Item1, pair.Item2, value, separator];
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Single_sign_padding_comments_and_generation_identity_remain_supported(
        string number, string generation, string value, string separator)
    {
        var file = PdfFile.Load(Build(number, generation, value, separator));
        var reference = new PdfRef(2, int.Parse(generation));
        var target = file.GetObject(reference);
        AssertValue(target, value);
        Assert.Same(target, file.GetObject(reference));
        if (reference.Generation != 0) Assert.IsType<PdfNull>(file.GetObject(new PdfRef(2, 0)));
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("++2", "0")]
    [InlineData("2", "++0")]
    public void Already_repaired_header_recovery_keeps_its_existing_scope(string number, string generation)
    {
        var file = PdfFile.Load(Build(number, generation, "42", repair: true));
        Assert.True(file.Repaired);
        if (number == "++2") Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(2)).Value);
        else Assert.IsType<PdfNull>(file.GetObject(2));
    }

    [Fact]
    public void Ordinary_numeric_value_recovery_is_not_made_strict_by_header_checks()
    {
        var file = PdfFile.Load(Build("2", "0", "++42"));
        Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(2)).Value);
        Assert.False(file.Repaired);
    }

    internal static byte[] Build(string number, string generation, string value, string separator = " ", bool repair = false)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var root = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = text.Length;
        text.Append($"{number}{separator}{generation}{separator}obj\n{value}\nendobj\n");
        var xref = text.Length;
        var entryGeneration = int.TryParse(generation, out var gen) ? gen : 0;
        text.Append($"xref\n0 3\n0000000000 65535 f \n{root:D10} 00000 n \n{target:D10} {entryGeneration:D5} n \n");
        text.Append($"trailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{(repair ? -1 : xref)}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    static void AssertValue(PdfObject actual, string value)
    {
        if (value == "42") Assert.Equal(42, Assert.IsType<PdfInteger>(actual).Value);
        else if (value.Contains("stream", StringComparison.Ordinal))
            Assert.Equal("abc"u8.ToArray(), Assert.IsType<PdfStream>(actual).RawData);
        else Assert.Equal("Target", Assert.IsType<PdfDictionary>(actual).Get("Type").AsName());
    }
}
