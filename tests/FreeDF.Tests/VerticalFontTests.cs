using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Vertical_font_applies_vert_substitution_without_changing_vertical_metrics(int subtableFormat)
    {
        const string content = "BT /F1 10 Tf 100 150 Td <0001> Tj <0002> Tj ET";
        var source = FontProgram();
        var tt = new TrueTypeFont(source);
        var sourceGlyph = tt.LookupAny('A');
        var expectedGlyph = tt.LookupAny('B');
        var gsub = SingleSubstitution([("vert", sourceGlyph, expectedGlyph)], 1, subtableFormat);

        var substituted = Fixture(content, Metrics, gsub: gsub);
        var expected = Fixture(content, Metrics, cidOneGlyph: expectedGlyph);
        var original = Fixture(content, Metrics);
        Assert.Equal(DocumentRenderer.RenderPage(expected.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(substituted.Pages[0], 72).Rgba);
        Assert.NotEqual(DocumentRenderer.RenderPage(original.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(substituted.Pages[0], 72).Rgba);

        var spans = DocumentRenderer.ExtractText(substituted.Pages[0]);
        Assert.Equal("AB", string.Concat(spans.Select(span => span.Text)));
        Assert.Equal(1200, spans[0].Advance, 8);
        Assert.Equal(62, spans[1].Y, 8);
    }

    [Fact]
    public void Script_specific_vertical_feature_uses_the_unicode_script_tag()
    {
        var source = FontProgram();
        var originalFont = new TrueTypeFont(source);
        var sourceGlyph = originalFont.LookupAny('A');
        var expectedGlyph = originalFont.LookupAny('B');
        var font = new TrueTypeFont(AddGsub(source, SingleSubstitution(
            [("vert", sourceGlyph, expectedGlyph)], 1, 2, "hani")));

        Assert.Equal(expectedGlyph, font.SubstituteVertical(sourceGlyph, "\u4E00"));
        Assert.Equal(sourceGlyph, font.SubstituteVertical(sourceGlyph, "A"));
    }

    [Fact]
    public void OpenType_CFF_outlines_use_the_same_vertical_GSUB_mapping()
    {
        var program = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "SourceCodePro-Regular.otf"));
        var sourceFont = new CffFont(program);
        var sourceGlyph = sourceFont.GlyphByName("A");
        var expectedGlyph = sourceFont.GlyphByName("B");
        var gsubProgram = AddGsub(program, SingleSubstitution(("vert", sourceGlyph, expectedGlyph)));

        var editor = PdfEditor.Open(TestPdf.Build(""));
        var fontStream = editor.AddObject(new PdfStream(Dict("<< /Subtype /OpenType >>"), gsubProgram));
        var descriptor = editor.AddObject(Dict(
            $"<< /Type /FontDescriptor /FontName /TestCff /Flags 4 /FontBBox [0 -200 1000 900] " +
            $"/ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 {fontStream.Number} 0 R >>"));
        var cidFont = editor.AddObject(Dict(
            $"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /TestCff " +
            $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {descriptor.Number} 0 R /DW2 [900 -1000] >>"));
        var type0 = editor.AddObject(Dict(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /TestCff /Encoding /Identity-V " +
            $"/DescendantFonts [{cidFont.Number} 0 R] >>"));
        editor.SetOverlay(0, Encoding.ASCII.GetBytes("BT /F1 10 Tf 100 150 Td <0001> Tj ET"),
            new Dictionary<string, IReadOnlyDictionary<string, PdfObject>>
            {
                ["Font"] = new Dictionary<string, PdfObject> { ["F1"] = type0 }
            });
        var document = PdfDocument.Load(editor.Save());
        var resources = Assert.IsType<PdfDictionary>(document.File.Resolve(document.Pages[0].Resources.Get("Font")));
        var font = new PdfFont(document.File, Assert.IsType<PdfDictionary>(document.File.Resolve(resources.Get("F1"))), new NoFallback());
        var actual = Assert.IsType<PathData>(font.GetGlyph(sourceGlyph, "A"));
        var expected = Assert.IsType<PathData>(sourceFont.GetOutline(expectedGlyph));

        Assert.Equal(expected.Ops, actual.Ops);
        Assert.Equal(expected.Pts, actual.Pts);
    }

    [Fact]
    public void Vrt2_takes_precedence_over_vert_and_horizontal_text_is_unchanged()
    {
        const string content = "BT /F1 10 Tf 100 150 Td <0001> Tj ET";
        var source = FontProgram();
        var tt = new TrueTypeFont(source);
        var sourceGlyph = tt.LookupAny('A');
        var vertGlyph = tt.LookupAny('B');
        var vrt2Glyph = tt.LookupAny('C');
        var gsub = SingleSubstitution(
            ("vert", sourceGlyph, vertGlyph),
            ("vrt2", sourceGlyph, vrt2Glyph));

        var vertical = Fixture(content, Metrics, gsub: gsub);
        var expectedVertical = Fixture(content, Metrics, cidOneGlyph: vrt2Glyph);
        Assert.Equal(DocumentRenderer.RenderPage(expectedVertical.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(vertical.Pages[0], 72).Rgba);

        var horizontal = Fixture(content, Metrics, "/Identity-H", gsub: gsub);
        var expectedHorizontal = Fixture(content, Metrics, "/Identity-H");
        Assert.Equal(DocumentRenderer.RenderPage(expectedHorizontal.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(horizontal.Pages[0], 72).Rgba);
    }

    [Fact]
    public void Missing_vertical_feature_keeps_original_glyph_without_warning()
    {
        const string content = "BT /F1 10 Tf 100 150 Td <0001> Tj ET";
        var tt = new TrueTypeFont(FontProgram());
        var gsub = SingleSubstitution(("liga", tt.LookupAny('A'), tt.LookupAny('B')));
        var withUnrelatedFeature = Fixture(content, Metrics, gsub: gsub);
        var original = Fixture(content, Metrics);
        var rendered = DocumentRenderer.RenderPage(withUnrelatedFeature.Pages[0], 72);

        Assert.Equal(DocumentRenderer.RenderPage(original.Pages[0], 72).Rgba,
            rendered.Rgba);
        Assert.DoesNotContain(rendered.Warnings, warning => warning.Contains("GSUB", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Malformed_or_unsupported_vertical_lookup_keeps_glyph_and_reports_warning(bool malformed)
    {
        const string content = "BT /F1 10 Tf 100 150 Td <0001> Tj ET";
        var tt = new TrueTypeFont(FontProgram());
        var gsub = SingleSubstitution([("vert", tt.LookupAny('A'), tt.LookupAny('B'))], malformed ? 1 : 4);
        if (malformed) gsub = gsub[..^1];
        var page = Fixture(content, Metrics, gsub: gsub).Pages[0];
        var original = Fixture(content, Metrics).Pages[0];

        var rendered = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal(DocumentRenderer.RenderPage(original, 72).Rgba, rendered.Rgba);
        Assert.Contains(rendered.Warnings, warning => warning.Contains("GSUB", StringComparison.Ordinal));
    }

    static ContentInterpreter.TextGlyph[] Glyphs(ContentInterpreter.TextSpan span) =>
        span.Pieces!.SelectMany(p => p.Glyphs ?? []).ToArray();

    static PdfDocument Fixture(string content, string metrics, string encoding = "/Identity-V", int rotation = 0,
        byte[]? gsub = null, int? cidOneGlyph = null)
    {
        var source = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cjk-identity.pdf"));
        var resources = Assert.IsType<PdfDictionary>(source.File.Resolve(source.Pages[0].Resources.Get("Font")));
        var original = Assert.IsType<PdfDictionary>(source.File.Resolve(resources.Get("F1")));
        var font = new PdfFont(source.File, original, new NoFallback());
        var program = source.File.DecodeStream(font.FontFile!);
        if (gsub is not null) program = AddGsub(program, gsub);
        var tt = new TrueTypeFont(program);
        var editor = PdfEditor.Open(TestPdf.Build(""));
        PdfRef Stream(byte[] data, string dictionary = "<< >>") => editor.AddObject(new PdfStream(Dict(dictionary), data));
        var file = Stream(program);
        var map = new byte[10];
        for (var i = 1; i <= 4; i++)
        {
            var gid = i == 1 && cidOneGlyph.HasValue ? cidOneGlyph.Value : tt.LookupAny('A' + (i - 1) % 3);
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

    static byte[] FontProgram()
    {
        var source = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cjk-identity.pdf"));
        var resources = Assert.IsType<PdfDictionary>(source.File.Resolve(source.Pages[0].Resources.Get("Font")));
        var original = Assert.IsType<PdfDictionary>(source.File.Resolve(resources.Get("F1")));
        var font = new PdfFont(source.File, original, new NoFallback());
        return source.File.DecodeStream(font.FontFile!);
    }

    static byte[] SingleSubstitution(params (string Tag, int Source, int Target)[] features)
        => SingleSubstitution(features, 1, 2, "DFLT");

    static byte[] SingleSubstitution((string Tag, int Source, int Target)[] features, int lookupType)
        => SingleSubstitution(features, lookupType, 2, "DFLT");

    static byte[] SingleSubstitution((string Tag, int Source, int Target)[] features, int lookupType,
        int subtableFormat, string scriptTag = "DFLT")
    {
        var data = new List<byte>([0, 1, 0, 0, 0, 0, 0, 0, 0, 0]);
        void U16(int value) { data.Add((byte)(value >> 8)); data.Add((byte)value); }
        void Tag(string value) => data.AddRange(Encoding.ASCII.GetBytes(value));
        void Patch16(int offset, int value) { data[offset] = (byte)(value >> 8); data[offset + 1] = (byte)value; }

        Patch16(4, data.Count);
        var scriptList = data.Count;
        U16(1); Tag(scriptTag); U16(8);
        U16(4); U16(0); U16(0); U16(0xFFFF); U16(features.Length);
        for (var i = 0; i < features.Length; i++) U16(i);

        Patch16(6, data.Count);
        var featureList = data.Count;
        U16(features.Length);
        var featureOffsets = data.Count;
        for (var i = 0; i < features.Length; i++) { Tag(features[i].Tag); U16(0); }
        var featureData = data.Count;
        for (var i = 0; i < features.Length; i++)
        {
            Patch16(featureOffsets + i * 6 + 4, featureData - featureList);
            U16(0); U16(1); U16(i);
            featureData += 6;
        }

        Patch16(8, data.Count);
        var lookupList = data.Count;
        U16(features.Length);
        var lookupOffsets = data.Count;
        for (var i = 0; i < features.Length; i++) U16(0);
        for (var i = 0; i < features.Length; i++)
        {
            Patch16(lookupOffsets + i * 2, data.Count - lookupList);
            U16(lookupType); U16(0); U16(1); U16(8);
            U16(subtableFormat); U16(subtableFormat == 1 ? 6 : 8);
            if (subtableFormat == 1) U16((features[i].Target - features[i].Source) & 0xFFFF);
            else { U16(1); U16(features[i].Target); }
            U16(1); U16(1); U16(features[i].Source);
        }
        return data.ToArray();
    }

    static byte[] AddGsub(byte[] font, byte[] gsub)
    {
        int U16(int offset) => font[offset] << 8 | font[offset + 1];
        int U32(int offset) => (int)((uint)font[offset] << 24 | (uint)font[offset + 1] << 16 | (uint)font[offset + 2] << 8 | font[offset + 3]);
        void Put16(byte[] output, int offset, int value) { output[offset] = (byte)(value >> 8); output[offset + 1] = (byte)value; }
        void Put32(byte[] output, int offset, uint value)
        {
            output[offset] = (byte)(value >> 24); output[offset + 1] = (byte)(value >> 16);
            output[offset + 2] = (byte)(value >> 8); output[offset + 3] = (byte)value;
        }
        uint Checksum(byte[] bytes)
        {
            uint sum = 0;
            for (var i = 0; i < bytes.Length; i += 4)
                sum += (uint)(bytes[i] << 24 | (i + 1 < bytes.Length ? bytes[i + 1] << 16 : 0) |
                    (i + 2 < bytes.Length ? bytes[i + 2] << 8 : 0) | (i + 3 < bytes.Length ? bytes[i + 3] : 0));
            return sum;
        }

        var sfnt = U32(0) == 0x74746366 ? U32(12) : 0;
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var count = U16(sfnt + 4);
        for (var i = 0; i < count; i++)
        {
            var record = sfnt + 12 + i * 16;
            var name = Encoding.ASCII.GetString(font, record, 4);
            var offset = U32(record + 8);
            var length = U32(record + 12);
            tables[name] = font[offset..(offset + length)];
        }
        tables["GSUB"] = gsub;
        if (tables.TryGetValue("head", out var head) && head.Length >= 12) Array.Clear(head, 8, 4);

        var output = new byte[12 + tables.Count * 16];
        Array.Copy(font, sfnt, output, 0, 4);
        var tableSize = tables.Count * 16;
        var searchRange = 16;
        var entrySelector = 0;
        while (searchRange * 2 <= tableSize) { searchRange *= 2; entrySelector++; }
        Put16(output, 4, tables.Count); Put16(output, 6, searchRange);
        Put16(output, 8, entrySelector); Put16(output, 10, tableSize - searchRange);

        var payload = new List<byte>();
        var headOffset = -1;
        var index = 0;
        foreach (var (name, bytes) in tables)
        {
            while (payload.Count % 4 != 0) payload.Add(0);
            var offset = output.Length + payload.Count;
            var record = 12 + index++ * 16;
            Encoding.ASCII.GetBytes(name).CopyTo(output, record);
            Put32(output, record + 4, Checksum(bytes));
            Put32(output, record + 8, (uint)offset); Put32(output, record + 12, (uint)bytes.Length);
            if (name == "head") headOffset = offset;
            payload.AddRange(bytes);
        }
        var result = new byte[output.Length + payload.Count];
        output.CopyTo(result, 0); payload.CopyTo(result, output.Length);
        if (headOffset >= 0)
        {
            var adjustment = 0xB1B0AFBAu - Checksum(result);
            Put32(result, headOffset + 8, adjustment);
        }
        return result;
    }

    static PdfDictionary Dict(string source) =>
        Assert.IsType<PdfDictionary>(new PdfLexer(Encoding.ASCII.GetBytes(source)).ReadObject());

    sealed class NoFallback : IFontFallback
    {
        public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif) => null;
    }
}
