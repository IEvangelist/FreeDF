using System.Globalization;

namespace PortableDocument.Content;

/// <summary>
/// Glyph advance widths (1/1000 em) and the built-in Symbol/ZapfDingbats encodings of the standard 14 fonts, read from the
/// embedded <c>std14-widths.txt</c>. The numbers are the factual advance widths of the Adobe Core 14 AFM metrics.
/// </summary>
internal sealed class Std14Metrics
{
    const string ResourceName = "PortableDocument.Content.Resources.std14-widths.txt";
    static readonly Lazy<Dictionary<string, Std14Metrics>> All = new(Load);

    readonly Dictionary<string, int> _byName = [];
    readonly string?[] _encoding = new string?[256];
    Dictionary<int, int>? _byUnicode;
    public string Name { get; }
    public bool IsSymbolic => Name is "Symbol" or "ZapfDingbats";

    Std14Metrics(string name) => Name = name;

    public static Std14Metrics? Get(string canonical) => All.Value.GetValueOrDefault(canonical);

    /// <summary>The glyph name the font's own encoding assigns to a code (Symbol and ZapfDingbats only).</summary>
    public string? BuiltinName(int code) => code is >= 0 and < 256 ? _encoding[code] : null;

    public bool TryWidth(string? glyphName, out int width)
    {
        width = 0;
        return glyphName is not null && _byName.TryGetValue(glyphName, out width);
    }

    /// <summary>Width of the glyph a Unicode scalar (as produced by the encoding tables) stands for.</summary>
    public bool TryWidthByUnicode(int codePoint, out int width)
    {
        if (_byUnicode is null)
        {
            var map = new Dictionary<int, int>();
            foreach (var (n, w) in _byName)
                if (GlyphList.Lookup(n, Name == "ZapfDingbats") is { Length: > 0 } u && char.ConvertToUtf32(u, 0) is var cp && u.Length == (cp > 0xFFFF ? 2 : 1))
                    map.TryAdd(cp, w);
            _byUnicode = map;
        }
        return _byUnicode.TryGetValue(codePoint, out width);
    }

    static Dictionary<string, Std14Metrics> Load()
    {
        var all = new Dictionary<string, Std14Metrics>(StringComparer.Ordinal);
        using var s = typeof(Std14Metrics).Assembly.GetManifestResourceStream(ResourceName);
        if (s is null) return all;
        using var r = new StreamReader(s);
        Std14Metrics? cur = null;
        for (var line = r.ReadLine(); line is not null; line = r.ReadLine())
        {
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '@') { all[line[1..]] = cur = new Std14Metrics(line[1..]); continue; }
            if (cur is null) continue;
            var f = line.Split(' ');
            if (f.Length != 3 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ||
                !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)) continue;
            cur._byName[f[1]] = w;
            if (code is >= 0 and < 256) cur._encoding[code] = f[1];
        }
        return all;
    }
}
