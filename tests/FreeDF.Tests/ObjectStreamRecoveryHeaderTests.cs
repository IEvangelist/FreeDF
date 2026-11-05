using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamRecoveryHeaderTests
{
    internal static byte[] Build(string header, string body, int count = 1, int? first = null)
    {
        var stream = header + body;
        return Encoding.ASCII.GetBytes("%PDF-1.7\n" +
            "1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Target >>\nendobj\n" +
            $"3 0 obj\n<< /Type /ObjStm /N {count} /First {first ?? header.Length} /Length {stream.Length} >>\nstream\n" +
            stream + "\nendstream\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
    }

    [Theory]
    [InlineData("4294967298 0 ")]
    [InlineData("-4294967294 0 ")]
    [InlineData("9223372036854775807 0 ")]
    [InlineData("-1 0 ")]
    [InlineData("2 -1 ")]
    [InlineData("2 2147483647 ")]
    [InlineData("2 4294967296 ")]
    [InlineData("2 /Bad ")]
    public void Invalid_header_cannot_replace_an_uncompressed_identity(string header)
    {
        var file = PdfFile.Load(Build(header, "<< /Type /Compressed >>"));
        Assert.True(file.Repaired);
        Assert.False(file.Xref[2].Compressed);
        Assert.False(file.Xref.ContainsKey(-1));
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Theory]
    [InlineData("2 0 2 3 ")]
    [InlineData("2 0 /Bad 3 ")]
    [InlineData("2 0 5 /Bad ")]
    [InlineData("2 0 5 -1 ")]
    [InlineData("2 0 5 2147483647 ")]
    public void Invalid_later_header_does_not_install_a_partial_index(string header)
    {
        var file = PdfFile.Load(Build(header, "42 43", count: 2));
        Assert.True(file.Repaired);
        Assert.False(file.Xref[2].Compressed);
        Assert.False(file.Xref.ContainsKey(5));
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe"))).Get("Type").AsName());
    }

    [Fact]
    public void Header_pairs_must_fit_before_first()
    {
        var file = PdfFile.Load(Build("2 0000000 ", "<< /Type /Compressed >>", first: 4));
        Assert.True(file.Repaired);
        Assert.False(file.Xref[2].Compressed);
    }

    [Fact]
    public void Valid_member_recovery_keeps_on_demand_body_isolation()
    {
        var file = PdfFile.Load(Build("2 0 5 3 ", "42 (", count: 2));
        Assert.True(file.Repaired);
        Assert.True(file.Xref[2].Compressed);
        Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(2)).Value);
        Assert.Same(file.GetObject(2), file.GetObject(2));
        Assert.True(file.Xref[5].Compressed);
    }

    [Fact]
    public void Empty_object_stream_does_not_replace_uncompressed_entries()
    {
        var file = PdfFile.Load(Build("", "", count: 0));
        Assert.True(file.Repaired);
        Assert.False(file.Xref[2].Compressed);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }
}
