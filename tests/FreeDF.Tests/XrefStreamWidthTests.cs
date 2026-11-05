using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamWidthTests
{
    internal static byte[] Build(string width, int[]? encoding = null)
    {
        encoding ??= [1, 4, 2];
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var root = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = data.Position;
        Write("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        foreach (var offset in new[] { root, target, xref })
        {
            long[] fields = [1, offset, 0, 0];
            for (var field = 0; field < encoding.Length; field++)
                for (var shift = (encoding[field] - 1) * 8; shift >= 0; shift -= 8)
                    rows.WriteByte((byte)(fields[field] >> shift));
        }
        Write($"3 0 obj\n<< /Type /XRef /Root 1 0 R /Size 4 /Index [1 3] /W {width} /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData("[1.5 4 2]")]
    [InlineData("[1 4.5 2]")]
    [InlineData("[1 4 2.5]")]
    [InlineData("[1 4 2 0.5]")]
    [InlineData("[1 4 2 -0.5]")]
    [InlineData("[1 4 2 4294967296]")]
    [InlineData("[1 4 -1]")]
    [InlineData("[1 4 9]")]
    [InlineData("[1 4 2 0 0 0.5]")]
    [InlineData("[1 4]")]
    [InlineData("[0 0 0]")]
    [InlineData("[1 4 2 0 0 2 0 R]")]
    [InlineData("2 0 R")]
    [InlineData("null")]
    [InlineData("/Bad")]
    public void Invalid_raw_widths_use_explicit_recovery(string width)
    {
        var file = PdfFile.Load(Build(width));
        Assert.True(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Theory]
    [InlineData("[1 4 2]", new[] { 1, 4, 2 })]
    [InlineData("[0 4 0]", new[] { 0, 4, 0 })]
    [InlineData("[1 8 2]", new[] { 1, 8, 2 })]
    [InlineData("[1 4 2 1]", new[] { 1, 4, 2, 1 })]
    public void Supported_widths_keep_defaults_stride_and_no_repair(string width, int[] encoding)
    {
        var file = PdfFile.Load(Build(width, encoding));
        Assert.False(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.IsType<PdfStream>(file.GetObject(new PdfRef(3, 0)));
        Assert.False(file.Repaired);
    }
}
