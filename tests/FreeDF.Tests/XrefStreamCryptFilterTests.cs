using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamCryptFilterTests
{
    [Theory]
    [InlineData("/Crypt", false)]
    [InlineData("[/Crypt]", false)]
    [InlineData("/#43rypt", false)]
    [InlineData("[/Crypt /FlateDecode]", true)]
    [InlineData("[/FlateDecode /Crypt]", true)]
    [InlineData("[/Crypt /Crypt]", false)]
    public void Crypt_filter_in_cross_reference_stream_requires_recovery(string filter, bool flate)
    {
        var file = PdfFile.Load(Build(filter, flate));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("null", false)]
    [InlineData("/FlateDecode", true)]
    [InlineData("[/FlateDecode]", true)]
    public void Cross_reference_streams_without_Crypt_keep_indexed_loading(string? filter, bool flate)
    {
        var file = PdfFile.Load(Build(filter, flate));
        Assert.False(file.Repaired);
        AssertTarget(file);
        Assert.False(file.Repaired);
    }

    [Theory]
    [InlineData("/Crypt", false)]
    [InlineData("[/Crypt /FlateDecode]", true)]
    [InlineData("[/FlateDecode /Crypt]", true)]
    public void Ordinary_stream_Crypt_passthrough_is_unchanged(string filter, bool flate)
    {
        var bytes = "ordinary identity-filter recovery"u8.ToArray();
        var dictionary = (PdfDictionary)new PdfLexer(Encoding.ASCII.GetBytes($"<< /Filter {filter} >>")).ReadObject()!;
        var filters = PdfFilters.GetFilters(dictionary, item => item);
        Assert.Equal(bytes, PdfFilters.Decode(flate ? PdfFilters.DeflateEncode(bytes) : bytes, filters, item => item).Data);
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }

    internal static byte[] Build(string? filter, bool flate, string? decodeParms = null, string? indirectObject = null,
        Func<byte[], byte[]>? encode = null)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var root = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = data.Position;
        Write("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var indirectOffset = data.Position;
        if (indirectObject is not null) Write($"4 0 obj\n{indirectObject}\nendobj\n");
        var xref = data.Position;
        using var rows = new MemoryStream();
        var offsets = new List<long> { root, target, xref };
        if (indirectObject is not null) offsets.Add(indirectOffset);
        foreach (var offset in offsets)
        {
            rows.WriteByte(1);
            for (var shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(offset >> shift));
            rows.WriteByte(0);
            rows.WriteByte(0);
        }
        var payload = flate ? PdfFilters.DeflateEncode(rows.ToArray()) : rows.ToArray();
        if (encode is not null) payload = encode(payload);
        var entry = filter is null ? "" : $"/Filter {filter}";
        var parameters = decodeParms is null ? "" : $"/DecodeParms {decodeParms}";
        Write($"3 0 obj\n<< /Type /XRef /Root 1 0 R /Size {offsets.Count + 1} /Index [1 {offsets.Count}] " +
            $"/W [1 4 2] {entry} {parameters} " +
            $"/Length {payload.Length} >>\nstream\n");
        data.Write(payload);
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return data.ToArray();
    }
}
