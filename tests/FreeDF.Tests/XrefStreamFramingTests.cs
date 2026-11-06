using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamFramingTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var flate in new[] { false, true })
            foreach (var change in new[]
            {
                "opening-cr", "opening-space", "opening-none", "opening-space-lf",
                "closing-space", "closing-tab", "closing-two-eol", "closing-comment",
                "endstream-suffix", "endobj-value", "endobj-suffix", "endobj-missing",
                "length-short", "length-long", "length-huge"
            })
                yield return [change, flate];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Malformed_framing_requires_explicit_xref_recovery(string change, bool flate)
    {
        var file = PdfFile.Load(Build(change, flate));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData(false, "\n", "")]
    [InlineData(true, "\n", "")]
    [InlineData(false, "\r\n", "\n")]
    [InlineData(true, "\r\n", "\n")]
    [InlineData(false, "\n", "\r")]
    [InlineData(true, "\n", "\r")]
    [InlineData(false, "\r\n", "\r\n")]
    [InlineData(true, "\r\n", "\r\n")]
    public void Exact_extent_and_legal_opening_eol_preserve_clean_index(bool flate, string opening, string separator)
    {
        var text = Encoding.Latin1.GetString(Build("complete", flate));
        text = text.Replace("\nstream\n", "\nstream" + opening, StringComparison.Ordinal)
            .Replace("\nendstream", separator + "endstream", StringComparison.Ordinal);
        var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData(" \t\r\n")]
    [InlineData("\n% complete comment\n")]
    public void Complete_closing_trivia_preserves_clean_index(string trivia)
    {
        var text = Encoding.Latin1.GetString(Build("complete", false))
            .Replace("endstream\nendobj", "endstream" + trivia + "endobj", StringComparison.Ordinal);
        var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
        Assert.False(file.Repaired);
        AssertTarget(file);
    }

    internal static byte[] Build(string change, bool flate)
    {
        var bytes = XrefStreamCryptFilterTests.Build(flate ? "/FlateDecode" : null, flate);
        var text = Encoding.Latin1.GetString(bytes);
        var opening = text.IndexOf("\nstream\n", StringComparison.Ordinal);
        var start = opening + "\nstream\n".Length;
        var end = text.IndexOf("\nendstream", start, StringComparison.Ordinal);
        var length = end - start;
        text = change switch
        {
            "opening-cr" => text.Replace("\nstream\n", "\nstream\r", StringComparison.Ordinal),
            "opening-space" => text.Replace("\nstream\n", "\nstream ", StringComparison.Ordinal),
            "opening-none" => text.Replace("\nstream\n", "\nstream", StringComparison.Ordinal),
            "opening-space-lf" => text.Replace("\nstream\n", "\nstream \n", StringComparison.Ordinal),
            "closing-space" => text.Replace("\nendstream", " endstream", StringComparison.Ordinal),
            "closing-tab" => text.Replace("\nendstream", "\tendstream", StringComparison.Ordinal),
            "closing-two-eol" => text.Replace("\nendstream", "\n\nendstream", StringComparison.Ordinal),
            "closing-comment" => text.Replace("\nendstream", "\n% ignored\nendstream", StringComparison.Ordinal),
            "endstream-suffix" => text.Replace("endstream\n", "endstreamX\n", StringComparison.Ordinal),
            "endobj-value" => text.Replace("endstream\nendobj", "endstream\n42\nendobj", StringComparison.Ordinal),
            "endobj-suffix" => text.Replace("endstream\nendobj", "endstream\nendobjX", StringComparison.Ordinal),
            "endobj-missing" => text.Replace("endstream\nendobj", "endstream\n", StringComparison.Ordinal),
            "length-short" => text.Replace($"/Length {length}", $"/Length {length - 1}", StringComparison.Ordinal),
            "length-long" => text.Replace($"/Length {length}", $"/Length {length + 2}", StringComparison.Ordinal),
            "length-huge" => text.Replace($"/Length {length}", "/Length 9223372036854775807", StringComparison.Ordinal),
            _ => text
        };
        return Encoding.Latin1.GetBytes(text);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
