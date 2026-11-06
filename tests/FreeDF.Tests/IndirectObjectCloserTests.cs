using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class IndirectObjectCloserTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var value in new[] { "<< /Type /Target >>", "42", "[42]", "(Target)", "/Target", "null" })
        {
            foreach (var closing in new[] { "", "endobX\n", "endobjX\n", "endobj0\n", "42\nendobj\n", "%endobj\n" })
                yield return [value, closing, true];
            foreach (var closing in new[] { "endobj\n", "%comment\r\nendobj\n", "\0\t\f\r\nendobj%comment\n" })
                yield return [value, closing, false];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Indexed_values_require_endobj_but_keep_explicit_recovery(string value, string closing, bool repaired)
    {
        var file = PdfFile.Load(Build(value, closing));
        Assert.False(file.Repaired);
        AssertValue(file.GetObject(new PdfRef(2, 0)), value);
        Assert.Equal(repaired, file.Repaired);
        Assert.Same(file.GetObject(2), file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(repaired, file.Repaired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("endobjX\n")]
    [InlineData("42\nendobj\n")]
    public void Malformed_catalog_closing_repairs_during_open(string closing)
    {
        var file = PdfFile.Load(Build("42", "endobj\n", rootClosing: closing));
        Assert.True(file.Repaired);
        Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
        Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(2)).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("endobjX\n")]
    [InlineData("42\nendobj\n")]
    public void Already_repaired_files_keep_ordinary_value_recovery(string closing)
    {
        var file = PdfFile.Load(Build("42", closing, repair: true));
        Assert.True(file.Repaired);
        Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(2)).Value);
    }

    [Fact]
    public void Unterminated_hex_string_at_EOF_enters_controlled_recovery()
    {
        var source = Encoding.ASCII.GetString(Build("<41>", "endobj\n"));
        var oldTarget = source.IndexOf("2 0 obj", StringComparison.Ordinal);
        var text = source.Replace($"{oldTarget:D10} 00000 n", $"{source.Length:D10} 00000 n", StringComparison.Ordinal);
        var file = PdfFile.Load(Encoding.ASCII.GetBytes(text + "2 0 obj\n<41"));
        Assert.False(file.Repaired);
        Assert.Equal("A", Assert.IsType<PdfString>(file.GetObject(2)).Text);
        Assert.True(file.Repaired);
    }

    internal static byte[] Build(string value, string closing, bool repair = false, string rootClosing = "endobj\n")
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var root = text.Length;
        text.Append($"1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\n{rootClosing}");
        var target = text.Length;
        text.Append($"2 0 obj\n{value}\n{closing}");
        var xref = text.Length;
        text.Append($"xref\n0 3\n0000000000 65535 f \n{root:D10} 00000 n \n{target:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{(repair ? -1 : xref)}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    static void AssertValue(PdfObject actual, string value)
    {
        switch (value)
        {
            case "<< /Type /Target >>": Assert.Equal("Target", Assert.IsType<PdfDictionary>(actual).Get("Type").AsName()); break;
            case "42": Assert.Equal(42, Assert.IsType<PdfInteger>(actual).Value); break;
            case "[42]": Assert.Equal(42, Assert.IsType<PdfInteger>(Assert.Single(Assert.IsType<PdfArray>(actual).Items)).Value); break;
            case "(Target)": Assert.Equal("Target", Assert.IsType<PdfString>(actual).Text); break;
            case "/Target": Assert.Equal("Target", Assert.IsType<PdfName>(actual).Value); break;
            case "null": Assert.IsType<PdfNull>(actual); break;
            default: throw new ArgumentException("Unknown test value", nameof(value));
        }
    }
}
