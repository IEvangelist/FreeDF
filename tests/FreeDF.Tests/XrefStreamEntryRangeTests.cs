using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamEntryRangeTests
{
    internal static byte[] Build(ulong type, ulong? second = null, ulong third = 0, bool compressed = false, int generation = 0,
        string count = "1", string first = "4", bool indirectBounds = false, string value = "<< /Type /Target >>",
        string header = "2 0 ", string? length = null, string terminator = "\nendstream", string opening = "\n",
        string closing = "\nendobj\n")
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var root = data.Position;
        Write($"1 0 obj\n<< /Type /Catalog /Probe 2 {generation} R >>\nendobj\n");
        var target = data.Position;
        if (!compressed) Write($"2 {generation} obj\n<< /Type /Target >>\nendobj\n");
        var container = data.Position;
        if (compressed)
        {
            var body = header + value;
            Write($"3 0 obj\n<< /Type /ObjStm /N {(indirectBounds ? "5 0 R" : count)} /First {(indirectBounds ? "6 0 R" : first)} /Length {length ?? body.Length.ToString()} >>\nstream{opening}{body}{terminator}{closing}");
        }
        var countOffset = data.Position;
        if (indirectBounds) Write($"5 0 obj\n{count}\nendobj\n");
        var firstOffset = data.Position;
        if (indirectBounds) Write($"6 0 obj\n{first}\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong value, ulong index)
        {
            foreach (var field in new[] { kind, value, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)root, 0);
        Row(type, second ?? (compressed ? 3UL : (ulong)target), third);
        Row(compressed ? 1UL : 0UL, compressed ? (ulong)container : 0, 0);
        Row(1, (ulong)xref, 0);
        if (indirectBounds)
        {
            Row(1, (ulong)countOffset, 0);
            Row(1, (ulong)firstOffset, 0);
        }
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size {(indirectBounds ? 7 : 5)} /Index [1 {(indirectBounds ? 6 : 4)}] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData(0UL, null, 4294967296UL, false)]
    [InlineData(1UL, null, 4294967296UL, false)]
    [InlineData(1UL, null, 9223372036854775808UL, false)]
    [InlineData(1UL, 9223372036854775808UL, 0UL, false)]
    [InlineData(2UL, 4294967299UL, 0UL, true)]
    [InlineData(2UL, 9223372036854775811UL, 0UL, true)]
    [InlineData(2UL, null, 4294967296UL, true)]
    [InlineData(2UL, null, 9223372036854775808UL, true)]
    public void Unsupported_fields_repair_without_wrapped_identity(ulong type, ulong? second, ulong third, bool compressed)
    {
        var file = PdfFile.Load(Build(type, second, third, compressed));
        Assert.True(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.DoesNotContain(file.Xref.Values, e => e.GenOrIndex < 0 || e.OffsetOrStream < 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    [InlineData(99999)]
    public void Existing_lossless_generation_range_is_preserved(int generation)
    {
        var file = PdfFile.Load(Build(1, third: (ulong)generation, generation: generation));
        Assert.False(file.Repaired);
        Assert.Equal(generation, file.Xref[2].GenOrIndex);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.False(file.Repaired);
    }

    [Fact]
    public void Int_generation_boundary_is_lossless_for_explicit_reference_lookup()
    {
        var file = PdfFile.Load(Build(1, third: int.MaxValue, generation: int.MaxValue));
        Assert.False(file.Repaired);
        Assert.Equal(int.MaxValue, file.Xref[2].GenOrIndex);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(new PdfRef(2, int.MaxValue))).Get("Type").AsName());
        Assert.False(file.Repaired);
    }

    [Fact]
    public void Compressed_zero_member_retains_container_and_generation_identity()
    {
        var file = PdfFile.Load(Build(2, compressed: true));
        Assert.False(file.Repaired);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData(3UL)]
    [InlineData(4294967297UL)]
    [InlineData(9223372036854775809UL)]
    [InlineData(18446744073709551615UL)]
    public void Unknown_entry_types_remain_null_without_narrowing_to_known_types(ulong type)
    {
        var file = PdfFile.Load(Build(type, ulong.MaxValue, ulong.MaxValue));
        Assert.False(file.Repaired);
        Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.False(file.Repaired);
    }
}
