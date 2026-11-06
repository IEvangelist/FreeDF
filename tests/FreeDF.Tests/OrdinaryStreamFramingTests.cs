using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class OrdinaryStreamFramingTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var repaired in new[] { false, true })
        {
            foreach (var opening in new[] { "\r", " ", "\t", "\0", "\f", "%note\n" })
            {
                var bytes = opening == "\r" ? "abc" : opening + "abc";
                yield return ["opening", Build(opening: opening, length: bytes.Length.ToString(), repair: repaired),
                    bytes, true];
            }
            foreach (var length in new[] { "missing", "null", "3.0", "3.5", "-1", "true", "/Bad", "[]", "<< >>", "9 0 R", "2", "99" })
                yield return ["length " + length, Build(length: length, repair: repaired), "abc", true];
            foreach (var padding in new[] { " ", "\t", "\0", "\f", "\n\n", "\r\n\n", "%note\n" })
                yield return ["extent", Build(ending: padding + "endstream", repair: repaired), "abc", true];
            foreach (var marker in new[] { "endstreamX", "endstream0" })
                yield return [marker, Build(ending: "\n" + marker, repair: repaired), "abc", true];
            foreach (var closing in new[] { "\n", "\n42\nendobj\n", "\nnull\nendobj\n", "\nendobjX\n", "\nendobj0\n", "\n%endobj\n" })
                yield return ["closing", Build(closing: closing, repair: repaired), "abc", true];
            foreach (var opening in new[] { "\n", "\r\n" })
                foreach (var eol in new[] { "", "\n", "\r", "\r\n" })
                    yield return ["valid EOL", Build(opening: opening, ending: eol + "endstream", repair: repaired),
                        "abc", repaired];
            foreach (var trivia in new[] { " ", "\t", "\0", "\f", "\r", "\n", "\r\n", "%comment\r\n" })
                yield return ["valid closer trivia", Build(closing: trivia + "endobj\n", repair: repaired),
                    "abc", repaired];
            foreach (var data in new[] { "", "abc ", "abc\n", "abc\r\n", "endstream", "\0endstream\nendobj" })
                yield return ["counted bytes", Build(data: data, length: data.Length.ToString(), repair: repaired),
                    data, repaired];
            foreach (var length in new[] { "+3", "0003", "4 0 R" })
                yield return ["integer or indirect Length", Build(length: length, repair: repaired), "abc", repaired];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Embedded_streams_validate_framing_before_recovery(string name, byte[] pdf, string expected, bool repaired)
    {
        var file = PdfFile.Load(pdf);
        var stream = Assert.IsType<PdfStream>(file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(Encoding.ASCII.GetBytes(expected), stream.RawData);
        Assert.True(file.Repaired == repaired, $"{name}: expected repaired={repaired}, actual={file.Repaired}");
        Assert.Same(stream, file.GetObject(2));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("3.0")]
    public void External_file_Length_recommendation_is_not_treated_as_an_embedded_extent_requirement(string length)
    {
        var file = PdfFile.Load(Build(length: length, external: true));
        Assert.Equal("abc"u8.ToArray(), Assert.IsType<PdfStream>(file.GetObject(2)).RawData);
        Assert.False(file.Repaired);
    }

    [Fact]
    public void Missing_endstream_at_physical_EOF_enters_explicit_recovery()
    {
        var original = Encoding.ASCII.GetString(Build());
        var oldTarget = original.IndexOf("2 0 obj", StringComparison.Ordinal);
        var text = original.Replace($"{oldTarget:D10} 00000 n", $"{original.Length:D10} 00000 n", StringComparison.Ordinal);
        var file = PdfFile.Load(Encoding.ASCII.GetBytes(text + "2 0 obj\n<< /Length 3 >>\nstream\nabc"));
        Assert.False(file.Repaired);
        Assert.Equal("abc"u8.ToArray(), Assert.IsType<PdfStream>(file.GetObject(2)).RawData);
        Assert.True(file.Repaired);
    }

    internal static byte[] Build(string opening = "\n", string length = "3", string data = "abc",
        string ending = "\nendstream", string closing = "\nendobj\n", bool repair = false, bool external = false)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var root = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = text.Length;
        var entry = length == "missing" ? "" : "/Length " + length;
        text.Append($"2 0 obj\n<< {entry}{(external ? " /F (external.bin)" : "")} >>\nstream{opening}{data}{ending}{closing}");
        var indirectLength = text.Length;
        text.Append("4 0 obj\n3\nendobj\n");
        var xref = text.Length;
        text.Append($"xref\n0 5\n0000000000 65535 f \n{root:D10} 00000 n \n{target:D10} 00000 n \n");
        text.Append($"0000000000 00000 f \n{indirectLength:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{(repair ? -1 : xref)}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
