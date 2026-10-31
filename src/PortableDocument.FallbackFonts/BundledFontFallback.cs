using PortableDocument.Content;

namespace PortableDocument.FallbackFonts;

/// <summary>Which 256-scalar blocks have glyph data per CJK ordering (from the embedded <c>cjk-index.bin</c>: one 32-byte bitmap per ordering).</summary>
public static class CjkIndex
{
    public static readonly string[] Orderings = ["Japan1", "GB1", "CNS1", "Korea1"];
    static readonly Lazy<byte[]> Data = new(() =>
    {
        using var s = typeof(CjkIndex).Assembly.GetManifestResourceStream("PortableDocument.FallbackFonts.cjk-index.bin");
        if (s is null) return [];
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });

    public static bool HasBlock(string ordering, int block)
    {
        var i = Array.IndexOf(Orderings, ordering);
        if (i < 0 || block is < 0 or > 255) return false;
        var d = Data.Value;
        var at = i * 32 + (block >> 3);
        return at < d.Length && (d[at] >> (block & 7) & 1) != 0;
    }

    public static string ChunkPath(string ordering, int block) => $"cjk/{ordering}/{block:x2}.bin";
}

/// <summary>
/// Non-embedded font substitutes that do not depend on the host: metric-compatible Liberation Sans/Serif/Mono (Helvetica, Times, Courier families),
/// DejaVu Sans for Symbol and ZapfDingbats, and Noto Sans CJK outlines for the Adobe-Japan1/GB1/CNS1/Korea1 orderings, all read through an
/// <see cref="IFallbackAssetSource"/> so the data is only loaded when a document needs it. Installed CJK fonts, when a fallback is supplied, win.
/// </summary>
public sealed class BundledFontFallback : IFontFallback, ICjkFontFallback, ICjkGlyphFallback, IFontAssetPending, IFontAssetDiagnostics
{
    readonly IFallbackAssetSource _source;
    readonly IFontFallback? _inner;
    readonly Dictionary<string, TrueTypeFont?> _fonts = [];
    readonly Dictionary<(string Ordering, int Block), Dictionary<int, PathData>?> _chunks = [];

    /// <param name="source">Where the fallback font data is read from.</param>
    /// <param name="inner">Optional last resort for Latin text when a bundled asset is missing, and the first choice (installed fonts) for CJK.</param>
    public BundledFontFallback(IFallbackAssetSource source, IFontFallback? inner = null)
    {
        _source = source;
        _inner = inner;
    }

    /// <summary>Desktop default: bundled data beside the application, installed CJK fonts first.</summary>
    public static BundledFontFallback CreateDefault() => new(new DirectoryAssetSource(), new SystemFontFallback());

    readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings { get { lock (_warnings) return _warnings.ToArray(); } }

    void Warn(string message) { lock (_warnings) if (!_warnings.Contains(message)) _warnings.Add(message); }

    public bool HasPending => _source is IFontAssetPending { HasPending: true };

    public static string LatinAsset(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif)
    {
        var name = baseFont.Length > 7 && baseFont[6] == '+' ? baseFont[7..] : baseFont;
        if (name.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase) || name.Contains("Dingbats", StringComparison.OrdinalIgnoreCase)) return "latin/DejaVuSans.ttf";
        var family = fixedPitch ? "Mono" : serif ? "Serif" : "Sans";
        var style = bold ? italic ? "BoldItalic" : "Bold" : italic ? "Italic" : "Regular";
        return $"latin/Liberation{family}-{style}.ttf";
    }

    public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif)
    {
        var path = LatinAsset(baseFont, bold, italic, fixedPitch, serif);
        lock (_fonts)
        {
            if (_fonts.TryGetValue(path, out var cached) && cached is not null) return cached;
            if (!_source.TryRead(path, out var data)) return null;
            TrueTypeFont? font = null;
            if (data is not null)
            {
                try { font = new TrueTypeFont(data); }
                catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException) { }
            }
            if (font is null) Warn($"fallback font {path} is unavailable or corrupt; a built-in substitute is used");
            if (font is null) return _inner?.Find(baseFont, bold, italic, fixedPitch, serif);
            return _fonts[path] = font;
        }
    }

    public TrueTypeFont? FindCjk(string ordering, bool serif) => (_inner as ICjkFontFallback)?.FindCjk(ordering, serif);

    public PathData? GetCjkGlyph(string ordering, bool serif, int codePoint)
    {
        var block = codePoint >> 8;
        if (!CjkIndex.HasBlock(ordering, block)) return null;
        lock (_chunks)
        {
            if (!_chunks.TryGetValue((ordering, block), out var glyphs))
            {
                if (!_source.TryRead(CjkIndex.ChunkPath(ordering, block), out var data)) return null;
                glyphs = data is null ? null : CjkChunkCodec.Decode(data);
                if (glyphs is null or { Count: 0 }) { glyphs = null; Warn($"CJK fallback data {CjkIndex.ChunkPath(ordering, block)} is unavailable or corrupt; those glyphs are not drawn"); }
                _chunks[(ordering, block)] = glyphs;
            }
            return glyphs is not null && glyphs.TryGetValue(codePoint & 0xFF, out var p) ? p : null;
        }
    }
}
