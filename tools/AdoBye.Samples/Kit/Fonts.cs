using System.Text;

namespace AdoBye.Samples.Kit;

public interface IFont
{
    /// <summary>Object number of the font dictionary.</summary>
    int Ref { get; }
    double Width(string s, double size, bool kern = true, double tracking = 0);
    string Tj(string s, double size, bool kern = true, double spaceExtra = 0);
}

/// <summary>One of the 14 standard fonts (not embedded).</summary>
public sealed class StdFont : IFont
{
    readonly Doc _doc;
    int _ref;

    public string Name { get; }
    public bool Symbolic { get; }
    public string? Encoding { get; }

    public StdFont(Doc doc, string name, string encoding)
    {
        _doc = doc; Name = name;
        Symbolic = name is "Symbol" or "ZapfDingbats";
        Encoding = Symbolic ? null : encoding;
    }

    public int Ref
    {
        get
        {
            if (_ref == 0)
            {
                var enc = Encoding != null ? $" /Encoding /{Encoding}" : "";
                _ref = _doc.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /{Name}{enc} >>");
            }
            return _ref;
        }
    }

    public double Width(string s, double size, bool kern = false, double tracking = 0) => Std14Metrics.Width(Name, s) * size / 1000 + tracking * s.Length;

    public string Tj(string s, double size, bool kern = false, double spaceExtra = 0)
    {
        if (spaceExtra == 0) return $"[{K.PStr(s)}] TJ";
        var parts = s.Split(' ');
        var o = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) o.Add(K.PStr(" ") + " " + K.N(-spaceExtra * 1000 / size));
            o.Add(K.PStr(parts[i]));
        }
        return "[" + string.Join(" ", o) + "] TJ";
    }
}

/// <summary>An OpenType/CFF or TrueType font embedded as a subset using Identity-H (Type0 / CIDFont).</summary>
public sealed class EmbFont : IFont
{
    readonly Doc _doc;
    readonly FontFile _ft;
    readonly bool _cff;
    readonly Dictionary<int, int> _used = [];
    readonly string _tag;
    readonly string _ps;
    int _ref;

    public EmbFont(Doc doc, string filename)
    {
        _doc = doc;
        _ft = FontFile.Load(Path.IsPathRooted(filename) ? filename : Path.Combine(Paths.Fonts, filename));
        _cff = _ft.IsCff;
        doc.Fin.Add(EmitFont);
        var t = ++doc.NextFontTag;
        _tag = new string(new[] { 1, 3, 5, 7, 11, 13 }.Select(k => (char)(65 + t * k % 26)).ToArray());
        _ps = new string((_ft.PostScriptName ?? "Font").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
    }

    public FontFile File => _ft;

    int Gid(char ch) => _ft.GlyphForCodePoint(ch);

    double Adv(int gid) => _ft.AdvanceUnits(gid) * 1000 / _ft.UnitsPerEm;

    int[] Gids(string s)
    {
        var o = new int[s.Length];
        for (var i = 0; i < s.Length; i++) { o[i] = Gid(s[i]); _used[o[i]] = s[i]; }
        return o;
    }

    public double Width(string s, double size, bool kern = true, double tracking = 0)
    {
        var g = s.Select(Gid).ToArray();
        var w = g.Sum(Adv);
        if (kern) for (var i = 0; i + 1 < g.Length; i++) w += _ft.KernUnits(g[i], g[i + 1]) * 1000.0 / _ft.UnitsPerEm;
        return w * size / 1000 + tracking * s.Length;
    }

    public string Tj(string s, double size, bool kern = true, double spaceExtra = 0)
    {
        var g = Gids(s);
        var items = new List<string>();
        var cur = new StringBuilder();
        for (var i = 0; i < g.Length; i++)
        {
            cur.Append(g[i].ToString("X4"));
            double adj = 0;
            if (kern && i + 1 < g.Length) adj += _ft.KernUnits(g[i], g[i + 1]) * 1000.0 / _ft.UnitsPerEm;
            if (spaceExtra != 0 && s[i] == ' ') adj += spaceExtra * 1000 / size;
            if (adj != 0)
            {
                items.Add($"<{cur}>");
                items.Add(K.N(-adj));
                cur.Clear();
            }
        }
        if (cur.Length > 0) items.Add($"<{cur}>");
        return "[" + string.Join(" ", items) + "] TJ";
    }

    public int Ref => _ref != 0 ? _ref : _ref = _doc.New();

    void EmitFont()
    {
        if (_ref == 0) return;
        var used = _used.Keys.Append(0).Distinct().Order().ToList();
        var data = _ft.Subset(used);
        string fkey, cidsub;
        int ff;
        if (_cff) { ff = _doc.Stream("/Subtype /OpenType", data); fkey = "FontFile3"; cidsub = "CIDFontType0"; }
        else { ff = _doc.Stream($"/Length1 {data.Length}", data); fkey = "FontFile2"; cidsub = "CIDFontType2"; }
        var sc = 1000.0 / _ft.UnitsPerEm;
        var cap = (int)(_ft.CapHeight * sc);
        if (cap == 0) cap = (int)(_ft.Ascent * sc * 0.7);
        var flags = 32 | (_ft.ItalicAngle != 0 ? 64 : 0) | (_ps.Contains("serif", StringComparison.OrdinalIgnoreCase) && !_ps.Contains("Sans") ? 2 : 0);
        var fname = $"{_tag}+{_ps}";
        var desc = _doc.Add($"<< /Type /FontDescriptor /FontName /{fname} /Flags {flags} " +
            $"/FontBBox [{K.N(_ft.XMin * sc)} {K.N(_ft.YMin * sc)} {K.N(_ft.XMax * sc)} {K.N(_ft.YMax * sc)}] " +
            $"/ItalicAngle {K.N(_ft.ItalicAngle)} /Ascent {K.N(_ft.Ascent * sc)} /Descent {K.N(_ft.Descent * sc)} " +
            $"/CapHeight {cap} /StemV 80 /{fkey} {ff} 0 R >>");
        var w = string.Join(" ", used.Select(g => $"{g} [{K.N(Adv(g))}]"));
        var cid = _doc.Add($"<< /Type /Font /Subtype /{cidsub} /BaseFont /{fname} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {desc} 0 R /DW 1000 /W [{w}]" + (_cff ? "" : " /CIDToGIDMap /Identity") + " >>");
        var bf = string.Join("\n", _used.OrderBy(k => k.Key).Select(kv => kv.Value < 0x10000
            ? $"<{kv.Key:X4}> <{kv.Value:X4}>"
            : $"<{kv.Key:X4}> <{0xD800 + ((kv.Value - 0x10000) >> 10):X4}{0xDC00 + ((kv.Value - 0x10000) & 0x3FF):X4}>"));
        var cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def " +
            "/CMapName /Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <0000> <FFFF> endcodespacerange\n" +
            $"{_used.Count} beginbfchar\n{bf}\nendbfchar\nendcmap CMapName currentdict /CMap defineresource pop end end";
        var tu = _doc.Stream("", K.Latin1(cmap));
        _doc.Put(_ref, $"<< /Type /Font /Subtype /Type0 /BaseFont /{fname} /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {tu} 0 R >>");
    }
}
