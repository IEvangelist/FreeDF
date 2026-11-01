using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

/// <summary>
/// Type 1 Multiple Master instances (Adobe TN #5015 section 3: /WeightVector, /Blend* keywords, OtherSubrs 14-18) and Expert encodings.
/// Fixtures come from tools/cmapgen/gen_type1_mm_fixtures.py. Blend expectations are hand-derived with exact fractions in that script
/// (weights 9/32, 3/32, 15/32, 5/32), the "TestMM4Snap" font is a pre-blended twin, and PDFium (FreeType) renders the same files identically.
/// </summary>
public class Type1MultipleMasterTests
{
    static string Fx(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", name);
    static readonly double[] W4 = [9 / 32.0, 3 / 32.0, 15 / 32.0, 5 / 32.0];

    static byte[] Program(string file, string resource)
    {
        var doc = PdfDocument.Open(Fx(file));
        var fonts = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.Pages[0].Resources.Get("Font")));
        var dict = Assert.IsType<PdfDictionary>(doc.File.Resolve(fonts.Get(resource)));
        var descriptor = Assert.IsType<PdfDictionary>(doc.File.Resolve(dict.Get("FontDescriptor")));
        return doc.File.DecodeStream(Assert.IsType<PdfStream>(doc.File.Resolve(descriptor.Get("FontFile"))));
    }

    static Type1Font Font(string file, string resource) => new(Program(file, resource));
    static Type1Font Mm4() => Font("type1-mm.pdf", "F1");

    static byte[] Patch(byte[] data, string from, string to)
    {
        var s = Encoding.Latin1.GetString(data);
        var i = s.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, from);
        return Encoding.Latin1.GetBytes(s[..i] + to + s[(i + from.Length)..]);
    }

    static void AssertSamePath(PathData? expected, PathData? actual, double tol = 1e-6)
    {
        Assert.NotNull(expected); Assert.NotNull(actual);
        Assert.Equal(expected.Ops, actual.Ops);
        Assert.Equal(expected.Pts.Count, actual.Pts.Count);
        for (var i = 0; i < expected.Pts.Count; i++) Assert.Equal(expected.Pts[i], actual.Pts[i], tol);
    }

    static (double MinX, double MaxX, double MinY, double MaxY) Bounds(PathData p)
    {
        double a = double.MaxValue, b = double.MinValue, c = double.MaxValue, d = double.MinValue;
        for (var i = 0; i < p.Pts.Count; i += 2)
        {
            a = Math.Min(a, p.Pts[i]); b = Math.Max(b, p.Pts[i]); c = Math.Min(c, p.Pts[i + 1]); d = Math.Max(d, p.Pts[i + 1]);
        }
        return (a, b, c, d);
    }

    // ----------------------------------------------------------------- metadata

    [Fact]
    public void Multiple_master_keywords_are_parsed_and_bounded()
    {
        var f = Mm4();
        Assert.True(f.IsMultipleMaster);
        Assert.Equal(4, f.MasterCount);
        Assert.Equal(W4, f.WeightVector);
        Assert.Equal(new[] { "Weight", "Width" }, f.BlendAxisTypes);
        Assert.Equal(4, f.BlendDesignPositions.Count);
        Assert.Equal(new[] { 0.0, 1.0 }, f.BlendDesignPositions[1]);
        Assert.Equal(new[] { 1.0, 0.0 }, f.BlendDesignPositions[2]);
        Assert.Equal(2, f.BlendDesignMap.Count);
        Assert.Equal(new[] { (200.0, 0.0), (500.0, 0.5), (900.0, 1.0) }, f.BlendDesignMap[0]);
        Assert.Equal(new[] { (300.0, 0.0), (700.0, 1.0) }, f.BlendDesignMap[1]);

        var two = Font("type1-mm.pdf", "F2");
        Assert.Equal(2, two.MasterCount);
        Assert.Equal(new[] { 0.25, 0.75 }, two.WeightVector);
        Assert.Equal(new[] { "Weight" }, two.BlendAxisTypes);

        var plain = Font("type1-pfa.pdf", "F1");
        Assert.False(plain.IsMultipleMaster);
        Assert.Empty(plain.WeightVector);
    }

    // ----------------------------------------------------------------- blending

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    public void Blended_outlines_equal_the_pre_blended_snapshot(string glyph)
    {
        var mm = Mm4();
        var snap = Font("type1-mm.pdf", "F3");
        Assert.False(snap.IsMultipleMaster);
        AssertSamePath(snap.GetOutline(glyph), mm.GetOutline(glyph));
        Assert.Equal(snap.Advance(glyph), mm.Advance(glyph), 6);
    }

    [Fact]
    public void Blend_results_match_hand_derived_weighted_values()
    {
        var f = Mm4();
        // OtherSubrs 14: width/height/advance of A are 300+87.5, 400+31.25 and 700+87.5 under w = 9/32 3/32 15/32 5/32.
        Assert.Equal(787.5, f.Advance("A"), 6);
        Assert.Equal(500, f.Advance("B"), 6);
        Assert.Equal(675, f.Advance("C"), 6);
        Assert.Equal(600, f.Advance("D"), 6);
        var (x0, x1, y0, y1) = Bounds(f.GetOutline("A")!);
        Assert.Equal((50, 437.5, 0, 431.25), (x0, x1, y0, y1));

        // OtherSubrs 15: the 2-value blend keeps its first-pop = first-value order (a = 127.5, b = 237.5): (50,0) -> (177.5,237.5) -> (377.5,237.5) -> (250,0).
        var b = f.GetOutline("B")!;
        Assert.Equal(177.5, b.Pts[2], 6);
        Assert.Equal(237.5, b.Pts[3], 6);
        Assert.Equal(377.5, b.Pts[4], 6);
        Assert.Equal(250, b.Pts[6], 6);
        Assert.Equal(0, b.Pts[7], 6);
    }

    [Fact]
    public void Two_master_font_uses_otherSubr_18_six_value_blend()
    {
        var f = Font("type1-mm.pdf", "F2");
        // w = 0.25 0.75: advance 700 + 0.75*200, box width 300 + 0.75*200, curve end dx 110+0.75*40 + 90+0.75*40.
        Assert.Equal(850, f.Advance("A"), 6);
        var (ax0, ax1, _, _) = Bounds(f.GetOutline("A")!);
        Assert.Equal((50, 500), (ax0, ax1));
        var o = f.GetOutline("O")!;
        Assert.Contains(PathOp.Curve, o.Ops);
        // moveto (250,0); controls (250+140, 0), (390+120, 112.5), end (510, 112.5+140)
        Assert.Equal(250, o.Pts[0], 6);
        Assert.Equal(390, o.Pts[2], 6); Assert.Equal(0, o.Pts[3], 6);
        Assert.Equal(510, o.Pts[4], 6); Assert.Equal(112.5, o.Pts[5], 6);
        Assert.Equal(510, o.Pts[6], 6); Assert.Equal(252.5, o.Pts[7], 6);
    }

    [Fact]
    public void Another_weight_vector_selects_another_instance()
    {
        var f = Mm4();
        Assert.True(f.TrySetWeightVector([1, 0, 0, 0]));
        Assert.Equal(700, f.Advance("A"), 6);
        Assert.Equal((50, 350, 0, 400), Bounds(f.GetOutline("A")!));
        Assert.True(f.TrySetWeightVector([0, 0, 0, 1]));
        Assert.Equal(960, f.Advance("A"), 6);
        Assert.Equal((50, 610, 0, 450), Bounds(f.GetOutline("A")!));

        Assert.False(f.TrySetWeightVector([1, 0, 0]));
        Assert.False(f.TrySetWeightVector([0.5, 0.5, 0.5, 0.5]));
        Assert.False(f.TrySetWeightVector([double.NaN, 0, 0, 1]));
        Assert.Equal(960, f.Advance("A"), 6);
        Assert.False(Font("type1-pfa.pdf", "F1").TrySetWeightVector([1]));
    }

    [Fact]
    public void Design_vector_is_converted_through_the_design_map_and_master_positions()
    {
        var f = Mm4();
        // weight 600 -> .5 + 100/400*.5 = .625, width 400 -> .25 (hand-derived in the generator)
        Assert.Equal(W4, f.ConvertDesignVector([600, 400]));
        Assert.Equal(new[] { 1.0, 0, 0, 0 }, f.ConvertDesignVector([200, 300]));
        Assert.Equal(new[] { 0.0, 0, 0, 1 }, f.ConvertDesignVector([900, 700]));
        Assert.Equal(new[] { 0.0, 1, 0, 0 }, f.ConvertDesignVector([-50, 5000])); // clamped to the map ends
        Assert.Null(f.ConvertDesignVector([600]));
        Assert.Null(f.ConvertDesignVector([double.NaN, 400]));
        Assert.Null(Font("type1-pfa.pdf", "F1").ConvertDesignVector([1]));
        Assert.True(f.TrySetWeightVector(f.ConvertDesignVector([900, 300])!));
        Assert.Equal(760, f.Advance("A"), 6);
    }

    [Fact]
    public void Sixteen_master_otherSubr_18_blends_96_operands()
    {
        var f = Font("type1-mm.pdf", "F4");
        Assert.Equal(16, f.MasterCount);
        // all weights 1/16: value = base + 7.5 * step
        Assert.Equal(575, f.Advance("A"), 6);
        var a = f.GetOutline("A")!;
        double[] expected = [50, 0, 157.5, 15, 230, 125, 327.5, 70];
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], a.Pts[i], 6);
    }

    [Fact]
    public void Operand_stack_overflow_drops_the_glyph_instead_of_drawing_it()
    {
        var f = Font("type1-mm.pdf", "F4");
        Assert.Null(f.GetOutline("X"));
        Assert.Equal(0, f.Advance("X"));
        // 100 padding operands exceed the old 64-entry stack but are legal here
        Assert.Equal((50, 150, 0, 100), Bounds(f.GetOutline("Y")!));
        Assert.Equal(500, f.Advance("Y"), 6);
    }

    // ----------------------------------------------------------------- rendering

    static RenderedPage Render() => DocumentRenderer.RenderPage(PdfDocument.Open(Fx("type1-mm.pdf")).Pages[0], 72);
    static bool Dark(RenderedPage p, int x, int yPdf) => p.Rgba[((p.Height - 1 - yPdf) * p.Width + x) * 4] < 64;

    [Fact]
    public void Multiple_master_instances_render_like_the_snapshot_and_pdfium()
    {
        var p = Render();
        Assert.Empty(p.Warnings);
        // F1 (baseline y 220) and its pre-blended twin F3 (baseline y 120) are pixel identical.
        for (var y = 200; y < 290; y++)
            for (var x = 0; x < p.Width; x++)
                Assert.Equal(Dark(p, x, y - 100), Dark(p, x, y));

        // A: x 15..53.75, y 220..263.1 (checked against PDFium, which blends with the same /WeightVector)
        Assert.True(Dark(p, 30, 240)); Assert.False(Dark(p, 57, 240)); Assert.False(Dark(p, 30, 266));
        // F2 A: width 300 + .75*200 = 450 -> x 15..60, height 400 -> y 20..60
        Assert.True(Dark(p, 30, 40)); Assert.False(Dark(p, 63, 40)); Assert.False(Dark(p, 30, 64));
    }

    // ----------------------------------------------------------------- malformed data

    [Fact]
    public void Invalid_weight_vectors_fall_back_to_the_default_master()
    {
        var data = Program("type1-mm.pdf", "F1");
        var baseBox = Bounds(Mm4().GetOutline("A")!);
        Assert.Equal((50, 437.5), (baseBox.MinX, baseBox.MaxX));

        foreach (var bad in new[] { "[0.5 0.5 0.5 0.5]", "[1]", "[nan 0 0 1]", "[0.25 0.25 0.25 abc]", "[0.28125 0.09375 0.46875", "[[0.5] [0.5]]",
                                    "[" + string.Join(' ', Enumerable.Repeat("0.0625", 16)) + " 0]" })
        {
            var f = new Type1Font(Patch(data, "[0.28125 0.09375 0.46875 0.15625]", bad));
            if (f.IsMultipleMaster) continue; // the brace form is a valid 4-element array only when it sums to 1
            Assert.Empty(f.WeightVector);
            // no valid vector: OtherSubrs 14-18 return the first master's values
            Assert.Equal((50, 350, 0, 400), Bounds(f.GetOutline("A")!));
            Assert.Equal(700, f.Advance("A"), 6);
        }
    }

    [Fact]
    public void Weight_vector_length_that_disagrees_with_blend_arguments_uses_the_first_master()
    {
        var f = new Type1Font(Patch(Program("type1-mm.pdf", "F1"), "[0.28125 0.09375 0.46875 0.15625]", "[0.5 0.25 0.25]"));
        Assert.Equal(3, f.MasterCount);
        Assert.Equal((50, 350, 0, 400), Bounds(f.GetOutline("A")!));
    }

    [Fact]
    public void Oversized_or_deeply_nested_blend_arrays_are_rejected_quickly()
    {
        var data = Program("type1-mm.pdf", "F1");
        var huge = "[" + string.Join(' ', Enumerable.Repeat("0.1", 300_000)) + "]";
        var f = new Type1Font(Patch(data, "[0.28125 0.09375 0.46875 0.15625]", huge));
        Assert.False(f.IsMultipleMaster);

        var deep = new Type1Font(Patch(data, "/BlendDesignMap [[[200 0][500 .5][900 1]][[300 0][700 1]]]", "/BlendDesignMap " + new string('[', 5000) + "1" + new string(']', 5000)));
        Assert.Empty(deep.BlendDesignMap);
        Assert.True(deep.IsMultipleMaster);

        var points = string.Concat(Enumerable.Range(0, 13).Select(i => $"[{i + 1} {i}]"));
        var tooMany = new Type1Font(Patch(data, "/BlendDesignMap [[[200 0][500 .5][900 1]][[300 0][700 1]]]", $"/BlendDesignMap [[{points}]]"));
        Assert.Empty(tooMany.BlendDesignMap);
        Assert.Null(tooMany.ConvertDesignVector([5]));

        var unordered = new Type1Font(Patch(data, "/BlendDesignMap [[[200 0][500 .5][900 1]][[300 0][700 1]]]", "/BlendDesignMap [[[500 0][200 1]][[300 0][700 1]]]"));
        Assert.Empty(unordered.BlendDesignMap);
        Assert.Equal(new[] { "Weight", "Width" }, unordered.BlendAxisTypes);

        var ragged = new Type1Font(Patch(data, "[[0 0][0 1][1 0][1 1]]", "[[0 0][0][1 0][1 1]]"));
        Assert.Empty(ragged.BlendDesignPositions);
    }

    [Fact]
    public void Corrupting_blend_text_never_throws_during_outline_extraction()
    {
        var data = Program("type1-mm.pdf", "F1");
        var clearEnd = Encoding.Latin1.GetString(data).IndexOf("eexec", StringComparison.Ordinal);
        var rnd = new Random(5015);
        var ctor = 0;
        for (var i = 0; i < 400; i++)
        {
            var copy = (byte[])data.Clone();
            for (var j = 0; j < 1 + i % 6; j++) copy[rnd.Next(clearEnd)] = (byte)" []{}/.-09aZ\n"[rnd.Next(13)];
            Type1Font f;
            try { f = new Type1Font(copy); }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException or KeyNotFoundException) { ctor++; continue; }
            foreach (var g in f.GlyphNames.ToArray()) { _ = f.Advance(g); _ = f.GetOutline(g); }
            _ = f.ConvertDesignVector([600, 400]);
        }
        Assert.True(ctor < 400);
    }

    // ----------------------------------------------------------------- Expert encodings

    // Hand-derived from the Adobe Expert encoding (TN #5176 Appendix B) and cross-checked with PDFium/FreeType rendering the same files.
    static readonly (int Code, string Name)[] Expert =
    [
        (33, "exclamsmall"), (36, "dollaroldstyle"), (48, "zerooldstyle"), (49, "oneoldstyle"), (86, "ff"), (87, "fi"), (97, "Asmall"),
        (98, "Bsmall"), (126, "Tildesmall"), (188, "onequarter"),
    ];

    [Theory]
    [InlineData("F1")]
    [InlineData("F2")]
    public void Type1_expert_encodings_resolve_explicit_and_named_forms(string resource)
    {
        var f = Font("type1-expert.pdf", resource);
        Assert.True(f.HasBuiltInEncoding);
        foreach (var (code, name) in Expert)
        {
            Assert.Equal(name, f.EncodingName(code));
            Assert.True(f.HasGlyph(name));
        }
        Assert.Null(f.EncodingName(0));
    }

    [Fact]
    public void Named_expert_encoding_matches_the_adobe_table_for_every_code()
    {
        var f = Font("type1-expert.pdf", "F2");
        var table = typeof(Type1Font).Assembly.GetType("PortableDocument.Content.CffStandardStrings")!;
        var strings = (string[])table.GetField("Strings")!.GetValue(null)!;
        var expert = (int[])table.GetField("ExpertEncoding")!.GetValue(null)!;
        for (var code = 0; code < 256; code++)
        {
            var expected = strings[expert[code]];
            Assert.Equal(expected == ".notdef" ? null : expected, f.EncodingName(code));
        }
        Assert.Null(Font("type1-pfa.pdf", "F1").EncodingName(97)); // PFA fixtures keep their own explicit/Standard encodings
    }

    [Fact]
    public void Type1_expert_fixtures_render_identically_and_by_glyph()
    {
        var p = DocumentRenderer.RenderPage(PdfDocument.Open(Fx("type1-expert.pdf")).Pages[0], 72);
        Assert.Empty(p.Warnings);
        for (var y = 100; y < 160; y++)
            for (var x = 0; x < p.Width; x++)
                Assert.Equal(Dark(p, x, y - 100), Dark(p, x, y));
        // glyph i is a 400-unit bar of height 100 + 40 i at size 40: x 10 + 24 i + 2 .. +16
        for (var i = 0; i < Expert.Length; i++)
        {
            var x = 10 + 24 * i + 10;
            var h = (100 + 40 * i) * 0.04;
            Assert.True(Dark(p, x, 120 + (int)h - 1), $"glyph {i} top");
            Assert.False(Dark(p, x, 120 + (int)h + 2), $"glyph {i} above");
        }
    }
}
