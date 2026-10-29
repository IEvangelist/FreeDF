using System.Globalization;
using System.IO.Compression;
using System.Text;
using Pdf.Content;
using Pdf.Core;
using Pdf.Document;
using Pdf.Render;

/// <summary>An editable page object. Coordinates are points in the unrotated MediaBox with the origin at the top-left (y down).</summary>
public sealed record Item(
    string Id, string Type, double X, double Y, double W, double H,
    string? Text, string? Font, double Size, bool Bold, bool Italic, string? Color,
    string? Fill, string? Stroke, double StrokeWidth, double Opacity, string? Asset, double X2, double Y2, int? Orig = null,
    string? Align = null, bool Underline = false, bool Strike = false, double Spacing = 0, double LineHeight = 0);

public sealed record Asset(byte[] Data, string Mime);

/// <summary>Turns <see cref="Item"/>s into a PDF content stream plus resources, creating fonts/images in the editor as needed.</summary>
sealed class OverlayBuilder(PdfEditor editor, Dictionary<string, Asset> assets, FontCatalog catalog)
{
    readonly Dictionary<string, PdfRef> _fonts = [], _images = [], _gstates = [];

    sealed class Embedded(FontFace face, byte[] data, TrueTypeFont font, string key)
    {
        public FontFace Face = face; public byte[] Data = data; public TrueTypeFont Font = font; public string Key = key;
        public SortedDictionary<int, int> Uni = [];
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

        var subset = FontSubsetter.Subset(e.Data, new HashSet<int>(e.Uni.Keys));
        var tag = string.Concat(Enumerable.Range(0, 6).Select(i => (char)('A' + (Math.Abs(HashCode.Combine(e.Face.Path, e.Uni.Count, e.Uni.Keys.Sum())) >> (i * 3)) % 26)));
        var name = tag + "+" + new string(e.Face.PostScript.Where(c => c is > ' ' and < '\u007f' and not ('(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or '/' or '%')).ToArray());
        var upm = e.Font.UnitsPerEm;
        int Tbl(string t) => FontSubsetter.TableOffset(e.Data, t);
        int S16(int o) => (short)(e.Data[o] << 8 | e.Data[o + 1]);
        double K(int v) => Math.Round(v * 1000.0 / upm);
        var head = Tbl("head"); var hhea = Tbl("hhea"); var post = Tbl("post"); var os2 = Tbl("OS/2");
        var ascent = hhea >= 0 ? S16(hhea + 4) : upm; var descent = hhea >= 0 ? S16(hhea + 6) : 0;
        var angle = post >= 0 ? S16(post + 4) + (e.Data[post + 6] << 8 | e.Data[post + 7]) / 65536.0 : 0;
        var cap = os2 >= 0 && FontSubsetter.TableLength(e.Data, "OS/2") >= 90 ? S16(os2 + 88) : ascent;

        var file = editor.AddObject(Stream(subset, ("Length1", new PdfInteger(subset.Length))));
        var desc = editor.AddObject(Dict(
            ("Type", new PdfName("FontDescriptor")), ("FontName", new PdfName(name)),
            ("Flags", new PdfInteger(4 | (e.Face.Italic ? 64 : 0))),
            ("FontBBox", new PdfArray([new PdfInteger((long)K(S16(head + 36))), new PdfInteger((long)K(S16(head + 38))), new PdfInteger((long)K(S16(head + 40))), new PdfInteger((long)K(S16(head + 42)))])),
            ("ItalicAngle", new PdfReal(angle)), ("Ascent", new PdfInteger((long)K(ascent))), ("Descent", new PdfInteger((long)K(descent))),
            ("CapHeight", new PdfInteger((long)K(cap == 0 ? ascent : cap))), ("StemV", new PdfInteger(e.Face.Bold ? 140 : 80)), ("FontFile2", file)));
        var w = new List<PdfObject>();
        foreach (var gid in e.Uni.Keys) { w.Add(new PdfInteger(gid)); w.Add(new PdfArray([new PdfInteger((long)Math.Round(e.Font.Advance(gid)))])); }
        var cid = editor.AddObject(Dict(
            ("Type", new PdfName("Font")), ("Subtype", new PdfName("CIDFontType2")), ("BaseFont", new PdfName(name)),
            ("CIDSystemInfo", Dict(("Registry", new PdfString(Encoding.ASCII.GetBytes("Adobe"))), ("Ordering", new PdfString(Encoding.ASCII.GetBytes("Identity"))), ("Supplement", new PdfInteger(0)))),
            ("FontDescriptor", desc), ("CIDToGIDMap", new PdfName("Identity")), ("DW", new PdfInteger(1000)), ("W", new PdfArray(w))));

        var cm = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n/CMapName /Adobe-Identity-UCS def /CMapType 2 def\n1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
        foreach (var chunk in e.Uni.Chunk(100))
        {
            cm.Append(chunk.Length).Append(" beginbfchar\n");
            foreach (var (g, u) in chunk)
                cm.Append('<').Append(g.ToString("X4")).Append("> <").Append(u > 0xFFFF ? char.ConvertFromUtf32(u).Select(c => ((int)c).ToString("X4")).Aggregate((a, b) => a + b) : u.ToString("X4")).Append(">\n");
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

    public (byte[] Content, Dictionary<string, IReadOnlyDictionary<string, PdfObject>> Resources) Build(IEnumerable<Item> source, PdfRect box)
    {
        var items = source.ToList();
        var sb = new StringBuilder();
        var fonts = new Dictionary<string, PdfObject>(); var xobjs = new Dictionary<string, PdfObject>(); var gs = new Dictionary<string, PdfObject>();
        double Px(double x) => box.Left + x;
        double Py(double y) => box.Top - y;

        // Installed fonts are embedded as subsets, so first gather the glyphs each one needs on this page.
        var emb = new Dictionary<string, Embedded>();
        Embedded? Face(Item it, out FontFace? face)
        {
            face = FontCatalog.Standard.Contains(it.Font ?? "Helvetica") ? null : catalog.Resolve(it.Font, it.Bold, it.Italic);
            return face is not null && emb.TryGetValue(face.Path, out var e) ? e : null;
        }
        double Measure(Item it, Embedded? ef, string line, double size)
        {
            var font = ef?.Font;
            if (font is null)
            {
                // Arial, Times New Roman and Courier New are metric-compatible with the standard Helvetica, Times and Courier.
                var proxy = (it.Font ?? "Helvetica") switch { "Times" => "Times New Roman", "Courier" => "Courier New", _ => "Arial" };
                var f = catalog.Resolve(proxy, it.Bold, it.Italic);
                if (f is not null) font = catalog.Load(f).Item2;
            }
            if (font is null) return line.Length * size * 0.5;
            double w = 0;
            foreach (var rn in line.EnumerateRunes()) w += font.Advance(font.LookupAny(rn.Value));
            return w / 1000.0 * size;
        }
        foreach (var it in items.Where(i => i.Type == "text"))
        {
            if (FontCatalog.Standard.Contains(it.Font ?? "Helvetica") || catalog.Resolve(it.Font, it.Bold, it.Italic) is not { } face) continue;
            if (!emb.TryGetValue(face.Path, out var e))
            {
                var (data, tt) = catalog.Load(face);
                emb[face.Path] = e = new Embedded(face, data, tt, "EdE" + emb.Count);
            }
            foreach (var r in (it.Text ?? "").EnumerateRunes()) { var gid = e.Font.LookupAny(r.Value); if (gid > 0) e.Uni.TryAdd(gid, r.Value); }
        }
        foreach (var it in items)
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
                    var ef = Face(it, out var face);
                    var widths = lines.Select(l => Measure(it, ef, l, size) + tc * l.EnumerateRunes().Count()).ToArray();
                    var boxW = it.W > 0 ? it.W : widths.Max();
                    double Left(int li) => it.Align switch { "center" => (boxW - widths[li]) / 2, "right" => boxW - widths[li], _ => 0 };
                    var rule = new StringBuilder();
                    string fkey = "";
                    var synthBold = ef is not null && it.Bold && !face!.Bold; var skew = ef is not null && it.Italic && !face!.Italic ? 0.2 : 0;
                    if (ef is null)
                    {
                        var bf = BaseFont(it.Font, it.Bold, it.Italic);
                        fkey = "EdF" + bf.Replace("-", "");
                        if (!_fonts.TryGetValue(fkey, out var f))
                        {
                            var fd = new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName(bf) };
                            if (bf is not ("Symbol" or "ZapfDingbats")) fd["Encoding"] = new PdfName("WinAnsiEncoding");
                            _fonts[fkey] = f = editor.AddObject(new PdfDictionary(fd));
                        }
                        fonts[fkey] = f;
                    }
                    else fkey = ef.Key;
                    sb.Append($"{N(r)} {N(g2)} {N(b)} rg\n");
                    if (synthBold) sb.Append($"{N(r)} {N(g2)} {N(b)} RG\n{N(size * 0.03)} w\n2 Tr\n");
                    sb.Append($"BT\n/{fkey} {N(size)} Tf\n{N(tc)} Tc\n");
                    for (var li = 0; li < lines.Length; li++)
                    {
                        var x = Px(it.X) + Left(li); var y = Py(it.Y) - size * 0.95 - li * lead;
                        string shown;
                        if (ef is not null)
                        {
                            var hex = new StringBuilder("<");
                            foreach (var rn in lines[li].EnumerateRunes()) hex.Append(ef.Font.LookupAny(rn.Value).ToString("X4"));
                            shown = hex + ">";
                        }
                        else shown = Escape(lines[li]);
                        sb.Append($"1 0 {N(skew)} 1 {N(x)} {N(y)} Tm\n{shown} Tj\n");
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
                    if (!_images.TryGetValue(aid, out var img)) { var made = MakeImage(asset); if (made is null) break; _images[aid] = img = made; }
                    var key = "EdX" + _images.Keys.ToList().IndexOf(aid);
                    xobjs[key] = img;
                    sb.Append($"{N(it.W)} 0 0 {N(it.H)} {N(Px(it.X))} {N(Py(it.Y + it.H))} cm\n/{key} Do\n");
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
