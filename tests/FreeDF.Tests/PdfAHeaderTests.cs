using System.Text;
using PortableDocument.Document;

namespace FreeDF.Tests;

/// <summary>ISO 19005-1/2/3 6.1.2: file header position and the binary comment (veraPDF PDFA-1B/2B/3B 6.1.2 tests 1 and 2).</summary>
public class PdfAHeaderTests
{
    static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    static byte[] Minimal() => ObjPdf.Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>");

    static ValidationReport Validate(byte[] pdf, PdfValidationProfile p) => PdfValidator.Validate(PdfDocument.Load(pdf), p);

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void BinaryCommentPresent_IsAccepted(PdfValidationProfile profile)
    {
        var r = Validate(Minimal(), profile);
        Assert.False(r.Has("PDFA-HEADER-BINARY"));
        Assert.False(r.Has("PDFA-HEADER-OFFSET"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MissingBinaryComment_IsReported(PdfValidationProfile profile)
    {
        var pdf = Encoding.Latin1.GetBytes(Text(Minimal()).Replace("%\u00E2\u00E3\u00CF\u00D3\n", "%abcd\n"));
        Assert.True(Validate(pdf, profile).Has("PDFA-HEADER-BINARY"));
    }

    [Fact]
    public void CommentWithFewerThanFourHighBytes_IsReported()
    {
        var pdf = Encoding.Latin1.GetBytes(Text(Minimal()).Replace("%\u00E2\u00E3\u00CF\u00D3\n", "%\u00E2\u00E3\u00CFx\n"));
        Assert.True(Validate(pdf, PdfValidationProfile.PdfA2b).Has("PDFA-HEADER-BINARY"));
    }

    [Fact]
    public void CarriageReturnLineEndingIsAccepted()
    {
        var pdf = Encoding.Latin1.GetBytes(Text(Minimal()).Replace("%PDF-1.7\n", "%PDF-1.7\r"));
        Assert.False(Validate(pdf, PdfValidationProfile.PdfA2b).Has("PDFA-HEADER-BINARY"));
    }

    [Fact]
    public void LeadingBytesBeforeTheHeader_AreReported()
    {
        var pdf = Encoding.Latin1.GetBytes("junk\n" + Text(Minimal()));
        Assert.True(Validate(pdf, PdfValidationProfile.PdfA2b).Has("PDFA-HEADER-OFFSET"));
    }

    [Fact]
    public void WriterOutputHasTheBinaryComment()
    {
        var ed = PdfEditor.Create();
        ed.ImportPages(PdfDocument.Load(Minimal()));
        var r = Validate(ed.Save(), PdfValidationProfile.PdfA2b);
        Assert.False(r.Has("PDFA-HEADER-BINARY"));
    }
}
