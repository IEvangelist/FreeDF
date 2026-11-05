using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamExtendsTests
{
    internal static byte[] Build(string? extends, bool forceRepair, string parentType = "/ObjStm",
        string? parentExtends = null, int parentGeneration = 0, bool parentStream = true, bool indirectType = false)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var container = data.Position;
        const string body = "2 0 << /Type /Target >>";
        Write($"3 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Length {body.Length}" +
            (extends is null ? "" : $" /Extends {extends}") + $" >>\nstream\n{body}\nendstream\nendobj\n");
        var parent = data.Position;
        var parentDictionary = $"<< /Type {(indirectType ? "7 0 R" : parentType)} /N 1 /First 4 /Length 5" +
            (parentExtends is null ? "" : $" /Extends {parentExtends}") + " >>";
        Write($"5 {parentGeneration} obj\n{parentDictionary}" +
            (parentStream ? "\nstream\n8 0 (\nendstream" : "") + "\nendobj\n");
        var grandparent = data.Position;
        Write("6 0 obj\n<< /Type /ObjStm /N 0 /First 0 /Length 0 >>\nstream\n\nendstream\nendobj\n");
        var type = data.Position;
        Write($"7 0 obj\n{parentType}\nendobj\n");
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
        Row(1, (ulong)parent, (ulong)parentGeneration);
        Row(1, (ulong)grandparent, 0);
        Row(1, (ulong)type, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 8 /Index [1 7] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData("42", "/ObjStm", null, 0, true)]
    [InlineData("/ObjStm", "/ObjStm", null, 0, true)]
    [InlineData("<< /Type /ObjStm >>", "/ObjStm", null, 0, true)]
    [InlineData("99 0 R", "/ObjStm", null, 0, true)]
    [InlineData("5 1 R", "/ObjStm", null, 0, true)]
    [InlineData("5 1 R", "/ObjStm", null, 1, true)]
    [InlineData("1 0 R", "/ObjStm", null, 0, true)]
    [InlineData("5 0 R", "/Other", null, 0, true)]
    [InlineData("5 0 R", "/ObjStm", null, 0, false)]
    [InlineData("3 0 R", "/ObjStm", null, 0, true)]
    [InlineData("5 0 R", "/ObjStm", "3 0 R", 0, true)]
    [InlineData("5 0 R", "/ObjStm", "5 0 R", 0, true)]
    [InlineData("5 0 R", "/ObjStm", "42", 0, true)]
    public void Invalid_collection_links_cannot_supply_compressed_members(string extends,
        string parentType, string? parentExtends, int parentGeneration, bool parentStream)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(extends, forceRepair, parentType, parentExtends, parentGeneration, parentStream));
            Assert.IsType<PdfNull>(file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("null", null, false)]
    [InlineData("5 0 R", null, false)]
    [InlineData("5 0 R", "6 0 R", false)]
    [InlineData("5 0 R", null, true)]
    public void Valid_collections_do_not_eagerly_parse_ancestor_members(string? extends, string? parentExtends, bool indirectType)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(extends, forceRepair, parentExtends: parentExtends, indirectType: indirectType));
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }
}
