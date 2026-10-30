using System.IO.Compression;

namespace AdoBye.Samples.Kit;

/// <summary>Deterministic SplitMix64 generator (the sample images must be byte-stable across runs and platforms).</summary>
public sealed class Prng(ulong seed)
{
    private ulong _s = seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;

    public ulong NextUInt64()
    {
        var z = _s += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public double Uniform(double lo, double hi) => lo + (hi - lo) * NextDouble();

    public double Normal(double mean, double sigma)
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return mean + sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

/// <summary>An 8-bit RGB raster (interleaved, row-major, top-down).</summary>
public sealed class Rgb8
{
    public int W { get; }

    public int H { get; }

    public byte[] Pixels { get; }

    public Rgb8(int w, int h, (int R, int G, int B)? fill = null)
    {
        W = w;
        H = h;
        Pixels = new byte[w * h * 3];
        if (fill is { } c)
        {
            for (var i = 0; i < Pixels.Length; i += 3)
            {
                Pixels[i] = (byte)c.R;
                Pixels[i + 1] = (byte)c.G;
                Pixels[i + 2] = (byte)c.B;
            }
        }
    }

    public Rgb8(int w, int h, byte[] pixels)
    {
        if (pixels.Length != w * h * 3)
        {
            throw new ArgumentException("Pixel buffer does not match the dimensions.", nameof(pixels));
        }

        W = w;
        H = h;
        Pixels = pixels;
    }

    public (byte R, byte G, byte B) this[int x, int y]
    {
        get
        {
            var i = (y * W + x) * 3;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
        }
        set
        {
            var i = (y * W + x) * 3;
            Pixels[i] = value.R;
            Pixels[i + 1] = value.G;
            Pixels[i + 2] = value.B;
        }
    }

    /// <summary>Luma (ITU-R 601, as PIL's convert("L")): one byte per pixel.</summary>
    public byte[] ToGray()
    {
        var g = new byte[W * H];
        for (int i = 0, p = 0; i < g.Length; i++, p += 3)
        {
            g[i] = (byte)((Pixels[p] * 19595 + Pixels[p + 1] * 38470 + Pixels[p + 2] * 7471 + 0x8000) >> 16);
        }

        return g;
    }

    /// <summary>Naive RGB to CMYK (as PIL's convert("CMYK")): C=255-R, M=255-G, Y=255-B, K=0. Four bytes per pixel.</summary>
    public byte[] ToCmyk()
    {
        var c = new byte[W * H * 4];
        for (int i = 0, p = 0, o = 0; i < W * H; i++, p += 3, o += 4)
        {
            c[o] = (byte)(255 - Pixels[p]);
            c[o + 1] = (byte)(255 - Pixels[p + 1]);
            c[o + 2] = (byte)(255 - Pixels[p + 2]);
        }

        return c;
    }

    /// <summary>Bilinear resample.</summary>
    public Rgb8 Resize(int w, int h)
    {
        var o = new Rgb8(w, h);
        for (var y = 0; y < h; y++)
        {
            var fy = Math.Clamp((y + 0.5) * H / h - 0.5, 0, H - 1);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, H - 1);
            var ty = fy - y0;
            for (var x = 0; x < w; x++)
            {
                var fx = Math.Clamp((x + 0.5) * W / w - 0.5, 0, W - 1);
                int x0 = (int)fx, x1 = Math.Min(x0 + 1, W - 1);
                var tx = fx - x0;
                for (var c = 0; c < 3; c++)
                {
                    var top = Pixels[(y0 * W + x0) * 3 + c] * (1 - tx) + Pixels[(y0 * W + x1) * 3 + c] * tx;
                    var bot = Pixels[(y1 * W + x0) * 3 + c] * (1 - tx) + Pixels[(y1 * W + x1) * 3 + c] * tx;
                    o.Pixels[(y * w + x) * 3 + c] = (byte)Math.Clamp((int)Math.Round(top * (1 - ty) + bot * ty), 0, 255);
                }
            }
        }

        return o;
    }

    /// <summary>Fills the ellipse inscribed in the inclusive box (PIL ImageDraw.ellipse semantics).</summary>
    public void FillEllipse(double x0, double y0, double x1, double y1, (int R, int G, int B) colour)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, rx = (x1 - x0 + 1) / 2, ry = (y1 - y0 + 1) / 2;
        for (var y = Math.Max(0, (int)Math.Floor(y0)); y <= Math.Min(H - 1, (int)Math.Ceiling(y1)); y++)
        {
            for (var x = Math.Max(0, (int)Math.Floor(x0)); x <= Math.Min(W - 1, (int)Math.Ceiling(x1)); x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry;
                if (dx * dx + dy * dy <= 1)
                {
                    this[x, y] = ((byte)colour.R, (byte)colour.G, (byte)colour.B);
                }
            }
        }
    }

    private static readonly (int, int, int)[] DefaultPalette =
        [(15, 23, 42), (79, 70, 229), (14, 165, 233), (245, 158, 11), (255, 247, 237)];

    /// <summary>Sine-interference colour field mapped through a palette (mirrors imgs.plasma).</summary>
    public static Rgb8 Plasma(int w, int h, int seed, (int R, int G, int B)[]? pal = null)
    {
        var rng = new Prng((ulong)seed);
        var waves = new (double Fx, double Fy, double Ph)[6];
        for (var i = 0; i < waves.Length; i++)
        {
            waves[i] = (rng.Uniform(0.01, 0.06), rng.Uniform(0.01, 0.06), rng.Uniform(0, 6.28));
        }

        var v = new double[w * h];
        double min = double.MaxValue, max = double.MinValue;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var s = 0.0;
                foreach (var (fx, fy, ph) in waves)
                {
                    s += Math.Sin(x * fx * 2 + y * fy * 2 + ph + Math.Sin(y * fy * 3 + ph));
                }

                v[y * w + x] = s;
                min = Math.Min(min, s);
                max = Math.Max(max, s);
            }
        }

        pal ??= DefaultPalette;
        var img = new Rgb8(w, h);
        var span = Math.Max(max - min, 1e-9);
        for (var i = 0; i < v.Length; i++)
        {
            var t = (v[i] - min) / span * (pal.Length - 1);
            var k = Math.Min((int)t, pal.Length - 2);
            var f = t - k;
            img.Pixels[i * 3] = (byte)(pal[k].R + (pal[k + 1].R - pal[k].R) * f);
            img.Pixels[i * 3 + 1] = (byte)(pal[k].G + (pal[k + 1].G - pal[k].G) * f);
            img.Pixels[i * 3 + 2] = (byte)(pal[k].B + (pal[k + 1].B - pal[k].B) * f);
        }

        return img;
    }

    /// <summary>Sunset over layered hills with film grain (mirrors imgs.landscape).</summary>
    public static Rgb8 Landscape(int w, int h)
    {
        var buf = new double[w * h * 3];
        double[] sky0 = [30, 27, 75], sky1 = [251, 146, 60], sunCol = [255, 220, 140];
        var sigma = h * 0.09;
        for (var y = 0; y < h; y++)
        {
            var ty = h > 1 ? Math.Pow(y / (double)(h - 1), 0.8) : 0;
            for (var x = 0; x < w; x++)
            {
                var sun = Math.Exp(-(Math.Pow(x - w * 0.68, 2) + Math.Pow(y - h * 0.62, 2)) / (2 * sigma * sigma));
                for (var c = 0; c < 3; c++)
                {
                    buf[(y * w + x) * 3 + c] = sky0[c] * (1 - ty) + sky1[c] * ty + sun * sunCol[c] * 0.9;
                }
            }
        }

        (int, int, int)[] hills = [(120, 53, 92), (76, 29, 79), (40, 20, 60), (15, 15, 35)];
        for (var i = 0; i < hills.Length; i++)
        {
            var (r, g, b) = hills[i];
            var bas = h * (0.55 + i * 0.1);
            for (var x = 0; x < w; x++)
            {
                var ridge = bas + Math.Sin(x * (0.012 + i * 0.006) + i) * h * 0.05 + Math.Sin(x * 0.04 * (i + 1)) * h * 0.012;
                for (var y = Math.Max(0, (int)Math.Ceiling(ridge)); y < h; y++)
                {
                    var p = (y * w + x) * 3;
                    buf[p] = r;
                    buf[p + 1] = g;
                    buf[p + 2] = b;
                }
            }
        }

        var rng = new Prng(3);
        var img = new Rgb8(w, h);
        for (var i = 0; i < buf.Length; i++)
        {
            img.Pixels[i] = (byte)Math.Clamp((int)(buf[i] + rng.Normal(0, 4)), 0, 255);
        }

        return img;
    }
}

/// <summary>A 1-bit canvas (true = 1) with a few PIL-style drawing primitives, for stencil masks.</summary>
public sealed class BitCanvas
{
    public int W { get; }

    public int H { get; }

    private readonly bool[] _bits;

    public BitCanvas(int w, int h, bool initial = true)
    {
        W = w;
        H = h;
        _bits = new bool[w * h];
        Array.Fill(_bits, initial);
    }

    public bool this[int x, int y]
    {
        get => _bits[y * W + x];
        set => _bits[y * W + x] = value;
    }

    private void Fill(double x0, double y0, double x1, double y1, Func<double, double, bool> inside, bool value)
    {
        for (var y = Math.Max(0, (int)Math.Floor(y0)); y <= Math.Min(H - 1, (int)Math.Ceiling(y1)); y++)
        {
            for (var x = Math.Max(0, (int)Math.Floor(x0)); x <= Math.Min(W - 1, (int)Math.Ceiling(x1)); x++)
            {
                if (inside(x, y))
                {
                    _bits[y * W + x] = value;
                }
            }
        }
    }

    /// <summary>Ellipse inscribed in the inclusive box.</summary>
    public void FillEllipse(double x0, double y0, double x1, double y1, bool value)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, rx = (x1 - x0 + 1) / 2, ry = (y1 - y0 + 1) / 2;
        Fill(x0, y0, x1, y1, (x, y) => Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2) <= 1, value);
    }

    /// <summary>Rounded rectangle over the inclusive box.</summary>
    public void FillRoundedRect(double x0, double y0, double x1, double y1, double radius, bool value)
    {
        Fill(x0, y0, x1, y1, (x, y) =>
        {
            var cx = Math.Clamp(x, x0 + radius, x1 - radius);
            var cy = Math.Clamp(y, y0 + radius, y1 - radius);
            return Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2) <= radius * radius;
        }, value);
    }

    /// <summary>Thick line segment.</summary>
    public void Line(double x0, double y0, double x1, double y1, double width, bool value)
    {
        var half = width / 2;
        double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
        Fill(Math.Min(x0, x1) - half, Math.Min(y0, y1) - half, Math.Max(x0, x1) + half, Math.Max(y0, y1) + half, (x, y) =>
        {
            var t = len2 == 0 ? 0 : Math.Clamp(((x - x0) * dx + (y - y0) * dy) / len2, 0, 1);
            double px = x0 + t * dx - x, py = y0 + t * dy - y;
            return px * px + py * py <= half * half;
        }, value);
    }

    /// <summary>1 bit per pixel, MSB first, rows padded to bytes (np.packbits axis=1). <paramref name="invert"/> flips every bit.</summary>
    public byte[] PackBits(bool invert = false)
    {
        var stride = (W + 7) / 8;
        var o = new byte[stride * H];
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (_bits[y * W + x] != invert)
                {
                    o[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return o;
    }
}

/// <summary>Image encoding helpers shared by the colour-space and image samples.</summary>
public static class Raster
{
    /// <summary>PNG "Up" filtered rows (predictor 15): each row prefixed with filter byte 2.</summary>
    public static byte[] PngPredictUp(byte[] rgb, int w, int h, int comps)
    {
        var stride = w * comps;
        var o = new byte[h * (stride + 1)];
        for (var y = 0; y < h; y++)
        {
            var d = y * (stride + 1);
            o[d] = 2;
            for (var i = 0; i < stride; i++)
            {
                var prev = y > 0 ? rgb[(y - 1) * stride + i] : 0;
                o[d + 1 + i] = (byte)(rgb[y * stride + i] - prev);
            }
        }

        return o;
    }

    /// <summary>zlib stream (RFC 1950) at optimal compression.</summary>
    public static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data);
        }

        return ms.ToArray();
    }

    /// <summary>Packs one-byte-per-pixel palette indices (0..15) into nibbles, high nibble first; odd rows are zero padded.</summary>
    public static byte[] PackNibbles(byte[] indices, int w, int h)
    {
        var stride = (w + 1) / 2;
        var o = new byte[stride * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var v = indices[y * w + x] & 0xF;
                o[y * stride + (x >> 1)] |= (byte)((x & 1) == 0 ? v << 4 : v);
            }
        }

        return o;
    }

    /// <summary>Flattens a palette to RGB bytes padded with zeros to <paramref name="entries"/> entries.</summary>
    public static byte[] PaletteBytes((byte R, byte G, byte B)[] palette, int entries)
    {
        var o = new byte[entries * 3];
        for (var i = 0; i < Math.Min(entries, palette.Length); i++)
        {
            o[i * 3] = palette[i].R;
            o[i * 3 + 1] = palette[i].G;
            o[i * 3 + 2] = palette[i].B;
        }

        return o;
    }

    /// <summary>Median-cut colour quantisation to at most <paramref name="colors"/> colours.</summary>
    public static (byte[] Indices, (byte R, byte G, byte B)[] Palette) Quantize(Rgb8 img, int colors)
    {
        var n = img.W * img.H;
        var hist = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            var key = img.Pixels[i * 3] << 16 | img.Pixels[i * 3 + 1] << 8 | img.Pixels[i * 3 + 2];
            hist[key] = hist.GetValueOrDefault(key) + 1;
        }

        var boxes = new List<List<(int Key, int Count)>> { hist.Select(kv => (kv.Key, kv.Value)).OrderBy(e => e.Key).ToList() };
        while (boxes.Count < colors)
        {
            var best = -1;
            long bestPop = 0;
            for (var i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count > 1)
                {
                    var pop = boxes[i].Sum(e => (long)e.Count);
                    if (pop > bestPop)
                    {
                        best = i;
                        bestPop = pop;
                    }
                }
            }

            if (best < 0)
            {
                break;
            }

            var box = boxes[best];
            int[] lo = [255, 255, 255], hi = [0, 0, 0];
            foreach (var (key, _) in box)
            {
                for (var c = 0; c < 3; c++)
                {
                    var v = (key >> (16 - 8 * c)) & 255;
                    lo[c] = Math.Min(lo[c], v);
                    hi[c] = Math.Max(hi[c], v);
                }
            }

            var axis = 0;
            for (var c = 1; c < 3; c++)
            {
                if (hi[c] - lo[c] > hi[axis] - lo[axis])
                {
                    axis = c;
                }
            }

            var shift = 16 - 8 * axis;
            var sorted = box.OrderBy(e => (e.Key >> shift) & 255).ThenBy(e => e.Key).ToList();
            var half = bestPop / 2;
            long acc = 0;
            var cut = 1;
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                acc += sorted[i].Count;
                cut = i + 1;
                if (acc >= half)
                {
                    break;
                }
            }

            boxes[best] = sorted.GetRange(0, cut);
            boxes.Add(sorted.GetRange(cut, sorted.Count - cut));
        }

        var palette = boxes.Select(b =>
        {
            double r = 0, g = 0, bl = 0, tot = 0;
            foreach (var (key, count) in b)
            {
                r += (key >> 16 & 255) * count;
                g += (key >> 8 & 255) * count;
                bl += (key & 255) * count;
                tot += count;
            }

            return ((byte)Math.Round(r / tot), (byte)Math.Round(g / tot), (byte)Math.Round(bl / tot));
        }).ToArray();

        var cache = new Dictionary<int, byte>();
        var idx = new byte[n];
        for (var i = 0; i < n; i++)
        {
            int r = img.Pixels[i * 3], g = img.Pixels[i * 3 + 1], b = img.Pixels[i * 3 + 2];
            var key = r << 16 | g << 8 | b;
            if (!cache.TryGetValue(key, out var best))
            {
                var bd = int.MaxValue;
                for (var p = 0; p < palette.Length; p++)
                {
                    var d = Sq(r - palette[p].Item1) + Sq(g - palette[p].Item2) + Sq(b - palette[p].Item3);
                    if (d < bd)
                    {
                        bd = d;
                        best = (byte)p;
                    }
                }

                cache[key] = best;
            }

            idx[i] = best;
        }

        return (idx, palette);

        static int Sq(int v) => v * v;
    }

    /// <summary>
    /// A small ICC v2.1 sRGB display profile (D50-adapted primaries, 1024-point sRGB transfer curve), with a fixed creation date
    /// so the bytes are deterministic.
    /// </summary>
    public static byte[] SrgbIccProfile()
    {
        const string description = "sRGB built-in";
        const string copyright = "Public Domain";

        var curve = new byte[12 + 1024 * 2];
        WriteSig(curve, 0, "curv");
        WriteU32(curve, 8, 1024);
        for (var i = 0; i < 1024; i++)
        {
            var c = i / 1023.0;
            var lin = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            var v = (ushort)Math.Round(lin * 65535);
            curve[12 + i * 2] = (byte)(v >> 8);
            curve[13 + i * 2] = (byte)v;
        }

        byte[] Xyz(double x, double y, double z)
        {
            var b = new byte[20];
            WriteSig(b, 0, "XYZ ");
            WriteS15(b, 8, x);
            WriteS15(b, 12, y);
            WriteS15(b, 16, z);
            return b;
        }

        var desc = new byte[12 + description.Length + 1 + 78];
        WriteSig(desc, 0, "desc");
        WriteU32(desc, 8, (uint)description.Length + 1);
        System.Text.Encoding.ASCII.GetBytes(description).CopyTo(desc, 12);

        var cprt = new byte[8 + copyright.Length + 1];
        WriteSig(cprt, 0, "text");
        System.Text.Encoding.ASCII.GetBytes(copyright).CopyTo(cprt, 8);

        var tags = new (string Sig, byte[] Data)[]
        {
            ("desc", desc),
            ("cprt", cprt),
            ("wtpt", Xyz(0.9642, 1.0, 0.8249)),
            ("rXYZ", Xyz(0.4360747, 0.2225045, 0.0139322)),
            ("gXYZ", Xyz(0.3850649, 0.7168786, 0.0971045)),
            ("bXYZ", Xyz(0.1430804, 0.0606169, 0.7141733)),
            ("rTRC", curve),
            ("gTRC", curve),
            ("bTRC", curve),
        };

        var offset = 128 + 4 + tags.Length * 12;
        var dataOffsets = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        var body = new MemoryStream();
        var table = new byte[tags.Length * 12];
        for (var i = 0; i < tags.Length; i++)
        {
            if (!dataOffsets.TryGetValue(tags[i].Data, out var at))
            {
                at = offset + (int)body.Length;
                dataOffsets[tags[i].Data] = at;
                body.Write(tags[i].Data);
                while (body.Length % 4 != 0)
                {
                    body.WriteByte(0);
                }
            }

            WriteSig(table, i * 12, tags[i].Sig);
            WriteU32(table, i * 12 + 4, (uint)at);
            WriteU32(table, i * 12 + 8, (uint)tags[i].Data.Length);
        }

        var profile = new byte[offset + (int)body.Length];
        WriteU32(profile, 0, (uint)profile.Length);
        WriteU32(profile, 8, 0x02100000);
        WriteSig(profile, 12, "mntr");
        WriteSig(profile, 16, "RGB ");
        WriteSig(profile, 20, "XYZ ");
        WriteU16(profile, 24, 2000);
        WriteU16(profile, 26, 1);
        WriteU16(profile, 28, 1);
        WriteSig(profile, 36, "acsp");
        WriteS15(profile, 68, 0.9642);
        WriteS15(profile, 72, 1.0);
        WriteS15(profile, 76, 0.8249);
        WriteU32(profile, 128, (uint)tags.Length);
        table.CopyTo(profile, 132);
        body.ToArray().CopyTo(profile, offset);
        return profile;

        static void WriteSig(byte[] b, int o, string s) => System.Text.Encoding.ASCII.GetBytes(s).CopyTo(b, o);
        static void WriteU16(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 8);
            b[o + 1] = (byte)v;
        }

        static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }

        static void WriteS15(byte[] b, int o, double v) => WriteU32(b, o, (uint)(int)Math.Round(v * 65536));
    }
}

/// <summary>Baseline (SOF0) JPEG encoder with the standard Annex K Huffman tables and libjpeg-style quality scaling.</summary>
public static class Jpeg
{
    /// <summary>Encodes an RGB image as YCbCr JFIF (4:2:0 chroma subsampling by default, like PIL).</summary>
    public static byte[] Encode(Rgb8 img, int quality = 80, bool subsample420 = true) =>
        Encode(img.Pixels, img.W, img.H, 3, quality, subsample420);

    public static byte[] EncodeGray(byte[] gray, int w, int h, int quality = 80) => Encode(gray, w, h, 1, quality);

    /// <summary>
    /// Encodes interleaved CMYK (4 bytes per pixel) with an Adobe APP14 marker (transform 0). Like PIL, the stored samples are
    /// inverted (Adobe convention); pair the PDF image with <c>/Decode [1 0 1 0 1 0 1 0]</c>.
    /// </summary>
    public static byte[] EncodeCmyk(byte[] cmyk, int w, int h, int quality = 80, bool invert = true)
    {
        var data = cmyk;
        if (invert)
        {
            data = new byte[cmyk.Length];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(255 - cmyk[i]);
            }
        }

        return Encode(data, w, h, 4, quality);
    }

    /// <summary>Encodes interleaved samples; <paramref name="comps"/> is 1 (gray), 3 (RGB to YCbCr) or 4 (CMYK as-is).</summary>
    public static byte[] Encode(byte[] samples, int w, int h, int comps, int quality = 80, bool subsample420 = true)
    {
        if (comps is not (1 or 3 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(comps));
        }

        if (samples.Length != w * h * comps)
        {
            throw new ArgumentException("Sample buffer does not match the dimensions.", nameof(samples));
        }

        quality = Math.Clamp(quality, 1, 100);
        var scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        var qLum = ScaleTable(LumQ, scale);
        var qChr = ScaleTable(ChrQ, scale);

        var sub = comps == 3 && subsample420;
        int hMax = sub ? 2 : 1, vMax = hMax;
        var mcuW = 8 * hMax;
        var mcuH = 8 * vMax;
        var mcusX = (w + mcuW - 1) / mcuW;
        var mcusY = (h + mcuH - 1) / mcuH;
        if (comps == 1)
        {
            mcusX = (w + 7) / 8;
            mcusY = (h + 7) / 8;
        }

        // Component planes at full padded resolution, then subsampled per component.
        var pw = mcusX * mcuW;
        var ph = mcusY * mcuH;
        var planes = new byte[comps][];
        for (var c = 0; c < comps; c++)
        {
            planes[c] = new byte[pw * ph];
        }

        for (var y = 0; y < ph; y++)
        {
            var sy = Math.Min(y, h - 1);
            for (var x = 0; x < pw; x++)
            {
                var sx = Math.Min(x, w - 1);
                var s = (sy * w + sx) * comps;
                if (comps == 3)
                {
                    double r = samples[s], g = samples[s + 1], b = samples[s + 2];
                    planes[0][y * pw + x] = Clamp(0.299 * r + 0.587 * g + 0.114 * b);
                    planes[1][y * pw + x] = Clamp(-0.168736 * r - 0.331264 * g + 0.5 * b + 128);
                    planes[2][y * pw + x] = Clamp(0.5 * r - 0.418688 * g - 0.081312 * b + 128);
                }
                else
                {
                    for (var c = 0; c < comps; c++)
                    {
                        planes[c][y * pw + x] = samples[s + c];
                    }
                }
            }
        }

        var factors = new (int H, int V)[comps];
        for (var c = 0; c < comps; c++)
        {
            factors[c] = (c == 0 && sub ? 2 : 1, c == 0 && sub ? 2 : 1);
        }

        var cw = new int[comps];
        for (var c = 1; c < comps && sub; c++)
        {
            cw[c] = pw / 2;
            var small = new byte[pw / 2 * (ph / 2)];
            for (var y = 0; y < ph / 2; y++)
            {
                for (var x = 0; x < pw / 2; x++)
                {
                    var i = 2 * y * pw + 2 * x;
                    small[y * (pw / 2) + x] = (byte)((planes[c][i] + planes[c][i + 1] + planes[c][i + pw] + planes[c][i + pw + 1] + 2) >> 2);
                }
            }

            planes[c] = small;
        }

        for (var c = 0; c < comps; c++)
        {
            if (cw[c] == 0)
            {
                cw[c] = pw;
            }
        }

        var chroma = comps == 3;
        var dcCodes = new[] { BuildCodes(DcLumBits, DcVals), BuildCodes(DcChrBits, DcVals) };
        var acCodes = new[] { BuildCodes(AcLumBits, AcLumVals), BuildCodes(AcChrBits, AcChrVals) };

        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        if (comps == 4)
        {
            ms.Write([0xFF, 0xEE, 0x00, 0x0E, (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x00]);
        }
        else
        {
            ms.Write([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        }

        WriteDqt(ms, 0, qLum);
        if (chroma)
        {
            WriteDqt(ms, 1, qChr);
        }

        ms.Write([0xFF, 0xC0]);
        WriteU16(ms, 8 + comps * 3);
        ms.WriteByte(8);
        WriteU16(ms, h);
        WriteU16(ms, w);
        ms.WriteByte((byte)comps);
        for (var c = 0; c < comps; c++)
        {
            ms.WriteByte((byte)(c + 1));
            ms.WriteByte((byte)(factors[c].H << 4 | factors[c].V));
            ms.WriteByte((byte)(c > 0 && chroma ? 1 : 0));
        }

        WriteDht(ms, 0x00, DcLumBits, DcVals);
        WriteDht(ms, 0x10, AcLumBits, AcLumVals);
        if (chroma)
        {
            WriteDht(ms, 0x01, DcChrBits, DcVals);
            WriteDht(ms, 0x11, AcChrBits, AcChrVals);
        }

        ms.Write([0xFF, 0xDA]);
        WriteU16(ms, 6 + comps * 2);
        ms.WriteByte((byte)comps);
        for (var c = 0; c < comps; c++)
        {
            ms.WriteByte((byte)(c + 1));
            ms.WriteByte((byte)(c > 0 && chroma ? 0x11 : 0x00));
        }

        ms.Write([0x00, 0x3F, 0x00]);

        var bits = new BitWriter(ms);
        var pred = new int[comps];
        var coef = new int[64];
        for (var my = 0; my < mcusY; my++)
        {
            for (var mx = 0; mx < mcusX; mx++)
            {
                for (var c = 0; c < comps; c++)
                {
                    var t = c > 0 && chroma ? 1 : 0;
                    var q = t == 1 ? qChr : qLum;
                    for (var by = 0; by < factors[c].V; by++)
                    {
                        for (var bx = 0; bx < factors[c].H; bx++)
                        {
                            var x0 = (mx * factors[c].H + bx) * 8;
                            var y0 = (my * factors[c].V + by) * 8;
                            Fdct(planes[c], cw[c], x0, y0, q, coef);
                            EncodeBlock(bits, coef, ref pred[c], dcCodes[t], acCodes[t]);
                        }
                    }
                }
            }
        }

        bits.Flush();
        ms.Write([0xFF, 0xD9]);
        return ms.ToArray();
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static void WriteU16(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static int[] ScaleTable(int[] baseTable, int scale)
    {
        var t = new int[64];
        for (var i = 0; i < 64; i++)
        {
            t[i] = Math.Clamp((baseTable[i] * scale + 50) / 100, 1, 255);
        }

        return t;
    }

    private static void WriteDqt(Stream s, int id, int[] table)
    {
        s.Write([0xFF, 0xDB]);
        WriteU16(s, 67);
        s.WriteByte((byte)id);
        for (var i = 0; i < 64; i++)
        {
            s.WriteByte((byte)table[Zigzag[i]]);
        }
    }

    private static void WriteDht(Stream s, int tcTh, byte[] bits, byte[] vals)
    {
        s.Write([0xFF, 0xC4]);
        WriteU16(s, 3 + 16 + vals.Length);
        s.WriteByte((byte)tcTh);
        s.Write(bits);
        s.Write(vals);
    }

    private static (int Code, int Len)[] BuildCodes(byte[] bits, byte[] vals)
    {
        var codes = new (int, int)[256];
        var code = 0;
        var k = 0;
        for (var len = 1; len <= 16; len++)
        {
            for (var i = 0; i < bits[len - 1]; i++)
            {
                codes[vals[k++]] = (code++, len);
            }

            code <<= 1;
        }

        return codes;
    }

    private static readonly double[] Cos = BuildCos();

    private static double[] BuildCos()
    {
        var m = new double[64];
        for (var u = 0; u < 8; u++)
        {
            for (var x = 0; x < 8; x++)
            {
                m[u * 8 + x] = (u == 0 ? Math.Sqrt(0.125) : 0.5) * Math.Cos((2 * x + 1) * u * Math.PI / 16);
            }
        }

        return m;
    }

    // Forward DCT of an 8x8 block, quantised, output in zigzag order.
    private static void Fdct(byte[] plane, int stride, int x0, int y0, int[] q, int[] zz)
    {
        Span<double> tmp = stackalloc double[64];
        Span<double> blk = stackalloc double[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                blk[y * 8 + x] = plane[(y0 + y) * stride + x0 + x] - 128;
            }
        }

        for (var y = 0; y < 8; y++)
        {
            for (var u = 0; u < 8; u++)
            {
                var s = 0.0;
                for (var x = 0; x < 8; x++)
                {
                    s += blk[y * 8 + x] * Cos[u * 8 + x];
                }

                tmp[y * 8 + u] = s;
            }
        }

        for (var u = 0; u < 8; u++)
        {
            for (var v = 0; v < 8; v++)
            {
                var s = 0.0;
                for (var y = 0; y < 8; y++)
                {
                    s += tmp[y * 8 + u] * Cos[v * 8 + y];
                }

                var n = v * 8 + u;
                zz[Array.IndexOf(Zigzag, n)] = (int)Math.Round(s / q[n]);
            }
        }
    }

    private static void EncodeBlock(BitWriter w, int[] zz, ref int pred, (int Code, int Len)[] dc, (int Code, int Len)[] ac)
    {
        var diff = zz[0] - pred;
        pred = zz[0];
        var cat = Category(diff);
        w.Put(dc[cat].Code, dc[cat].Len);
        if (cat > 0)
        {
            w.Put(diff < 0 ? diff - 1 & ((1 << cat) - 1) : diff, cat);
        }

        var run = 0;
        for (var i = 1; i < 64; i++)
        {
            var v = zz[i];
            if (v == 0)
            {
                run++;
                continue;
            }

            while (run > 15)
            {
                w.Put(ac[0xF0].Code, ac[0xF0].Len);
                run -= 16;
            }

            cat = Category(v);
            var sym = run << 4 | cat;
            w.Put(ac[sym].Code, ac[sym].Len);
            w.Put(v < 0 ? v - 1 & ((1 << cat) - 1) : v, cat);
            run = 0;
        }

        if (run > 0)
        {
            w.Put(ac[0].Code, ac[0].Len);
        }
    }

    private static int Category(int v)
    {
        v = Math.Abs(v);
        var c = 0;
        while (v != 0)
        {
            c++;
            v >>= 1;
        }

        return c;
    }

    private sealed class BitWriter(Stream s)
    {
        private int _acc;
        private int _n;

        public void Put(int code, int len)
        {
            _acc = _acc << len | code & ((1 << len) - 1);
            _n += len;
            while (_n >= 8)
            {
                var b = (_acc >> (_n - 8)) & 0xFF;
                s.WriteByte((byte)b);
                if (b == 0xFF)
                {
                    s.WriteByte(0);
                }

                _n -= 8;
            }

            _acc &= (1 << _n) - 1;
        }

        public void Flush()
        {
            if (_n > 0)
            {
                Put(0x7F, 8 - _n);
            }
        }
    }

    private static readonly int[] Zigzag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static readonly int[] LumQ =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    private static readonly int[] ChrQ =
    [
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
    ];

    private static readonly byte[] DcLumBits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] DcChrBits = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
    private static readonly byte[] DcVals = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    private static readonly byte[] AcLumBits = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];

    private static readonly byte[] AcLumVals =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    private static readonly byte[] AcChrBits = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];

    private static readonly byte[] AcChrVals =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];
}
