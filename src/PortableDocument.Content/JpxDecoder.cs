namespace PortableDocument.Content;

/// <summary>Decoded JPEG 2000 image: 8-bit interleaved colour samples plus an optional opacity channel.</summary>
public sealed class JpxImage
{
    public int Width, Height, Components;
    public byte[] Samples = [];
    public byte[]? Alpha;
    public bool PremultipliedAlpha;
    /// <summary>Big-endian 16-bit interleaved colour samples, scaled to the full 0..65535 range; only set for components deeper than 8 bits when the caller allows it.</summary>
    public byte[]? Samples16;
    /// <summary>Embedded ICC profile from a JP2 'colr' box (method 2), if present.</summary>
    public byte[]? IccProfile;
}

/// <summary>JPEG 2000 (JPXDecode): JP2 box parsing, colour/palette/channel handling and 8-bit conversion.</summary>
public static class JpxDecoder
{
    sealed class Jp2Info
    {
        public int EnumCs = -1, IccColors;
        public int[][]? Palette; // [column][entry]
        public int[]? PaletteDepth;
        public byte[]? Icc;
        public List<(int Comp, int Type, int Col)>? CMap;
        public List<(int Cn, int Typ, int Asoc)>? CDef;
    }

    static int U16(byte[] d, int p) => d[p] << 8 | d[p + 1];
    static uint U32(byte[] d, int p) => (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]);

    /// <param name="pdfComps">Colour component count implied by the PDF /ColorSpace (0 if absent).</param>
    /// <param name="skipPalette">Do not expand a JP2 palette (PDF Indexed colour space supplies its own).</param>
    /// <param name="assumeAlpha">SMaskInData is non-zero: treat the first extra channel as opacity when no cdef box exists.</param>
    /// <param name="allowWide">Also produce <see cref="JpxImage.Samples16"/> when a colour channel is deeper than 8 bits.</param>
    public static JpxImage? Decode(byte[] data, int pdfComps, bool skipPalette, bool assumeAlpha, out string? warning, bool allowWide = false)
    {
        warning = null;
        try { return DecodeCore(data, pdfComps, skipPalette, assumeAlpha, allowWide, out warning); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or         InvalidDataException or OverflowException or OutOfMemoryException or AggregateException)
        {
            warning = "JPX could not be decoded: " + e.Message;
            return null;
        }
    }

    static JpxImage? DecodeCore(byte[] d, int pdfComps, bool skipPalette, bool assumeAlpha, bool allowWide, out string? warning)
    {
        warning = null;
        var info = new Jp2Info();
        int csStart = -1, csEnd = d.Length;
        if (d.Length >= 12 && U32(d, 4) == 0x6A502020)
        {
            ParseBoxes(d, 0, d.Length, info, ref csStart, ref csEnd, 0);
            if (csStart < 0) { warning = "JPX: no codestream box"; return null; }
        }
        else
        {
            csStart = Array.IndexOf(d, (byte)0xFF);
            if (csStart < 0 || csStart + 1 >= d.Length || d[csStart + 1] != 0x4F) { warning = "JPX: not a JPEG 2000 stream"; return null; }
        }

        var cs = new JpxCodestream();
        if (!cs.Decode(d, csStart, csEnd)) { warning = cs.Warning ?? "JPX could not be decoded"; return null; }
        warning = cs.Warning;

        var w = cs.Xsiz - cs.XOsiz; var h = cs.Ysiz - cs.YOsiz;

        // Build the channel list: (component, palette column or -1).
        var channels = new List<(int Comp, int Col)>();
        if (info.Palette is not null && !skipPalette && info.CMap is { Count: > 0 })
        {
            foreach (var (comp, type, col) in info.CMap)
                if (comp < cs.Csiz) channels.Add((comp, type == 1 && col < info.Palette.Length ? col : -1));
        }
        else
            for (var c = 0; c < cs.Csiz; c++) channels.Add((c, -1));

        var color = new List<int>(); var alphaCh = -1; var premult = false;
        if (info.CDef is { Count: > 0 })
        {
            var byAsoc = new SortedDictionary<int, int>();
            foreach (var (cn, typ, asoc) in info.CDef)
            {
                if (cn >= channels.Count) continue;
                if (typ == 0 && asoc >= 1) byAsoc[asoc] = cn;
                else if (typ is 1 or 2 && alphaCh < 0 && asoc == 0) { alphaCh = cn; premult = typ == 2; }
            }
            color.AddRange(byAsoc.Values);
        }
        if (color.Count == 0)
        {
            var want = pdfComps > 0 ? pdfComps : info.EnumCs switch { 17 => 1, 12 => 4, 16 or 18 or 20 or 24 => 3, _ => info.IccColors > 0 ? info.IccColors : 0 };
            if (want == 0) want = channels.Count >= 3 ? (channels.Count == 4 && !assumeAlpha ? 4 : 3) : 1;
            want = Math.Min(want, channels.Count);
            for (var i = 0; i < want; i++) color.Add(i);
            if (alphaCh < 0 && assumeAlpha && channels.Count > want) alphaCh = want;
        }
        else if (pdfComps > 0 && pdfComps < color.Count) color.RemoveRange(pdfComps, color.Count - pdfComps);

        var nc = color.Count;
        var samples = new byte[(long)w * h * nc];
        var wide = false;
        if (allowWide)
            foreach (var k in color)
            {
                var (cc, col) = channels[k];
                var dp = col >= 0 && info.PaletteDepth is not null ? info.PaletteDepth[col] : cs.Depth[cc];
                if (dp > 8) wide = true;
            }
        var samples16 = wide ? new byte[(long)w * h * nc * 2] : null;
        for (var k = 0; k < nc; k++) FillChannel(cs, info, channels[color[k]], w, h, samples, k, nc, samples16);
        if (info.EnumCs is 18 or 24 && nc == 3) { SyccToRgb(samples); if (samples16 is not null) SyccToRgb16(samples16); }

        byte[]? alpha = null;
        if (alphaCh >= 0 && alphaCh < channels.Count)
        {
            alpha = new byte[w * h];
            FillChannel(cs, info, channels[alphaCh], w, h, alpha, 0, 1);
        }
        return new JpxImage { Width = w, Height = h, Components = nc, Samples = samples, Samples16 = samples16, Alpha = alpha, PremultipliedAlpha = premult, IccProfile = info.Icc };
    }

    static void FillChannel(JpxCodestream cs, Jp2Info info, (int Comp, int Col) ch, int w, int h, byte[] dst, int offset, int stride, byte[]? dst16 = null)
    {
        var c = ch.Comp;
        var plane = cs.Planes[c];
        int rawDepth = cs.Depth[c], depth = Math.Min(rawDepth, 16), shift = rawDepth - depth, cw = cs.CW[c], chh = cs.CH[c];
        var pal = ch.Col >= 0 && info.Palette is not null ? info.Palette[ch.Col] : null;
        var lutDepth = pal is not null ? Math.Min(info.PaletteDepth![ch.Col], 16) : depth;
        var lutLen = pal?.Length ?? 1 << depth;
        var lut = new byte[lutLen];
        var lut16 = dst16 is null ? null : new ushort[lutLen];
        for (var i = 0; i < lutLen; i++)
        {
            var v = pal is not null ? pal[i] : i;
            lut[i] = (byte)Scale(v, lutDepth, 255);
            if (lut16 is not null) lut16[i] = (ushort)Scale(v, lutDepth, 65535);
        }
        var last = lutLen - 1;
        var xr = cs.Xr[c]; var yr = cs.Yr[c];
        var cx0 = (cs.XOsiz + xr - 1) / xr; var cy0 = (cs.YOsiz + yr - 1) / yr;
        var sub = xr != 1 || yr != 1;
        // Subsampled components sit at multiples of XRsiz/YRsiz on the reference grid (T.800 B.2), so the
        // reference-grid samples between them are bilinearly interpolated; palette indices stay nearest.
        var smooth = sub && pal is null;
        int[]? xi = null, xf = null;
        if (sub)
        {
            xi = new int[w]; xf = new int[w];
            for (var x = 0; x < w; x++)
            {
                var gx = cs.XOsiz + x;
                var i0 = gx / xr - cx0; var f = gx % xr;
                if (i0 < 0) { i0 = 0; f = 0; }
                if (i0 >= cw - 1) { i0 = cw - 1; f = 0; }
                xi[x] = i0; xf[x] = smooth ? f : 0;
            }
        }
        for (var y = 0; y < h; y++)
        {
            int sy, fy = 0;
            if (sub)
            {
                var gy = cs.YOsiz + y;
                sy = gy / yr - cy0; fy = gy % yr;
                if (sy < 0) { sy = 0; fy = 0; }
                if (sy >= chh - 1) { sy = chh - 1; fy = 0; }
                if (!smooth) fy = 0;
            }
            else sy = Math.Min(y, chh - 1);
            var row = sy * cw;
            var o = y * w * stride + offset;
            for (var x = 0; x < w; x++, o += stride)
            {
                int v;
                if (!sub) v = plane[row + Math.Min(x, cw - 1)];
                else
                {
                    var sx = xi![x]; var fx = xf![x];
                    if (fx == 0 && fy == 0) v = plane[row + sx];
                    else
                    {
                        long a = plane[row + sx], b = plane[row + (fx == 0 ? sx : sx + 1)];
                        long cc = fy == 0 ? a : plane[row + cw + sx], dd = fy == 0 ? b : plane[row + cw + (fx == 0 ? sx : sx + 1)];
                        long wy = yr - fy, wx = xr - fx;
                        v = (int)(((a * wx + b * fx) * wy + (cc * wx + dd * fx) * fy + (long)xr * yr / 2) / ((long)xr * yr));
                    }
                }
                if (shift > 0) v >>= shift;
                if ((uint)v > (uint)last) v = v < 0 ? 0 : last;
                dst[o] = lut[v];
                if (lut16 is not null) { var q = o * 2; var s16 = lut16[v]; dst16![q] = (byte)(s16 >> 8); dst16[q + 1] = (byte)s16; }
            }
        }
    }

    /// <summary>Scales a <paramref name="depth"/>-bit sample to 0..<paramref name="max"/> with round-half-up.</summary>
    static int Scale(int v, int depth, int max)
    {
        var m = (1L << depth) - 1;
        return (int)(((long)v * max + m / 2) / m);
    }
    static void SyccToRgb(byte[] s)
    {
        for (var i = 0; i + 2 < s.Length; i += 3)
        {
            float y = s[i], cb = s[i + 1] - 128f, cr = s[i + 2] - 128f;
            s[i] = Clamp(y + 1.402f * cr);
            s[i + 1] = Clamp(y - 0.344136f * cb - 0.714136f * cr);
            s[i + 2] = Clamp(y + 1.772f * cb);
        }
    }

    static byte Clamp(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5f);

    static void SyccToRgb16(byte[] s)
    {
        for (var i = 0; i + 5 < s.Length; i += 6)
        {
            float y = s[i] << 8 | s[i + 1], cb = (s[i + 2] << 8 | s[i + 3]) - 32768f, cr = (s[i + 4] << 8 | s[i + 5]) - 32768f;
            Put16(s, i, y + 1.402f * cr);
            Put16(s, i + 2, y - 0.344136f * cb - 0.714136f * cr);
            Put16(s, i + 4, y + 1.772f * cb);
        }
    }

    static void Put16(byte[] s, int i, float v)
    {
        var q = v <= 0 ? 0 : v >= 65535 ? 65535 : (int)(v + 0.5f);
        s[i] = (byte)(q >> 8); s[i + 1] = (byte)q;
    }

    static void ParseBoxes(byte[] d, int p, int end, Jp2Info info, ref int csStart, ref int csEnd, int depth)
    {
        while (p + 8 <= end)
        {
            long len = U32(d, p); var type = U32(d, p + 4); var hdr = 8;
            if (len == 1) { if (p + 16 > end) return; len = (long)((ulong)U32(d, p + 8) << 32 | U32(d, p + 12)); hdr = 16; }
            else if (len == 0) len = end - p;
            if (len < hdr || p + len > end) len = end - p;
            int body = p + hdr, bend = (int)(p + len);
            switch (type)
            {
                case 0x6A703268 when depth == 0: ParseBoxes(d, body, bend, info, ref csStart, ref csEnd, 1); break; // jp2h
                case 0x6A703263 when csStart < 0: csStart = body; csEnd = bend; break; // jp2c
                case 0x636F6C72 when info.EnumCs < 0 && info.IccColors == 0: // colr
                    if (bend - body >= 3)
                    {
                        if (d[body] == 1 && bend - body >= 7) info.EnumCs = (int)U32(d, body + 3);
                        else if (d[body] == 2 && bend - body >= 3 + 20)
                        {
                            var sig = U32(d, body + 3 + 16);
                            info.IccColors = sig switch { 0x47524159 => 1, 0x52474220 => 3, 0x434D594B => 4, _ => 0 };
                            if (info.IccColors > 0) info.Icc = d.AsSpan(body + 3, bend - body - 3).ToArray();
                        }
                    }
                    break;
                case 0x70636C72: ParsePclr(d, body, bend, info); break; // pclr
                case 0x636D6170: // cmap
                    info.CMap = [];
                    for (var q = body; q + 4 <= bend; q += 4) info.CMap.Add((U16(d, q), d[q + 2], d[q + 3]));
                    break;
                case 0x63646566: // cdef
                    if (bend - body >= 2)
                    {
                        info.CDef = [];
                        var n = U16(d, body);
                        for (var i = 0; i < n && body + 2 + i * 6 + 6 <= bend; i++)
                            info.CDef.Add((U16(d, body + 2 + i * 6), U16(d, body + 4 + i * 6), U16(d, body + 6 + i * 6)));
                    }
                    break;
            }
            p = bend;
        }
    }

    static void ParsePclr(byte[] d, int p, int end, Jp2Info info)
    {
        if (end - p < 3) return;
        var ne = U16(d, p); var npc = d[p + 2];
        if (ne == 0 || npc == 0 || p + 3 + npc > end) return;
        var depths = new int[npc];
        for (var i = 0; i < npc; i++) depths[i] = (d[p + 3 + i] & 0x7F) + 1;
        var q = p + 3 + npc;
        var pal = new int[npc][];
        for (var i = 0; i < npc; i++) pal[i] = new int[ne];
        for (var e = 0; e < ne; e++)
            for (var i = 0; i < npc; i++)
            {
                var nb = (depths[i] + 7) / 8; var v = 0;
                for (var b = 0; b < nb; b++) v = v << 8 | (q < end ? d[q++] : 0);
                pal[i][e] = v;
            }
        info.Palette = pal; info.PaletteDepth = depths;
    }
}
