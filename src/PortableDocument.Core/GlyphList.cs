using System.Globalization;
using System.Text;

namespace PortableDocument.Core;

/// <summary>Adobe Glyph List 2.9 name-to-text algorithm, with lazily loaded official mapping tables.</summary>
internal static class GlyphList
{
    static readonly Lazy<Dictionary<string, string>> Map = new(() => Load("glyphlist.txt", 4281));
    static readonly Lazy<Dictionary<string, string>> Dingbats = new(() => Load("zapfdingbats.txt", 201));

    static Dictionary<string, string> Load(string file, int capacity)
    {
        var d = new Dictionary<string, string>(capacity, StringComparer.Ordinal);
        using var s = typeof(GlyphList).Assembly.GetManifestResourceStream("PortableDocument.Core.Resources." + file)
            ?? throw new InvalidOperationException($"Missing embedded Adobe glyph list: {file}.");
        using var r = new StreamReader(s);
        for (var line = r.ReadLine(); line is not null; line = r.ReadLine())
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var i = line.IndexOf(';');
            if (i <= 0) throw new InvalidDataException($"Invalid Adobe glyph list entry: {line}");
            var text = new StringBuilder();
            foreach (var value in line[(i + 1)..].Split(' '))
                text.Append(char.ConvertFromUtf32(int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            d.Add(line[..i], text.ToString());
        }
        return d;
    }

    public static bool ContainsName(string name) => Map.Value.ContainsKey(name);

    public static string? Lookup(string name, bool zapfDingbats = false)
    {
        var dot = name.IndexOf('.');
        if (dot >= 0) name = name[..dot];
        if (!name.Contains('_')) return Component(name, zapfDingbats);
        var text = new StringBuilder();
        foreach (var component in name.Split('_'))
            text.Append(Component(component, zapfDingbats));
        return text.Length == 0 ? null : text.ToString();
    }

    static string? Component(string name, bool zapfDingbats)
    {
        if (zapfDingbats && Dingbats.Value.TryGetValue(name, out var dingbat)) return dingbat;
        if (Map.Value.TryGetValue(name, out var text)) return text;
        if (name.StartsWith("uni", StringComparison.Ordinal))
        {
            var hex = name.AsSpan(3);
            if (hex.Length == 0 || hex.Length % 4 != 0) return null;
            for (var i = 0; i < hex.Length; i += 4)
                if (!Scalar(hex.Slice(i, 4), out _)) return null;
            return string.Create(hex.Length / 4, name, static (chars, source) =>
            {
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = (char)int.Parse(source.AsSpan(3 + 4 * i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            });
        }
        return name.StartsWith('u') && name.Length is >= 5 and <= 7 && Scalar(name.AsSpan(1), out var cp)
            ? char.ConvertFromUtf32(cp) : null;
    }

    static bool Scalar(ReadOnlySpan<char> hex, out int cp)
    {
        cp = 0;
        foreach (var c in hex)
        {
            if (c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) return false;
            cp = (cp << 4) + (c <= '9' ? c - '0' : c - 'A' + 10);
        }
        return cp <= 0x10FFFF && cp is not (>= 0xD800 and <= 0xDFFF);
    }
}
