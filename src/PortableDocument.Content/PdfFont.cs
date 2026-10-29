using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Content;

public interface IFontFallback
{
    TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif);
}

/// <summary>Optional capability of an <see cref="IFontFallback"/>: a Unicode-cmap font covering an Adobe CJK ordering (Japan1, GB1, CNS1, Korea1).</summary>
public interface ICjkFontFallback
{
    TrueTypeFont? FindCjk(string ordering, bool serif);
}

/// <summary>Resolves non-embedded fonts from the host's installed fonts (Windows, macOS, Linux directories).</summary>
public sealed class SystemFontFallback : IFontFallback, ICjkFontFallback
{
    static readonly string[] Dirs =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts", "/System/Library/Fonts"
    ];
    // Shared across interpreters: locating and parsing an installed font is far more expensive than rendering a page of text.
    static readonly Dictionary<string, TrueTypeFont?> _cache = [];

    static readonly Dictionary<string, string[]> CjkSans = new()
    {
        ["Japan1"] = ["msgothic.ttc", "YuGothR.ttc", "meiryo.ttc", "NotoSansCJK-Regular.ttc", "NotoSansCJKjp-Regular.otf", "HiraginoSans-W3.ttc", "ipag.ttf", "TakaoPGothic.ttf"],
        ["GB1"] = ["simhei.ttf", "msyh.ttc", "NotoSansSC-VF.ttf", "NotoSansCJK-Regular.ttc", "PingFang.ttc", "wqy-zenhei.ttc", "DroidSansFallbackFull.ttf"],
        ["CNS1"] = ["msjh.ttc", "mingliub.ttc", "kaiu.ttf", "NotoSansCJK-Regular.ttc", "PingFang.ttc", "wqy-zenhei.ttc"],
        ["Korea1"] = ["malgun.ttf", "gulim.ttc", "batang.ttc", "NotoSansCJK-Regular.ttc", "AppleSDGothicNeo.ttc", "NanumGothic.ttf"],
    };
    static readonly Dictionary<string, string[]> CjkSerif = new()
    {
        ["Japan1"] = ["msmincho.ttc", "YuMincho.ttc", "NotoSerifCJK-Regular.ttc", "HiraMinProN-W3.ttc"],
        ["GB1"] = ["simsun.ttc", "NotoSerifSC-VF.ttf", "NotoSerifCJK-Regular.ttc", "STSong.ttf"],
        ["CNS1"] = ["mingliub.ttc", "NotoSerifCJK-Regular.ttc"],
        ["Korea1"] = ["batang.ttc", "NotoSerifCJK-Regular.ttc"],
    };

    public TrueTypeFont? FindCjk(string ordering, bool serif)
    {
        var key = "cjk:" + ordering + (serif ? "s" : "");
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            TrueTypeFont? font = null;
            var lists = serif ? new[] { CjkSerif, CjkSans } : [CjkSans];
            foreach (var list in lists)
            {
                if (!list.TryGetValue(ordering, out var cands)) continue;
                font = Load(cands, ordering == "Korea1" ? 0xAC00 : 0x4E00);
                if (font is not null) break;
            }
            return _cache[key] = font;
        }
    }

    static TrueTypeFont? Load(string[] candidates, int probe)
    {
        foreach (var c in candidates)
            foreach (var d in Dirs)
            {
                if (!Directory.Exists(d)) continue;
                string? p;
                try { p = Directory.EnumerateFiles(d, c, SearchOption.AllDirectories).FirstOrDefault(); } catch (UnauthorizedAccessException) { continue; }
                if (p is null) continue;
                try
                {
                    var tt = new TrueTypeFont(File.ReadAllBytes(p));
                    if (tt.HasGlyf && tt.LookupAny(probe) != 0) return tt;
                }
                catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentException) { }
            }
        return null;
    }

    public TrueTypeFont? Find(string baseFont, bool bold, bool italic, bool fixedPitch, bool serif)
    {
        string[] candidates;
        var key = (fixedPitch ? "cour" : serif ? "times" : "arial") + (bold ? "b" : "") + (italic ? "i" : "");
        lock (_cache)
        {
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
}

public readonly record struct GlyphRun(int Code, int ByteLength, double Width, int Raw = -1);

public sealed class PdfFont
{
    public PdfDictionary Dict { get; }
    public string BaseFont { get; } = "";
    public bool IsType0 { get; }
    public bool IsType3 { get; }
    /// <summary>
    /// The encoding CMap selects vertical writing. W2/DW2 place the selected CID outlines;
    /// OpenType GSUB vert/vrt2 substitution is not performed (vertical CMap CID mappings are honored).
    /// </summary>
    public bool Vertical { get; }
    public PdfStream? FontFile { get; }
    TrueTypeFont? _tt;
    CffFont? _cff;
    Type1Font? _t1;
    Dictionary<string, int>? _cffUni;
    Dictionary<string, string>? _t1Uni;
    CMap? _cmap;
    string _ordering = "";
    Dictionary<int, int>? _cidToUni;
    public bool HasOutlines => _tt?.HasGlyf == true || _cff is not null || _t1 is not null;
    readonly PdfFile _f;
    readonly string?[] _names = new string?[256];
    readonly double[] _widths = new double[256];
    readonly bool[] _hasWidth = new bool[256];
    double _missingWidth;
    readonly Dictionary<int, double> _cidWidths = [];
    double _dw = 1000;
    readonly Dictionary<int, (double Advance, double X, double Y)> _cidVerticalMetrics = [];
    double _defaultVerticalY = 880, _defaultVerticalAdvance = -1000;
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
            var encObj = f.Resolve(dict.Get("Encoding"));
            var encName = encObj.AsName();
            if (encName is not null) _cmap = CMap.Predefined(encName);
            else if (encObj is PdfStream ecs)
            {
                CMap? parent = null;
                var use = f.Resolve(ecs.Dict.Get("UseCMap"));
                if (use.AsName() is { } un) parent = CMap.Predefined(un);
                else if (use is PdfStream us) parent = CMap.Parse(f.DecodeStream(us));
                try { _cmap = CMap.Parse(f.DecodeStream(ecs), parent); }
                catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException) { }
            }
            Vertical = (encObj is PdfStream es ? f.Resolve(es.Dict.Get("WMode")).AsNumber() : null) is { } mode
                ? mode == 1 : _cmap?.WMode == 1;
            if (_cmap is { IsIdentity: true }) _cmap = null;
            if (f.Resolve(fontDict.Get("CIDSystemInfo")) is PdfDictionary csi && f.Resolve(csi.Get("Ordering")) is PdfString os) _ordering = Encoding.Latin1.GetString(os.Bytes);
            if (_ordering is "" or "Identity" && encName is not null) _ordering = OrderingFromCMap(encName);
            _dw = f.Resolve(fontDict.Get("DW")).AsNumber() ?? 1000;
            ParseW(f.Resolve(fontDict.Get("W")) as PdfArray);
            if (f.Resolve(fontDict.Get("DW2")) is PdfArray dw2 && dw2.Count == 2)
            {
                _defaultVerticalY = f.Resolve(dw2[0]).AsNumber() ?? 880;
                _defaultVerticalAdvance = f.Resolve(dw2[1]).AsNumber() ?? -1000;
            }
            ParseW2(f.Resolve(fontDict.Get("W2")) as PdfArray);
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
            var ffData = f.DecodeStream(FontFile);
            if (Type1Font.Looks(ffData))
            {
                try { _t1 = new Type1Font(ffData); }
                catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException or KeyNotFoundException) { }
            }
            else
            {
                try
                {
                    var tt = new TrueTypeFont(ffData);
                    if (tt.HasGlyf) _tt = tt;
                }
                catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException) { }
            }
            if (_tt is null && _t1 is null)
            {
                try { _cff = new CffFont(ffData); }
                catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException or KeyNotFoundException) { }
            }
        }
        if (_tt is null && _cff is null && _t1 is null && !IsType3)
        {
            var n = BaseFont.ToLowerInvariant();
            var bold = n.Contains("bold") || n.Contains("black") || (flags & (1 << 18)) != 0;
            var italic = n.Contains("italic") || n.Contains("oblique") || (flags & 64) != 0;
            var mono = n.Contains("courier") || n.Contains("mono") || (flags & 1) != 0;
            var serif = n.Contains("times") || n.Contains("serif") && !n.Contains("sans") || n.Contains("georgia") || n.Contains("garamond") || n.Contains("palatino") || n.Contains("roman") || (flags & 2) != 0 && !n.Contains("sans");
            if (IsType0 && _ordering is "Japan1" or "GB1" or "CNS1" or "Korea1" && fallback is ICjkFontFallback cjk)
            {
                _tt = cjk.FindCjk(_ordering, serif && !n.Contains("gothic") && !n.Contains("hei"));
                if (_tt is not null) _cidToUni = CidToUnicode(_ordering);
            }
            _tt ??= fallback.Find(BaseFont, bold, italic, mono, serif);
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

    void ParseW2(PdfArray? w)
    {
        if (w is null) return;
        (double, double, double) Metrics(PdfArray a, int i) =>
            (_f.Resolve(a[i]).AsNumber() ?? 0, _f.Resolve(a[i + 1]).AsNumber() ?? 0, _f.Resolve(a[i + 2]).AsNumber() ?? 0);
        for (var i = 0; i < w.Count;)
        {
            var first = _f.Resolve(w[i]).AsNumber(); if (first is null) break;
            if (i + 1 < w.Count && _f.Resolve(w[i + 1]) is PdfArray arr)
            {
                for (var k = 0; k + 2 < arr.Count; k += 3)
                    _cidVerticalMetrics[(int)first.Value + k / 3] = Metrics(arr, k);
                i += 2;
            }
            else if (i + 4 < w.Count)
            {
                var last = (int)(_f.Resolve(w[i + 1]).AsNumber() ?? first.Value);
                var metrics = Metrics(w, i + 2);
                for (var c = (int)first.Value; c <= last && c - (int)first.Value < 70000; c++)
                    _cidVerticalMetrics[c] = metrics;
                i += 5;
            }
            else break;
        }
    }

    /// <summary>
    /// Vertical displacement w1y and origin vector v in em units (9.7.4.3).
    /// This places the selected CID's outline; it does not perform OpenType vertical glyph substitution.
    /// </summary>
    internal (double Advance, double X, double Y) VerticalMetrics(GlyphRun run) =>
        _cidVerticalMetrics.TryGetValue(run.Code, out var m)
            ? (m.Advance / 1000, m.X / 1000, m.Y / 1000)
            : (_defaultVerticalAdvance / 1000, run.Width / 2, _defaultVerticalY / 1000);

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
        baseEnc ??= (_symbolic || _t1 is not null) && FontFile is not null ? null : "StandardEncoding";
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

    public string ToUnicode(int code) => ToUnicode(new GlyphRun(code, IsType0 ? 2 : 1, 0));

    /// <summary>Unicode text for a decoded run: ToUnicode CMap first, then the Unicode CMap/CID ordering tables for CJK, then glyph names.</summary>
    public string ToUnicode(GlyphRun r)
    {
        var code = r.Code;
        var raw = r.Raw >= 0 ? r.Raw : code;
        if (_toUnicode is not null && _toUnicode.TryGetValue(raw, out var cp)) return char.ConvertFromUtf32(cp);
        if (IsType0)
        {
            if (_cmap is { Name: var cn } && cn.StartsWith("Uni", StringComparison.Ordinal))
            {
                if (r.ByteLength == 2 && raw is < 0xD800 or > 0xDFFF) return raw >= 0x20 ? ((char)raw).ToString() : "";
                if (r.ByteLength == 4) { int hi = raw >> 16, lo = raw & 0xFFFF; if (hi is >= 0xD800 and < 0xDC00 && lo is >= 0xDC00 and < 0xE000) return char.ConvertFromUtf32(0x10000 + (hi - 0xD800 << 10) + (lo - 0xDC00)); }
            }
            if (_ordering is "Japan1" or "GB1" or "CNS1" or "Korea1" && CidToUnicode(_ordering).TryGetValue(code, out var u0)) return char.ConvertFromUtf32(u0);
            return "";
        }
        if (code < 256 && (_names[code] ?? _t1?.EncodingName(code)) is { } n && UnicodeForName(n) is { } u) return u;
        return code is >= 32 and < 127 ? ((char)code).ToString() : "";
    }

    static string OrderingFromCMap(string n) =>
        n.StartsWith("Uni", StringComparison.Ordinal) ? n[3..].StartsWith("JIS") ? "Japan1" : n[3..].StartsWith("GB") ? "GB1" : n[3..].StartsWith("CNS") ? "CNS1" : n[3..].StartsWith("KS") ? "Korea1" : "" :
        n.Contains("RKSJ") || n is "H" or "V" or "EUC-H" or "EUC-V" ? "Japan1" :
        n.StartsWith("GB") ? "GB1" :
        n.StartsWith("B5") || n.Contains("B5-") || n.StartsWith("CNS") ? "CNS1" :
        n.StartsWith("KSC") ? "Korea1" : "";

    static readonly Dictionary<string, Dictionary<int, int>> CidUniCache = [];

    /// <summary>CID to Unicode for an Adobe CJK ordering, inverted from the Uni*-UTF16-H CMap (lowest code point wins).</summary>
    static Dictionary<int, int> CidToUnicode(string ordering)
    {
        lock (CidUniCache)
        {
            if (CidUniCache.TryGetValue(ordering, out var d)) return d;
            d = [];
            var name = ordering switch { "Japan1" => "UniJIS-UTF16-H", "GB1" => "UniGB-UTF16-H", "CNS1" => "UniCNS-UTF16-H", "Korea1" => "UniKS-UTF16-H", _ => "" };
            if (name.Length > 0 && CMap.Predefined(name) is { } m)
                foreach (var (code, len, cid) in m.Entries())
                {
                    int cp;
                    if (len == 2) cp = (int)code;
                    else if (len == 4 && code >> 16 is >= 0xD800 and < 0xDC00) cp = 0x10000 + (((int)(code >> 16) - 0xD800) << 10) + ((int)(code & 0xFFFF) - 0xDC00);
                    else continue;
                    if (cid > 0 && (!d.TryGetValue(cid, out var old) || cp < old)) d[cid] = cp;
                }
            // Half-width/proportional Latin CIDs are absent from the Unicode CMaps; recover them from the legacy 1-byte ASCII ranges.
            foreach (var sec in ordering switch { "Japan1" => new[] { "UniJIS-UCS2-HW-H", "90ms-RKSJ-H" }, "GB1" => ["GBK-EUC-H"], "CNS1" => ["ETen-B5-H"], "Korea1" => ["KSCms-UHC-H"], _ => [] })
                if (CMap.Predefined(sec) is { } sm)
                    foreach (var (code, len, cid) in sm.Entries())
                        if (cid > 0 && ((len == 1 && code is >= 0x20 and < 0x7F) || (len == 2 && code is >= 0x20 and < 0x7F && sec.StartsWith("UniJIS")))) d.TryAdd(cid, (int)code);
            return CidUniCache[ordering] = d;
        }
    }

    Dictionary<int, int>? _reverse;

    /// <summary>
    /// Encodes a code point with this font's own embedded program, used when editing existing text. Fails unless the font is embedded
    /// and already contains a glyph for the character (subset fonts only keep the glyphs the original text used).
    /// </summary>
    public bool TryEncode(int codepoint, out byte[] bytes, out double width)
    {
        bytes = []; width = 0;
        if (FontFile is null || IsType3 || Vertical) return false;
        if (_reverse is null)
        {
            _reverse = [];
            var codes = IsType0 ? (_toUnicode?.Keys.ToList() ?? []) : Enumerable.Range(0, 256).ToList();
            foreach (var c in codes)
            {
                var u = ToUnicode(c);
                if (u.Length == 0) continue;
                var cp = char.ConvertToUtf32(u, 0);
                if (u.Length != (cp > 0xFFFF ? 2 : 1)) continue;
                if (_reverse.ContainsKey(cp)) continue;
                var g = GetGlyph(c);
                if (g is null || g.IsEmpty && !char.IsWhiteSpace(u, 0)) continue;
                _reverse[cp] = c;
            }
        }
        if (!_reverse.TryGetValue(codepoint, out var code)) return false;
        bytes = IsType0 ? [(byte)(code >> 8), (byte)code] : [(byte)code];
        width = Decode(bytes).First().Width;
        return true;
    }

    public IEnumerable<GlyphRun> Decode(byte[] s)
    {
        if (IsType0 && _cmap is not null)
        {
            for (var i = 0; i < s.Length;)
            {
                var len = _cmap.ReadCode(s, i, out var code);
                i += len;
                var cid = _cmap.ToCid(code, len);
                yield return new GlyphRun(cid, len, _cidWidths.GetValueOrDefault(cid, _dw) / 1000.0, code);
            }
            yield break;
        }
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
            if (!_hasWidth[c])
            {
                _widths[c] = FallbackWidth(c);
                _hasWidth[c] = true;
            }
            var w = _widths[c];
            yield return new GlyphRun(c, 1, IsType3 ? w * Type3Matrix[0] : w / 1000.0);
        }
    }

    double FallbackWidth(int c)
    {
        if (_cff is not null) { var cg = CffGlyphId(c); return cg > 0 ? _cff.Advance(cg) : _missingWidth; }
        if (_t1 is not null) { var tn = T1Name(c); return tn is not null ? _t1.Advance(tn) : _missingWidth; }
        if (_tt is null) return _missingWidth > 0 ? _missingWidth : 500;
        var g = GlyphIdFor(c);
        return g > 0 ? _tt.Advance(g) : _missingWidth;
    }

    public bool IsSpace(GlyphRun r) => r.ByteLength == 1 && (r.Raw >= 0 ? r.Raw : r.Code) == 32;

    string? T1Name(int code)
    {
        var t1 = _t1!;
        var name = code is >= 0 and < 256 ? _names[code] : null;
        if (name is not null)
        {
            if (t1.HasGlyph(name)) return name;
            if (UnicodeForName(name) is { Length: > 0 } u)
            {
                if (_t1Uni is null)
                {
                    _t1Uni = [];
                    foreach (var gn in t1.GlyphNames)
                        if (UnicodeForName(gn) is { Length: > 0 } gu) _t1Uni.TryAdd(gu, gn);
                }
                if (_t1Uni.TryGetValue(u, out var ug)) return ug;
            }
        }
        return t1.EncodingName(code) is { } bn && t1.HasGlyph(bn) ? bn : null;
    }

    int CffGlyphId(int code)
    {
        var cff = _cff!;
        if (IsType0)
        {
            if (_cidToGid is not null) return code * 2 + 1 < _cidToGid.Length ? _cidToGid[code * 2] << 8 | _cidToGid[code * 2 + 1] : 0;
            return cff.GlyphByCid(code);
        }
        var name = code is >= 0 and < 256 ? _names[code] : null;
        if (name is not null)
        {
            var g = cff.GlyphByName(name);
            if (g > 0) return g;
            if (UnicodeForName(name) is { Length: > 0 } u)
            {
                if (_cffUni is null)
                {
                    _cffUni = [];
                    for (var i = cff.GlyphCount - 1; i > 0; i--)
                        if (UnicodeForName(cff.GlyphName(i)) is { Length: > 0 } gu) _cffUni[gu] = i;
                }
                if (_cffUni.TryGetValue(u, out var ug)) return ug;
            }
        }
        return cff.GlyphByCode(code);
    }

    int GlyphIdFor(int code)
    {
        if (_tt is null) return 0;
        if (IsType0)
        {
            if (FontFile is null) return _tt.LookupAny(_cidToUni is not null ? _cidToUni.GetValueOrDefault(code) : code);
            if (_cidToGid is not null) return code * 2 + 1 < _cidToGid.Length ? _cidToGid[code * 2] << 8 | _cidToGid[code * 2 + 1] : 0;
            return code;
        }
        var name = _names[code];
        if (_fallbackUsed || !_symbolic || name is not null && FontFile is null)
        {
            if (name is not null)
            {
                if (FontFile is not null) { var g = _tt.GlyphByName(name); if (g > 0) return g; }
                if (UnicodeForName(name) is { } u && u.Length > 0) { var g = _tt.LookupAny(char.ConvertToUtf32(u, 0)); if (g > 0) return g; }
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
        if (_cff is not null)
        {
            var cg = CffGlyphId(code);
            if (cg > 0 || IsType0) p = _cff.GetOutline(cg);
        }
        else if (_t1 is not null)
        {
            if (T1Name(code) is { } tn) p = _t1.GetOutline(tn);
        }
        else if (_tt is not null)
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

    string? UnicodeForName(string name) => GlyphList.Lookup(name, BaseFont == "ZapfDingbats" ||
        BaseFont.Length > 7 && BaseFont[6] == '+' && BaseFont.AsSpan(7).SequenceEqual("ZapfDingbats"));

    /// <summary>Maps an Adobe glyph name to its complete Unicode sequence, or null when no component maps.</summary>
    public static string? NameToUnicode(string n) => GlyphList.Lookup(n);
}
