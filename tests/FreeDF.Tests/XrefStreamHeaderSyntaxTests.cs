using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamHeaderSyntaxTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var flate in new[] { false, true })
        {
            foreach (var number in new[] { "++3", "+++3", "++003", "--3", "3.0", "3abc" })
                yield return [number, "0", flate, true];
            foreach (var generation in new[] { "++0", "+++0", "--0", "+-0", "-+0", "0.0", "0abc" })
                yield return ["3", generation, flate, true];
            foreach (var pair in new[] { ("3", "0"), ("+3", "+0"), ("003", "000"), ("3", "-0") })
                yield return [pair.Item1, pair.Item2, flate, false];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Header_integer_tokens_require_valid_syntax_before_indexed_loading(string number, string generation, bool flate, bool repaired)
    {
        var file = PdfFile.Load(XrefStreamHeaderRangeTests.Build(number, generation, flate));
        Assert.Equal(repaired, file.Repaired);
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(new PdfRef(2, 0)));
        Assert.Equal(repaired, file.Repaired);
        if (!repaired)
        {
            Assert.IsType<PdfStream>(file.GetObject(new PdfRef(3, 0)));
            Assert.False(file.Repaired);
        }
    }
}
