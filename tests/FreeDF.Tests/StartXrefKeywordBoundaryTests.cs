using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class StartXrefKeywordBoundaryTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var stream in new[] { false, true })
        {
            foreach (var prefix in new[] { "a", "0", "+", "-", "startxref" })
                yield return [prefix, "\n", stream, true];
            foreach (var separator in new[] { "", "+", "-0" })
                yield return ["", separator, stream, true];
            foreach (var separator in new[] { "\n", "\r\n", " ", "\t", "\0", "\f", "% comment\n" })
                yield return ["", separator, stream, false];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Startxref_requires_token_boundaries_without_narrowing_existing_separators(string prefix, string separator, bool stream, bool repaired)
    {
        var file = PdfFile.Load(Build(prefix, separator, stream));
        Assert.Equal(repaired, file.Repaired);
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(repaired, file.Repaired);
    }

    internal static byte[] Build(string prefix, string separator, bool stream)
    {
        var original = stream ? XrefStreamHeaderRangeTests.Build("3", "0", false)
            : XrefClassicGenerationTests.Build(0, fixedEntry: true);
        var text = Encoding.ASCII.GetString(original);
        var footer = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        return Encoding.ASCII.GetBytes(text[..footer] + prefix + "startxref" + separator + text[(footer + 10)..]);
    }
}
