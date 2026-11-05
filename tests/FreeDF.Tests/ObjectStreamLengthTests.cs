using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamLengthTests
{
    internal static byte[] Build(bool compressedLength, bool forceRepair, bool indirectType = false,
        bool lengthStreamFirst = false, bool ordinaryStream = false)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        const string body = "2 0 << /Type /Target >>";
        long container = 0, lengthContainer = 0;
        void WriteContainer()
        {
            container = data.Position;
            var type = ordinaryStream ? "/Example" : indirectType ? "7 0 R" : "/ObjStm";
            Write($"3 0 obj\n<< /Type {type} /N 1 /First 4 /Length 5 0 R >>\nstream\n{body}\nendstream\nendobj\n");
        }
        void WriteLengthContainer()
        {
            if (!compressedLength) return;
            lengthContainer = data.Position;
            var lengthBody = $"5 0 {body.Length}";
            Write($"4 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Length {lengthBody.Length} >>\nstream\n{lengthBody}\nendstream\nendobj\n");
        }
        if (lengthStreamFirst) { WriteLengthContainer(); WriteContainer(); }
        else { WriteContainer(); WriteLengthContainer(); }
        var lengthOffset = data.Position;
        if (!compressedLength) Write($"5 0 obj\n{body.Length}\nendobj\n");
        var typeOffset = data.Position;
        Write("7 0 obj\n/ObjStm\nendobj\n");
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
        Row(compressedLength ? 1UL : 0UL, (ulong)lengthContainer, 0);
        Row(compressedLength ? 2UL : 1UL, compressedLength ? 4UL : (ulong)lengthOffset, 0);
        Row(1, (ulong)xref, 0);
        Row(1, (ulong)typeOffset, 0);
        Write($"6 0 obj\n<< /Type /XRef /Root 1 0 R /Size 8 /Index [1 7] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Compressed_length_cannot_supply_object_stream_members(bool forceRepair, bool indirectType, bool lengthStreamFirst)
    {
        var file = PdfFile.Load(Build(true, forceRepair, indirectType, lengthStreamFirst));
        if (lengthStreamFirst)
            Assert.IsType<PdfNull>(file.GetObject(2));
        else
            Assert.Throws<PdfParseException>(() => file.GetObject(2));
        Assert.True(file.Repaired);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Uncompressed_indirect_length_remains_supported(bool forceRepair, bool indirectType)
    {
        var file = PdfFile.Load(Build(false, forceRepair, indirectType));
        var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.GetObject(2));
        Assert.Equal(forceRepair, file.Repaired);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Ordinary_stream_length_may_be_compressed(bool forceRepair, bool lengthStreamFirst)
    {
        var file = PdfFile.Load(Build(true, forceRepair, lengthStreamFirst: lengthStreamFirst, ordinaryStream: true));
        var stream = Assert.IsType<PdfStream>(file.GetObject(3));
        Assert.Equal("Example", stream.Dict.Get("Type").AsName());
        Assert.Equal(Encoding.ASCII.GetBytes("2 0 << /Type /Target >>"), stream.RawData);
        Assert.Equal(forceRepair, file.Repaired);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Own_length_member_cannot_trigger_recursive_loading_or_recovery_indexing(bool forceRepair, bool laterMember)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true,
            header: laterMember ? "2 0 5 3 " : "2 0 ", first: laterMember ? "8" : "4",
            count: laterMember ? "2" : "1", value: laterMember ? "42 13" : "5",
            length: laterMember ? "5 0 R" : "2 0 R");
        if (forceRepair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        var file = PdfFile.Load(bytes);
        Assert.IsType<PdfNull>(file.GetObject(2));
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(2));
        Assert.False(file.Xref.ContainsKey(5));
    }
}
