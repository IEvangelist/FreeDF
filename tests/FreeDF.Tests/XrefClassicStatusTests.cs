using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefClassicStatusTests
{
    internal static byte[] Build(string status)
    {
        var text = Encoding.ASCII.GetString(XrefClassicGenerationTests.Build(status == "f" ? 1 : 0, free: status == "f", fixedEntry: true));
        var end = text.IndexOf("trailer\n", StringComparison.Ordinal);
        var start = text.LastIndexOf('\n', end - 2) + 1;
        var row = text[start..end].Replace(" n ", $" {status} ", StringComparison.Ordinal);
        text = text[..start] + row + text[end..];
        if (status == "f") text = text.Replace("0000000000 65535 f ", "0000000002 65535 f ", StringComparison.Ordinal);
        return Encoding.ASCII.GetBytes(text);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("N")]
    [InlineData("F")]
    [InlineData("nonsense")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("/n")]
    public void Invalid_status_tokens_repair_instead_of_marking_target_free(string status)
    {
        var file = PdfFile.Load(Build(status));
        Assert.True(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Theory]
    [InlineData("n")]
    [InlineData("f")]
    public void Exact_status_tokens_keep_existing_in_use_and_free_behavior(string status)
    {
        var file = PdfFile.Load(Build(status));
        Assert.False(file.Repaired);
        if (status == "n")
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        else
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.False(file.Repaired);
    }
}
