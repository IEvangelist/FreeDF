using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class GlyphNameTests
{
    [Theory]
    [InlineData("glyphlist.txt", 4281, "A3B2F61CED9F3644CC0D4ECDE5C59DF34CA286C689D9484A43A710A81C466789")]
    [InlineData("zapfdingbats.txt", 201, "F6394E3CB8A447E84A1DAD75D4BAAF2AA7F45DC104FAF369F4720E1A774EF2DC")]
    public void Every_official_mapping_is_embedded_unmodified_and_resolves(string file, int count, string sha256)
    {
        using var resource = typeof(PdfFont).Assembly.GetManifestResourceStream("PortableDocument.Content.Resources." + file);
        Assert.NotNull(resource);
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        var entries = Encoding.ASCII.GetString(bytes.ToArray()).Split('\n')
            .Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0 && line[0] != '#').ToArray();
        Assert.Equal(count, entries.Length);
        foreach (var entry in entries)
        {
            var parts = entry.Split(';');
            var expected = string.Concat(parts[1].Split(' ').Select(value =>
                char.ConvertFromUtf32(int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            var actual = file == "glyphlist.txt" ? PdfFont.NameToUnicode(parts[0]) :
                Font(parts[0], "ZapfDingbats").ToUnicode(128);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData("Lcommaaccent", "\u013B")]
    [InlineData("afii10017", "\u0410")]
    [InlineData("Asmall", "\uF761")]
    [InlineData("dalethatafpatah", "\u05D3\u05B2")]
    [InlineData("lamedholam", "\u05DC\u05B9")]
    [InlineData("fi", "\uFB01")]
    [InlineData("f_f_i", "ffi")]
    [InlineData("uni20AC0308", "\u20AC\u0308")]
    [InlineData("u1040C", "\U0001040C")]
    [InlineData("Lcommaaccent_uni20AC0308_u1040C.alternate", "\u013B\u20AC\u0308\U0001040C")]
    [InlineData("A.swash_h", "A")]
    [InlineData("A_B.swash.more", "AB")]
    [InlineData("_A__unknown_B_", "AB")]
    [InlineData("uni0041D800_B", "B")]
    [InlineData("u000041", "A")]
    [InlineData("uni0000", "\0")]
    [InlineData("uD7FF", "\uD7FF")]
    [InlineData("uE000", "\uE000")]
    [InlineData("u10FFFF", "\U0010FFFF")]
    public void Adobe_algorithm_preserves_complete_sequences(string name, string expected) =>
        Assert.Equal(expected, PdfFont.NameToUnicode(name));

    [Theory]
    [InlineData("")]
    [InlineData(".notdef")]
    [InlineData(".")]
    [InlineData("1")]
    [InlineData("unknown")]
    [InlineData("unknown_other")]
    [InlineData("a1")]
    [InlineData("uni")]
    [InlineData("uni004")]
    [InlineData("uni00410")]
    [InlineData("uni0041junk")]
    [InlineData("uni20ac")]
    [InlineData("uniD801DC0C")]
    [InlineData("uni0041DFFF")]
    [InlineData("uD800")]
    [InlineData("uDFFF")]
    [InlineData("u110000")]
    [InlineData("uFFFFFF")]
    [InlineData("u1000000")]
    [InlineData("u123")]
    [InlineData("u1f600")]
    [InlineData("u+0041")]
    [InlineData("u 041")]
    [InlineData("Uni0041")]
    [InlineData("U0041")]
    public void Invalid_components_are_unmapped_without_throwing(string name) =>
        Assert.Null(PdfFont.NameToUnicode(name));

    [Theory]
    [InlineData("Helvetica", "A")]
    [InlineData("ZapfDingbats", "\u2701\u2703A")]
    [InlineData("ABCDEF+ZapfDingbats", "\u2701\u2703A")]
    public void Dingbats_mapping_requires_font_context(string baseFont, string expected) =>
        Assert.Equal(expected, Font("a1_a202_A.alt", baseFont).ToUnicode(128));

    [Fact]
    public void Simple_font_differences_extract_complete_text_without_changing_glyph_advances()
    {
        const string names = "/afii10017 /dalethatafpatah /f_f_i /uni20AC0308 /u1040C /A.swash";
        var page = PdfDocument.Load(TestPdf.Build("BT /F1 10 Tf 10 100 Td <808182838485> Tj ET",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
            "/FirstChar 128 /Widths [600 600 600 600 600 600] " +
            $"/Encoding << /BaseEncoding /WinAnsiEncoding /Differences [128 {names}] >> >> >>")).Pages[0];
        var spans = DocumentRenderer.ExtractText(page);
        Assert.Equal("\u0410\u05D3\u05B2ffi\u20AC\u0308\U0001040CA", string.Concat(spans.Select(s => s.Text)));
        var glyphs = spans.SelectMany(s => s.Pieces!).SelectMany(p => p.Glyphs ?? []).ToArray();
        Assert.Equal(6, glyphs.Length);
        Assert.All(glyphs, glyph =>
        {
            Assert.Equal(-600d, glyph.Adv);
            Assert.Equal(6d, glyph.X1 - glyph.X0);
        });
        Assert.Equal(new byte[] { 128, 129, 130, 131, 132, 133 }, glyphs.SelectMany(g => g.Bytes));
    }

    [Fact]
    public void Simple_font_extraction_uses_dingbats_context()
    {
        var page = PdfDocument.Load(TestPdf.Build("BT /F1 10 Tf <8081> Tj ET",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats " +
            "/Encoding << /Differences [128 /a1 /a202.alt] >> >> >>")).Pages[0];
        Assert.Equal("\u2701\u2703", string.Concat(DocumentRenderer.ExtractText(page).Select(s => s.Text)));
    }

    [Fact]
    public void Explicit_ToUnicode_still_takes_precedence_over_glyph_names()
    {
        var font = Font("dalethatafpatah");
        var cmap = "1 beginbfchar <80> <0058> endbfchar";
        font.Dict.Entries["ToUnicode"] = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(cmap));
        var resolved = new PdfFont(PdfDocument.Load(TestPdf.Build("")).File, font.Dict, new NoFallback());
        Assert.Equal("X", resolved.ToUnicode(128));
    }

    [Fact]
    public void Repeated_table_lookups_reuse_text_without_allocating()
    {
        for (var i = 0; i < 100; i++) PdfFont.NameToUnicode("dalethatafpatah");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var length = 0;
        for (var i = 0; i < 1000; i++) length += PdfFont.NameToUnicode("dalethatafpatah")!.Length;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(2000, length);
        Assert.Equal(0, allocated);
    }

    static PdfFont Font(string name, string baseFont = "Helvetica")
    {
        var dict = Assert.IsType<PdfDictionary>(new PdfLexer(Encoding.ASCII.GetBytes(
            $"<< /Subtype /Type1 /BaseFont /{baseFont} /Encoding << /Differences [128 /{name}] >> >>")).ReadObject(false));
        return new PdfFont(PdfDocument.Load(TestPdf.Build("")).File, dict, new NoFallback());
    }

    sealed class NoFallback : IFontFallback
    {
        public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif) => null;
    }
}
