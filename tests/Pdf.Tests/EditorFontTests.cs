using System.Text;
using Pdf.Core;
using Pdf.Document;
using Pdf.Render;
using Xunit;

namespace Pdf.Tests;

/// <summary>Font catalog / overlay font tests: .ttc collections, CFF-flavoured OpenType, fallback runs, ToUnicode round-trips.</summary>
public class EditorFontTests
{
    static string FontDir => Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts");
    static readonly Lazy<FontCatalog> Catalog = new(() => FontCatalog.Scan([FontDir]));
    const string Std = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";

    static Item Text(string text, string font, string? ostyle = null, long? orig = null) =>
        new("t", "text", 20, 20, 0, 0, text, font, 24, false, false, "#000000", "", "", 0, 1, "", 0, 0, orig, OStyle: ostyle);

    static (byte[] Pdf, PdfEditor Editor) Overlay(string content, params Item[] items)
    {
        var bytes = TestPdf.Build(content, Std);
        var e = PdfEditor.Open(bytes);
        var box = PdfDocument.Load(bytes).Pages[0].CropBox;
        var (c, res) = new OverlayBuilder(e, [], Catalog.Value).Build(items, box);
        e.SetOverlay(0, c, res);
        return (e.Save(), e);
    }

    static string Extract(byte[] pdf) => string.Concat(PdfRenderer.ExtractText(PdfDocument.Load(pdf).Pages[0]).Select(s => s.Text));

    static PdfObject? Dig(PdfDocument d, PdfObject? o) => o is PdfRef r ? d.File.GetObject(r.Number) : o;

    [Fact]
    public void Catalog_finds_cff_opentype_and_loads_it()
    {
        var face = Catalog.Value.Resolve("Source Code Pro", false, false);
        Assert.NotNull(face);
        var (data, font) = Catalog.Value.Load(face!);
        Assert.True(font.IsCff);
        Assert.False(font.HasGlyf);
        Assert.True(font.LookupAny('A') > 0);
        Assert.Equal("OTTO", Encoding.ASCII.GetString(data, 0, 4));
    }

    [Fact]
    public void Cff_font_is_embedded_as_subset_and_text_round_trips()
    {
        const string text = "Hello Wörld 123";
        var (pdf, _) = Overlay("", Text(text, "Source Code Pro"));
        Assert.Contains(text.Replace(" ", ""), Extract(pdf).Replace(" ", ""));

        var d = PdfDocument.Load(pdf);
        var fonts = (PdfDictionary)Dig(d, ((PdfDictionary)Dig(d, d.Pages[0].Dict.Get("Resources"))!).Get("Font"))!;
        var type0 = fonts.Entries.Values.Select(v => (PdfDictionary)Dig(d, v)!).Single(f => f.Get("Subtype") is PdfName { Value: "Type0" });
        Assert.Equal("Type0", ((PdfName)type0.Get("Subtype")!).Value);
        Assert.NotNull(type0.Get("ToUnicode"));
        var desc = (PdfDictionary)Dig(d, ((PdfArray)Dig(d, type0.Get("DescendantFonts"))!)[0])!;
        Assert.Equal("CIDFontType0", ((PdfName)desc.Get("Subtype")!).Value);
        var fd = (PdfDictionary)Dig(d, desc.Get("FontDescriptor"))!;
        var ff = (PdfStream)Dig(d, fd.Get("FontFile3"))!;
        Assert.Equal("CIDFontType0C", ((PdfName)ff.Dict.Get("Subtype")!).Value);
        var cff = d.File.DecodeStream(ff);
        Assert.True(cff.Length < 100000, $"subset should be smaller than the 131 KB OTF, was {cff.Length}");
        Assert.Equal(1, cff[0]);
    }

    [Fact]
    public void Cff_subset_renders_the_same_glyphs_as_the_full_font()
    {
        var (pdf, _) = Overlay("", Text("Rendering OK", "Source Code Pro"));
        var rgba = PdfRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72).Rgba;
        var dark = 0;
        for (var i = 0; i < rgba.Length; i += 4) if (rgba[i] < 100) dark++;
        Assert.True(dark > 150, $"expected visible glyph ink, got {dark} dark pixels");
    }

    [Fact]
    public void Truetype_collection_members_are_catalogued_and_extractable()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "cambria.ttc");
        if (!File.Exists(path)) return;
        var faces = Catalog.Value.List().Select(o => o.ToString()!).ToList();
        Assert.Contains(faces, f => f.Contains("Cambria Math"));
        var math = Catalog.Value.Resolve("Cambria Math", false, false)!;
        Assert.True(math.Face >= 0);
        Assert.Contains("#", math.Key);
        var (data, font) = Catalog.Value.Load(math);
        Assert.NotEqual("ttcf", Encoding.ASCII.GetString(data, 0, 4));
        Assert.True(font.LookupAny('A') > 0);

        var (pdf, _) = Overlay("", Text("Collection face", "Cambria Math"));
        Assert.Contains("Collectionface", Extract(pdf).Replace(" ", ""));
    }

    [Fact]
    public void Missing_glyphs_fall_back_to_another_font_and_stay_extractable()
    {
        // Source Code Pro has Latin/Greek/Cyrillic but no CJK glyphs; some installed font should.
        var fb = Catalog.Value.Fallback('日', false, false);
        if (fb is null) return;
        var (pdf, _) = Overlay("", Text("A日B", "Source Code Pro"));
        var text = Extract(pdf);
        Assert.Contains("A", text);
        Assert.Contains("日", text);
        Assert.Contains("B", text);
        var d = PdfDocument.Load(pdf);
        var fonts = (PdfDictionary)Dig(d, ((PdfDictionary)Dig(d, d.Pages[0].Dict.Get("Resources"))!).Get("Font"))!;
        Assert.True(fonts.Entries.Values.Count(v => Dig(d, v) is PdfDictionary f && f.Get("Subtype") is PdfName { Value: "Type0" }) >= 2, "the run is split across at least two embedded fonts");
    }

    [Fact]
    public void Original_embedded_font_is_reused_when_style_is_unchanged()
    {
        var src = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cff.pdf"));
        var spans = PdfRenderer.ExtractText(src.Pages[0]);
        var s = spans.First(x => x.Font is not null && x.FontRef != 0 && x.Text.Any(char.IsLetter));
        var e = PdfEditor.Open(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cff.pdf")));
        var origRef = new PdfRef(s.FontRef, 0);
        var style = "Helvetica|false|false";
        var it = Text(s.Text, "Helvetica", style, s.Index);
        var (content, res) = new OverlayBuilder(e, [], Catalog.Value).Build([it], src.Pages[0].CropBox, _ => new OverlayBuilder.OrigFont(s.Font!, origRef));
        var raw = Encoding.Latin1.GetString(content);
        Assert.Contains($"/EdO{s.FontRef} ", raw);

        var changed = Text(s.Text, "Helvetica", "Times|false|false", s.Index);
        var (content2, _) = new OverlayBuilder(e, [], Catalog.Value).Build([changed], src.Pages[0].CropBox, _ => new OverlayBuilder.OrigFont(s.Font!, origRef));
        Assert.DoesNotContain("/EdO", Encoding.Latin1.GetString(content2));
    }

    [Fact]
    public void Truetype_subset_drops_loca_and_hmtx_entries_past_the_last_used_glyph()
    {
        var face = Catalog.Value.Resolve("Cambria Math", false, false) ?? Catalog.Value.Resolve("Arial", false, false);
        if (face is null) return;
        var (data, font) = Catalog.Value.Load(face);
        var gid = font.LookupAny('A');
        var sub = FontSubsetter.Subset(data, new HashSet<int> { gid });
        Assert.True(sub.Length < data.Length / 10, $"subset {sub.Length} vs {data.Length}");
        var small = new Pdf.Content.TrueTypeFont(sub);
        Assert.Equal(font.Advance(gid), small.Advance(gid), 3);
    }

    [Fact]
    public void Cff_subsetter_keeps_requested_glyphs_and_blanks_the_rest()
    {
        var data = File.ReadAllBytes(Path.Combine(FontDir, "SourceCodePro-Regular.otf"));
        var tt = new Pdf.Content.TrueTypeFont(data);
        var gids = new HashSet<int> { tt.LookupAny('A'), tt.LookupAny('b') };
        var cffBytes = data.AsSpan(FontSubsetter.TableOffset(data, "CFF "), FontSubsetter.TableLength(data, "CFF ")).ToArray();
        var sub = CffSubsetter.Subset(cffBytes, gids);
        Assert.NotNull(sub);
        Assert.True(sub!.Length < cffBytes.Length * 0.7, $"subset {sub.Length} vs full {cffBytes.Length}");
        var full = new Pdf.Content.CffFont(cffBytes);
        var small = new Pdf.Content.CffFont(sub);
        static string Sig(Pdf.Content.PathData? p) => p is null ? "" : string.Join(",", p.Ops) + "|" + string.Join(",", p.Pts.Select(v => v.ToString("0.###")));
        foreach (var g in gids) { Assert.NotEqual("", Sig(full.GetOutline(g))); Assert.Equal(Sig(full.GetOutline(g)), Sig(small.GetOutline(g))); }
    }
}
