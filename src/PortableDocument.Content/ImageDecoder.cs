using PortableDocument.Core;

namespace PortableDocument.Content;

public static class ImageDecoder
{
    public static ImageData? Decode(PdfFile f, PdfStream xs, PdfDictionary res, out bool isMask, out string? warning, int depth = 0)
        => Decode(f, xs, res, out isMask, out warning, RenderingIntent.RelativeColorimetric, depth);

    public static ImageData? Decode(PdfFile f, PdfStream xs, PdfDictionary res, out bool isMask, out string? warning, RenderingIntent intent, int depth = 0)
        => Decode(f, xs, res, out isMask, out warning, intent, BlackPointCompensation.Default, depth);

    public static ImageData? Decode(PdfFile f, PdfStream xs, PdfDictionary res, out bool isMask, out string? warning, RenderingIntent intent, BlackPointCompensation blackPointCompensation, int depth = 0)
    {
        warning = null;
        var d = xs.Dict;
        PdfObject? R(PdfObject? o) => f.Resolve(o);
        var imageIntent = R(d.Get("Intent")) is PdfName name ? RenderingIntentNames.Parse(name.Value) : intent;
        int w = (int)(R(d.Get("Width")).AsNumber() ?? 0), h = (int)(R(d.Get("Height")).AsNumber() ?? 0);
        isMask = R(d.Get("ImageMask")) is PdfBoolean { Value: true };
        if (w <= 0 || h <= 0 || (long)w * h > 200_000_000) return null;
        var data = f.DecodeStream(xs, out var codec, out var parms);
        var bpc = isMask ? 1 : (int)(R(d.Get("BitsPerComponent")).AsNumber() ?? 8);
        PdfColorSpace cs = isMask ? PdfColorSpace.Gray : PdfColorSpace.ParseWithDefaults(f, d.Get("ColorSpace"), res);
        double[]? decode = R(d.Get("Decode")) is PdfArray da ? da.Items.Select(x => R(x).AsNumber() ?? 0).ToArray() : null;

        JpxImage? jpxAlpha = null;
        if (codec is "DCTDecode")
        {
            var jpg = JpegDecoder.Decode(data, out var jw, out var jh, out var jn);
            if (jpg is null) { warning = "JPEG could not be decoded"; return null; }
            data = jpg; w = jw; h = jh; bpc = 8;
            if (R(d.Get("ColorSpace")) is null || cs.Components != jn) cs = jn switch { 1 => PdfColorSpace.Gray, 4 => PdfColorSpace.Cmyk, _ => PdfColorSpace.Rgb };
        }
        else if (codec is "CCITTFaxDecode" or "CCF")
        {
            int Pi(string k, int def) => (int)(R(parms?.Get(k)).AsNumber() ?? def);
            bool Pb(string k) => R(parms?.Get(k)) is PdfBoolean { Value: true };
            data = CcittDecoder.Decode(data, Pi("K", 0), Pi("Columns", 1728), Pi("Rows", 0), Pb("BlackIs1"), Pb("EncodedByteAlign"), h);
            bpc = 1;
        }
        else if (codec is "JBIG2Decode")
        {
            byte[]? globals = R(parms?.Get("JBIG2Globals")) is PdfStream gs ? f.DecodeStream(gs, out _, out _) : null;
            var jb = Jbig2Decoder.Decode(data, globals, w, h, out var jbWarn);
            if (jb is null) { warning = jbWarn ?? "JBIG2 could not be decoded"; return null; }
            warning = jbWarn;
            data = jb; bpc = 1;
        }
        else if (codec is "JPXDecode")
        {
            var csObj = R(d.Get("ColorSpace"));
            var indexed = csObj is PdfArray { Items.Count: > 0 } ca && R(ca.Items[0]) is PdfName { Value: "Indexed" or "I" };
            var smaskInData = (int)(R(d.Get("SMaskInData")).AsNumber() ?? 0);
            var colorKeyMask = R(d.Get("Mask")) is PdfArray;
            var jx = JpxDecoder.Decode(data, csObj is null ? 0 : cs.Components, indexed, smaskInData > 0, out var jxWarn, allowWide: !indexed);
            if (jx is null) { warning = jxWarn ?? "JPX could not be decoded"; return null; }
            warning = jxWarn;
            data = jx.Samples; w = jx.Width; h = jx.Height; bpc = 8;
            if (csObj is null || cs.Components != jx.Components)
            {
                cs = (csObj is null && jx.IccProfile is not null ? PdfColorSpace.FromIccProfile(jx.IccProfile, jx.Components) : null)
                    ?? jx.Components switch { 1 => PdfColorSpace.Gray, 4 => PdfColorSpace.Cmyk, _ => PdfColorSpace.Rgb };
            }
            // Keep native precision only where the 8-bit direct path below would not already be exact. A colour-key
            // /Mask keeps matching 8-bit samples, as before 16-bit support was added.
            if (jx.Samples16 is not null && !colorKeyMask && (decode is not null || (cs != PdfColorSpace.Gray && cs != PdfColorSpace.Rgb)))
            { data = jx.Samples16; bpc = 16; }
            if (smaskInData > 0) jpxAlpha = jx;
        }
        else if (codec is not null)
        {
            warning = $"image codec {codec} not supported";
            return null;
        }

        var rgba = new byte[w * h * 4];
        var nc = cs.Components;
        decode ??= cs.DefaultDecode(bpc);
        if (isMask)
        {
            var invert = decode.Length >= 2 && decode[0] == 1;
            var rb = (w + 7) / 8;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var bit = y * rb + x / 8 < data.Length ? data[y * rb + x / 8] >> (7 - x % 8) & 1 : 1;
                    // Sample 0 paints (with default Decode [0 1]).
                    var paint = invert ? bit == 1 : bit == 0;
                    rgba[(y * w + x) * 4 + 3] = (byte)(paint ? 255 : 0);
                }
            return new ImageData(w, h, rgba, true);
        }

        var rowBytes = (w * nc * bpc + 7) / 8;
        var maxv = (1 << Math.Min(bpc, 16)) - 1.0;
        var comps = new double[nc];
        long[]? colorKey = R(d.Get("Mask")) is PdfArray ck ? ck.Items.Select(x => (long)(R(x).AsNumber() ?? 0)).ToArray() : null;
        var direct = bpc == 8 && colorKey is null && (cs == PdfColorSpace.Rgb || cs == PdfColorSpace.Gray)
            && decode.Length == nc * 2 && Enumerable.Range(0, nc).All(k => decode[2 * k] == 0 && decode[2 * k + 1] == 1);
        if (direct)
        {
            for (var i = 0; i < w * h; i++)
            {
                var source = i * nc;
                var p = i * 4;
                rgba[p] = source < data.Length ? data[source] : (byte)0;
                rgba[p + 1] = cs == PdfColorSpace.Gray ? rgba[p] : source + 1 < data.Length ? data[source + 1] : (byte)0;
                rgba[p + 2] = cs == PdfColorSpace.Gray ? rgba[p] : source + 2 < data.Length ? data[source + 2] : (byte)0;
                rgba[p + 3] = 255;
            }
        }
        else
        {
            var cache = new Dictionary<long, RgbColor>();
            var raw = new int[nc];
            for (var y = 0; y < h; y++)
            {
                var rowOff = y * rowBytes;
                for (var x = 0; x < w; x++)
                {
                    long key = 0;
                    for (var k = 0; k < nc; k++)
                    {
                        var idx = x * nc + k;
                        int v;
                        if (bpc == 8) { var o = rowOff + idx; v = o < data.Length ? data[o] : 0; }
                        else if (bpc == 16) { var o = rowOff + idx * 2; v = o + 1 < data.Length ? data[o] << 8 | data[o + 1] : 0; }
                        else
                        {
                            var bit = idx * bpc; var o = rowOff + bit / 8;
                            v = o < data.Length ? data[o] >> (8 - bpc - bit % 8) & ((1 << bpc) - 1) : 0;
                        }
                        raw[k] = v; key = key * 65599 + v;
                    }
                    if (!cache.TryGetValue(key, out var col))
                    {
                        for (var k = 0; k < nc; k++) comps[k] = decode[2 * k] + raw[k] / maxv * (decode[2 * k + 1] - decode[2 * k]);
                        col = cs.ToRgb(comps, imageIntent, blackPointCompensation);
                        if (cache.Count < 100000) cache[key] = col;
                    }
                    var p = (y * w + x) * 4;
                    rgba[p] = (byte)Math.Round(col.R * 255); rgba[p + 1] = (byte)Math.Round(col.G * 255); rgba[p + 2] = (byte)Math.Round(col.B * 255);
                    var alpha = 255;
                    if (colorKey is { Length: >= 2 } && colorKey.Length >= nc * 2)
                    {
                        var masked = true;
                        for (var k = 0; k < nc; k++) if (raw[k] < colorKey[2 * k] || raw[k] > colorKey[2 * k + 1]) { masked = false; break; }
                        if (masked) alpha = 0;
                    }
                    rgba[p + 3] = (byte)alpha;
                }
            }
        }

        if (jpxAlpha?.Alpha is { } ja)
        {
            var pre = jpxAlpha.PremultipliedAlpha;
            for (var i = 0; i < ja.Length; i++)
            {
                var p = i * 4;
                var a = ja[i];
                if (pre && a > 0 && a < 255)
                    for (var k = 0; k < 3; k++) rgba[p + k] = (byte)Math.Min(255, (rgba[p + k] * 255 + a / 2) / a);
                rgba[p + 3] = (byte)(rgba[p + 3] * a / 255);
            }
        }

        if (depth == 0)
        {
            if (R(d.Get("SMask")) is PdfStream sm && Decode(f, sm, res, out _, out _, intent, blackPointCompensation, depth + 1) is { } smi)
                ApplyAlpha(rgba, w, h, smi, true);
            else if (R(d.Get("Mask")) is PdfStream mk && Decode(f, mk, res, out var mkMask, out _, intent, blackPointCompensation, depth + 1) is { } mki)
                ApplyAlpha(rgba, w, h, mki, !mkMask);
        }
        return new ImageData(w, h, rgba);
    }

    /// <summary>Applies a soft mask (gray luminosity) or stencil mask (alpha channel) scaled to the image size.</summary>
    static void ApplyAlpha(byte[] rgba, int w, int h, ImageData mask, bool useLuma, bool invertStencil = false)
    {
        for (var y = 0; y < h; y++)
        {
            var my = Math.Min(mask.Height - 1, y * mask.Height / h);
            for (var x = 0; x < w; x++)
            {
                var mx = Math.Min(mask.Width - 1, x * mask.Width / w);
                var mp = (my * mask.Width + mx) * 4;
                int a;
                if (useLuma) a = mask.Rgba[mp];
                else a = invertStencil ? 255 - mask.Rgba[mp + 3] : mask.Rgba[mp + 3];
                rgba[(y * w + x) * 4 + 3] = (byte)(rgba[(y * w + x) * 4 + 3] * a / 255);
            }
        }
    }
}
