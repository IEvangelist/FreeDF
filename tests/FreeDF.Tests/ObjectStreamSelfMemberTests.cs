using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamSelfMemberTests
{
    internal static byte[] Build(bool selfFirst, bool forceRepair, bool includeSelf = true)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        Write("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var container = data.Position;
        const string target = "<< /Type /Compressed >>";
        var other = includeSelf ? 3 : 5;
        var header = selfFirst ? $"{other} 0 2 3 " : $"2 0 {other} {target.Length + 1} ";
        var body = header + (selfFirst ? "42 " + target : target + " 42");
        Write($"3 0 obj\n<< /Type /ObjStm /N 2 /First {header.Length} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong value, ulong index)
        {
            foreach (var field in new[] { kind, value, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)catalog, 0);
        Row(2, 3, selfFirst ? 1UL : 0UL);
        Row(1, (ulong)container, 0);
        Row(1, (ulong)xref, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 5 /Index [1 4] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovery_cannot_publish_its_container_as_its_own_compressed_member(bool selfFirst)
    {
        var file = PdfFile.Load(Build(selfFirst, forceRepair: true));
        Assert.False(file.Xref[3].Compressed);
        Assert.False(file.Xref[2].Compressed);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.IsType<PdfStream>(file.GetObject(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Normal_lookup_rejects_the_complete_self_containing_index(bool selfFirst)
    {
        var file = PdfFile.Load(Build(selfFirst, forceRepair: false));
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.True(file.Repaired);
        Assert.False(file.Xref[3].Compressed);
        Assert.False(file.Xref[2].Compressed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Distinct_member_numbers_keep_container_and_member_identities(bool selfFirst, bool repair)
    {
        var file = PdfFile.Load(Build(selfFirst, repair, includeSelf: false));
        Assert.Equal("Compressed", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(repair, file.Repaired);
        Assert.False(file.Xref[3].Compressed);
        Assert.IsType<PdfStream>(file.GetObject(3));
        if (repair) Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(5)).Value);
    }
}
