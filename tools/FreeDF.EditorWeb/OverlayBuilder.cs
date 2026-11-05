using System.Globalization;
using System.IO.Compression;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Content.Shaping;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

/// <summary>An editable page object. Coordinates are points in the unrotated MediaBox with the origin at the top-left (y down).</summary>
public sealed record Item(
    string Id, string Type, double X, double Y, double W, double H,
    string? Text, string? Font, double Size, bool Bold, bool Italic, string? Color,
    string? Fill, string? Stroke, double StrokeWidth, double Opacity, string? Asset, double X2, double Y2, long? Orig = null,
    string? Align = null, bool Underline = false, bool Strike = false, double Spacing = 0, double LineHeight = 0, string? OStyle = null,
    ImageCrop? Crop = null, int Rotation = 0, bool FlipX = false, bool FlipY = false);

/// <summary>The retained source rectangle, normalized to the original image with a top-left origin. Cropping is not redaction.</summary>
public sealed record ImageCrop(double X, double Y, double W, double H);

public sealed record Asset(byte[] Data, string Mime);

/// <summary>Turns <see cref="Item"/>s into a PDF content stream plus resources, creating fonts/images in the editor as needed.</summary>
sealed class OverlayBuilder(PdfEditor editor, Dictionary<string, Asset> assets, FontCatalog catalog)
{
    readonly Dictionary<string, PdfRef> _fonts = [], _images = [], _gstates = [];

    internal static void ValidateImage(Item item)
    {
        if (item.Type != "image") return;
        if (!double.IsFinite(item.X) || !double.IsFinite(item.Y) || !double.IsFinite(item.W) || !double.IsFinite(item.H) ||
            item.W <= 0 || item.H <= 0 || item.Rotation is not (0 or 90 or 180 or 270))
            throw new ArgumentException("Image bounds must be finite and positive, and rotation must be a quarter turn.");
        if (item.Crop is { } c && (!double.IsFinite(c.X) || !double.IsFinite(c.Y) || !double.IsFinite(c.W) || !double.IsFinite(c.H) ||
            c.X < 0 || c.Y < 0 || c.W <= 0 || c.H <= 0 || c.X + c.W > 1 || c.Y + c.H > 1))
            throw new ArgumentException("The image crop must be a non-empty rectangle inside the original image.");
    }

    sealed class Embedded(FontFace face, byte[] data, TrueTypeFont font, string key)
    {
        public FontFace Face = face; public byte[] Data = data; public TrueTypeFont Font = font; public string Key = key;
        public SortedDictionary<int, string> Uni = [];
        /// <summary>Records the ToUnicode text of a glyph (empty text only marks the glyph as used); false when the glyph is already mapped to different text.</summary>
        public bool Map(int gid, string text)
        {
            if (Uni.TryGetValue(gid, out var have))
            {
                if (have.Length == 0) { Uni[gid] = text; return true; }
                return text.Length == 0 || have == text;
            }
            Uni[gid] = text;
            return true;
        }
        Dictionary<int, int>? _cid;

        /// <summary>The Identity-H code for a glyph: the GID, or the CID for a CID-keyed CFF font.</summary>
        public int Code(int gid)
        {
            if (!Font.IsCff || Font.HasGlyf) return gid;
            if (_cid is null)
            {
                _cid = [];
                var cff = new CffFont(Data.AsSpan(FontSubsetter.TableOffset(Data, "CFF "), FontSubsetter.TableLength(Data, "CFF ")).ToArray());
                if (cff.IsCid) for (var c = 0; c < 65536; c++) { var g = cff.GlyphByCid(c); if (g > 0) _cid.TryAdd(g, c); }
            }
            return _cid.Count == 0 ? gid : _cid.GetValueOrDefault(gid, gid);
        }
    }

    /// <summary>The embedded program of an existing page font, reused when editing text that was drawn with it.</summary>
    public sealed record OrigFont(PdfFont Font, PdfRef? Ref, Matrix? Transform = null, double WordSpacing = 0);

    sealed record Seg(string Text, Embedded? Emb, OrigFont? Orig, int Level = 0, IReadOnlyList<ShapedRun>? Runs = null);
    static readonly Dictionary<string, int> NoLigatures = new() { ["liga"] = 0, ["clig"] = 0 };
    static int Units(Seg s) => s.Runs is { } r ? r.Sum(x => x.Glyphs.Length) : s.Text.EnumerateRunes().Count();
    static bool MayBeRtl(string s)
    {
        foreach (var c in s)
            if (c >= '\u0590' && (c <= '\u08FF' || c is >= '\uFB1D' and <= '\uFEFF' or >= '\u200E' and <= '\u200F' or >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069') || c is >= '\uD802' and <= '\uD803' or >= '\uD83A' and <= '\uD83B') return true;
        return false;
    }

    static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    static (double R, double G, double B) Rgb(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#')
            return (Convert.ToInt32(hex[1..3], 16) / 255.0, Convert.ToInt32(hex[3..5], 16) / 255.0, Convert.ToInt32(hex[5..7], 16) / 255.0);
        return (0, 0, 0);
    }

    static string BaseFont(string? family, bool bold, bool italic) => (family ?? "Helvetica") switch
    {
        "Symbol" => "Symbol",
        "ZapfDingbats" => "ZapfDingbats",
        "Times" => bold ? (italic ? "Times-BoldItalic" : "Times-Bold") : (italic ? "Times-Italic" : "Times-Roman"),
        "Courier" => bold ? (italic ? "Courier-BoldOblique" : "Courier-Bold") : (italic ? "Courier-Oblique" : "Courier"),
        _ => bold ? (italic ? "Helvetica-BoldOblique" : "Helvetica-Bold") : (italic ? "Helvetica-Oblique" : "Helvetica"),
    };

    PdfRef BuildEmbedded(Embedded e)
    {
        static byte[] Deflate(byte[] d)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(d);
            return ms.ToArray();
        }
        PdfStream Stream(byte[] raw, params (string, PdfObject)[] extra)
        {
            var z = Deflate(raw);
            var d = new Dictionary<string, PdfObject> { ["Filter"] = new PdfName("FlateDecode"), ["Length"] = new PdfInteger(z.Length) };
            foreach (var (k, v) in extra) d[k] = v;
            return new PdfStream(new PdfDictionary(d), z);
        }
        static PdfDictionary Dict(params (string, PdfObject)[] kv) => new(kv.ToDictionary(x => x.Item1, x => x.Item2));

        var cffFont = e.Font.IsCff && !e.Font.HasGlyf;
        byte[] subset;
        if (cffFont)
        {
            var cffTable = e.Data.AsSpan(FontSubsetter.TableOffset(e.Data, "CFF "), FontSubsetter.TableLength(e.Data, "CFF ")).ToArray();
            subset = CffSubsetter.Subset(cffTable, new HashSet<int>(e.Uni.Keys)) ?? cffTable;
        }
        else subset = FontSubsetter.Subset(e.Data, new HashSet<int>(e.Uni.Keys));
        var tag = string.Concat(Enumerable.Range(0, 6).Select(i => (char)('A' + (Math.Abs(HashCode.Combine(e.Face.Key, e.Uni.Count, e.Uni.Keys.Sum())) >> (i * 3)) % 26)));
        var name = tag + "+" + new string(e.Face.PostScript.Where(c => c is > ' ' and < '\u007f' and not ('(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or '/' or '%')).ToArray());
        var upm = e.Font.UnitsPerEm;
        int Tbl(string t) => FontSubsetter.TableOffset(e.Data, t);
        int S16(int o) => (short)(e.Data[o] << 8 | e.Data[o + 1]);
        double K(int v) => Math.Round(v * 1000.0 / upm);
        var head = Tbl("head"); var hhea = Tbl("hhea"); var post = Tbl("post"); var os2 = Tbl("OS/2");
        var ascent = hhea >= 0 ? S16(hhea + 4) : upm; var descent = hhea >= 0 ? S16(hhea + 6) : 0;
        var angle = post >= 0 ? S16(post + 4) + (e.Data[post + 6] << 8 | e.Data[post + 7]) / 65536.0 : 0;
        var cap = os2 >= 0 && FontSubsetter.TableLength(e.Data, "OS/2") >= 90 ? S16(os2 + 88) : ascent;

        var file = cffFont
            ? editor.AddObject(Stream(subset, ("Subtype", new PdfName("CIDFontType0C"))))
            : editor.AddObject(Stream(subset, ("Length1", new PdfInteger(subset.Length))));
        var desc = editor.AddObject(Dict(
            ("Type", new PdfName("FontDescriptor")), ("FontName", new PdfName(name)),
            ("Flags", new PdfInteger(4 | (e.Face.Italic ? 64 : 0))),
            ("FontBBox", new PdfArray([new PdfInteger((long)K(S16(head + 36))), new PdfInteger((long)K(S16(head + 38))), new PdfInteger((long)K(S16(head + 40))), new PdfInteger((long)K(S16(head + 42)))])),
            ("ItalicAngle", new PdfReal(angle)), ("Ascent", new PdfInteger((long)K(ascent))), ("Descent", new PdfInteger((long)K(descent))),
            ("CapHeight", new PdfInteger((long)K(cap == 0 ? ascent : cap))), ("StemV", new PdfInteger(e.Face.Bold ? 140 : 80)), (cffFont ? "FontFile3" : "FontFile2", file)));
        var w = new List<PdfObject>();
        foreach (var gid in e.Uni.Keys) { w.Add(new PdfInteger(e.Code(gid))); w.Add(new PdfArray([new PdfInteger((long)Math.Round(e.Font.Advance(gid)))])); }
        var cidDict = new List<(string, PdfObject)>
        {
            ("Type", new PdfName("Font")), ("Subtype", new PdfName(cffFont ? "CIDFontType0" : "CIDFontType2")), ("BaseFont", new PdfName(name)),
            ("CIDSystemInfo", Dict(("Registry", new PdfString(Encoding.ASCII.GetBytes("Adobe"))), ("Ordering", new PdfString(Encoding.ASCII.GetBytes("Identity"))), ("Supplement", new PdfInteger(0)))),
            ("FontDescriptor", desc), ("DW", new PdfInteger(1000)), ("W", new PdfArray(w)),
        };
        if (!cffFont) cidDict.Add(("CIDToGIDMap", new PdfName("Identity")));
        var cid = editor.AddObject(Dict([.. cidDict]));

        var cm = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n/CMapName /Adobe-Identity-UCS def /CMapType 2 def\n1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
        foreach (var chunk in e.Uni.Where(x => x.Value.Length > 0).Chunk(100))
        {
            cm.Append(chunk.Length).Append(" beginbfchar\n");
            foreach (var (g, u) in chunk.Where(x => x.Value.Length > 0))
                cm.Append('<').Append(e.Code(g).ToString("X4")).Append("> <").Append(string.Concat(u.Select(c => ((int)c).ToString("X4")))).Append(">\n");
            cm.Append("endbfchar\n");
        }
        cm.Append("endcmap CMapName currentdict /CMap defineresource pop end end\n");
        var tu = editor.AddObject(Stream(Encoding.ASCII.GetBytes(cm.ToString())));
        return editor.AddObject(Dict(
            ("Type", new PdfName("Font")), ("Subtype", new PdfName("Type0")), ("BaseFont", new PdfName(name)),
            ("Encoding", new PdfName("Identity-H")), ("DescendantFonts", new PdfArray([cid])), ("ToUnicode", tu)));
    }

    const string Cp1252High = "€\0‚ƒ„…†‡ˆ‰Š‹Œ\0Ž\0\0‘’“”•–—˜™š›œ\0žŸ";

    static string Escape(string s)
    {
        var sb = new StringBuilder("(");
        foreach (var ch in s)
        {
            var hi = ch >= 256 ? Cp1252High.IndexOf(ch) : -1;
            var b = ch < 256 ? ch : hi >= 0 ? (char)(0x80 + hi) : '?';
            if (b is '(' or ')' or '\\') sb.Append('\\').Append(b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append(b);
        }
        return sb.Append(')').ToString();
    }

    public (byte[] Content, Dictionary<string, IReadOnlyDictionary<string, PdfObject>> Resources) Build(IEnumerable<Item> source, PdfRect box, Func<Item, OrigFont?>? original = null)
    {
        var items = source.ToList();
        var sb = new StringBuilder();
        var fonts = new Dictionary<string, PdfObject>(); var xobjs = new Dictionary<string, PdfObject>(); var gs = new Dictionary<string, PdfObject>();
        double Px(double x) => box.Left + x;
        double Py(double y) => box.Top - y;

        // Installed fonts are embedded as subsets; the glyphs each needs are collected while the text is split into runs.
        var emb = new Dictionary<string, Embedded>();
        Embedded GetEmb(FontFace face)
        {
            if (!emb.TryGetValue(face.Key, out var e))
            {
                var (data, tt) = catalog.Load(face);
                emb[face.Key] = e = new Embedded(face, data, tt, "EdE" + emb.Count);
            }
            return e;
        }
        double MeasureStd(Item it, string line, double size)
        {
            // Arial, Times New Roman and Courier New are metric-compatible with the standard Helvetica, Times and Courier.
            var proxy = (it.Font ?? "Helvetica") switch { "Times" => "Times New Roman", "Courier" => "Courier New", _ => "Arial" };
            var f = catalog.Resolve(proxy, it.Bold, it.Italic);
            var font = f is not null ? catalog.Load(f).Item2 : null;
            if (font is null) return line.Length * size * 0.5;
            double w = 0;
            foreach (var rn in line.EnumerateRunes()) w += font.Advance(font.LookupAny(rn.Value));
            return w / 1000.0 * size;
        }
        double MeasureSeg(Item it, Seg s, double size)
        {
            if (s.Emb is not null)
            {
                return s.Runs!.Sum(r => r.Advance) / 1000.0 * size;
            }
            if (s.Orig is { } o)
            {
                double w = 0;
                foreach (var rn in s.Text.EnumerateRunes()) if (o.Font.TryEncode(rn.Value, out _, out var gw)) w += gw;
                return w * size;
            }
            return MeasureStd(it, s.Text, size);
        }
        static bool StdCovers(int r) => r < 256 || Cp1252High.IndexOf((char)Math.Min(r, 0xFFFF)) >= 0 && r <= 0xFFFF;
        // Splits a line into runs: the original page font where it has the glyph, then the chosen font, then any installed font that has it.
        List<Seg> Segment(Item it, string line, OrigFont? orig, Embedded? primary)
        {
            var levels = MayBeRtl(line) ? Bidi.Resolve(line).Levels : null;
            var segs = new List<(Embedded? E, OrigFont? O, StringBuilder T, int Level)>();
            for (var i = 0; i < line.Length;)
            {
                System.Text.Rune.DecodeFromUtf16(line.AsSpan(i), out var rn, out var consumed);
                var emoji = UnicodeData.EmojiClusterLength(line, i, line.Length);
                var unit = emoji > 0 ? emoji : consumed;
                Embedded? e = null; OrigFont? o = null;
                if (orig?.Ref is not null && orig.Font.TryEncode(rn.Value, out _, out _)) o = orig;
                else if (primary is not null)
                {
                    if (primary.Font.LookupAny(rn.Value) > 0 || char.IsControl((char)Math.Min(rn.Value, 0xFFFF))) e = primary;
                    else if (catalog.Fallback(rn.Value, it.Bold, it.Italic) is { } fb) e = GetEmb(fb);
                    else e = primary;
                }
                else if (!StdCovers(rn.Value) && catalog.Fallback(rn.Value, it.Bold, it.Italic) is { } fb2) e = GetEmb(fb2);
                var level = levels?[i] ?? 0;
                if (segs.Count == 0 || segs[^1].E != e || segs[^1].O != o || segs[^1].Level != level) segs.Add((e, o, new StringBuilder(), level));
                segs[^1].T.Append(line, i, unit);
                i += unit;
            }
            var opts = it.Spacing != 0 ? new ShapingOptions { Features = NoLigatures } : null;
            var result = new List<Seg>(segs.Count);
            foreach (var (e, o, tb, level) in segs)
            {
                var text = tb.ToString();
                var rtl = (level & 1) == 1;
                if (e is not null)
                {
                    var runs = TextShaper.ItemizeRun(text, 0, text.Length, rtl).Select(x => TextShaper.Shape(e.Font, text, x.Start, x.Length, rtl, x.Script, opts)).ToList();
                    if (rtl) runs.Reverse();
                    result.Add(new Seg(text, e, o, level, runs));
                }
                else
                {
                    if (rtl) text = string.Concat(text.EnumerateRunes().Reverse().Select(r => char.ConvertFromUtf32(UnicodeData.Mirrored(r.Value))));
                    result.Add(new Seg(text, e, o, level));
                }
            }
            if (levels is null) return result;
            return Bidi.VisualOrder(result.Select(s => s.Level).ToList()).Select(i => result[i]).ToList();
        }        foreach (var it in items)
        {
            sb.Append("q\n");
            if (it.Opacity is > 0 and < 1)
            {
                var key = "EdG" + N(it.Opacity * 1000).Replace(".", "_");
                if (!_gstates.TryGetValue(key, out var g))
                    _gstates[key] = g = editor.AddObject(new PdfDictionary(new Dictionary<string, PdfObject>
                    { ["Type"] = new PdfName("ExtGState"), ["ca"] = new PdfReal(it.Opacity), ["CA"] = new PdfReal(it.Opacity) }));
                gs[key] = g; sb.Append($"/{key} gs\n");
            }
            switch (it.Type)
            {
                case "text":
                {
                    var (r, g2, b) = Rgb(it.Color);
                    var size = it.Size > 0 ? it.Size : 12;
                    var lines = (it.Text ?? "").Replace("\r", "").Split('\n');
                    var lead = size * (it.LineHeight > 0 ? it.LineHeight : 1.2); var tc = it.Spacing;
                    var face = FontCatalog.Standard.Contains(it.Font ?? "Helvetica") ? null : catalog.Resolve(it.Font, it.Bold, it.Italic);
                    var ef = face is null ? null : GetEmb(face);
                    // The page's own font is reused only while the user has not changed the family or style.
                    var sourceText = it.Orig is not null ? original?.Invoke(it) : null;
                    var orig = it.OStyle is not null && it.OStyle == $"{it.Font}|{it.Bold.ToString().ToLowerInvariant()}|{it.Italic.ToString().ToLowerInvariant()}" ? sourceText : null;
                    var segLines = lines.Select(l => Segment(it, l, orig, ef)).ToList();
                    var widths = segLines.Select((segs, li) => segs.Sum(s => MeasureSeg(it, s, size) + tc * Units(s))).ToArray();
                    var boxW = it.W > 0 ? it.W : widths.Max();
                    double Left(int li) => it.Align switch { "center" => (boxW - widths[li]) / 2, "right" => boxW - widths[li], _ => 0 };
                    var rule = new StringBuilder();
                    var synthBold = ef is not null && it.Bold && !face!.Bold; var skew = ef is not null && it.Italic && !face!.Italic ? 0.2 : 0;
                    string StdKey()
                    {
                        var bf = BaseFont(it.Font, it.Bold, it.Italic);
                        var key = "EdF" + bf.Replace("-", "");
                        if (!_fonts.TryGetValue(key, out var f))
                        {
                            var fd = new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName(bf) };
                            if (bf is not ("Symbol" or "ZapfDingbats")) fd["Encoding"] = new PdfName("WinAnsiEncoding");
                            _fonts[key] = f = editor.AddObject(new PdfDictionary(fd));
                        }
                        fonts[key] = f;
                        return key;
                    }
                    sb.Append($"{N(r)} {N(g2)} {N(b)} rg\n");
                    if (synthBold) sb.Append($"{N(r)} {N(g2)} {N(b)} RG\n{N(size * 0.03)} w\n2 Tr\n");
                    sb.Append($"BT\n{N(tc)} Tc\n{N(sourceText?.WordSpacing ?? 0)} Tw\n");
                    for (var li = 0; li < lines.Length; li++)
                    {
                        var matrix = sourceText?.Transform ?? new Matrix(1, 0, skew, 1, 0, 0);
                        var x = Px(it.X) + matrix.A * Left(li) - matrix.C * li * lead;
                        var y = Py(it.Y) - size * 0.95 + matrix.B * Left(li) - matrix.D * li * lead;
                        sb.Append($"{N(matrix.A)} {N(matrix.B)} {N(matrix.C)} {N(matrix.D)} {N(x)} {N(y)} Tm\n");
                        foreach (var seg in segLines[li])
                        {
                            string key, shown;
                            if (seg.Emb is { } se)
                            {
                                sb.Append($"/{se.Key} {N(size)} Tf\n");
                                foreach (var run in seg.Runs!) ShapedPdfWriter.Write(sb, run, size, se.Code, g => Math.Round(se.Font.Advance(g)), se.Map);
                                continue;
                            }
                            else if (seg.Orig is { Ref: { } fontRef } so)
                            {
                                key = "EdO" + fontRef.Number;
                                fonts[key] = fontRef;
                                var hex = new StringBuilder("<");
                                foreach (var rn in seg.Text.EnumerateRunes()) if (so.Font.TryEncode(rn.Value, out var bytes, out _)) hex.Append(Convert.ToHexString(bytes));
                                shown = hex + ">";
                            }
                            else { key = StdKey(); shown = Escape(seg.Text); }
                            sb.Append($"/{key} {N(size)} Tf\n{shown} Tj\n");
                        }
                        if (it.Underline) rule.Append($"{N(x)} {N(y - size * 0.14)} {N(widths[li])} {N(Math.Max(size * 0.06, 0.5))} re\n");
                        if (it.Strike) rule.Append($"{N(x)} {N(y + size * 0.3)} {N(widths[li])} {N(Math.Max(size * 0.06, 0.5))} re\n");
                    }
                    sb.Append("ET\n");
                    if (rule.Length > 0) sb.Append("0 Tr\n").Append(rule).Append("f\n");
                    break;
                }
                case "rect":
                case "ellipse":
                    Paint(sb, it);
                    if (it.Type == "rect") sb.Append($"{N(Px(it.X))} {N(Py(it.Y + it.H))} {N(it.W)} {N(it.H)} re\n");
                    else Ellipse(sb, Px(it.X + it.W / 2), Py(it.Y + it.H / 2), it.W / 2, it.H / 2);
                    sb.Append(Op(it)).Append('\n');
                    break;
                case "line":
                    Paint(sb, it);
                    sb.Append($"{N(Px(it.X))} {N(Py(it.Y))} m {N(Px(it.X2))} {N(Py(it.Y2))} l S\n");
                    break;
                case "image" when it.Asset is { } aid && assets.TryGetValue(aid, out var asset):
                {
                    ValidateImage(it);
                    if (!_images.TryGetValue(aid, out var img)) { var made = MakeImage(asset); if (made is null) break; _images[aid] = img = made; }
                    var key = "EdX" + _images.Keys.ToList().IndexOf(aid);
                    xobjs[key] = img;
                    var crop = it.Crop ?? new ImageCrop(0, 0, 1, 1);
                    (double X, double Y) Map(double u, double v)
                    {
                        var x = (u - crop.X) / crop.W; var y = (1 - v - crop.Y) / crop.H;
                        if (it.FlipX) x = 1 - x;
                        if (it.FlipY) y = 1 - y;
                        (x, y) = it.Rotation switch { 90 => (1 - y, x), 180 => (1 - x, 1 - y), 270 => (y, 1 - x), _ => (x, y) };
                        return (Px(it.X + x * it.W), Py(it.Y + y * it.H));
                    }
                    var origin = Map(0, 0); var right = Map(1, 0); var top = Map(0, 1);
                    sb.Append($"{N(Px(it.X))} {N(Py(it.Y + it.H))} {N(it.W)} {N(it.H)} re W n\n");
                    sb.Append($"{N(right.X - origin.X)} {N(right.Y - origin.Y)} {N(top.X - origin.X)} {N(top.Y - origin.Y)} {N(origin.X)} {N(origin.Y)} cm\n/{key} Do\n");
                    break;
                }
            }
            sb.Append("Q\n");
        }

        foreach (var e in emb.Values) fonts[e.Key] = BuildEmbedded(e);
        var res = new Dictionary<string, IReadOnlyDictionary<string, PdfObject>>();
        if (fonts.Count > 0) res["Font"] = fonts;
        if (xobjs.Count > 0) res["XObject"] = xobjs;
        if (gs.Count > 0) res["ExtGState"] = gs;
        return (Encoding.Latin1.GetBytes(sb.ToString()), res);
    }

    static void Paint(StringBuilder sb, Item it)
    {
        if (it.Fill is { Length: 7 }) { var (r, g, b) = Rgb(it.Fill); sb.Append($"{N(r)} {N(g)} {N(b)} rg\n"); }
        var (sr, sg, sbl) = Rgb(it.Stroke);
        sb.Append($"{N(sr)} {N(sg)} {N(sbl)} RG\n{N(Math.Max(it.StrokeWidth, 0))} w\n");
    }

    static string Op(Item it)
    {
        var fill = it.Fill is { Length: 7 }; var stroke = it.Stroke is { Length: 7 } && it.StrokeWidth > 0;
        return fill && stroke ? "B" : fill ? "f" : stroke ? "S" : "n";
    }

    static void Ellipse(StringBuilder sb, double cx, double cy, double rx, double ry)
    {
        const double k = 0.5522847498;
        sb.Append($"{N(cx + rx)} {N(cy)} m\n");
        sb.Append($"{N(cx + rx)} {N(cy + k * ry)} {N(cx + k * rx)} {N(cy + ry)} {N(cx)} {N(cy + ry)} c\n");
        sb.Append($"{N(cx - k * rx)} {N(cy + ry)} {N(cx - rx)} {N(cy + k * ry)} {N(cx - rx)} {N(cy)} c\n");
        sb.Append($"{N(cx - rx)} {N(cy - k * ry)} {N(cx - k * rx)} {N(cy - ry)} {N(cx)} {N(cy - ry)} c\n");
        sb.Append($"{N(cx + k * rx)} {N(cy - ry)} {N(cx + rx)} {N(cy - k * ry)} {N(cx + rx)} {N(cy)} c h\n");
    }

    PdfRef? MakeImage(Asset a)
    {
        static byte[] Deflate(byte[] d)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(d);
            return ms.ToArray();
        }
        PdfDictionary Dict(int w, int h, string cs, int bpc, params (string, PdfObject)[] extra)
        {
            var d = new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Image"), ["Width"] = new PdfInteger(w), ["Height"] = new PdfInteger(h),
                ["ColorSpace"] = new PdfName(cs), ["BitsPerComponent"] = new PdfInteger(bpc),
            };
            foreach (var (k, v) in extra) d[k] = v;
            return new PdfDictionary(d);
        }
        try
        {
            if (a.Mime == "image/jpeg")
            {
                if (JpegDecoder.Decode(a.Data, out var w, out var h, out var nc) is null) return null;
                var cs = nc == 1 ? "DeviceGray" : nc == 4 ? "DeviceCMYK" : "DeviceRGB";
                return editor.AddObject(new PdfStream(Dict(w, h, cs, 8, ("Filter", new PdfName("DCTDecode")), ("Length", new PdfInteger(a.Data.Length))), a.Data));
            }
            var (pw, ph, rgba) = PngWriter.Decode(a.Data);
            var rgb = new byte[pw * ph * 3]; var alpha = new byte[pw * ph]; var hasAlpha = false;
            for (var i = 0; i < pw * ph; i++)
            {
                rgb[i * 3] = rgba[i * 4]; rgb[i * 3 + 1] = rgba[i * 4 + 1]; rgb[i * 3 + 2] = rgba[i * 4 + 2];
                alpha[i] = rgba[i * 4 + 3]; hasAlpha |= alpha[i] != 255;
            }
            var extra = new List<(string, PdfObject)> { ("Filter", new PdfName("FlateDecode")) };
            if (hasAlpha)
            {
                var ad = Deflate(alpha);
                extra.Add(("SMask", editor.AddObject(new PdfStream(Dict(pw, ph, "DeviceGray", 8, ("Filter", new PdfName("FlateDecode")), ("Length", new PdfInteger(ad.Length))), ad))));
            }
            var data = Deflate(rgb);
            extra.Add(("Length", new PdfInteger(data.Length)));
            return editor.AddObject(new PdfStream(Dict(pw, ph, "DeviceRGB", 8, [.. extra]), data));
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException or IndexOutOfRangeException or ArgumentException) { return null; }
    }
}
