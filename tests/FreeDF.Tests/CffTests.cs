using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

public class CffTests
{
    static bool Dark(RenderedPage p, int x, int y) => p.Rgba[(y * p.Width + x) * 4] < 64;

    [Fact]
    public void Embedded_cff_glyphs_render_for_bare_and_opentype_wrappers()
    {
        var doc = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cff.pdf"));
        var p = DocumentRenderer.RenderPage(doc.Pages[0], 72);
        // Font size 50: A is a 30x35 square at x 10..40, B a blob at x 45..75; baselines y=20 and y=120 (PDF), flipped to device.
        foreach (var baseline in new[] { 20, 120 })
        {
            var cy = 200 - baseline - 17;
            Assert.True(Dark(p, 25, cy), $"A missing at baseline {baseline}");
            Assert.True(Dark(p, 60, cy), $"B missing at baseline {baseline}");
            Assert.False(Dark(p, 46, 200 - baseline - 1), "blob corner should be empty");
            Assert.False(Dark(p, 100, cy));
        }
    }

    [Theory]
    [InlineData("ExpertBare")]
    [InlineData("ExpertOpenType")]
    public void Embedded_expert_encoding_selects_expert_charset_glyphs(string resourceName)
    {
        var doc = ExpertFixture();
        var font = Font(doc, resourceName);
        var cff = EmbeddedCff(doc, font);

        Assert.Equal(5, cff.GlyphCount);
        Assert.Equal("exclamsmall", cff.GlyphName(2));
        Assert.Equal("Hungarumlautsmall", cff.GlyphName(3));
        Assert.Equal(2, cff.GlyphByCode(33));
        Assert.Equal(3, cff.GlyphByCode(34));
        Assert.Equal(PdfFont.NameToUnicode("exclamsmall"), font.ToUnicode(33));
        Assert.Equal(PdfFont.NameToUnicode("Hungarumlautsmall"), font.ToUnicode(34));
        Assert.Equal("", font.ToUnicode(35));
        var exclam = Assert.IsType<PathData>(font.GetGlyph(33));
        var hungarumlaut = Assert.IsType<PathData>(font.GetGlyph(34));
        Assert.NotEqual(exclam.Pts[2], hungarumlaut.Pts[2]);
        Assert.Equal(0.51, font.Decode([33]).Single().Width, 2);
        Assert.Equal(0.62, font.Decode([34]).Single().Width, 2);

        var unicode = PdfFont.NameToUnicode("exclamsmall")!;
        Assert.True(font.TryEncode(char.ConvertToUtf32(unicode, 0), out var bytes, out var width));
        Assert.Equal(new byte[] { 33 }, bytes);
        Assert.Equal(0.51, width, 2);
        Assert.False(font.TryEncode('#', out _, out _));
    }

    [Theory]
    [InlineData("SubsetBare")]
    [InlineData("SubsetOpenType")]
    public void Embedded_expert_subset_uses_its_predefined_charset_and_reports_missing_glyphs(string resourceName)
    {
        var doc = ExpertFixture();
        var font = Font(doc, resourceName);
        var cff = EmbeddedCff(doc, font);

        Assert.Equal(4, cff.GlyphCount);
        Assert.Equal("dollaroldstyle", cff.GlyphName(2));
        Assert.Equal("dollarsuperior", cff.GlyphName(3));
        Assert.Equal(0, cff.GlyphByCode(33));
        Assert.Equal(2, cff.GlyphByCode(36));
        Assert.Equal("", font.ToUnicode(33));
        Assert.Null(font.GetGlyph(33));
        Assert.Equal(PdfFont.NameToUnicode("dollaroldstyle"), font.ToUnicode(36));
        Assert.NotNull(font.GetGlyph(36));
        Assert.Equal(0.74, font.Decode([36]).Single().Width, 2);
    }

    [Fact]
    public void Differences_override_the_embedded_encoding_and_explicit_base_encoding_is_not_replaced()
    {
        var doc = ExpertFixture();
        var differences = Font(doc, "ExpertDifferences");
        Assert.Equal(PdfFont.NameToUnicode("dollaroldstyle"), differences.ToUnicode(33));
        Assert.Equal(PdfFont.NameToUnicode("Hungarumlautsmall"), differences.ToUnicode(34));
        Assert.Equal(0.74, differences.Decode([33]).Single().Width, 2);
        Assert.Equal(0.62, differences.Decode([34]).Single().Width, 2);
        Assert.True(differences.TryEncode(0xF724, out var differenceBytes, out var differenceWidth));
        Assert.Equal(new byte[] { 33 }, differenceBytes);
        Assert.Equal(0.74, differenceWidth, 2);

        var standard = Font(doc, "ExpertStandard");
        Assert.Equal(PdfFont.NameToUnicode("dollaroldstyle"), standard.ToUnicode(33));
        Assert.NotNull(standard.GetGlyph(33));
        Assert.Null(standard.GetGlyph(34));

        var macExpert = Font(doc, "ExpertMac");
        Assert.Equal(PdfFont.NameToUnicode("exclamsmall"), macExpert.ToUnicode(33));
        Assert.Equal(PdfFont.NameToUnicode("Hungarumlautsmall"), macExpert.ToUnicode(34));
        Assert.Equal("", macExpert.ToUnicode(127));
        Assert.NotNull(macExpert.GetGlyph(33));
        Assert.NotNull(macExpert.GetGlyph(34));
        Assert.Null(macExpert.GetGlyph(127));
        Assert.Equal(0.51, macExpert.Decode([33]).Single().Width, 2);
        Assert.Equal(0.62, macExpert.Decode([34]).Single().Width, 2);
        var macExpertUnicode = PdfFont.NameToUnicode("exclamsmall")!;
        Assert.True(macExpert.TryEncode(char.ConvertToUtf32(macExpertUnicode, 0), out var macExpertBytes, out var macExpertWidth));
        Assert.Equal(new byte[] { 33 }, macExpertBytes);
        Assert.Equal(0.51, macExpertWidth, 2);
        Assert.False(macExpert.TryEncode('#', out _, out _));

        var macExpertDifferences = Font(doc, "MacExpertDifferences");
        Assert.Equal(PdfFont.NameToUnicode("dollaroldstyle"), macExpertDifferences.ToUnicode(33));
        Assert.Equal(PdfFont.NameToUnicode("Hungarumlautsmall"), macExpertDifferences.ToUnicode(34));
        Assert.NotNull(macExpertDifferences.GetGlyph(33));
        Assert.Equal(0.74, macExpertDifferences.Decode([33]).Single().Width, 2);
        Assert.True(macExpertDifferences.TryEncode(0xF724, out var macExpertDifferenceBytes, out var macExpertDifferenceWidth));
        Assert.Equal(new byte[] { 33 }, macExpertDifferenceBytes);
        Assert.Equal(0.74, macExpertDifferenceWidth, 2);

        var macExpertToUnicode = Font(doc, "MacExpertToUnicode");
        Assert.Equal("X", macExpertToUnicode.ToUnicode(33));
        Assert.NotNull(macExpertToUnicode.GetGlyph(33));
        Assert.True(macExpertToUnicode.TryEncode('X', out var macExpertToUnicodeBytes, out _));
        Assert.Equal(new byte[] { 33 }, macExpertToUnicodeBytes);
    }

    [Fact]
    public void Explicit_ToUnicode_overrides_expert_glyph_names_without_changing_glyph_selection()
    {
        var doc = ExpertFixture();
        var font = Font(doc, "ExpertToUnicode");

        Assert.Equal("X", font.ToUnicode(33));
        Assert.Equal(0.51, font.Decode([33]).Single().Width, 2);
        Assert.NotNull(font.GetGlyph(33));
        Assert.True(font.TryEncode('X', out var bytes, out _));
        Assert.Equal(new byte[] { 33 }, bytes);
    }

    [Theory]
    [InlineData("IsoBare")]
    [InlineData("IsoOpenType")]
    public void Embedded_standard_encoding_uses_the_predefined_iso_adobe_charset(string resourceName)
    {
        var doc = ExpertFixture();
        var font = Font(doc, resourceName);
        var cff = EmbeddedCff(doc, font);

        Assert.Equal(4, cff.GlyphCount);
        Assert.Equal("exclam", cff.GlyphName(2));
        Assert.Equal("quotedbl", cff.GlyphName(3));
        Assert.Equal(2, cff.GlyphByCode(33));
        Assert.Equal(3, cff.GlyphByCode(34));
        Assert.Equal("!", font.ToUnicode(33));
        Assert.Equal("\"", font.ToUnicode(34));
        Assert.Equal(0.48, font.Decode([33]).Single().Width, 2);
        Assert.Equal(0.52, font.Decode([34]).Single().Width, 2);
    }

    static PdfDocument ExpertFixture() =>
        PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cff-expert.pdf"));

    static PdfFont Font(PdfDocument doc, string resourceName)
    {
        var fonts = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.Pages[0].Resources.Get("Font")));
        var dict = Assert.IsType<PdfDictionary>(doc.File.Resolve(fonts.Get(resourceName)));
        return new PdfFont(doc.File, dict, new NoFallback());
    }

    static CffFont EmbeddedCff(PdfDocument doc, PdfFont font)
    {
        var descriptor = Assert.IsType<PdfDictionary>(doc.File.Resolve(font.Dict.Get("FontDescriptor")));
        var stream = Assert.IsType<PdfStream>(doc.File.Resolve(descriptor.Get("FontFile3")));
        return new CffFont(doc.File.DecodeStream(stream));
    }

    sealed class NoFallback : IFontFallback
    {
        public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif) => null;
    }
}
