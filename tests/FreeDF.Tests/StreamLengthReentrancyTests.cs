using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class StreamLengthReentrancyTests
{
    static byte[] Build(string variant, bool repair)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var offsets = new long[6];
        void Object(int number, string value)
        {
            offsets[number] = data.Position;
            Write($"{number} 0 obj\n{value}\nendobj\n");
        }
        Object(1, "<< /Type /Catalog /Probe 3 0 R >>");
        Object(2, "<< /Type /Unrelated >>");
        var length = variant switch { "self" => "3 0 R", "direct" => "3", _ => "5 0 R" };
        Object(3, $"<< /Length {length} >>\nstream\nabc\nendstream");
        Object(4, "3");
        Object(5, variant switch
        {
            "mutual" => "<< /Length 3 0 R >>\nstream\nxyz\nendstream",
            "chain" => "3 0 R",
            "valid-chain" => "4 0 R",
            _ => "3"
        });
        var xref = data.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");
        for (var n = 1; n < 6; n++) Write($"{offsets[n]:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{(repair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData("self", false)]
    [InlineData("self", true)]
    [InlineData("chain", false)]
    [InlineData("chain", true)]
    [InlineData("mutual", false)]
    [InlineData("mutual", true)]
    public void Cyclic_stream_lengths_fail_explicitly_without_poisoning_later_lookups(string variant, bool repair)
    {
        var file = PdfFile.Load(Build(variant, repair));
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal("cyclic indirect object loading", Assert.Throws<PdfParseException>(() => file.GetObject(3)).Message);
        Assert.True(file.Repaired);
        Assert.Equal("Unrelated", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(3, Assert.IsType<PdfInteger>(file.GetObject(4)).Value);
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    [InlineData("valid", false)]
    [InlineData("valid", true)]
    [InlineData("valid-chain", false)]
    [InlineData("valid-chain", true)]
    public void Acyclic_direct_and_indirect_lengths_preserve_stream_bytes_and_cache(string variant, bool repair)
    {
        var file = PdfFile.Load(Build(variant, repair));
        var stream = Assert.IsType<PdfStream>(file.GetObject(3));
        Assert.Equal("abc"u8.ToArray(), stream.RawData);
        Assert.Same(stream, file.GetObject(3));
        Assert.Equal(repair, file.Repaired);
    }
}
