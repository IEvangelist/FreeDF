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
    string? Fill, string? Stroke, double StrokeWidth, double Opacity, string? Asset, double X2, double Y2);

public sealed record Asset(byte[] Data, string Mime);

/// <summary>Turns <see cref="Item"/>s into a PDF content stream plus resources, creating fonts/images in the editor as needed.</summary>
sealed class OverlayBuilder(PdfEditor editor, Dictionary<string, Asset> assets)
{
    readonly Dictionary<string, PdfRef> _fonts = [], _images = [], _gstates = [];

    static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    static (double R, double G, double B) Rgb(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#')
            return (Convert.ToInt32(hex[1..3], 16) / 255.0, Convert.ToInt32(hex[3..5], 16) / 255.0, Convert.ToInt32(hex[5..7], 16) / 255.0);
        return (0, 0, 0);
    }

    static string BaseFont(string? family, bool bold, bool italic) => (family ?? "Helvetica") switch
    {
        "Times" => bold ? (italic ? "Times-BoldItalic" : "Times-Bold") : (italic ? "Times-Italic" : "Times-Roman"),
        "Courier" => bold ? (italic ? "Courier-BoldOblique" : "Courier-Bold") : (italic ? "Courier-Oblique" : "Courier"),
        _ => bold ? (italic ? "Helvetica-BoldOblique" : "Helvetica-Bold") : (italic ? "Helvetica-Oblique" : "Helvetica"),
    };

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

    public (byte[] Content, Dictionary<string, IReadOnlyDictionary<string, PdfObject>> Resources) Build(IEnumerable<Item> items, PdfRect box)
    {
        var sb = new StringBuilder();
        var fonts = new Dictionary<string, PdfObject>(); var xobjs = new Dictionary<string, PdfObject>(); var gs = new Dictionary<string, PdfObject>();
        double Px(double x) => box.Left + x;
        double Py(double y) => box.Top - y;

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
                    var bf = BaseFont(it.Font, it.Bold, it.Italic);
                    var fkey = "EdF" + bf.Replace("-", "");
                    if (!_fonts.TryGetValue(fkey, out var f))
                        _fonts[fkey] = f = editor.AddObject(new PdfDictionary(new Dictionary<string, PdfObject>
                        { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName(bf), ["Encoding"] = new PdfName("WinAnsiEncoding") }));
                    fonts[fkey] = f;
                    var (r, g2, b) = Rgb(it.Color);
                    var size = it.Size > 0 ? it.Size : 12;
                    sb.Append($"{N(r)} {N(g2)} {N(b)} rg\nBT\n/{fkey} {N(size)} Tf\n{N(size * 1.2)} TL\n{N(Px(it.X))} {N(Py(it.Y) - size * 0.95)} Td\n");
                    foreach (var line in (it.Text ?? "").Replace("\r", "").Split('\n')) sb.Append($"{Escape(line)} '\n");
                    sb.Append("ET\n");
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
