using AdoBye.Content;
using AdoBye.Document;
using AdoBye.Render;
using Xunit;

namespace AdoBye.Tests;

public class FontCmapTests
{
    static string Fx(params string[] p) => Path.Combine([AppContext.BaseDirectory, "fixtures", "fonts", .. p]);
    static RenderedPage Render(string name) => PdfRenderer.RenderPage(PdfDocument.Open(Fx(name)).Pages[0], 72);
    static bool Dark(RenderedPage p, int x, int yPdf) => p.Rgba[((p.Height - 1 - yPdf) * p.Width + x) * 4] < 64;
    static string Text(string name) => string.Concat(PdfRenderer.ExtractText(PdfDocument.Open(Fx(name)).Pages[0]).Select(s => s.Text));

    // Glyph origins are 10 + 70*i pt, baseline y=50, font size 100 (1 unit = 0.1 pt).
    [Theory]
    [InlineData("type1-pfb.pdf")]
    [InlineData("type1-pfa.pdf")]
    [InlineData("type1-raw.pdf")]
    public void Type1_outlines_flex_seac_subrs_and_div_render(string file)
    {
        var p = Render(file);
        Assert.Empty(p.Warnings.Where(w => w.Contains("font", StringComparison.OrdinalIgnoreCase)));
        // A: 300x400 box at x 50..350
        Assert.True(Dark(p, 40, 70), "A body");
        Assert.False(Dark(p, 60, 70), "gap after A");
        Assert.False(Dark(p, 40, 95), "above A");
        // B: flex bulges up around x=275 and dips below the straight edge around x=125
        Assert.True(Dark(p, 107, 81), "flex bulge");
        Assert.True(Dark(p, 95, 70), "B body");
        Assert.False(Dark(p, 92, 80), "flex dip");
        // C: hint replacement and subr lines
        Assert.True(Dark(p, 180, 70), "C body");
        // D: div operator (width 600/2 = 300)
        Assert.True(Dark(p, 250, 70), "D body");
        Assert.False(Dark(p, 281, 70), "D right of box");
        // E: seac - base A plus accent at x 175..275, y 450..530
        Assert.True(Dark(p, 320, 70), "seac base");
        Assert.True(Dark(p, 312, 99), "seac accent");
        Assert.False(Dark(p, 312, 92), "gap between base and accent");
        Assert.False(Dark(p, 296, 99), "accent left edge");
        // F: curve blob
        Assert.True(Dark(p, 385, 70), "O centre");
        Assert.False(Dark(p, 366, 51), "O corner");
    }

    [Fact]
    public void Type1_text_extraction_uses_encoding_names()
    {
        Assert.Equal("ABCDEF", Text("type1-pfa.pdf"));
        Assert.Equal("ABCD\u00C1O", Text("type1-raw.pdf"));
        Assert.Equal("ABCD\u00C1O", Text("type1-pfb.pdf").Replace("E", "\u00C1").Replace("F", "O"));
    }

    [Fact]
    public void Cff_seac_arithmetic_ops_and_standard_strings()
    {
        var p = Render("cff-seac.pdf");
        Assert.True(Dark(p, 40, 70), "A body");
        // Arith: (100+200) x (20*20) box at x 50..350, width via neg(600/2)
        Assert.True(Dark(p, 110, 70), "arith body");
        Assert.False(Dark(p, 150, 95), "above arith");
        Assert.False(Dark(p, 122, 70) && !Dark(p, 100, 70), "arith body consistency");
        // Aacute via endchar seac: A box + acute at x 125+50.. y 450..
        Assert.True(Dark(p, 180, 70), "seac base");
        Assert.True(Dark(p, 172, 99), "seac accent");
        Assert.False(Dark(p, 172, 92), "seac gap");
        // exclamsmall (SID 229) and Semibold (SID 390) glyphs are found by name
        Assert.True(Dark(p, 240, 55), "exclamsmall");
        Assert.False(Dark(p, 240, 75), "exclamsmall height 200");
        Assert.True(Dark(p, 305, 80), "Semibold");
        Assert.True(Dark(p, 380, 70), "Asmall");
        Assert.False(Dark(p, 380, 52), "Asmall starts at y=100");
    }


    // ----------------------------------------------------------------- CMaps

    [Fact]
    public void Predefined_cmaps_match_samples_from_the_adobe_sources()
    {
        var lines = File.ReadAllLines(Fx("cmap-samples.txt")).Where(l => !l.StartsWith('#') && l.Length > 0).ToArray();
        Assert.True(lines.Length > 400);
        foreach (var l in lines)
        {
            var t = l.Split(' ');
            var cmap = CMap.Predefined(t[0]);
            Assert.NotNull(cmap);
            var code = Convert.ToInt32(t[1], 16);
            Assert.Equal(int.Parse(t[3]), cmap!.ToCid(code, int.Parse(t[2])));
        }
    }

    [Fact]
    public void All_predefined_cjk_cmap_families_are_present()
    {
        var names = CMap.PredefinedNames;
        foreach (var n in new[] { "UniJIS-UCS2-H", "UniJIS-UTF16-V", "90ms-RKSJ-H", "90pv-RKSJ-H", "EUC-H", "H", "V", "GBK-EUC-H", "GBK2K-V", "UniGB-UCS2-H", "GB-EUC-H",
                                   "ETen-B5-H", "B5pc-V", "HKscs-B5-H", "UniCNS-UTF16-H", "CNS-EUC-H", "KSCms-UHC-H", "KSC-EUC-V", "UniKS-UCS2-H", "UniKS-UTF16-V" })
            Assert.Contains(n, names);
        Assert.Equal(59, names.Count);
    }

    [Fact]
    public void Cmap_codespace_decoding_and_usecmap_chaining()
    {
        var h = CMap.Predefined("90ms-RKSJ-H")!;
        var v = CMap.Predefined("90ms-RKSJ-V")!;
        Assert.Equal(1, v.WMode);
        Assert.Equal(0, h.WMode);
        byte[] s = [0x41, 0x82, 0xA0, 0xB1];
        var pos = 0;
        var lens = new List<int>();
        while (pos < s.Length) { var n = h.ReadCode(s, pos, out _); lens.Add(n); pos += n; }
        Assert.Equal([1, 2, 1], lens);
        Assert.Equal(h.ToCid(0x41, 1), v.ToCid(0x41, 1));
        Assert.Equal(843, CMap.Predefined("UniJIS-UCS2-H")!.ToCid(0x3042, 2));
        Assert.Equal(843, h.ToCid(0x82A0, 2));
    }

    [Fact]
    public void Embedded_cmap_streams_parse_with_codespaces_ranges_and_usecmap()
    {
        var src = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /Adobe-Japan1-UCS2 usecmap\n/CMapName /Test def /WMode 1 def\n" +
                  "1 begincodespacerange <00> <80> endcodespacerange\n1 begincodespacerange <8100> <9FFF> endcodespacerange\n" +
                  "2 begincidrange <20> <7E> 500 <8140> <817E> 1000 endcidrange\n1 begincidchar <9000> 77 endcidchar\nendcmap";
        var m = CMap.Parse(System.Text.Encoding.ASCII.GetBytes(src));
        Assert.Equal(1, m.WMode);
        Assert.Equal(500 + 0x21, m.ToCid(0x41, 1));
        Assert.Equal(1000 + 2, m.ToCid(0x8142, 2));
        Assert.Equal(77, m.ToCid(0x9000, 2));
        Assert.Equal(2, m.ReadCode([0x81, 0x42], 0, out var code));
        Assert.Equal(0x8142, code);
    }

    [Fact]
    public void Rksj_text_renders_identically_to_the_equivalent_identity_cid_string()
    {
        var a = Render("cjk-rksj.pdf");
        var b = Render("cjk-identity.pdf");
        Assert.Equal(b.Rgba, a.Rgba);
        // Vera 'A','B','C' glyphs in three 70pt cells: ink present in each.
        foreach (var x in new[] { 10, 80, 150 })
            Assert.Contains(Enumerable.Range(0, 60).SelectMany(dx => Enumerable.Range(50, 70).Select(y => (dx, y))), q => Dark(a, x + q.dx, q.y));
        // single-byte codes consumed one byte each: 'A','B' then double-byte 0x82A0 as the third glyph
        Assert.Equal("AB\u3042", Text("cjk-rksj.pdf"));
    }

    [Fact]
    public void Non_embedded_cjk_fonts_fall_back_to_installed_fonts_when_available()
    {
        var fb = new SystemFontFallback();
        var p = Render("cjk-noembed.pdf");
        var rows = new[] { ("Japan1", 150), ("GB1", 100), ("Korea1", 50), ("CNS1", 5) };
        foreach (var (ordering, y) in rows)
        {
            if (fb.FindCjk(ordering, true) is null) continue; // no suitable CJK font installed on this machine
            var ink = false;
            for (var dx = 0; dx < 80 && !ink; dx++)
                for (var dy = 0; dy < 40 && !ink; dy++) ink = Dark(p, 10 + dx, y + dy);
            Assert.True(ink, $"no ink for {ordering}");
        }
        // Text extraction works regardless of installed fonts (CID -> Unicode via the Adobe ordering tables).
        Assert.Equal("\u4E2D\u6587\u4E2D\u6587\uD55C\uAE00\u4E2D\u6587", Text("cjk-noembed.pdf"));
    }
}
