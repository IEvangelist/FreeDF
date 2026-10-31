using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.FallbackFonts;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

public class FallbackFontTests
{
    static readonly string Root = Path.Combine(AppContext.BaseDirectory, "fallback-fonts");
    static BundledFontFallback Bundled(IFontFallback? inner = null) => new(new DirectoryAssetSource(Root), inner);

    static PdfFont Font(string dict, IFontFallback? fallback = null) =>
        new(PdfDocument.Load(TestPdf.Build("")).File, Assert.IsType<PdfDictionary>(new PdfLexer(Encoding.ASCII.GetBytes(dict)).ReadObject(false)), fallback ?? Bundled());

    [Fact]
    public void LicenceManifestCoversEveryBundledFile()
    {
        using var s = typeof(CjkIndex).Assembly.GetManifestResourceStream("PortableDocument.FallbackFonts.licenses.json")!;
        using var doc = JsonDocument.Parse(s);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in doc.RootElement.GetProperty("components").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("license").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("source").GetString()));
            if (c.TryGetProperty("licenseFile", out var lf)) Assert.True(File.Exists(Path.Combine(Root, lf.GetString()!)), lf.GetString());
            foreach (var f in c.GetProperty("files").EnumerateArray())
            {
                var path = f.GetString()!;
                listed.Add(path);
                if (!path.EndsWith("**", StringComparison.Ordinal)) Assert.True(File.Exists(Path.Combine(Root, path)), path);
            }
        }
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "latin")))
            Assert.Contains("latin/" + Path.GetFileName(file), listed);
        Assert.Contains("cjk/**", listed);
        Assert.True(Directory.GetFiles(Path.Combine(Root, "licenses"), "*OFL*").Length >= 2);
    }

    [Fact]
    public void AssetManifestHashesMatchTheShippedFiles()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "manifest.json")));
        var count = 0;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var bytes = File.ReadAllBytes(Path.Combine(Root, a.GetProperty("path").GetString()!));
            Assert.Equal(a.GetProperty("size").GetInt64(), bytes.Length);
            Assert.Equal(a.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            count++;
        }
        Assert.True(count > 13);
    }

    [Fact]
    public void FontDataIsNotCompiledIntoTheAssembly()
    {
        var assembly = typeof(BundledFontFallback).Assembly;
        Assert.True(new FileInfo(assembly.Location).Length < 200_000);
        Assert.DoesNotContain(assembly.GetManifestResourceNames(), n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || n.Contains("cjk/", StringComparison.Ordinal));
    }

    [Fact]
    public void StartupAssembliesCarryOnlyTheCompactMetricsTable()
    {
        var content = typeof(PdfFont).Assembly;
        var resources = content.GetManifestResourceNames();
        Assert.DoesNotContain(resources, n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || n.Contains("cjk", StringComparison.OrdinalIgnoreCase) && !n.Contains("cmap", StringComparison.OrdinalIgnoreCase));
        long Size(string name)
        {
            using var s = content.GetManifestResourceStream(name)!;
            return s.Length;
        }
        Assert.True(Size("PortableDocument.Content.Resources.std14-widths.bin") < 20_000, "std14 table grew");
        using var notice = new StreamReader(content.GetManifestResourceStream(Std14Metrics.NoticeResourceName)!);
        var text = notice.ReadToEnd();
        Assert.Contains("Adobe Systems Incorporated", text);
        Assert.Contains("may be used, copied, and distributed for any purpose and without charge", text);
        Assert.Contains("Adobe", File.ReadAllText(Path.Combine(Root, "licenses", "Adobe-Core14-AFM-NOTICE.txt")));
    }

    [Fact]
    public void ThirdPartyNoticesListEveryComponent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "THIRD-PARTY-NOTICES.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir.FullName, "THIRD-PARTY-NOTICES.md"));
        foreach (var name in new[] { "Liberation", "DejaVu", "Noto Sans", "Adobe Core 14" }) Assert.Contains(name, text);
    }

    [Fact]
    public void ManifestVerificationRejectsTruncatedAndCorruptAssets()
    {
        var manifest = AssetManifest.TryParse(File.ReadAllBytes(Path.Combine(Root, "manifest.json")))!;
        const string path = "cjk/GB1/4e.bin";
        var good = File.ReadAllBytes(Path.Combine(Root, path));
        Assert.True(manifest.Verify(path, good));
        Assert.False(manifest.Verify(path, good[..^1]));
        var flipped = (byte[])good.Clone(); flipped[good.Length / 2] ^= 0xFF;
        Assert.False(manifest.Verify(path, flipped));
        Assert.False(manifest.Verify("cjk/GB1/unlisted.bin", good));
        Assert.Null(AssetManifest.TryParse([1, 2, 3]));

        var source = new ProvidedAssetSource { Manifest = manifest };
        var fallback = new BundledFontFallback(source);
        Assert.Null(fallback.GetCjkGlyph("GB1", false, 0x4E2D));
        source.TakeRequests();
        source.Provide(path, good[..^1]);
        Assert.Null(fallback.GetCjkGlyph("GB1", false, 0x4E2D));
        Assert.False(fallback.HasPending);
        Assert.Contains(fallback.Warnings, w => w.Contains(path) && w.Contains("corrupt"));
    }

    [Fact]
    public void CorruptChunksDecodeToNothingAndWarn()
    {
        var good = File.ReadAllBytes(Path.Combine(Root, "cjk", "Japan1", "65.bin"));
        Assert.NotEmpty(CjkChunkCodec.Decode(good));
        Assert.Empty(CjkChunkCodec.Decode(good[..(good.Length / 2)]));
        var garbage = new byte[200]; new Random(7).NextBytes(garbage);
        Assert.Empty(CjkChunkCodec.Decode(garbage));

        using var bomb = new MemoryStream();
        using (var z = new System.IO.Compression.DeflateStream(bomb, System.IO.Compression.CompressionLevel.SmallestSize, true))
            z.Write(new byte[CjkChunkCodec.MaxDecodedBytes * 4]);
        Assert.True(bomb.Length < 10_000);
        Assert.Empty(CjkChunkCodec.Decode(bomb.ToArray()));

        var dir = Path.Combine(Path.GetTempPath(), "fb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "cjk", "Japan1"));
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "cjk", "Japan1", "65.bin"), good[..(good.Length / 2)]);
            var fallback = new BundledFontFallback(new DirectoryAssetSource(dir));
            Assert.Null(fallback.GetCjkGlyph("Japan1", false, 0x65E5));
            Assert.Contains(fallback.Warnings, w => w.Contains("corrupt"));
            var font = Font("<< /Subtype /Type0 /BaseFont /MSGothic /Encoding /UniJIS-UTF16-H /DescendantFonts [<< /Subtype /CIDFontType2 /BaseFont /MSGothic /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> >>] >>", fallback);
            font.GetGlyph(font.Decode([0x65, 0xE5]).First().Code);
            Assert.Contains(font.Warnings, w => w.Contains("CJK fallback data"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DirectorySourceRejectsTamperedFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "latin"));
        try
        {
            File.Copy(Path.Combine(Root, "manifest.json"), Path.Combine(dir, "manifest.json"));
            var bytes = File.ReadAllBytes(Path.Combine(Root, "latin", "LiberationSans-Regular.ttf"));
            File.WriteAllBytes(Path.Combine(dir, "latin", "LiberationSans-Regular.ttf"), bytes[..^10]);
            var fallback = new BundledFontFallback(new DirectoryAssetSource(dir), new FixedFallback());
            Assert.NotNull(fallback.Find("Arial", false, false, false, false));
            Assert.Contains(fallback.Warnings, w => w.Contains("LiberationSans-Regular.ttf"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WidthFittingIsClampedAndHasADeadZone()
    {
        PathData Glyph(string widths) => Font($"<< /Subtype /Type1 /BaseFont /Verdana /FirstChar 65 /LastChar 65 /Widths [{widths}] /Encoding /WinAnsiEncoding >>").GetGlyph('A')!;
        double MaxX(PathData g) => Enumerable.Range(0, g.Pts.Count / 2).Max(i => g.Pts[i * 2]);
        double MaxY(PathData g) => Enumerable.Range(0, g.Pts.Count / 2).Max(i => g.Pts[i * 2 + 1]);
        var natural = Font("<< /Subtype /Type1 /BaseFont /Verdana /Encoding /WinAnsiEncoding >>").GetGlyph('A')!;
        var advance = new TrueTypeFont(File.ReadAllBytes(Path.Combine(Root, "latin", "LiberationSans-Regular.ttf")));
        var adv = advance.Advance(advance.LookupAny('A'));
        Assert.Equal(natural.Pts, Glyph(((int)Math.Round(adv * 1.02)).ToString()).Pts);
        Assert.Equal(MaxX(natural) * 0.5, MaxX(Glyph("10")), 0.5);
        Assert.Equal(MaxX(natural) * 1.5, MaxX(Glyph("5000")), 0.5);
        Assert.Equal(MaxY(natural), MaxY(Glyph("5000")), 0.01);
        Assert.Equal(MaxY(natural), MaxY(Glyph("10")), 0.01);
    }

    [Theory]
    [InlineData("Arial", 0, "Helvetica")]
    [InlineData("ArialMT", 0, "Helvetica")]
    [InlineData("Arial-BoldMT", 0, "Helvetica-Bold")]
    [InlineData("ABCDEF+Arial,Italic", 0, "Helvetica-Oblique")]
    [InlineData("Helvetica-BoldOblique", 0, "Helvetica-BoldOblique")]
    [InlineData("TimesNewRomanPSMT", 0, "Times-Roman")]
    [InlineData("TimesNewRomanPS-BoldItalicMT", 0, "Times-BoldItalic")]
    [InlineData("Times-Italic", 0, "Times-Italic")]
    [InlineData("CourierNewPS-BoldMT", 0, "Courier-Bold")]
    [InlineData("Courier-Oblique", 0, "Courier-Oblique")]
    [InlineData("SymbolMT", 0, "Symbol")]
    [InlineData("ZapfDingbats", 0, "ZapfDingbats")]
    [InlineData("ITCZapfDingbats", 0, "ZapfDingbats")]
    [InlineData("ArialNarrow", 0, null)]
    [InlineData("Verdana", 0, null)]
    public void NamesResolveToStandardFonts(string baseFont, int flags, string? expected)
    {
        var c = FontSubstitution.Classify(baseFont, flags);
        Assert.Equal(expected, FontSubstitution.Std14(baseFont, c));
    }

    [Fact]
    public void DescriptorFlagsDriveWeightSlantAndFamily()
    {
        Assert.True(FontSubstitution.Classify("Foo", 1 << 18).Bold);
        Assert.True(FontSubstitution.Classify("Foo", 0, fontWeight: 700).Bold);
        Assert.False(FontSubstitution.Classify("Foo-Light", 0, stemV: 160).Bold);
        Assert.True(FontSubstitution.Classify("Foo", 0, italicAngle: -12).Italic);
        Assert.True(FontSubstitution.Classify("Foo", 64).Italic);
        Assert.True(FontSubstitution.Classify("Foo", 2).Serif);
        Assert.False(FontSubstitution.Classify("Foo-Sans", 2).Serif);
        Assert.True(FontSubstitution.Classify("Foo", 1).Mono);
        Assert.True(FontSubstitution.Classify("Georgia", 0).Serif);
        Assert.False(FontSubstitution.Classify("Courier", 2).Serif);
        Assert.True(FontSubstitution.Classify("LucidaConsole", 0).Mono);
    }

    [Fact]
    public void StandardMetricsMatchTheAdobeAfmValues()
    {
        Assert.True(Std14Metrics.Get("Helvetica")!.TryWidth("A", out var a) && a == 667);
        Assert.True(Std14Metrics.Get("Helvetica-Bold")!.TryWidth("A", out a) && a == 722);
        Assert.True(Std14Metrics.Get("Times-Roman")!.TryWidth("A", out a) && a == 722);
        Assert.True(Std14Metrics.Get("Courier-Bold")!.TryWidth("A", out a) && a == 600);
        var symbol = Std14Metrics.Get("Symbol")!;
        Assert.Equal("alpha", symbol.BuiltinName(97));
        Assert.True(symbol.TryWidth("alpha", out a) && a == 631);
        var zapf = Std14Metrics.Get("ZapfDingbats")!;
        Assert.Equal("a1", zapf.BuiltinName(33));
        Assert.True(zapf.TryWidth("a1", out a) && a == 974);
        Assert.True(Std14Metrics.Get("Helvetica")!.TryWidthByUnicode(0x00E9, out a) && a == 556);
    }

    [Theory]
    [InlineData("Sans", "Helvetica")]
    [InlineData("Serif", "Times-Roman")]
    [InlineData("Mono", "Courier")]
    public void LiberationAdvancesMatchTheStandardFonts(string family, string standard)
    {
        var font = new TrueTypeFont(File.ReadAllBytes(Path.Combine(Root, "latin", $"Liberation{family}-Regular.ttf")));
        var afm = Std14Metrics.Get(standard)!;
        var worst = 0.0;
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?()-")
        {
            var gid = font.LookupAny(c);
            Assert.True(gid > 0, $"U+{(int)c:X4} missing from Liberation {family}");
            Assert.True(afm.TryWidthByUnicode(c, out var expected));
            worst = Math.Max(worst, Math.Abs(font.Advance(gid) - expected));
        }
        Assert.True(worst <= (family == "Mono" ? 0.5 : 60), $"{family} deviates by {worst}");
    }

    [Fact]
    public void BundledFontsCoverLatinSymbolAndDingbats()
    {
        var fallback = Bundled();
        foreach (var (name, bold, italic, mono, serif) in new[]
        {
            ("Arial", false, false, false, false), ("Arial", true, false, false, false), ("Arial", false, true, false, false), ("Arial", true, true, false, false),
            ("Times", false, false, false, true), ("Times", true, false, false, true), ("Times", false, true, false, true), ("Times", true, true, false, true),
            ("Courier", false, false, true, false), ("Courier", true, false, true, false), ("Courier", false, true, true, false), ("Courier", true, true, true, false),
        })
        {
            var font = fallback.Find(name, bold, italic, mono, serif);
            Assert.NotNull(font);
            foreach (var cp in "AZaz09éüñ€\u2022\u201C") Assert.True(font.LookupAny(cp) > 0, $"{name} U+{(int)cp:X4}");
        }
        var symbol = fallback.Find("Symbol", false, false, false, false)!;
        foreach (var cp in new[] { 0x3B1, 0x3A9, 0x2200, 0x2211, 0x221E, 0x2192 }) Assert.True(symbol.LookupAny(cp) > 0, $"Symbol U+{cp:X4}");
        var zapf = fallback.Find("ZapfDingbats", false, false, false, false)!;
        foreach (var cp in new[] { 0x2701, 0x2714, 0x2756, 0x2776 }) Assert.True(zapf.LookupAny(cp) > 0, $"Zapf U+{cp:X4}");
    }

    [Fact]
    public void NonEmbeddedStandardFontUsesAfmWidthsAndFallbackOutlines()
    {
        var font = Font("<< /Subtype /TrueType /BaseFont /Arial-BoldMT /Encoding /WinAnsiEncoding >>");
        var run = Assert.Single(font.Decode([(byte)'A']));
        Assert.Equal(0.722, run.Width, 3);
        Assert.False(font.GetGlyph('A')!.IsEmpty);
        var courier = Font("<< /Subtype /Type1 /BaseFont /CourierNewPSMT /Encoding /WinAnsiEncoding >>");
        Assert.Equal(0.6, Assert.Single(courier.Decode([(byte)'i'])).Width, 3);
    }

    [Fact]
    public void SymbolAndZapfDingbatsUseTheirBuiltInEncodings()
    {
        var symbol = Font("<< /Subtype /Type1 /BaseFont /Symbol >>");
        Assert.Equal(0.631, Assert.Single(symbol.Decode([97])).Width, 3);
        Assert.False(symbol.GetGlyph(97)!.IsEmpty);
        Assert.Equal("\u03B1", symbol.ToUnicode(97));
        var zapf = Font("<< /Subtype /Type1 /BaseFont /ZapfDingbats >>");
        Assert.Equal(0.974, Assert.Single(zapf.Decode([33])).Width, 3);
        Assert.False(zapf.GetGlyph(33)!.IsEmpty);
        Assert.False(zapf.GetGlyph(0x6C)!.IsEmpty);
    }

    [Fact]
    public void GlyphsAreScaledToThePdfWidths()
    {
        double Extent(string widths)
        {
            var g = Font($"<< /Subtype /Type1 /BaseFont /Verdana /FirstChar 65 /LastChar 65 /Widths [{widths}] /Encoding /WinAnsiEncoding >>").GetGlyph('A')!;
            var xs = Enumerable.Range(0, g.Pts.Count / 2).Select(i => g.Pts[i * 2]).ToArray();
            return xs.Max() - xs.Min();
        }
        var natural = Extent("667");
        Assert.InRange(Extent("500") / natural, 0.72, 0.78);
        Assert.InRange(Extent("800") / natural, 1.15, 1.25);
    }

    [Fact]
    public void ChunkCodecRoundTripsLines_Quadratics_AndCubics()
    {
        var path = new PathData();
        path.MoveTo(0, 0); path.LineTo(500, 0);
        path.CurveTo(500 + 2.0 / 3 * 100, 0 + 2.0 / 3 * 300, 500 + 2.0 / 3 * 100 - 0, 600 + 2.0 / 3 * (300 - 600) + 0, 500, 600);
        path.CurveTo(450, 650, 50, 650, 0, 600);
        path.Close();
        var back = CjkChunkCodec.Decode(CjkChunkCodec.Encode([(0x41, path)]))[0x41];
        Assert.Equal(path.Ops, back.Ops);
        Assert.Equal(path.Pts.Count, back.Pts.Count);
        for (var i = 0; i < path.Pts.Count; i++) Assert.InRange(Math.Abs(path.Pts[i] - back.Pts[i]), 0, 1.01);
        Assert.Empty(CjkChunkCodec.Decode([1, 2, 3]));
    }

    [Fact]
    public void CjkIndexMatchesTheChunkFiles()
    {
        foreach (var ordering in CjkIndex.Orderings)
        {
            var present = 0;
            for (var block = 0; block < 256; block++)
            {
                var exists = File.Exists(Path.Combine(Root, CjkIndex.ChunkPath(ordering, block)));
                Assert.Equal(exists, CjkIndex.HasBlock(ordering, block));
                if (exists) present++;
            }
            Assert.True(present > 20, ordering);
        }
    }

    [Theory]
    [InlineData("Japan1", 0x65E5)]
    [InlineData("Japan1", 0x3042)]
    [InlineData("GB1", 0x4E2D)]
    [InlineData("CNS1", 0x4E2D)]
    [InlineData("Korea1", 0xD55C)]
    public void CjkGlyphsLoadFromChunks(string ordering, int codePoint)
    {
        var glyph = Bundled().GetCjkGlyph(ordering, false, codePoint);
        Assert.NotNull(glyph);
        Assert.False(glyph.IsEmpty);
        foreach (var v in glyph.Pts) Assert.InRange(v, -300, 1300);
    }

    [Fact]
    public void NonEmbeddedCjkFontRendersFromBundledData()
    {
        var font = Font("<< /Subtype /Type0 /BaseFont /MSGothic /Encoding /UniJIS-UTF16-H /DescendantFonts [<< /Subtype /CIDFontType2 /BaseFont /MSGothic /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> /DW 1000 >>] >>");
        var run = Assert.Single(font.Decode([0x65, 0xE5]));
        Assert.True(font.HasOutlines);
        var glyph = font.GetGlyph(run.Code);
        Assert.True(glyph is not null, $"code={run.Code} raw={run.Raw} len={run.ByteLength} uni={font.ToUnicode(run)}");
        Assert.False(glyph.IsEmpty);
    }

    [Fact]
    public void PendingAssetsAreRequestedOnceThenFallBackWhenUnavailable()
    {
        var source = new ProvidedAssetSource();
        var inner = new FixedFallback();
        var fallback = new BundledFontFallback(source, inner);
        Assert.Null(fallback.Find("Arial", false, false, false, false));
        Assert.Null(fallback.Find("Arial", false, false, false, false));
        Assert.True(fallback.HasPending);
        Assert.Equal(["latin/LiberationSans-Regular.ttf"], source.TakeRequests());
        Assert.Empty(source.TakeRequests());
        source.Provide("latin/LiberationSans-Regular.ttf", null);
        Assert.False(fallback.HasPending);
        Assert.Same(inner.Font, fallback.Find("Arial", false, false, false, false));

        source.Provide("latin/LiberationSerif-Regular.ttf", File.ReadAllBytes(Path.Combine(Root, "latin", "LiberationSerif-Regular.ttf")));
        Assert.NotSame(inner.Font, fallback.Find("Times", false, false, false, true));

        Assert.Null(fallback.GetCjkGlyph("GB1", false, 0x4E2D));
        var chunk = Assert.Single(source.TakeRequests());
        Assert.Equal("cjk/GB1/4e.bin", chunk);
        Assert.True(fallback.HasPending);
        source.Provide(chunk, File.ReadAllBytes(Path.Combine(Root, chunk)));
        Assert.NotNull(fallback.GetCjkGlyph("GB1", false, 0x4E2D));
        Assert.False(fallback.HasPending);
    }

    [Fact]
    public void NonEmbeddedTextRendersDeterministically()
    {
        var pdf = TestPdf.Build("BT /F1 24 Tf 10 150 Td (Hamburgefonts) Tj 0 -40 Td /F2 24 Tf (Hamburgefonts) Tj 0 -40 Td /F3 24 Tf (Hamburgefonts) Tj ET",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> /F2 << /Type /Font /Subtype /Type1 /BaseFont /Times-Italic >> /F3 << /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold >> >>");
        var options = new PdfRenderOptions { FontFallback = Bundled() };
        var a = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], options, 72);
        var b = DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], options, 72);
        Assert.Equal(a.Rgba, b.Rgba);
        var ink = 0;
        for (var i = 0; i < a.Rgba.Length; i += 4) if (a.Rgba[i] < 128) ink++;
        Assert.InRange(ink, 400, 8000);
    }

    [Theory]
    [InlineData("nonembedded-latin.pdf", 1500)]
    [InlineData("nonembedded-symbol-dingbats.pdf", 400)]
    [InlineData("nonembedded-cjk.pdf", 1500)]
    public void NonEmbeddedFixturesDrawBundledGlyphs(string file, int minInk)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts-nonembedded", file);
        var options = new PdfRenderOptions { FontFallback = Bundled() };
        var page = DocumentRenderer.RenderPage(PdfDocument.Open(path).Pages[0], options, 96);
        var ink = 0;
        for (var i = 0; i < page.Rgba.Length; i += 4) if (page.Rgba[i] < 128) ink++;
        Assert.True(ink >= minInk, $"{file}: {ink} dark pixels");
        Assert.DoesNotContain(page.Warnings, w => w.Contains("unavailable") || w.Contains("corrupt"));
    }

    sealed class FixedFallback : IFontFallback
    {
        public TrueTypeFont Font { get; } = new(File.ReadAllBytes(Path.Combine(Root, "latin", "LiberationMono-Regular.ttf")));
        public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif) => Font;
    }
}
