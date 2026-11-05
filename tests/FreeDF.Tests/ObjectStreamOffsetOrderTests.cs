using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamOffsetOrderTests
{
    const string FirstBody = "<< /Type /First >>";
    const string SecondBody = "<< /Type /Second >>";
    internal static int SecondOffset => FirstBody.Length + 1;

    internal static byte[] Build(int firstOffset, int secondOffset, bool forceRepair, bool reverseNumbers = false,
        string? secondBody = null, string? firstBody = null)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R /Other 5 0 R >>\nendobj\n");
        var container = data.Position;
        var header = $"{(reverseNumbers ? 5 : 2)} {firstOffset} {(reverseNumbers ? 2 : 5)} {secondOffset} ";
        var body = header + (firstBody ?? FirstBody) + " " + (secondBody ?? SecondBody);
        Write($"3 0 obj\n<< /Type /ObjStm /N 2 /First {header.Length} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong type, ulong value, ulong index)
        {
            foreach (var field in new[] { type, value, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)catalog, 0);
        Row(2, 3, reverseNumbers ? 1UL : 0UL);
        Row(1, (ulong)container, 0);
        Row(1, (ulong)xref, 0);
        Row(2, 3, reverseNumbers ? 0UL : 1UL);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 6 /Index [1 5] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(18, 0)]
    [InlineData(18, 18)]
    public void Equal_or_decreasing_offsets_cannot_alias_or_reorder_members(int firstOffset, int secondOffset)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(firstOffset, secondOffset, forceRepair));
            Assert.Equal(forceRepair, file.Repaired);
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.False(file.Xref.ContainsKey(5));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Increasing_offsets_do_not_require_object_number_order(bool forceRepair, bool reverseNumbers)
    {
        var file = PdfFile.Load(Build(0, SecondOffset, forceRepair, reverseNumbers));
        var probe = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        var other = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Other")));
        Assert.Equal(reverseNumbers ? "Second" : "First", probe.Get("Type").AsName());
        Assert.Equal(reverseNumbers ? "First" : "Second", other.Get("Type").AsName());
        Assert.Same(probe, file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal(forceRepair, file.Repaired);
    }
}
