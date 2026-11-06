using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamIndexReentrancyTests
{
    static byte[] Build(string key, bool repair, bool cyclic, bool throughReference = false)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var metadata = data.Position;
        Write($"5 0 obj\n{(cyclic && throughReference ? "2 0 R" : key == "N" ? "1" : "4")}\nendobj\n");
        var container = data.Position;
        var reference = cyclic && !throughReference ? "2 0 R" : "5 0 R";
        const string body = "2 0 << /Type /Target >>";
        Write($"3 0 obj\n<< /Type /ObjStm /N {(key == "N" ? reference : "1")} /First {(key == "First" ? reference : "4")} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong value, ulong index)
        {
            foreach (var field in new[] { kind, value, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)catalog, 0);
        Row(2, 3, 0);
        Row(1, (ulong)container, 0);
        Row(1, (ulong)xref, 0);
        Row(1, (ulong)metadata, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 6 /Index [1 5] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(repair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData("N", false, false)]
    [InlineData("N", true, false)]
    [InlineData("First", false, false)]
    [InlineData("First", true, false)]
    [InlineData("N", false, true)]
    [InlineData("N", true, true)]
    [InlineData("First", false, true)]
    [InlineData("First", true, true)]
    public void Metadata_cannot_recursively_request_its_unfinished_member_index(string key, bool repair, bool throughReference)
    {
        var file = PdfFile.Load(Build(key, repair, cyclic: true, throughReference));
        if (repair)
            Assert.IsType<PdfNull>(file.GetObject(2));
        else
            Assert.Equal("bad object stream header", Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(2));
        Assert.IsType<PdfStream>(file.GetObject(3));
        Assert.IsType<PdfNull>(file.GetObject(2));
    }

    [Theory]
    [InlineData("N", false)]
    [InlineData("N", true)]
    [InlineData("First", false)]
    [InlineData("First", true)]
    public void Ordinary_indirect_metadata_and_cached_member_lookup_remain_supported(string key, bool repair)
    {
        var file = PdfFile.Load(Build(key, repair, cyclic: false));
        var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(2));
        Assert.Equal(repair, file.Repaired);
    }
}
