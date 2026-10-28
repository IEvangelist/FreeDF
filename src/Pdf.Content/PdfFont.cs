using System.Text;
using Pdf.Core;

namespace Pdf.Content;

public interface IFontFallback
{
    TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif);
}

/// <summary>Resolves non-embedded fonts from the host's installed fonts (Windows, macOS, Linux directories).</summary>
public sealed class SystemFontFallback : IFontFallback
{
    static readonly string[] Dirs =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts", "/System/Library/Fonts"
    ];
    readonly Dictionary<string, TrueTypeFont?> _cache = [];

    public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif)
    {
        string[] candidates;
        var key = (fixedPitch ? "cour" : serif ? "times" : "arial") + (bold ? "b" : "") + (italic ? "i" : "");
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (fixedPitch) candidates = bold ? italic ? ["courbi.ttf"] : ["courbd.ttf"] : italic ? ["couri.ttf"] : ["cour.ttf", "LiberationMono-Regular.ttf", "DejaVuSansMono.ttf"];
        else if (serif) candidates = bold ? italic ? ["timesbi.ttf"] : ["timesbd.ttf"] : italic ? ["timesi.ttf"] : ["times.ttf", "LiberationSerif-Regular.ttf", "DejaVuSerif.ttf"];
        else candidates = bold ? italic ? ["arialbi.ttf"] : ["arialbd.ttf"] : italic ? ["ariali.ttf"] : ["arial.ttf", "LiberationSans-Regular.ttf", "DejaVuSans.ttf"];
        TrueTypeFont? font = null;
        foreach (var c in candidates)
        {
            foreach (var d in Dirs)
            {
                if (!Directory.Exists(d)) continue;
                var p = Directory.EnumerateFiles(d, c, SearchOption.AllDirectories).FirstOrDefault();
                if (p is null) continue;
                try { font = new TrueTypeFont(File.ReadAllBytes(p)); } catch (IOException) { }
                if (font is not null) break;
            }
            if (font is not null) break;
        }
        return _cache[key] = font;
    }
}

public readonly record struct GlyphRun(int Code, int ByteLength, double Width);

public sealed class PdfFont
{
    public PdfDictionary Dict { get; }
    public string BaseFont { get; } = "";
    public bool IsType0 { get; }
    public bool IsType3 { get; }
    public bool Vertical { get; }
    public PdfStream? FontFile { get; }
    TrueTypeFont? _tt;
    public bool HasOutlines => _tt?.HasGlyf == true;
    readonly PdfFile _f;
    readonly string?[] _names = new string?[256];
    readonly double[] _widths = new double[256];
    readonly bool[] _hasWidth = new bool[256];
    double _missingWidth;
    readonly Dictionary<int, double> _cidWidths = [];
    double _dw = 1000;
    byte[]? _cidToGid;
    bool _symbolic;
    bool _fallbackUsed;
    Dictionary<int, int>? _toUnicode;
    readonly Dictionary<int, PathData?> _glyphCache = [];
    public PdfDictionary? CharProcs { get; }
    public PdfDictionary? Type3Resources { get; }
    public double[] Type3Matrix { get; private set; } = [0.001, 0, 0, 0.001, 0, 0];
    static PdfFont() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public PdfFont(PdfFile f, PdfDictionary dict, IFontFallback fallback)
    {
        _f = f; Dict = dict;
        var subtype = f.Resolve(dict.Get("Subtype")).AsName();
        BaseFont = f.Resolve(dict.Get("BaseFont")).AsName() ?? "";
        IsType0 = subtype == "Type0";
        IsType3 = subtype == "Type3";
        PdfDictionary descriptor;
        PdfDictionary fontDict = dict;
        if (IsType0)
        {
            var desc = (f.Resolve(dict.Get("DescendantFonts")) as PdfArray)?.Items.FirstOrDefault();
            fontDict = f.Resolve(desc) as PdfDictionary ?? new PdfDictionary();
            Vertical = f.Resolve(dict.Get("Encoding")).AsName() is { } en && en.EndsWith("-V");
            _dw = f.Resolve(fontDict.Get("DW")).AsNumber() ?? 1000;
            ParseW(f.Resolve(fontDict.Get("W")) as PdfArray);
            var c2g = f.Resolve(fontDict.Get("CIDToGIDMap"));
            if (c2g is PdfStream cs) _cidToGid = f.DecodeStream(cs);
        }
        descriptor = f.Resolve(fontDict.Get("FontDescriptor")) as PdfDictionary ?? new PdfDictionary();
        var flags = (int)(f.Resolve(descriptor.Get("Flags")).AsNumber() ?? 0);
        _symbolic = (flags & 4) != 0 && (flags & 32) == 0;
        _missingWidth = f.Resolve(descriptor.Get("MissingWidth")).AsNumber() ?? 0;
        foreach (var key in new[] { "FontFile2", "FontFile3", "FontFile" })
            if (f.Resolve(descriptor.Get(key)) is PdfStream ff) { FontFile = ff; break; }

        if (IsType3)
        {
            CharProcs = f.Resolve(dict.Get("CharProcs")) as PdfDictionary;
            Type3Resources = f.Resolve(dict.Get("Resources")) as PdfDictionary;
            if (f.Resolve(dict.Get("FontMatrix")) is PdfArray fm && fm.Count == 6) Type3Matrix = fm.Items.Select(x => f.Resolve(x).AsNumber() ?? 0).ToArray();
        }

        if (FontFile is not null)
        {
            try
            {
                var tt = new TrueTypeFont(f.DecodeStream(FontFile));
                if (tt.HasGlyf) _tt = tt;
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException) { }
        }
        if (_tt is null && !IsType3)
        {
            var n = BaseFont.ToLowerInvariant();
            var bold = n.Contains("bold") || n.Contains("black") || (flags & (1 << 18)) != 0;
            var italic = n.Contains("italic") || n.Contains("oblique") || (flags & 64) != 0;
            var mono = n.Contains("courier") || n.Contains("mono") || (flags & 1) != 0;
            var serif = n.Contains("times") || n.Contains("serif") && !n.Contains("sans") || n.Contains("georgia") || n.Contains("garamond") || n.Contains("palatino") || n.Contains("roman") || (flags & 2) != 0 && !n.Contains("sans");
            _tt = fallback.Find(BaseFont, bold, italic, mono, serif);
            _fallbackUsed = true;
        }

        if (!IsType0) BuildSimpleEncoding(dict, fontDict);
        if (f.Resolve(dict.Get("ToUnicode")) is PdfStream tu) _toUnicode = ParseToUnicode(f.DecodeStream(tu));
    }

    void ParseW(PdfArray? w)
    {
        if (w is null) return;
        for (var i = 0; i < w.Count;)
        {
            var a = _f.Resolve(w[i]).AsNumber(); if (a is null) break;
            if (i + 1 < w.Count && _f.Resolve(w[i + 1]) is PdfArray arr)
            {
                for (var k = 0; k < arr.Count; k++) _cidWidths[(int)a.Value + k] = _f.Resolve(arr[k]).AsNumber() ?? 0;
                i += 2;
            }
            else if (i + 2 < w.Count)
            {
                var b = (int)(_f.Resolve(w[i + 1]).AsNumber() ?? a.Value); var width = _f.Resolve(w[i + 2]).AsNumber() ?? 0;
                for (var c = (int)a.Value; c <= b && c - (int)a.Value < 70000; c++) _cidWidths[c] = width;
                i += 3;
            }
            else break;
        }
    }

    void BuildSimpleEncoding(PdfDictionary dict, PdfDictionary fontDict)
    {
        var enc = _f.Resolve(dict.Get("Encoding"));
        string? baseEnc = enc.AsName();
        PdfArray? diffs = null;
        if (enc is PdfDictionary ed)
        {
            baseEnc = _f.Resolve(ed.Get("BaseEncoding")).AsName();
            diffs = _f.Resolve(ed.Get("Differences")) as PdfArray;
        }
        baseEnc ??= _symbolic && FontFile is not null ? null : "StandardEncoding";
        var cp = baseEnc switch { "WinAnsiEncoding" => 1252, "MacRomanEncoding" => 10000, "StandardEncoding" => 1252, _ => 0 };
        if (cp != 0)
        {
            var e = Encoding.GetEncoding(cp);
            for (var i = 32; i < 256; i++)
            {
                var s = e.GetString([(byte)i]);
                if (s.Length == 1 && s[0] != '\uFFFD') _names[i] = "uni" + ((int)s[0]).ToString("X4");
            }
        }
        if (diffs is not null)
        {
            var code = 0;
            foreach (var o in diffs.Items)
            {
                var r = _f.Resolve(o);
                if (r is PdfInteger n) code = (int)n.Value;
                else if (r is PdfName nm && code is >= 0 and < 256) _names[code++] = nm.Value;
            }
        }
        var first = (int)(_f.Resolve(dict.Get("FirstChar")).AsNumber() ?? 0);
        if (_f.Resolve(dict.Get("Widths")) is PdfArray wa)
            for (var i = 0; i < wa.Count && first + i < 256; i++)
                if (first + i >= 0 && _f.Resolve(wa[i]).AsNumber() is { } w) { _widths[first + i] = w; _hasWidth[first + i] = true; }
    }

    static Dictionary<int, int> ParseToUnicode(byte[] data)
    {
        var map = new Dictionary<int, int>();
        var lx = new PdfLexer(data);
        var stack = new List<PdfObject>();
        PdfObject? o;
        while ((o = lx.ReadObject(false)) is not null)
        {
            if (o is PdfKeyword k)
            {
                if (k.Value == "endbfchar")
                    for (var i = 0; i + 1 < stack.Count; i += 2)
                    {
                        if (stack[i] is PdfString s && stack[i + 1] is PdfString d) map[Be(s.Bytes)] = FirstCp(d.Bytes);
                    }
                else if (k.Value == "endbfrange")
                    for (var i = 0; i + 2 < stack.Count; i += 3)
                    {
                        if (stack[i] is not PdfString lo || stack[i + 1] is not PdfString hi) continue;
                        int a = Be(lo.Bytes), b = Be(hi.Bytes);
                        if (stack[i + 2] is PdfString d) { var cp = FirstCp(d.Bytes); for (var c = a; c <= b && c - a < 65536; c++) map[c] = cp + c - a; }
                        else if (stack[i + 2] is PdfArray arr) for (var c = a; c <= b && c - a < arr.Count; c++) if (arr[c - a] is PdfString ds) map[c] = FirstCp(ds.Bytes);
                    }
                stack.Clear();
            }
            else stack.Add(o);
        }
        return map;
    }

    static int Be(byte[] b) { var v = 0; foreach (var x in b) v = v << 8 | x; return v; }
    static int FirstCp(byte[] b) { if (b.Length < 2) return b.Length == 1 ? b[0] : 0; var s = Encoding.BigEndianUnicode.GetString(b); return char.ConvertToUtf32(s, 0); }

    public string ToUnicode(int code)
    {
        if (_toUnicode is not null && _toUnicode.TryGetValue(code, out var cp)) return char.ConvertFromUtf32(cp);
        if (!IsType0 && code < 256 && _names[code] is { } n && NameToUnicode(n) is { } u) return u;
        return code is >= 32 and < 127 && !IsType0 ? ((char)code).ToString() : "";
    }

    public IEnumerable<GlyphRun> Decode(byte[] s)
    {
        if (IsType0)
        {
            for (var i = 0; i + 1 < s.Length; i += 2)
            {
                var cid = s[i] << 8 | s[i + 1];
                yield return new GlyphRun(cid, 2, _cidWidths.GetValueOrDefault(cid, _dw) / 1000.0);
            }
            yield break;
        }
        foreach (var c in s)
        {
            var w = _hasWidth[c] ? _widths[c] : FallbackWidth(c);
            yield return new GlyphRun(c, 1, IsType3 ? w * Type3Matrix[0] : w / 1000.0);
        }
    }

    double FallbackWidth(int c)
    {
        if (_tt is null) return _missingWidth > 0 ? _missingWidth : 500;
        var g = GlyphIdFor(c);
        return g > 0 ? _tt.Advance(g) : _missingWidth;
    }

    public bool IsSpace(GlyphRun r) => r.ByteLength == 1 && r.Code == 32;

    int GlyphIdFor(int code)
    {
        if (_tt is null) return 0;
        if (IsType0)
        {
            if (FontFile is null) return _tt.LookupAny(code);
            if (_cidToGid is not null) return code * 2 + 1 < _cidToGid.Length ? _cidToGid[code * 2] << 8 | _cidToGid[code * 2 + 1] : 0;
            return code;
        }
        var name = _names[code];
        if (_fallbackUsed || !_symbolic || name is not null && FontFile is null)
        {
            if (name is not null)
            {
                if (FontFile is not null) { var g = _tt.GlyphByName(name); if (g > 0) return g; }
                if (NameToUnicode(name) is { } u && u.Length > 0) { var g = _tt.LookupAny(char.ConvertToUtf32(u, 0)); if (g > 0) return g; }
            }
        }
        // Symbolic TrueType: (3,0) with 0xF000 offset, or (1,0) direct.
        foreach (var off in new[] { 0xF000, 0, 0xF100, 0xF200 })
        {
            var g = _tt.Lookup(3, 0, off + code);
            if (g > 0) return g;
        }
        var m = _tt.Lookup(1, 0, code);
        if (m > 0) return m;
        return _fallbackUsed ? 0 : _tt.LookupAny(code);
    }

    /// <summary>Glyph outline in 1/1000 em units.</summary>
    public PathData? GetGlyph(int code)
    {
        if (_glyphCache.TryGetValue(code, out var p)) return p;
        p = null;
        if (_tt is not null)
        {
            var gid = GlyphIdFor(code);
            if (gid > 0 || FontFile is not null && IsType0) p = _tt.GetOutline(gid);
        }
        _glyphCache[code] = p;
        return p;
    }

    public PdfStream? GetCharProc(int code)
    {
        if (CharProcs is null || code is < 0 or > 255) return null;
        var name = _names[code];
        return name is null ? null : _f.Resolve(CharProcs.Get(name)) as PdfStream;
    }

    static readonly Dictionary<string, string> Aglite = new()
    {
        ["space"] = " ", ["bullet"] = "\u2022", ["endash"] = "\u2013", ["emdash"] = "\u2014", ["quoteleft"] = "\u2018", ["quoteright"] = "\u2019",
        ["quotedblleft"] = "\u201C", ["quotedblright"] = "\u201D", ["quotedblbase"] = "\u201E", ["quotesinglbase"] = "\u201A", ["ellipsis"] = "\u2026",
        ["fi"] = "\uFB01", ["fl"] = "\uFB02", ["dagger"] = "\u2020", ["daggerdbl"] = "\u2021", ["trademark"] = "\u2122", ["Euro"] = "\u20AC",
        ["exclam"] = "!", ["quotedbl"] = "\"", ["numbersign"] = "#", ["dollar"] = "$", ["percent"] = "%", ["ampersand"] = "&", ["quotesingle"] = "'",
        ["parenleft"] = "(", ["parenright"] = ")", ["asterisk"] = "*", ["plus"] = "+", ["comma"] = ",", ["hyphen"] = "-", ["minus"] = "\u2212", ["period"] = ".",
        ["slash"] = "/", ["colon"] = ":", ["semicolon"] = ";", ["less"] = "<", ["equal"] = "=", ["greater"] = ">", ["question"] = "?", ["at"] = "@",
        ["bracketleft"] = "[", ["backslash"] = "\\", ["bracketright"] = "]", ["asciicircum"] = "^", ["underscore"] = "_", ["grave"] = "`",
        ["braceleft"] = "{", ["bar"] = "|", ["braceright"] = "}", ["asciitilde"] = "~", ["nbspace"] = "\u00A0",
        ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
        ["copyright"] = "\u00A9", ["registered"] = "\u00AE", ["degree"] = "\u00B0", ["section"] = "\u00A7", ["paragraph"] = "\u00B6", ["multiply"] = "\u00D7", ["divide"] = "\u00F7",
        ["eacute"] = "\u00E9", ["egrave"] = "\u00E8", ["agrave"] = "\u00E0", ["aacute"] = "\u00E1", ["udieresis"] = "\u00FC", ["odieresis"] = "\u00F6", ["adieresis"] = "\u00E4",
        ["germandbls"] = "\u00DF", ["ntilde"] = "\u00F1", ["ccedilla"] = "\u00E7", ["Adieresis"] = "\u00C4", ["Odieresis"] = "\u00D6", ["Udieresis"] = "\u00DC",
    };

    public static string? NameToUnicode(string n)
    {
        if (n.Length == 1) return n;
        if (Aglite.TryGetValue(n, out var s)) return s;
        if (n.StartsWith("uni") && n.Length >= 7 && int.TryParse(n.AsSpan(3, 4), System.Globalization.NumberStyles.HexNumber, null, out var u)) return ((char)u).ToString();
        if (n.StartsWith('u') && n.Length is >= 5 and <= 7 && int.TryParse(n.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var u2) && u2 <= 0x10FFFF) return char.ConvertFromUtf32(u2);
        var dot = n.IndexOf('.');
        return dot > 0 ? NameToUnicode(n[..dot]) : null;
    }
}
