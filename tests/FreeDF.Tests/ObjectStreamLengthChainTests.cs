using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamLengthChainTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void Compressed_integer_cannot_supply_Length_through_uncompressed_references(int hops, bool lengthStreamFirst)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(hops, true, repair, lengthStreamFirst));
            Assert.IsType<PdfNull>(file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.IsType<PdfNull>(file.GetObject(2));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(31)]
    public void Uncompressed_Length_reference_chains_remain_supported(int hops)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(hops, false, repair, false));
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.GetObject(2));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void Ordinary_stream_Length_may_resolve_to_a_compressed_integer_through_references(int hops, bool lengthStreamFirst)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(hops, true, repair, lengthStreamFirst, ordinary: true));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), Assert.IsType<PdfStream>(file.GetObject(3)).RawData);
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Fact]
    public void Existing_reference_depth_limit_is_not_extended()
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(32, false, repair, false));
            Assert.IsType<PdfNull>(file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    internal static byte[] Build(int hops, bool compressedLength, bool repair, bool lengthStreamFirst, bool ordinary = false)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        var offsets = new Dictionary<int, long>();
        void Object(int number, string value)
        {
            offsets[number] = data.Position;
            Write($"{number} 0 obj\n{value}\nendobj\n");
        }
        Write("%PDF-1.7\n");
        Object(1, "<< /Type /Catalog /Probe 2 0 R >>");
        var finalLength = 8 + hops;
        const string body = "2 0 << /Type /Target >>";
        void Container() => Object(3,
            $"<< /Type /{(ordinary ? "OtherX" : "ObjStm")} /N 1 /First 4 /Length 8 0 R >>\nstream\n{body}\nendstream");
        void LengthContainer()
        {
            if (!compressedLength) return;
            var header = $"{finalLength} 0 ";
            var lengthBody = header + body.Length;
            Object(4, $"<< /Type /ObjStm /N 1 /First {header.Length} /Length {lengthBody.Length} >>\nstream\n{lengthBody}\nendstream");
        }
        if (lengthStreamFirst) { LengthContainer(); Container(); }
        else { Container(); LengthContainer(); }
        for (var number = 8; number < finalLength; number++) Object(number, $"{number + 1} 0 R");
        if (!compressedLength) Object(finalLength, body.Length.ToString());
        var xref = data.Position;
        using var rows = new MemoryStream();
        for (var number = 1; number <= finalLength; number++)
        {
            (ulong kind, ulong value, ulong index) entry = number switch
            {
                2 => (2, 3, 0),
                6 => (1, (ulong)xref, 0),
                _ when compressedLength && number == finalLength => (2, 4, 0),
                _ when offsets.TryGetValue(number, out var offset) => (1, (ulong)offset, 0),
                _ => (0, 0, 0)
            };
            foreach (var value in new[] { entry.kind, entry.value, entry.index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(value >> shift));
        }
        Write($"6 0 obj\n<< /Type /XRef /Root 1 0 R /Size {finalLength + 1} /Index [1 {finalLength}] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(repair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }
}
