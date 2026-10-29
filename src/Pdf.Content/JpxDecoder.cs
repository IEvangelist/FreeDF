namespace Pdf.Content;

/// <summary>Decoded JPEG 2000 image: 8-bit interleaved colour samples plus an optional opacity channel.</summary>
public sealed class JpxImage
{
    public int Width, Height, Components;
    public byte[] Samples = [];
    public byte[]? Alpha;
    public bool PremultipliedAlpha;
}

/// <summary>JPEG 2000 (JPXDecode): JP2 box parsing, colour/palette/channel handling and 8-bit conversion.</summary>
public static class JpxDecoder
{
    sealed class Jp2Info
    {
        public int EnumCs = -1, IccColors;
        public int[][]? Palette; // [column][entry]
        public int[]? PaletteDepth;
        public List<(int Comp, int Type, int Col)>? CMap;
        public List<(int Cn, int Typ, int Asoc)>? CDef;
    }

    static int U16(byte[] d, int p) => d[p] << 8 | d[p + 1];
    static uint U32(byte[] d, int p) => (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]);

    /// <param name="pdfComps">Colour component count implied by the PDF /ColorSpace (0 if absent).</param>
    /// <param name="skipPalette">Do not expand a JP2 palette (PDF Indexed colour space supplies its own).</param>
    /// <param name="assumeAlpha">SMaskInData is non-zero: treat the first extra channel as opacity when no cdef box exists.</param>
    public static JpxImage? Decode(byte[] data, int pdfComps, bool skipPalette, bool assumeAlpha, out string? warning)
    {
        warning = null;
        try { return DecodeCore(data, pdfComps, skipPalette, assumeAlpha, out warning); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or         InvalidDataException or OverflowException or OutOfMemoryException or AggregateException)
        {
            warning = "JPX could not be decoded: " + e.Message;
            return null;
        }
    }

    static JpxImage? DecodeCore(byte[] d, int pdfComps, bool skipPalette, bool assumeAlpha, out string? warning)
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
        for (var k = 0; k < nc; k++) FillChannel(cs, info, channels[color[k]], w, h, samples, k, nc);
        if (info.EnumCs is 18 or 24 && nc == 3) SyccToRgb(samples);

        byte[]? alpha = null;
        if (alphaCh >= 0 && alphaCh < channels.Count)
        {
            alpha = new byte[w * h];
            FillChannel(cs, info, channels[alphaCh], w, h, alpha, 0, 1);
        }
        return new JpxImage { Width = w, Height = h, Components = nc, Samples = samples, Alpha = alpha, PremultipliedAlpha = premult };
    }

    static void FillChannel(JpxCodestream cs, Jp2Info info, (int Comp, int Col) ch, int w, int h, byte[] dst, int offset, int stride)
    {
        var c = ch.Comp;
        var plane = cs.Planes[c];
        int depth = Math.Min(cs.Depth[c], 16), cw = cs.CW[c], chh = cs.CH[c];
        int[]? pal = null; int palMax = 0;
        byte[]? lut = null;
        if (ch.Col >= 0 && info.Palette is not null)
        {
            pal = info.Palette[ch.Col]; var palDepth = info.PaletteDepth![ch.Col]; palMax = pal.Length - 1;
            lut = new byte[pal.Length];
            for (var i = 0; i < pal.Length; i++) lut[i] = Scale(pal[i], palDepth);
        }
        else if (depth <= 8)
        {
            lut = new byte[1 << depth];
            for (var i = 0; i < lut.Length; i++) lut[i] = Scale(i, depth);
        }
        var xr = cs.Xr[c]; var yr = cs.Yr[c];
        var cx0 = (cs.XOsiz + xr - 1) / xr; var cy0 = (cs.YOsiz + yr - 1) / yr;
        var sub = xr != 1 || yr != 1;
        for (var y = 0; y < h; y++)
        {
            var sy = sub ? Math.Clamp((cs.YOsiz + y) / yr - cy0, 0, chh - 1) : Math.Min(y, chh - 1);
            var row = sy * cw;
            var o = y * w * stride + offset;
            for (var x = 0; x < w; x++, o += stride)
            {
                var sx = sub ? Math.Clamp((cs.XOsiz + x) / xr - cx0, 0, cw - 1) : Math.Min(x, cw - 1);
                int v = plane[row + sx];
                if (lut is not null)
                {
                    if (pal is not null) v = Math.Min(v, palMax);
                    dst[o] = lut[Math.Min(v, lut.Length - 1)];
                }
                else dst[o] = (byte)(v >> (depth - 8));
            }
        }
    }

    static byte Scale(int v, int depth) =>
        depth == 8 ? (byte)v : depth > 8 ? (byte)(v >> (depth - 8)) : (byte)((v * 255 + ((1 << depth) - 1) / 2) / ((1 << depth) - 1));

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
