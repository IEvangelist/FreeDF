using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefChainOffsetTests
{
    internal static byte[] Build(string key, string value, bool stream = false)
    {
        var original = XrefStreamEntryRangeTests.Build(2, compressed: true);
        var text = Encoding.Latin1.GetString(original);
        var oldXref = long.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        var root = text.IndexOf("1 0 obj", StringComparison.Ordinal);
        using var data = new MemoryStream();
        data.Write(original);
        void Write(string s) => data.Write(Encoding.ASCII.GetBytes(s));
        long? main = null;
        if (key == "XRefStm")
        {
            main = data.Position;
            var container = text.IndexOf("3 0 obj", StringComparison.Ordinal);
            Write($"xref\n0 5\n0000000002 65535 f \n{root:D10} 00000 n \n0000000000 65535 f \n{container:D10} 00000 n \n{oldXref:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{main}\n%%EOF\n");
        }
        var offset = data.Position;
        value = value.Replace("@OFFSET@", oldXref.ToString(), StringComparison.Ordinal);
        if (stream)
        {
            using var rows = new MemoryStream();
            foreach (var position in new[] { (long)root, offset })
            {
                rows.WriteByte(1);
                for (var shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(position >> shift));
                rows.WriteByte(0);
                rows.WriteByte(0);
            }
            Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 5 /Index [1 1 4 1] /W [1 4 2] /Length {rows.Length} /{key} {value} >>\nstream\n");
            data.Write(rows.ToArray());
            Write("\nendstream\nendobj\n");
        }
        else
        {
            var entries = key == "XRefStm" ? $"1 1\n{root:D10} 00000 n \n"
                : $"1 2\n{root:D10} 00000 n \n0000000000 00000 f \n";
            var prev = main is { } previous ? $" /Prev {previous}" : "";
            Write($"xref\n{entries}trailer\n<< /Size 5 /Root 1 0 R /{key} {value}{prev} >>\n");
        }
        Write($"startxref\n{offset}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData("Prev", "@OFFSET@.5", false)]
    [InlineData("Prev", "-1", false)]
    [InlineData("Prev", "/Bad", false)]
    [InlineData("Prev", "2 0 R", false)]
    [InlineData("Prev", "9223372036854775807", false)]
    [InlineData("Prev", "@OFFSET@.5", true)]
    [InlineData("Prev", "-1", true)]
    [InlineData("Prev", "/Bad", true)]
    [InlineData("Prev", "2 0 R", true)]
    [InlineData("XRefStm", "@OFFSET@.5", false)]
    [InlineData("XRefStm", "/Bad", false)]
    [InlineData("XRefStm", "2 0 R", false)]
    public void Invalid_present_chain_offsets_do_not_silently_truncate_or_disappear(string key, string value, bool stream)
    {
        var file = PdfFile.Load(Build(key, value, stream));
        Assert.True(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Theory]
    [InlineData("Prev", false)]
    [InlineData("Prev", true)]
    [InlineData("XRefStm", false)]
    public void Valid_integer_offsets_keep_incremental_and_hybrid_precedence(string key, bool stream)
    {
        var file = PdfFile.Load(Build(key, "@OFFSET@", stream));
        Assert.False(file.Repaired);
        if (key == "XRefStm" || stream)
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        else
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("Prev", false)]
    [InlineData("Prev", true)]
    [InlineData("XRefStm", false)]
    public void Null_chain_offsets_retain_absent_entry_semantics(string key, bool stream)
    {
        var file = PdfFile.Load(Build(key, "null", stream));
        Assert.False(file.Repaired);
        Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
    }
}
