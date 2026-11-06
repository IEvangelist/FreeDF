using PortableDocument.Core;
using PortableDocument.Document;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class PdfStringTextEncodingTests
{
    public static IEnumerable<object[]> PdfDocCharacters()
    {
        int[] accents = [0x02D8, 0x02C7, 0x02C6, 0x02D9, 0x02DD, 0x02DB, 0x02DA, 0x02DC];
        int[] symbols = [0x2022, 0x2020, 0x2021, 0x2026, 0x2014, 0x2013, 0x0192, 0x2044,
            0x2039, 0x203A, 0x2212, 0x2030, 0x201E, 0x201C, 0x201D, 0x2018, 0x2019,
            0x201A, 0x2122, 0xFB01, 0xFB02, 0x0141, 0x0152, 0x0160, 0x0178, 0x017D,
            0x0131, 0x0142, 0x0153, 0x0161, 0x017E];
        for (int i = 0; i < accents.Length; i++) yield return [i + 0x18, accents[i]];
        for (int i = 0; i < symbols.Length; i++) yield return [i + 0x80, symbols[i]];
        yield return [0xA0, 0x20AC];
    }

    [Theory]
    [MemberData(nameof(PdfDocCharacters))]
    public void TextUsesPdfDocEncodingWithoutChangingRawBytes(int value, int codepoint)
    {
        byte[] bytes = [(byte)value];
        var text = new PdfString(bytes);
        Assert.Equal(char.ConvertFromUtf32(codepoint), text.Text);
        Assert.Same(bytes, text.Bytes);
        Assert.Equal((byte)value, Assert.Single(text.Bytes));
    }

    [Theory]
    [MemberData(nameof(PdfDocCharacters))]
    public void DocumentTextSurfacesSharePdfDocEncoding(int value, int codepoint)
    {
        string encoded = $"<{value:X2}>", expected = char.ConvertFromUtf32(codepoint);
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R /AcroForm << /Fields [7 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [8 0 R] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>",
            ObjPdf.Stream($"/Span << /ActualText {encoded} >> BDC BT /F1 12 Tf 10 50 Td (Glyph) Tj ET EMC"),
            "<< /Type /Outlines /First 6 0 R /Last 6 0 R /Count 1 >>",
            $"<< /Title {encoded} /Parent 5 0 R /Dest [3 0 R /Fit] >>",
            $"<< /FT /Tx /T {encoded} /V {encoded} >>",
            $"<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents {encoded} >>"));
        Assert.Equal(expected, Assert.Single(doc.GetOutlines()).Title);
        Assert.Equal(expected, Assert.Single(doc.GetFormFields()).FullName);
        Assert.Equal(expected, Assert.Single(doc.GetFormFields()).Value);
        Assert.Equal(expected, Assert.Single(doc.GetFormFieldDetails().Fields).PartialName!.Text);
        Assert.Equal(expected, Assert.Single(doc.Pages[0].GetAnnotations()).Contents);
        Assert.Equal(expected, Assert.Single(DocumentRenderer.ExtractText(doc.Pages[0], false, true)).Text);
        Assert.Equal("Glyph", Assert.Single(DocumentRenderer.ExtractText(doc.Pages[0])).Text);
    }

    [Theory]
    [InlineData("FEFF20AC", "\u20AC")]
    [InlineData("FFFEAC20", "\u20AC")]
    [InlineData("EFBBBFE282AC", "\u20AC")]
    [InlineData("", "")]
    [InlineData("41424320E9FF", "ABC \u00E9\u00FF")]
    [InlineData("007F9FAD", "\0\u007F\u009F\u00AD")]
    public void ExistingBomAsciiLatinAndUndefinedByteBehaviorIsPreserved(string hex, string expected) =>
        Assert.Equal(expected, PdfString.Decode(Convert.FromHexString(hex)));
}
