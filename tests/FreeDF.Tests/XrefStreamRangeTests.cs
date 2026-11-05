using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamRangeTests
{
    internal static byte[] Build(string index, string size = "7")
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-2.0\n");
        var offsets = new List<long> { 0 };
        foreach (var body in new[] { "<< /Type /Catalog /Probe 4 0 R >>", "null", "null", "<< /Type /Target >>" })
        {
            offsets.Add(data.Position);
            Write($"{offsets.Count - 1} 0 obj\n{body}\nendobj\n");
        }
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(int type, long offset, int generation)
        {
            rows.WriteByte((byte)type);
            for (var shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(offset >> shift));
            rows.WriteByte((byte)(generation >> 8));
            rows.WriteByte((byte)generation);
        }
        Row(0, 0, 65535);
        foreach (var offset in offsets.Skip(1).Take(3)) Row(1, offset, 0);
        var standard = index.Length == 0 || index == "/Index null" || index.StartsWith("/Index [0 7", StringComparison.Ordinal);
        Row(standard ? 1 : 0, standard ? offsets[4] : 0, 0);
        Row(standard ? 0 : 1, standard ? 0 : offsets[4], 0);
        Row(1, xref, 0);
        Write($"6 0 obj\n<< /Type /XRef /Root 1 0 R /Size {size} {index} /W [1 4 2] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData("/Index [0 4 4 1 4 1 6 1]", "7")]
    [InlineData("/Index [0 4 5 1 4 1 6 1]", "7")]
    [InlineData("/Index [0 4 4294967300 1 4 1 6 1]", "7")]
    [InlineData("/Index [0 4 4 1 6]", "7")]
    [InlineData("/Index [0 4 4.5 1 6 1]", "7")]
    [InlineData("/Index [0 4 4 1.5 6 1]", "7")]
    [InlineData("/Index [0 4 -1 1 4 1 6 1]", "7")]
    [InlineData("/Index [0 4 4 -1 6 1]", "7")]
    [InlineData("/Index [0 4 2147483647 2]", "2147483648")]
    [InlineData("/Index [0 4 4 1 6 1]", "6")]
    [InlineData("/Index /Bad", "7")]
    [InlineData("/Index 2 0 R", "7")]
    [InlineData("/Index [0 4 2 0 R 1]", "7")]
    [InlineData("/Index [0 4 4 1 6 1]", "7.5")]
    [InlineData("/Index [0 4 4 1 6 1]", "4294967303")]
    [InlineData("/Index [0 4 4 1 6 1]", "-1")]
    [InlineData("/Index [0 4 4 1 6 1]", "null")]
    [InlineData("/Index [0 4 4 1 6 1]", "2 0 R")]
    [InlineData("/Index [0 4 4 1 6 1]", "0")]
    public void Invalid_raw_ranges_are_repaired_instead_of_narrowed(string index, string size)
    {
        var file = PdfFile.Load(Build(index, size));
        Assert.True(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.DoesNotContain(file.Xref.Keys, n => n < 0);
    }

    [Theory]
    [InlineData("/Index [0 7]", "7")]
    [InlineData("/Index [0 7 2147483647 0]", "2147483648")]
    [InlineData("", "7")]
    [InlineData("/Index null", "7")]
    public void Supported_ranges_retain_existing_no_repair_behavior(string index, string size)
    {
        var file = PdfFile.Load(Build(index, size));
        Assert.False(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }
}
