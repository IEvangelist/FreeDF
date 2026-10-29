using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

public class VerticalFontTests
{
    const string Widths = "/DW 1000 /W [1 [600 800] 3 4 400]";
    const string Metrics = "/DW2 [900 -1100] /W2 [1 [-1200 200 700 -800 300 600] 3 4 -600 100 500]";

    [Theory]
    [InlineData("/Identity-V", true)]
    [InlineData("embedded", true)]
    [InlineData("dictionary", true)]
    [InlineData("/Identity-H", false)]
    public void Writing_mode_comes_from_the_encoding_CMap(string encoding, bool vertical)
    {
        var page = Fixture("BT /F1 10 Tf 100 150 Td <0001> Tj <0002> Tj ET", Metrics, encoding).Pages[0];
        var spans = DocumentRenderer.ExtractText(page);
        Assert.All(spans, span => Assert.Equal(vertical, span.Vertical));
        Assert.Equal("AB", string.Concat(spans.Select(s => s.Text)));
        Assert.Equal(vertical ? 100 : 106, spans[1].X, 8);
        Assert.Equal(vertical ? 62 : 50, spans[1].Y, 8);
        Assert.Equal(vertical ? 1200 : -600, spans[0].Advance, 8);
    }

    [Theory]
    [InlineData("", 880, -1000)]
    [InlineData("/DW2 [950 -1250]", 950, -1250)]
    public void Default_metrics_use_half_each_CIDs_horizontal_width(string metrics, double y, double advance)
    {
        var spans = DocumentRenderer.ExtractText(Fixture(
            "BT /F1 10 Tf 100 150 Td <0001000200030005> Tj <0004> Tj ET", metrics).Pages[0]);
        var span = spans[0];
        Assert.Equal(100, span.X);
        Assert.Equal(50, span.Y);
        Assert.Equal(-advance * 4 / 100, span.Width, 8);
        Assert.Equal(-advance * 4, span.Advance, 8);
        Assert.Equal(50 - advance * 4 / 100, spans[1].Y, 8);
        var glyphs = Glyphs(span);
        var widths = new[] { 600d, 800d, 400d, 1000d };
        for (var i = 0; i < glyphs.Length; i++)
        {
            Assert.Equal(100 - widths[i] / 200, glyphs[i].X0, 8);
            Assert.Equal(100 + widths[i] / 200, glyphs[i].X1, 8);
            Assert.Equal(50 - advance * i / 100 + y / 100 - 9.5, glyphs[i].Y0, 8);
            Assert.Equal(-advance, glyphs[i].Adv, 8);
        }
    }

    [Fact]
    public void W2_array_and_range_override_defaults_without_changing_ToUnicode_order()
    {
        var spans = DocumentRenderer.ExtractText(Fixture(
            "BT /F1 10 Tf 100 150 Td <0001000200030004> Tj <0001> Tj ET", Metrics, "embedded").Pages[0]);
        Assert.Equal("ABCD", spans[0].Text);
        Assert.Equal(3200, spans[0].Advance, 8);
        Assert.Equal(32, spans[0].Width, 8);
        Assert.Equal(82, spans[1].Y, 8);
        var glyphs = Glyphs(spans[0]);
        Assert.Equal(new[] { 1200d, 800d, 600d, 600d }, glyphs.Select(g => g.Adv));
        Assert.Equal(new[] { 98d, 97d, 99d, 99d }, glyphs.Select(g => g.X0));
        Assert.Equal(new[] { 47.5, 58.5, 65.5, 71.5 }, glyphs.Select(g => g.Y0));
        Assert.Equal(new byte[] { 0, 1, 0, 2, 0, 3, 0, 4 }, glyphs.SelectMany(g => g.Bytes));
    }

    [Fact]
    public void Single_byte_CMap_uses_CID_metrics_raw_ToUnicode_and_word_spacing()
    {
        var spans = DocumentRenderer.ExtractText(Fixture(
            "BT /F1 10 Tf 50 Tz 1 Tc 4 Tw 100 150 Td <2021> Tj <22> Tj ET", Metrics, "single-byte").Pages[0]);
        Assert.Equal("AB", spans[0].Text);
        Assert.Equal(1400, spans[0].Advance, 8);
        Assert.Equal(14, spans[0].Width, 8);
        Assert.Equal(64, spans[1].Y, 8);
        var glyphs = Glyphs(spans[0]);
        Assert.Equal(new[] { 700d, 700d }, glyphs.Select(g => g.Adv));
        Assert.Equal(new byte[] { 0x20, 0x21 }, glyphs.SelectMany(g => g.Bytes));
        Assert.Equal(99, glyphs[0].X0, 8);
        Assert.Equal(98.5, glyphs[1].X0, 8);
    }

    [Theory]
    [InlineData(-300, 53)]
    [InlineData(0, 50)]
    [InlineData(500, 45)]
    public void Vertical_displacement_preserves_its_sign(int displacement, double nextY)
    {
        var spans = DocumentRenderer.ExtractText(Fixture(
            "BT /F1 10 Tf 100 150 Td <0001> Tj <0002> Tj ET",
            $"/W2 [1 [{displacement} 200 700]]").Pages[0]);
        Assert.Equal(-displacement, spans[0].Advance, 8);
        Assert.Equal(Math.Abs(displacement) / 100d, spans[0].Width, 8);
        Assert.Equal(nextY, spans[1].Y, 8);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("50")]
    [InlineData("-50")]
    public void Vertical_spacing_and_TJ_adjustments_do_not_scale_with_Tz(string scale)
    {
        var spans = DocumentRenderer.ExtractText(Fixture(
            $"BT /F1 10 Tf {scale} Tz 2 Tc 9 Tw 3 Ts 100 150 Td [<0001> 200 <0002> -100 <0003>] TJ <0004> Tj ET",
            Metrics).Pages[0]);
        Assert.Equal("ABC", spans[0].Text);
        Assert.Equal(2100, spans[0].Advance, 8);
        Assert.Equal(21, spans[0].Width, 8);
        Assert.Equal(100, spans[1].X, 8);
        Assert.Equal(68, spans[1].Y, 8);
        Assert.Equal(new[] { 1000d, 600d, 400d }, Glyphs(spans[0]).Select(g => g.Adv));
        Assert.Equal(new[] { 200d, -100d }, spans[0].Pieces!.Where(p => p.Adj.HasValue).Select(p => p.Adj!.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void Origin_offset_is_applied_to_fill_and_stroke_like_equivalent_horizontal_text(int mode)
    {
        var paintClip = mode == 7 ? "0 0 200 200 re f" : "";
        var vertical = Fixture($"BT /F1 20 Tf {mode} Tr 100 150 Td <0001> Tj <0002> Tj ET {paintClip}", Metrics);
        var horizontal = Fixture($"BT /F1 20 Tf {mode} Tr 1 0 0 1 96 136 Tm <0001> Tj 1 0 0 1 94 114 Tm <0002> Tj ET {paintClip}",
            Metrics, "/Identity-H");
        var rendered = DocumentRenderer.RenderPage(vertical.Pages[0], 72);
        Assert.Contains(rendered.Rgba, b => b == 0);
        Assert.Equal(DocumentRenderer.RenderPage(horizontal.Pages[0], 72).Rgba, rendered.Rgba);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Placement_and_signed_advance_survive_text_CTM_and_page_transforms(int rotation)
    {
        const string prefix = "q 1.5 0.2 0.3 0.8 7 11 cm BT /F1 10 Tf 50 Tz 3 Ts ";
        var vertical = Fixture(prefix + "0 1 -1 0 100 100 Tm <0001> Tj <0002> Tj ET Q", Metrics, rotation: rotation);
        var horizontal = Fixture(prefix +
            "0 1 -1 0 107 99 Tm <0001> Tj 0 1 -1 0 118 98.5 Tm <0002> Tj ET Q", Metrics, "/Identity-H", rotation);
        Assert.Equal(DocumentRenderer.RenderPage(horizontal.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(vertical.Pages[0], 72).Rgba);
        var page = vertical.Pages[0];
        using var raster = new SoftwareRasterizer(200, 200);
        var interpreter = new ContentInterpreter(vertical.File, raster) { TextSpans = [], DrawGraphics = false };
        var pageMatrix = DocumentRenderer.PageMatrix(page, 1, out _, out _);
        interpreter.Render(page, pageMatrix);
        var spans = interpreter.TextSpans;
        var textMatrix = new Matrix(0, 1, -1, 0, 100, 100);
        var ctm = new Matrix(1.5, 0.2, 0.3, 0.8, 7, 11).Then(pageMatrix);
        var start = textMatrix.Then(ctm).Apply(0, 3);
        var next = textMatrix.Then(ctm).Apply(0, -9);
        Assert.Equal(start.X, spans[0].X, 8);
        Assert.Equal(start.Y, spans[0].Y, 8);
        Assert.Equal(next.X, spans[1].X, 8);
        Assert.Equal(next.Y, spans[1].Y, 8);
        Assert.Equal(1200, spans[0].Advance, 8);
        Assert.Equal(Math.Sqrt(Math.Pow(next.X - start.X, 2) + Math.Pow(next.Y - start.Y, 2)), spans[0].Width, 8);
        var trm = Matrix.Translate(-0.2, -0.7).Then(new Matrix(5, 0, 0, 10, 0, 3)).Then(textMatrix).Then(ctm);
        var corners = new[] { trm.Apply(0, -0.25), trm.Apply(0.6, -0.25), trm.Apply(0, 0.95), trm.Apply(0.6, 0.95) };
        var glyph = Assert.Single(Glyphs(spans[0]));
        Assert.Equal(corners.Min(p => p.X), glyph.X0, 8);
        Assert.Equal(corners.Max(p => p.X), glyph.X1, 8);
        Assert.Equal(corners.Min(p => p.Y), glyph.Y0, 8);
        Assert.Equal(corners.Max(p => p.Y), glyph.Y1, 8);
    }

    [Fact]
    public void Vertical_metrics_are_ignored_by_horizontal_fonts()
    {
        const string content = "BT /F1 10 Tf 50 Tz 2 Tc 3 Ts 100 150 Td [<0001> 200 <0002>] TJ <0003> Tj ET";
        var a = Fixture(content, Metrics, "/Identity-H");
        var b = Fixture(content, "", "/Identity-H");
        Assert.Equal(DocumentRenderer.RenderPage(b.Pages[0], 72).Rgba, DocumentRenderer.RenderPage(a.Pages[0], 72).Rgba);
        var spans = DocumentRenderer.ExtractText(a.Pages[0]);
        Assert.Equal(-1600, spans[0].Advance, 8);
        Assert.Equal(8, spans[0].Width, 8);
        Assert.Equal(108, spans[1].X, 8);
        Assert.Equal(47, spans[1].Y, 8);
    }

    static ContentInterpreter.TextGlyph[] Glyphs(ContentInterpreter.TextSpan span) =>
        span.Pieces!.SelectMany(p => p.Glyphs ?? []).ToArray();

    static PdfDocument Fixture(string content, string metrics, string encoding = "/Identity-V", int rotation = 0)
    {
        var source = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cjk-identity.pdf"));
        var resources = Assert.IsType<PdfDictionary>(source.File.Resolve(source.Pages[0].Resources.Get("Font")));
        var original = Assert.IsType<PdfDictionary>(source.File.Resolve(resources.Get("F1")));
        var font = new PdfFont(source.File, original, new NoFallback());
        var program = source.File.DecodeStream(font.FontFile!);
        var tt = new TrueTypeFont(program);
        var editor = PdfEditor.Open(TestPdf.Build(""));
        PdfRef Stream(byte[] data, string dictionary = "<< >>") => editor.AddObject(new PdfStream(Dict(dictionary), data));
        var file = Stream(program);
        var map = new byte[10];
        for (var i = 1; i <= 4; i++)
        {
            var gid = tt.LookupAny('A' + (i - 1) % 3);
            Assert.NotEqual(0, gid);
            map[2 * i] = (byte)(gid >> 8); map[2 * i + 1] = (byte)gid;
        }
        var gidMap = Stream(map);
        var unicode = Stream(Encoding.ASCII.GetBytes(encoding == "single-byte"
            ? "4 beginbfchar <20> <0041> <21> <0042> <22> <0043> <23> <0044> endbfchar"
            : "4 beginbfchar <0001> <0041> <0002> <0042> <0003> <0043> <0004> <0044> endbfchar"));
        if (!encoding.StartsWith('/'))
        {
            var cmap = Stream(Encoding.ASCII.GetBytes(encoding == "single-byte"
                ? "begincmap /WMode 1 def 1 begincodespacerange <20> <23> endcodespacerange " +
                  "1 begincidrange <20> <23> 1 endcidrange endcmap"
                : $"begincmap /WMode {(encoding == "dictionary" ? 0 : 1)} def 1 begincodespacerange <0000> <FFFF> endcodespacerange " +
                  "1 begincidrange <0001> <0004> 1 endcidrange endcmap"),
                encoding == "dictionary" ? "<< /WMode 1 >>" : "<< >>");
            encoding = $"{cmap.Number} 0 R";
        }
        var cid = editor.AddObject(Dict($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /TestVera " +
            $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor << /Type /FontDescriptor /FontName /TestVera /Flags 4 /FontBBox [0 -200 1000 900] " +
            $"/ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 {file.Number} 0 R >> " +
            $"/CIDToGIDMap {gidMap.Number} 0 R {Widths} {metrics} >>"));
        var type0 = editor.AddObject(Dict($"<< /Type /Font /Subtype /Type0 /BaseFont /TestVera /Encoding {encoding} " +
            $"/DescendantFonts [{cid.Number} 0 R] /ToUnicode {unicode.Number} 0 R >>"));
        editor.SetOverlay(0, Encoding.ASCII.GetBytes(content),
            new Dictionary<string, IReadOnlyDictionary<string, PdfObject>> { ["Font"] = new Dictionary<string, PdfObject> { ["F1"] = type0 } });
        editor.SetRotation(0, rotation);
        return PdfDocument.Load(editor.Save());
    }

    static PdfDictionary Dict(string source) =>
        Assert.IsType<PdfDictionary>(new PdfLexer(Encoding.ASCII.GetBytes(source)).ReadObject());

    sealed class NoFallback : IFontFallback
    {
        public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif) => null;
    }
}
