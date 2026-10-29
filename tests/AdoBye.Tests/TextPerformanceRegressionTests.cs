using System.Buffers.Binary;
using AdoBye.Content;
using AdoBye.Core;
using AdoBye.Document;
using AdoBye.Render;

namespace AdoBye.Tests;

public class TextPerformanceRegressionTests
{
    [Theory]
    [InlineData("123", 123, true)]
    [InlineData("--5", -5, true)]
    [InlineData("+-+5", -5, true)]
    [InlineData(".", 0, false)]
    [InlineData(".junk", 0, false)]
    [InlineData("1..2", 1, false)]
    [InlineData("--+5.25junk", -5.25, false)]
    [InlineData("1.25e3", 1.25, false)]
    [InlineData("9223372036854775808", 9223372036854775808d, false)]
    public void Span_numeric_parser_preserves_tolerant_number_semantics(string token, double expected, bool integer)
    {
        var lexer = new PdfLexer(System.Text.Encoding.Latin1.GetBytes(token));
        var value = lexer.ReadObject(false);
        Assert.Equal(expected, value.AsNumber());
        Assert.Equal(integer, value is PdfInteger);
        Assert.Equal(token.Length, lexer.Pos);
    }

    [Theory]
    [InlineData("5-")]
    [InlineData("--")]
    [InlineData("1e3")]
    [InlineData("5oops")]
    public void Invalid_numeric_tokens_remain_keywords(string token)
    {
        var value = new PdfLexer(System.Text.Encoding.Latin1.GetBytes(token)).ReadObject(false);
        Assert.Equal(token, Assert.IsType<PdfKeyword>(value).Value);
    }

    [Fact]
    public void Preferred_cmaps_preserve_priority_fallback_and_first_duplicate()
    {
        var font = new TrueTypeFont(FontWithCmaps(
            (0, 3, 8, 9), (3, 1, 3, 0), (3, 1, 7, 7)));
        Assert.True(font.HasCmap(3, 1));
        Assert.False(font.HasCmap(3, 10));
        Assert.Equal(3, font.LookupAny('A'));
        Assert.Equal(9, font.LookupAny('B'));
        Assert.Equal(0, font.LookupAny('C'));
        Assert.Equal(3, font.Lookup(3, 1, 'A'));
    }

    [Fact]
    public void Repeated_cmap_lookup_does_not_allocate()
    {
        var font = new TrueTypeFont(FontWithCmaps((0, 3, 8, 9), (3, 1, 3, 0)));
        for (var i = 0; i < 100; i++)
        {
            font.HasCmap(3, 1);
            font.LookupAny('B');
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (font.HasCmap(3, 1)) sum += font.LookupAny('B');
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(9000, sum);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("type1-pfa.pdf", "ABCDEF")]
    [InlineData("cjk-rksj.pdf", "AB\u3042")]
    public void Repeated_text_extraction_preserves_glyph_bytes_geometry_and_advances(string file, string expected)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", file));
        var page = PdfDocument.Load(bytes).Pages[0];
        var first = PdfRenderer.ExtractText(page);
        var second = PdfRenderer.ExtractText(page);
        Assert.Equal(expected, string.Concat(first.Select(s => s.Text)));
        Assert.Equal(first.Select(s => (s.Text, s.X, s.Y, s.Width, s.Advance)),
            second.Select(s => (s.Text, s.X, s.Y, s.Width, s.Advance)));
        var glyphs = first.SelectMany(s => s.Pieces!).Where(p => p.Glyphs is not null)
            .SelectMany(p => p.Glyphs!).ToArray();
        var repeatedGlyphs = second.SelectMany(s => s.Pieces!).Where(p => p.Glyphs is not null)
            .SelectMany(p => p.Glyphs!).ToArray();
        Assert.Equal(expected.Length, glyphs.Length);
        for (var i = 0; i < glyphs.Length; i++)
        {
            Assert.Equal(glyphs[i].Bytes, repeatedGlyphs[i].Bytes);
            Assert.Equal((glyphs[i].X0, glyphs[i].Y0, glyphs[i].X1, glyphs[i].Y1, glyphs[i].Adv),
                (repeatedGlyphs[i].X0, repeatedGlyphs[i].Y0, repeatedGlyphs[i].X1, repeatedGlyphs[i].Y1, repeatedGlyphs[i].Adv));
            Assert.True(double.IsFinite(glyphs[i].Adv));
        }
    }

    [Theory]
    [InlineData("fonts")]
    [InlineData("features")]
    [InlineData("graphics")]
    public void Text_only_interpretation_matches_full_analysis(string directory)
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", directory), "*.pdf");
        Assert.NotEmpty(files);
        foreach (var file in files)
            foreach (var page in PdfDocument.Open(file).Pages)
            {
                var full = PdfRenderer.Analyze(page).Spans;
                var textOnly = PdfRenderer.ExtractText(page);
                Assert.Equal(full.Count, textOnly.Count);
                for (var i = 0; i < full.Count; i++)
                {
                    Assert.Equal(full[i] with { Font = null, Pieces = null },
                        textOnly[i] with { Font = null, Pieces = null });
                    var fullGlyphs = full[i].Pieces?.SelectMany(p => p.Glyphs ?? []).ToArray() ?? [];
                    var textGlyphs = textOnly[i].Pieces?.SelectMany(p => p.Glyphs ?? []).ToArray() ?? [];
                    Assert.Equal(fullGlyphs.Length, textGlyphs.Length);
                    for (var j = 0; j < fullGlyphs.Length; j++)
                    {
                        Assert.Equal(fullGlyphs[j].Bytes, textGlyphs[j].Bytes);
                        Assert.Equal(fullGlyphs[j], textGlyphs[j] with { Bytes = fullGlyphs[j].Bytes });
                    }
                }
            }
    }

    [Fact]
    public void Text_only_paths_preserve_text_transforms_color_and_state_restoration()
    {
        var paths = "0 0 m 10 10 l 10 15 15 20 20 20 c 25 25 30 30 v 35 35 40 40 y h 0 0 50 50 re ";
        var content = "q 2 0 0 3 10 20 cm 0.2 0.3 0.4 rg ";
        foreach (var paint in new[] { "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "W n", "W* n", "n" })
            content += paths + paint + " ";
        content += "BT /F1 12 Tf 1 0 0 1 4 5 Tm (AB) Tj ET Q BT /F1 12 Tf 7 11 Td (CD) Tj ET";
        var page = PdfDocument.Load(TestPdf.Build(content,
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>")).Pages[0];
        var full = PdfRenderer.Analyze(page).Spans;
        var textOnly = PdfRenderer.ExtractText(page);
        Assert.Equal("ABCD", string.Concat(textOnly.Select(s => s.Text)));
        Assert.Equal(full.Select(s => s with { Font = null, Pieces = null }),
            textOnly.Select(s => s with { Font = null, Pieces = null }));
        Assert.Equal((18d, 165d), (textOnly[0].X, textOnly[0].Y));
        Assert.Equal((7d, 189d), (textOnly[1].X, textOnly[1].Y));
    }

    static byte[] FontWithCmaps(params (ushort Platform, ushort Encoding, byte A, byte B)[] maps)
    {
        var data = new byte[32 + maps.Length * (8 + 262)];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
        U32(0, 0x00010000);
        U16(4, 1);
        "cmap"u8.CopyTo(data.AsSpan(12));
        U32(20, 28);
        U32(24, (uint)(data.Length - 28));
        U16(30, (ushort)maps.Length);
        for (var i = 0; i < maps.Length; i++)
        {
            var record = 32 + i * 8;
            var subtable = 32 + maps.Length * 8 + i * 262;
            U16(record, maps[i].Platform);
            U16(record + 2, maps[i].Encoding);
            U32(record + 4, (uint)(subtable - 28));
            U16(subtable + 2, 262);
            data[subtable + 6 + 'A'] = maps[i].A;
            data[subtable + 6 + 'B'] = maps[i].B;
        }
        return data;
    }
}
