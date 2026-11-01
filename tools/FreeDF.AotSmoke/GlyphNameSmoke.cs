using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;

static class GlyphNameSmoke
{
    public static void Run(PdfFile file)
    {
        if (PdfFont.NameToUnicode("afii10017") != "\u0410" ||
            PdfFont.NameToUnicode("dalethatafpatah") != "\u05D3\u05B2" ||
            PdfFont.NameToUnicode("Lcommaaccent_uni20AC0308_u1040C.alternate") != "\u013B\u20AC\u0308\U0001040C" ||
            PdfFont.NameToUnicode("uniD801DC0C") is not null)
            throw new InvalidOperationException("NativeAOT Adobe glyph list mapping failed.");

        var dict = (PdfDictionary)new PdfLexer(Encoding.ASCII.GetBytes(
            "<< /Subtype /Type1 /BaseFont /ZapfDingbats /Encoding << /Differences [128 /a1_a202.alt] >> >>")).ReadObject(false)!;
        if (new PdfFont(file, dict, new NoFallback()).ToUnicode(128) != "\u2701\u2703")
            throw new InvalidOperationException("NativeAOT Zapf Dingbats resource mapping failed.");
    }

    sealed class NoFallback : IFontFallback
    {
        public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif) => null;
    }
}
