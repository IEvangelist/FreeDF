using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamRecoveryGenerationTests
{
    internal static byte[] Build(int generation, bool forceRepair, bool objectStream = true)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = data.Position;
        Write("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var container = data.Position;
        const string body = "2 0 << /Type /Compressed >>";
        Write($"3 {generation} obj\n<< /Type /{(objectStream ? "ObjStm" : "Example")} /N 1 /First 4 /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong value, ulong index)
        {
            foreach (var field in new[] { kind, value, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)catalog, 0);
        Row(objectStream ? 2UL : 1UL, objectStream ? 3UL : (ulong)target, 0);
        Row(1, (ulong)container, (ulong)generation);
        Row(1, (ulong)xref, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 5 /Index [1 4] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(99999)]
    public void Nonzero_generation_container_cannot_replace_uncompressed_identity_in_recovery(int generation)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(generation, forceRepair));
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.True(file.Repaired);
            Assert.False(file.Xref[2].Compressed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zero_generation_object_stream_still_supplies_compressed_identity(bool forceRepair)
    {
        var file = PdfFile.Load(Build(0, forceRepair));
        var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.Equal("Compressed", target.Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.True(file.Xref[2].Compressed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(99999)]
    public void Nonzero_generation_ordinary_streams_remain_supported(int generation)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(generation, forceRepair, objectStream: false));
            var stream = Assert.IsType<PdfStream>(file.GetObject(new PdfRef(3, generation)));
            Assert.Equal("Example", stream.Dict.Get("Type").AsName());
            Assert.Equal(Encoding.ASCII.GetBytes("2 0 << /Type /Compressed >>"), stream.RawData);
            Assert.Equal(forceRepair, file.Repaired);
            Assert.False(file.Xref[2].Compressed);
        }
    }
}
