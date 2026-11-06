using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefClassicKeywordBoundaryTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var fixedEntry in new[] { false, true })
            foreach (var suffix in new[] { "0", "+0", "-0", "0000", "xref\n0", "XREF\n0" })
                yield return [suffix, fixedEntry, true];
        foreach (var fixedEntry in new[] { false, true })
            foreach (var suffix in new[] { "\n0", "\r\n0", " 0", "\t0", "\00", "\f0", "% comment\n0" })
                yield return [suffix, fixedEntry, false];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Xref_requires_a_token_boundary_without_narrowing_existing_whitespace_or_comments(string suffix, bool fixedEntry, bool repaired)
    {
        var file = PdfFile.Load(Build(suffix, fixedEntry));
        Assert.Equal(repaired, file.Repaired);
        AssertTarget(file);
        Assert.Equal(repaired, file.Repaired);
    }

    internal static byte[] Build(string suffix, bool fixedEntry)
    {
        var text = Encoding.ASCII.GetString(XrefClassicGenerationTests.Build(0, fixedEntry: fixedEntry));
        return Encoding.ASCII.GetBytes(text.Replace("xref\n0 3\n", $"xref{suffix} 3\n", StringComparison.Ordinal));
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
    }
}
