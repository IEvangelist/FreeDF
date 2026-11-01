using System.Globalization;
using System.Text;
using System.Text.Json;
using PortableDocument.Content;
using PortableDocument.Content.Shaping;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

/// <summary>
/// OpenType shaping, bidi and line breaking. Glyph sequences are compared with HarfBuzz (fixtures/shaping/hb-expected.json,
/// produced by generate_expected.py with uharfbuzz) using small OFL subsets of Source Sans 3 and Noto Sans Arabic/Hebrew/Devanagari.
/// </summary>
public class ShapingTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "shaping");
    static readonly Dictionary<string, TrueTypeFont> Fonts = [];

    static TrueTypeFont Font(string file)
    {
        lock (Fonts)
        {
            if (!Fonts.TryGetValue(file, out var f)) Fonts[file] = f = new TrueTypeFont(File.ReadAllBytes(Path.Combine(Dir, file)));
            return f;
        }
    }

    static TrueTypeFont Latin => Font("SourceSans3-subset.otf");
    static TrueTypeFont Arabic => Font("NotoSansArabic-subset.ttf");
    static TrueTypeFont Hebrew => Font("NotoSansHebrew-subset.ttf");
    static TrueTypeFont Deva => Font("NotoSansDevanagari-subset.ttf");

    static ShapedRun Shape(TrueTypeFont f, string text, bool rtl, ScriptId script, ShapingOptions? o = null) => TextShaper.Shape(f, text, 0, text.Length, rtl, script, o);

    // ---------- HarfBuzz parity ----------

    /// <summary>Cases where the C# shaper is known to differ from HarfBuzz, with the reason. Everything else must match exactly.</summary>
    static readonly Dictionary<string, string> KnownDivergences = new()
    {
    };

    public static IEnumerable<object[]> HbCases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "hb-expected.json")));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray()) yield return [c.GetProperty("id").GetString()!];
    }

    [Theory]
    [MemberData(nameof(HbCases))]
    public void Matches_harfbuzz_glyph_sequence(string id)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "hb-expected.json")));
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        var font = Font(c.GetProperty("font").GetString()!);
        var text = string.Concat(c.GetProperty("text").EnumerateArray().Select(x => char.ConvertFromUtf32(x.GetInt32())));
        var rtl = c.GetProperty("direction").GetString() == "rtl";
        var script = c.GetProperty("script").GetString() switch { "Arab" => ScriptId.Arabic, "Hebr" => ScriptId.Hebrew, "Deva" => ScriptId.Devanagari, _ => ScriptId.Latin };
        var features = c.GetProperty("features").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        var lang = c.GetProperty("language").ValueKind == JsonValueKind.Null ? null : c.GetProperty("language").GetString();
        var upem = c.GetProperty("upem").GetInt32();
        var run = Shape(font, text, rtl, script, new ShapingOptions { Language = lang, Features = features.Count == 0 ? null : features });

        var cpIndex = new Dictionary<int, int>();
        var cpn = 0;
        for (var i = 0; i < text.Length; i += char.IsHighSurrogate(text[i]) ? 2 : 1) cpIndex[i] = cpn++;
        var expected = c.GetProperty("glyphs").EnumerateArray().ToList();
        var actual = string.Join(' ', run.Glyphs.Select(g => $"{g.GlyphId}@{cpIndex.GetValueOrDefault(g.TextStart, -1)}"));
        var want = string.Join(' ', expected.Select(g => $"{g.GetProperty("gid").GetInt32()}@{g.GetProperty("cluster").GetInt32()}"));
        if (KnownDivergences.ContainsKey(id)) return;
        Assert.Equal(want, actual);
        for (var i = 0; i < expected.Count; i++)
        {
            double S(double v) => v * upem / 1000.0;
            Assert.InRange(S(run.Glyphs[i].XAdvance) - expected[i].GetProperty("xadv").GetInt32(), -1.01, 1.01);
            Assert.InRange(S(run.Glyphs[i].XOffset) - expected[i].GetProperty("xoff").GetInt32(), -1.01, 1.01);
            Assert.InRange(S(run.Glyphs[i].YOffset) - expected[i].GetProperty("yoff").GetInt32(), -1.01, 1.01);
        }
    }

    // ---------- GSUB / GPOS behaviour ----------

    [Fact]
    public void Ligatures_form_and_can_be_disabled()
    {
        var on = Shape(Latin, "office", false, ScriptId.Latin);
        var off = Shape(Latin, "office", false, ScriptId.Latin, new ShapingOptions { Features = new Dictionary<string, int> { ["liga"] = 0, ["clig"] = 0 } });
        Assert.True(on.Glyphs.Length < off.Glyphs.Length);
        var lig = on.Glyphs.First(g => g.IsLigature);
        Assert.True(lig.TextLength >= 2);
        Assert.Equal(6, off.Glyphs.Length);
    }

    [Fact]
    public void Pair_kerning_changes_advances_and_can_be_disabled()
    {
        var kerned = Shape(Latin, "AV", false, ScriptId.Latin);
        var plain = Shape(Latin, "AV", false, ScriptId.Latin, new ShapingOptions { Kerning = false, Features = new Dictionary<string, int> { ["kern"] = 0 } });
        Assert.True(kerned.Advance < plain.Advance - 5);
        Assert.Equal(plain.Glyphs.Sum(g => g.XAdvance), plain.Advance, 3);
    }

    [Fact]
    public void Combining_mark_is_positioned_by_gpos_and_has_zero_advance()
    {
        var run = Shape(Latin, "x\u0301", false, ScriptId.Latin);
        Assert.Equal(2, run.Glyphs.Length);
        Assert.Equal(0, run.Glyphs[1].XAdvance);
        Assert.NotEqual(0, run.Glyphs[1].GlyphId);
    }

    [Fact]
    public void Feature_coverage_is_reported_per_script()
    {
        var latin = Latin.Layout.FeaturesFor(["latn"]);
        Assert.Contains("kern", latin);
        var arabic = Arabic.Layout.FeaturesFor(["arab"]);
        Assert.Contains("init", arabic);
        Assert.Contains("fina", arabic);
        Assert.Contains("rlig", arabic);
        var deva = Deva.Layout.FeaturesFor(["dev2"]);
        Assert.Contains("rphf", deva);
        Assert.Contains("half", deva);
    }

    // ---------- scripts ----------

    [Fact]
    public void Arabic_letters_take_positional_forms_in_visual_order()
    {
        var text = "\u0645\u0631\u062d\u0628\u0627";
        var run = Shape(Arabic, text, true, ScriptId.Arabic);
        Assert.Equal(6, run.Glyphs.Length); // one letter splits into a base and a dot mark (ccmp)
        Assert.True(run.RightToLeft);
        // visual order: the first logical letter is the right-most glyph
        Assert.Equal(0, run.Glyphs[^1].TextStart);
        Assert.Equal(4, run.Glyphs[0].TextStart);
        var isolated = Shape(Arabic, "\u0645", true, ScriptId.Arabic).Glyphs[0].GlyphId;
        Assert.NotEqual(isolated, run.Glyphs[^1].GlyphId); // initial form differs from isolated
    }

    [Fact]
    public void Arabic_lam_before_alef_uses_its_contextual_form()
    {
        var run = Shape(Arabic, "\u0644\u0627", true, ScriptId.Arabic);
        Assert.Equal(2, run.Glyphs.Length);
        var isolatedLam = Shape(Arabic, "\u0644", true, ScriptId.Arabic).Glyphs[0].GlyphId;
        Assert.NotEqual(isolatedLam, run.Glyphs[^1].GlyphId);
    }

    [Fact]
    public void Arabic_zwnj_breaks_joining()
    {
        var joined = Shape(Arabic, "\u0628\u0628", true, ScriptId.Arabic);
        var split = Shape(Arabic, "\u0628\u200c\u0628", true, ScriptId.Arabic);
        Assert.NotEqual(joined.Glyphs.Select(g => g.GlyphId), split.Glyphs.Where(g => g.TextLength > 0 && g.TextStart != 1).Select(g => g.GlyphId));
    }

    [Fact]
    public void Hebrew_points_stay_with_their_letter_and_have_no_advance()
    {
        var run = Shape(Hebrew, "\u05e2\u05b4\u05d1", true, ScriptId.Hebrew);
        Assert.Equal(3, run.Glyphs.Length);
        var mark = run.Glyphs.Single(g => g.TextStart == 1);
        Assert.Equal(0, mark.XAdvance);
    }

    [Fact]
    public void Devanagari_pre_base_matra_is_reordered_before_the_consonant()
    {
        var run = Shape(Deva, "\u0915\u093f", false, ScriptId.Devanagari);
        Assert.Equal(2, run.Glyphs.Length);
        Assert.Equal(0, run.Glyphs[0].TextStart); // i-matra drawn first; reordering merges the two clusters
        Assert.Equal(2, run.Glyphs[0].TextLength);
        Assert.Equal(2, run.Glyphs[1].TextLength);
    }

    [Fact]
    public void Devanagari_conjunct_forms_single_glyph()
    {
        var run = Shape(Deva, "\u0915\u094d\u0937", false, ScriptId.Devanagari);
        Assert.Single(run.Glyphs);
        Assert.Equal(3, run.Glyphs[0].TextLength);
    }

    [Fact]
    public void Devanagari_reph_moves_after_the_base()
    {
        var run = Shape(Deva, "\u0915\u0930\u094d\u092e", false, ScriptId.Devanagari);
        // ka, then ma with the reph (ra+virama) drawn after it
        Assert.Equal(0, run.Glyphs[0].TextStart);
        Assert.True(run.Glyphs[^1].TextStart <= 2);
        Assert.Equal(3, run.Glyphs.Length);
    }

    // ---------- emoji / fallback ----------

    [Fact]
    public void Emoji_missing_from_the_font_becomes_one_notdef_glyph_per_code_point()
    {
        var run = Shape(Latin, "a\U0001F600b", false, ScriptId.Latin);
        Assert.True(run.HasMissingGlyphs);
        Assert.Equal(3, run.Glyphs.Length);
        Assert.Equal(0, run.Glyphs[1].GlyphId);
        Assert.Equal(1, run.Glyphs[1].TextStart);
        Assert.Equal(2, run.Glyphs[1].TextLength);
    }

    [Theory]
    [InlineData("\U0001F600", 2)]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467", 8)]
    [InlineData("\U0001F1FA\U0001F1F8", 4)]
    [InlineData("1\uFE0F\u20E3", 3)]
    [InlineData("\U0001F44D\U0001F3FD", 4)]
    [InlineData("\u2764\uFE0F", 2)]
    [InlineData("\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F", 14)]
    public void Emoji_sequences_are_single_clusters(string text, int units)
    {
        Assert.Equal(units, UnicodeData.EmojiClusterLength(text, 0, text.Length));
        var items = TextShaper.Itemize("a" + text + "b", BidiDirection.LeftToRight, out _);
        Assert.Equal(3, items.Count);
        Assert.True(items[1].IsEmoji);
        Assert.Equal(units, items[1].Length);
    }

    [Fact]
    public void Itemize_splits_scripts_and_orders_runs_visually()
    {
        var items = TextShaper.Itemize("abc \u05d0\u05d1\u05d2 def", BidiDirection.Auto, out var level);
        Assert.Equal(0, level);
        Assert.Contains(items, i => i.RightToLeft && i.Script == ScriptId.Hebrew);
        Assert.Equal(0, items[0].Start);
        Assert.False(items[0].RightToLeft);
    }

    [Fact]
    public void Layout_shapes_mixed_direction_text()
    {
        var shaped = TextShaper.Layout(Arabic, "\u0645\u0631\u062d\u0628\u0627 123");
        Assert.Equal(1, shaped.ParagraphLevel);
        Assert.True(shaped.Advance > 0);
        Assert.Contains(shaped.Runs, r => r.RightToLeft);
    }

    // ---------- bidi ----------

    [Fact]
    public void Bidi_levels_for_mixed_text()
    {
        var r = Bidi.Resolve("abc \u05d0\u05d1\u05d2 def");
        Assert.Equal(0, r.ParagraphLevel);
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0], r.Levels);
        var rl = Bidi.Resolve("\u05d0\u05d1\u05d2 abc \u05d3");
        Assert.Equal(1, rl.ParagraphLevel);
        Assert.Equal([1, 1, 1, 1, 2, 2, 2, 1, 1], rl.Levels);
    }

    [Fact]
    public void Bidi_numbers_and_brackets()
    {
        var r = Bidi.Resolve("\u05d0 123 \u05d1");
        Assert.Equal([1, 1, 2, 2, 2, 1, 1], r.Levels);
        var b = Bidi.Resolve("\u05d0(abc)\u05d1");
        Assert.Equal(1, b.ParagraphLevel);
        Assert.Equal([1, 1, 2, 2, 2, 1, 1], b.Levels);
        var arabicDigits = Bidi.Resolve("\u0627\u0661\u0662");
        Assert.Equal([1, 2, 2], arabicDigits.Levels);
    }

    [Fact]
    public void Bidi_explicit_embeddings_and_isolates()
    {
        var iso = Bidi.Resolve("a\u2067bc\u2069d");
        Assert.Equal(0, iso.ParagraphLevel);
        Assert.Equal(0, iso.Levels[0]);
        Assert.Equal(2, iso.Levels[2]);
        Assert.Equal(0, iso.Levels[^1]);
        var ov = Bidi.Resolve("\u202eabc\u202c", BidiDirection.LeftToRight);
        Assert.Equal(1, ov.Levels[1]);
        Assert.Equal(1, ov.Levels[3]);
    }

    [Fact]
    public void Bidi_visual_runs_follow_rule_l2()
    {
        var r = Bidi.Resolve("ab \u05d0\u05d1 cd", BidiDirection.RightToLeft);
        var runs = r.VisualRuns();
        Assert.Equal("cd", "ab \u05d0\u05d1 cd".Substring(runs[0].Start, runs[0].Length).Trim());
        Assert.Equal([0, 2, 1, 3], Bidi.VisualOrder([0, 1, 1, 0]));
    }

    [Theory]
    [InlineData('(', ')')]
    [InlineData('[', ']')]
    [InlineData('{', '}')]
    [InlineData('<', '>')]
    [InlineData('\u00ab', '\u00bb')]
    [InlineData('\u2308', '\u2309')]
    public void Bidi_mirroring_pairs(char open, char close)
    {
        Assert.Equal(close, (char)UnicodeData.Mirrored(open));
        Assert.Equal(open, (char)UnicodeData.Mirrored(close));
    }

    [Fact]
    public void Mirrored_brackets_use_the_counterpart_glyph_in_rtl_runs()
    {
        var f = Hebrew;
        var rtl = Shape(f, "(", true, ScriptId.Hebrew).Glyphs[0].GlyphId;
        var ltr = Shape(f, "(", false, ScriptId.Hebrew).Glyphs[0].GlyphId;
        Assert.Equal(f.LookupAny(')'), rtl);
        Assert.Equal(f.LookupAny('('), ltr);
    }

    // ---------- line breaking ----------

    [Fact]
    public void Line_breaks_follow_uax14_basics()
    {
        static List<int> Breaks(string s) => LineBreaker.FindBreaks(s).Select((k, i) => (k, i)).Where(x => x.i > 0 && x.i < s.Length && x.k != LineBreakKind.None).Select(x => x.i).ToList();
        Assert.Equal([6], Breaks("Hello world"));
        Assert.Equal([3], Breaks("no-break"));
        Assert.Empty(Breaks("a\u00a0b"));
        Assert.Equal([1, 2], Breaks("\u4e2d\u6587\u5b57").Take(2).ToList());
        Assert.Empty(Breaks("3.14"));
        Assert.Equal([2], Breaks("a\nb"));
        Assert.Equal([4], Breaks("foo (bar)"));
        Assert.Equal(LineBreakKind.Mandatory, LineBreaker.FindBreaks("a\nb")[2]);
        Assert.DoesNotContain(1, Breaks("(a b)"));
        Assert.Equal(LineBreakKind.Mandatory, LineBreaker.FindBreaks("abc")[3]);
    }

    [Fact]
    public void Wrap_breaks_lines_at_opportunities()
    {
        const string text = "aa bb cc dd";
        var widths = Enumerable.Repeat(10.0, text.Length).ToArray();
        var lines = LineBreaker.Wrap(text, widths, 55);
        Assert.Equal(["aa bb ", "cc dd"], lines.Select(l => text[l.Start..l.End]).ToArray());
    }

    // ---------- PDF output ----------

    [Fact]
    public void Writer_emits_tj_adjustments_and_actualtext_for_ligatures()
    {
        var run = Shape(Latin, "office AV", false, ScriptId.Latin);
        var sb = new StringBuilder();
        var map = new Dictionary<int, string>();
        ShapedPdfWriter.Write(sb, run, 12, g => g, g => Math.Round(Latin.Advance(g)), (g, t) => { if (t.Length == 0) return true; if (map.TryGetValue(g, out var h)) return h == t; map[g] = t; return true; });
        var s = sb.ToString();
        Assert.Contains("BDC", s);
        Assert.Contains("/ActualText <FEFF00660066>", s);
        Assert.Contains("] TJ", s);
        Assert.Equal("ff", map[run.Glyphs.First(g => g.IsLigature).GlyphId]);
    }

    [Fact]
    public void Writer_wraps_rtl_runs_in_actualtext_with_logical_text()
    {
        var run = Shape(Arabic, "\u0645\u0631", true, ScriptId.Arabic);
        var sb = new StringBuilder();
        ShapedPdfWriter.Write(sb, run, 12, g => g, g => Arabic.Advance(g), (_, _) => true);
        Assert.Contains("/ActualText <FEFF06450631>", sb.ToString());
    }

    static readonly Lazy<FontCatalog> Catalog = new(() => FontCatalog.Scan([Dir]));
    const string Std = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";

    static byte[] Overlay(string text, string font)
    {
        var bytes = TestPdf.Build("", Std);
        var e = PdfEditor.Open(bytes);
        var box = PdfDocument.Load(bytes).Pages[0].CropBox;
        var item = new Item("t", "text", 20, 20, 0, 0, text, font, 24, false, false, "#000000", "", "", 0, 1, "", 0, 0, null);
        var (c, res) = new OverlayBuilder(e, [], Catalog.Value).Build([item], box);
        e.SetOverlay(0, c, res);
        return e.Save();
    }

    static string Extract(byte[] pdf, bool actual) =>
        string.Concat(DocumentRenderer.ExtractText(PdfDocument.Load(pdf).Pages[0], false, actual).Select(s => s.Text));

    [Fact]
    public void Editor_emits_ligatures_with_multi_codepoint_tounicode()
    {
        var pdf = Overlay("office", "Shaping Test Sans");
        Assert.Equal("office", Extract(pdf, false).Replace(" ", ""));
        Assert.Equal("office", Extract(pdf, true).Replace(" ", ""));
    }

    [Fact]
    public void Editor_round_trips_arabic_through_actualtext()
    {
        const string text = "\u0645\u0631\u062d\u0628\u0627";
        var pdf = Overlay(text, "Noto Sans Arabic");
        Assert.Equal(text, Extract(pdf, true).Trim());
    }

    [Fact]
    public void Editor_round_trips_hebrew_and_devanagari()
    {
        const string he = "\u05e9\u05dc\u05d5\u05dd";
        Assert.Equal(he, Extract(Overlay(he, "Noto Sans Hebrew"), true).Trim());
        const string hi = "\u0939\u093f\u0928\u094d\u0926\u0940";
        Assert.Equal(hi, Extract(Overlay(hi, "Noto Sans Devanagari"), true).Trim());
    }

    [Fact]
    public void Editor_uses_fallback_font_for_hebrew_in_latin_text()
    {
        var pdf = Overlay("Hi \u05e9\u05dc\u05d5\u05dd", "Shaping Test Sans");
        Assert.Equal("Hi \u05e9\u05dc\u05d5\u05dd", Extract(pdf, true).Replace("  ", " ").Trim());
    }
}
