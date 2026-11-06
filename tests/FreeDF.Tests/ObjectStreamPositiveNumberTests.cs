using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamPositiveNumberTests
{
    internal static byte[] Build(bool laterZero, bool forceRepair)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write($"1 0 obj\n<< /Type /Catalog /Probe {(laterZero ? 2 : 0)} 0 R >>\nendobj\n");
        var target = data.Position;
        Write("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var container = data.Position;
        const string value = "<< /Type /Compressed >>";
        var header = laterZero ? $"2 0 0 {value.Length + 1} " : "0 0 ";
        var body = header + value + (laterZero ? " 42" : "");
        Write($"3 0 obj\n<< /Type /ObjStm /N {(laterZero ? 2 : 1)} /First {header.Length} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong offset, ulong index)
        {
            foreach (var field in new[] { kind, offset, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(2, 3, laterZero ? 1UL : 0UL);
        Row(1, (ulong)catalog, 0);
        Row(laterZero ? 2UL : 1UL, laterZero ? 3UL : (ulong)target, 0);
        Row(1, (ulong)container, 0);
        Row(1, (ulong)xref, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 5 /Index [0 5] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Zero_member_number_cannot_install_reserved_or_partial_identities(bool laterZero, bool forceRepair)
    {
        var file = PdfFile.Load(Build(laterZero, forceRepair));
        var probe = file.Resolve(file.Catalog.Get("Probe"));
        if (laterZero)
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(probe).Get("Type").AsName());
        else
            Assert.IsType<PdfNull>(probe);
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(0));
        Assert.False(file.Xref[2].Compressed);
    }

    [Theory]
    [InlineData("0 0 ")]
    [InlineData("2 0 0 3 ")]
    public void Zero_number_does_not_replace_valid_uncompressed_identity_in_recovery(string header)
    {
        var file = PdfFile.Load(ObjectStreamRecoveryHeaderTests.Build(header,
            header.StartsWith("2", StringComparison.Ordinal) ? "42 43" : "42",
            count: header.StartsWith("2", StringComparison.Ordinal) ? 2 : 1));
        Assert.True(file.Repaired);
        Assert.False(file.Xref.ContainsKey(0));
        Assert.False(file.Xref[2].Compressed);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void Positive_member_numbers_remain_supported(int number)
    {
        var file = PdfFile.Load(ObjectStreamRecoveryHeaderTests.Build($"{number} 0 ", "42"));
        Assert.True(file.Repaired);
        Assert.True(file.Xref[number].Compressed);
        Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(number)).Value);
        Assert.False(file.Xref.ContainsKey(0));
    }

    [Fact]
    public void Empty_object_stream_does_not_require_a_member_number()
    {
        var file = PdfFile.Load(ObjectStreamRecoveryHeaderTests.Build("", "", count: 0));
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.False(file.Xref.ContainsKey(0));
    }
}
