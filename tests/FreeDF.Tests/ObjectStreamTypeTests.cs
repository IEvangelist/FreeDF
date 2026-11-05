using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamTypeTests
{
    internal static byte[] Build(string? type, bool forceRepair, bool indirect = false)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var container = data.Position;
        const string body = "2 0 << /Type /Target >>";
        var entry = type is null ? "" : $"/Type {(indirect ? "5 0 R" : type)}";
        Write($"3 0 obj\n<< {entry} /N 1 /First 4 /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        var typeOffset = data.Position;
        Write($"5 0 obj\n{type ?? "null"}\nendobj\n");
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
        Row(1, (ulong)typeOffset, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 6 /Index [1 5] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("/Other")]
    [InlineData("/objstm")]
    [InlineData("(ObjStm)")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("1 0 R")]
    [InlineData("3 0 R")]
    public void Missing_or_wrong_type_cannot_supply_compressed_members(string? type)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(type, forceRepair));
            Assert.Equal(forceRepair, file.Repaired);
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Direct_or_indirect_object_stream_name_supports_member_lookup(bool forceRepair, bool indirect)
    {
        var file = PdfFile.Load(Build("/ObjStm", forceRepair, indirect));
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal(forceRepair, file.Repaired);
    }

    [Theory]
    [InlineData("/Other")]
    [InlineData("null")]
    [InlineData("(ObjStm)")]
    [InlineData("5 0 R")]
    public void Indirect_type_resolution_keeps_name_validation(string type)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(type, forceRepair, indirect: true));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Type_depending_on_its_own_member_cannot_reenter_header_loading(bool forceRepair, bool indirect)
    {
        var file = PdfFile.Load(Build("2 0 R", forceRepair, indirect));
        if (forceRepair)
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
        else
            Assert.Throws<PdfParseException>(() => file.Resolve(file.Catalog.Get("Probe")));
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(2));
    }
}
