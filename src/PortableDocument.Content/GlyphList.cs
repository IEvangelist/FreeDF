namespace PortableDocument.Content;

/// <summary>Adobe Glyph List lookup (name to a single Unicode code point), loaded lazily from the embedded glyphlist.txt.</summary>
internal static class GlyphList
{
    static readonly Lazy<Dictionary<string, int>> Map = new(Load);

    static Dictionary<string, int> Load()
    {
        var d = new Dictionary<string, int>(4300);
        using var s = typeof(GlyphList).Assembly.GetManifestResourceStream("PortableDocument.Content.Resources.glyphlist.txt");
        if (s is null) return d;
        using var r = new StreamReader(s);
        for (var line = r.ReadLine(); line is not null; line = r.ReadLine())
        {
            var i = line.IndexOf(';');
            if (i > 0 && int.TryParse(line.AsSpan(i + 1), System.Globalization.NumberStyles.HexNumber, null, out var cp)) d[line[..i]] = cp;
        }
        return d;
    }

    public static int Lookup(string name) => Map.Value.GetValueOrDefault(name, -1);
}
