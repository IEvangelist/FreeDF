using System.Text;
using PortableDocument.Core;

namespace AdoBye.Tests;

public class ObjectStreamIndexTests
{
    static PdfFile FileWithObjectStream(string body, int first, int count)
    {
        var bytes = Encoding.ASCII.GetBytes($"%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n" +
            $"2 0 obj << /Type /ObjStm /N {count} /First {first} /Length {body.Length} >> stream\n" +
            body + "\nendstream\nendobj\ntrailer << /Root 1 0 R >>\n%%EOF");
        var file = PdfFile.Load(bytes);
        file.Xref[3] = new(true, true, 2, 0);
        file.Xref[4] = new(true, true, 2, 1);
        return file;
    }

    [Fact]
    public void UnrequestedMalformedMemberDoesNotPreventValidMemberParsing()
    {
        var file = FileWithObjectStream("3 0 4 3 42 (", 8, 2);
        Assert.Equal(42, Assert.IsType<PdfInteger>(file.GetObject(3)).Value);
        Assert.Same(file.GetObject(3), file.GetObject(3));
    }

    [Theory]
    [InlineData("3 -1 4 3 42 (", 9, 2)]
    [InlineData("3 2147483647 4 3 42 (", 17, 2)]
    [InlineData("3 0 4 3 42 (", -1, 2)]
    [InlineData("3 0 4 3 42 (", 8, int.MaxValue)]
    [InlineData("3 0 4 3 42 (", int.MaxValue, 2)]
    public void InvalidHeaderOrMemberOffsetHasExplicitParseDiagnostic(string body, int first, int count)
    {
        var file = FileWithObjectStream(body, first, count);
        Assert.Throws<PdfParseException>(() => file.GetObject(3));
    }
}
