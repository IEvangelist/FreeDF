using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamDictionaryBoundsTests
{
    [Theory]
    [InlineData("1.5", "4")]
    [InlineData("1", "4.5")]
    [InlineData("1.0", "4")]
    [InlineData("1", "4.0")]
    [InlineData("null", "4")]
    [InlineData("1", "null")]
    [InlineData("/Bad", "4")]
    [InlineData("1", "/Bad")]
    [InlineData("-1", "4")]
    [InlineData("1", "-1")]
    [InlineData("4294967297", "4")]
    [InlineData("1", "4294967300")]
    [InlineData("9223372036854775807", "4")]
    [InlineData("1", "9223372036854775807")]
    [InlineData("1 0 R", "4")]
    [InlineData("1", "1 0 R")]
    public void Invalid_dictionary_bounds_do_not_become_members_after_recovery(string count, string first)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Fixture(count, first, forceRepair));
            Assert.Equal(forceRepair, file.Repaired);
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("N")]
    [InlineData("First")]
    public void Required_bounds_cannot_default_when_missing(string key)
    {
        var original = XrefStreamEntryRangeTests.Build(2, compressed: true);
        var text = Encoding.Latin1.GetString(original);
        var token = key == "N" ? "/N 1" : "/First 4";
        text = text.Replace(token, new string(' ', token.Length), StringComparison.Ordinal);
        var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
        Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(2));
    }

    [Theory]
    [InlineData("1", "4")]
    [InlineData("+1", "004")]
    public void Integral_bounds_keep_lazy_member_lookup_and_cache_behavior(string count, string first)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Fixture(count, first, forceRepair));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    static byte[] Fixture(string count, string first, bool forceRepair)
    {
        var data = XrefStreamEntryRangeTests.Build(2, compressed: true, count: count, first: first);
        if (!forceRepair) return data;
        var text = Encoding.Latin1.GetString(data);
        return Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
    }

    [Theory]
    [InlineData(false, "1", "4")]
    [InlineData(true, "1", "4")]
    [InlineData(false, "1.5", "4")]
    [InlineData(true, "1", "4.5")]
    public void Indirect_bounds_are_resolved_without_losing_integer_validation(bool forceRepair, string count, string first)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, count: count, first: first, indirectBounds: true);
        if (forceRepair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        var file = PdfFile.Load(bytes);
        if (count == "1" && first == "4")
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        else
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal(forceRepair || count != "1" || first != "4", file.Repaired);
    }
}
