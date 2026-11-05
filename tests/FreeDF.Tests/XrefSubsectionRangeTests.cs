using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefSubsectionRangeTests
{
    static PdfFile Load(string start, string count, bool fixedEntry)
    {
        var text = new StringBuilder("%PDF-2.0\n");
        var offsets = new List<int>();
        foreach (var body in new[]
        {
            "<< /Type /Catalog /Probe 4 0 R >>", "null", "null",
            "<< /Type /Target >>",
        })
        {
            offsets.Add(text.Length);
            text.Append($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }
        var xref = text.Length;
        text.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var offset in offsets.Take(3)) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"{start} {count}\n");
        if (count != "-1" && count != "0")
            text.Append(fixedEntry ? "0000000000 00000 f \n" : "0 0 f\n");
        if (count == "2")
            text.Append(fixedEntry ? "0000000000 00000 f \n" : "0 0 f\n");
        text.Append($"4 1\n{offsets[3]:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return PdfFile.Load(Encoding.ASCII.GetBytes(text.ToString()));
    }

    [Theory]
    [InlineData("-1", "1")]
    [InlineData("4294967300", "1")]
    [InlineData("9223372036854775807", "1")]
    [InlineData("2147483647", "2")]
    [InlineData("8", "-1")]
    [InlineData("8", "9223372036854775807")]
    public void Unsupported_ranges_trigger_repair_without_wrapped_or_negative_keys(string start, string count)
    {
        foreach (var fixedEntry in new[] { true, false })
        {
            var file = Load(start, count, fixedEntry);
            Assert.True(file.Repaired);
            Assert.DoesNotContain(file.Xref.Keys, number => number < 0);
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        }
    }

    [Theory]
    [InlineData("2147483647", "1")]
    [InlineData("8", "0")]
    [InlineData("2147483647", "0")]
    public void Supported_sparse_boundaries_and_empty_subsections_keep_existing_behavior(string start, string count)
    {
        foreach (var fixedEntry in new[] { true, false })
        {
            var file = Load(start, count, fixedEntry);
            Assert.False(file.Repaired);
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        }
    }
}
