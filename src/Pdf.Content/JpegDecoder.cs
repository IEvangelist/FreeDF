namespace Pdf.Content;

/// <summary>Baseline/extended-sequential Huffman JPEG decoder (no progressive/arithmetic). Returns interleaved 8-bit samples.</summary>
public static class JpegDecoder
{
    sealed class Comp { public int Id, H, V, Tq, Td, Ta, Pred; public int BW, BH; public byte[] Data = []; public int Stride; }
    sealed class Huff
    {
        public int[] MinCode = new int[17], MaxCode = new int[18], ValPtr = new int[17]; public byte[] Vals = [];
    }

    static readonly int[] Zig =
    [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];

    public static byte[]? Decode(byte[] d, out int width, out int height, out int ncomp)
    {
        width = height = ncomp = 0;
        try { return DecodeCore(d, out width, out height, out ncomp); }
        catch (Exception e) when (e is IndexOutOfRangeException or InvalidOperationException or ArgumentException) { return null; }
    }

    static byte[]? DecodeCore(byte[] d, out int width, out int height, out int ncomp)
    {
        width = height = ncomp = 0;
        var qt = new int[4][];
        var hdc = new Huff?[4]; var hac = new Huff?[4];
        Comp[] comps = [];
        int pos = 2, restart = 0, adobe = -1;
        int hmax = 1, vmax = 1;
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return null;
        while (pos + 4 <= d.Length)
        {
            if (d[pos] != 0xFF) { pos++; continue; }
            var m = d[pos + 1]; pos += 2;
            if (m == 0xFF) { pos--; continue; }
            if (m is 0xD8 or 0x01 || m is >= 0xD0 and <= 0xD7) continue;
            if (m == 0xD9) break;
            var len = d[pos] << 8 | d[pos + 1];
            var seg = pos + 2; var end = pos + len;
            switch (m)
            {
                case 0xDB:
                    while (seg < end)
                    {
                        var pq = d[seg] >> 4; var id = d[seg] & 15; seg++;
                        var t = new int[64];
                        for (var i = 0; i < 64; i++) { t[Zig[i]] = pq == 0 ? d[seg++] : (d[seg++] << 8 | d[seg++]); }
                        qt[id & 3] = t;
                    }
                    break;
                case 0xC4:
                    while (seg < end)
                    {
                        var tc = d[seg] >> 4; var th = d[seg] & 3; seg++;
                        var counts = new int[17]; var total = 0;
                        for (var i = 1; i <= 16; i++) { counts[i] = d[seg++]; total += counts[i]; }
                        var h = new Huff { Vals = d.AsSpan(seg, total).ToArray() }; seg += total;
                        int code = 0, k = 0;
                        for (var l = 1; l <= 16; l++)
                        {
                            h.ValPtr[l] = k; h.MinCode[l] = code; code += counts[l]; k += counts[l];
                            h.MaxCode[l] = counts[l] > 0 ? code - 1 : -1; code <<= 1;
                        }
                        h.MaxCode[17] = int.MaxValue;
                        if (tc == 0) hdc[th] = h; else hac[th] = h;
                    }
                    break;
                case 0xC0 or 0xC1:
                    height = d[seg + 1] << 8 | d[seg + 2]; width = d[seg + 3] << 8 | d[seg + 4];
                    ncomp = d[seg + 5];
                    comps = new Comp[ncomp];
                    for (var i = 0; i < ncomp; i++)
                    {
                        var o = seg + 6 + i * 3;
                        comps[i] = new Comp { Id = d[o], H = d[o + 1] >> 4, V = d[o + 1] & 15, Tq = d[o + 2] };
                        hmax = Math.Max(hmax, comps[i].H); vmax = Math.Max(vmax, comps[i].V);
                    }
                    break;
                case 0xC2 or 0xC3 or >= 0xC5 and <= 0xCF when m != 0xC4 && m != 0xC8 && m != 0xCC: return null;
                case 0xDD: restart = d[seg] << 8 | d[seg + 1]; break;
                case 0xEE: if (len >= 14 && d[seg] == 'A' && d[seg + 1] == 'd') adobe = d[seg + 11]; break;
                case 0xDA:
                    {
                        var ns = d[seg];
                        for (var i = 0; i < ns; i++)
                        {
                            var cid = d[seg + 1 + i * 2]; var tt = d[seg + 2 + i * 2];
                            var c = comps.First(x => x.Id == cid); c.Td = tt >> 4; c.Ta = tt & 15;
                        }
                        var mcuW = 8 * hmax; var mcuH = 8 * vmax;
                        var mx = (width + mcuW - 1) / mcuW; var my = (height + mcuH - 1) / mcuH;
                        foreach (var c in comps) { c.BW = mx * c.H; c.BH = my * c.V; c.Stride = c.BW * 8; c.Data = new byte[c.Stride * c.BH * 8]; }
                        var br = new BitReader(d, end);
                        var mcu = 0; var block = new int[64];
                        for (var r = 0; r < my; r++)
                            for (var c = 0; c < mx; c++, mcu++)
                            {
                                if (restart > 0 && mcu > 0 && mcu % restart == 0) { br.Restart(); foreach (var cc in comps) cc.Pred = 0; }
                                foreach (var cp in comps)
                                    for (var v = 0; v < cp.V; v++)
                                        for (var h = 0; h < cp.H; h++)
                                        {
                                            DecodeBlock(br, cp, hdc[cp.Td]!, hac[cp.Ta]!, qt[cp.Tq], block);
                                            Idct(block, cp.Data, cp.Stride, (c * cp.H + h) * 8, (r * cp.V + v) * 8);
                                        }
                            }
                        return Assemble(comps, width, height, hmax, vmax, adobe);
                    }
            }
            pos = end;
        }
        return null;
    }

    sealed class BitReader(byte[] d, int pos)
    {
        int _buf, _cnt;
        public int Pos = pos;
        public int Bit()
        {
            if (_cnt == 0)
            {
                int b = Pos < d.Length ? d[Pos++] : 0;
                if (b == 0xFF)
                {
                    var n = Pos < d.Length ? d[Pos] : 0;
                    if (n == 0) Pos++; else { Pos--; b = 0; }
                }
                _buf = b; _cnt = 8;
            }
            _cnt--;
            return _buf >> _cnt & 1;
        }
        public int Bits(int n) { var v = 0; for (var i = 0; i < n; i++) v = v << 1 | Bit(); return v; }
        public void Restart()
        {
            _cnt = 0;
            while (Pos + 1 < d.Length && !(d[Pos] == 0xFF && d[Pos + 1] is >= 0xD0 and <= 0xD7)) Pos++;
            Pos += 2;
        }
    }

    static int Sym(BitReader br, Huff h)
    {
        var code = 0;
        for (var l = 1; l <= 16; l++)
        {
            code = code << 1 | br.Bit();
            if (h.MaxCode[l] >= 0 && code <= h.MaxCode[l] && code >= h.MinCode[l]) return h.Vals[h.ValPtr[l] + code - h.MinCode[l]];
        }
        return 0;
    }

    static int Extend(int v, int t) => t == 0 ? 0 : v < 1 << (t - 1) ? v - (1 << t) + 1 : v;

    static void DecodeBlock(BitReader br, Comp c, Huff dc, Huff ac, int[]? q, int[] blk)
    {
        Array.Clear(blk);
        q ??= Enumerable.Repeat(1, 64).ToArray();
        var t = Sym(br, dc);
        c.Pred += Extend(br.Bits(t), t);
        blk[0] = c.Pred * q[0];
        for (var k = 1; k < 64;)
        {
            var rs = Sym(br, ac); int r = rs >> 4, s = rs & 15;
            if (s == 0) { if (r == 15) { k += 16; continue; } break; }
            k += r;
            if (k > 63) break;
            blk[Zig[k]] = Extend(br.Bits(s), s) * q[Zig[k]];
            k++;
        }
    }

    static readonly double[] Cos = BuildCos();
    static double[] BuildCos()
    {
        var t = new double[64];
        for (var x = 0; x < 8; x++) for (var u = 0; u < 8; u++) t[x * 8 + u] = (u == 0 ? Math.Sqrt(0.125) : 0.5) * Math.Cos((2 * x + 1) * u * Math.PI / 16);
        return t;
    }

    static void Idct(int[] blk, byte[] dst, int stride, int x0, int y0)
    {
        Span<double> tmp = stackalloc double[64];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                double s = 0;
                for (var u = 0; u < 8; u++) s += Cos[x * 8 + u] * blk[y * 8 + u];
                tmp[y * 8 + x] = s;
            }
        for (var x = 0; x < 8; x++)
            for (var y = 0; y < 8; y++)
            {
                double s = 0;
                for (var v = 0; v < 8; v++) s += Cos[y * 8 + v] * tmp[v * 8 + x];
                dst[(y0 + y) * stride + x0 + x] = (byte)Math.Clamp((int)Math.Round(s + 128), 0, 255);
            }
    }

    static byte[] Assemble(Comp[] comps, int w, int h, int hmax, int vmax, int adobe)
    {
        var n = comps.Length;
        var o = new byte[w * h * n];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                Span<int> v = stackalloc int[4];
                for (var i = 0; i < n; i++)
                {
                    var c = comps[i];
                    v[i] = c.Data[(y * c.V / vmax) * c.Stride + x * c.H / hmax];
                }
                var p = (y * w + x) * n;
                if (n == 3 && adobe != 0) YCC(v[0], v[1], v[2], o, p);
                else if (n == 4)
                {
                    if (adobe == 2) { YCC(v[0], v[1], v[2], o, p); o[p + 3] = (byte)v[3]; }
                    else { for (var i = 0; i < 4; i++) o[p + i] = (byte)v[i]; }
                    // Adobe CMYK JPEGs are stored inverted.
                    if (adobe >= 0) for (var i = 0; i < 4; i++) o[p + i] = (byte)(255 - o[p + i]);
                    if (adobe == 2) { o[p] = (byte)(255 - o[p]); o[p + 1] = (byte)(255 - o[p + 1]); o[p + 2] = (byte)(255 - o[p + 2]); }
                }
                else for (var i = 0; i < n; i++) o[p + i] = (byte)v[i];
            }
        return o;
    }

    static void YCC(int y, int cb, int cr, byte[] o, int p)
    {
        o[p] = (byte)Math.Clamp((int)Math.Round(y + 1.402 * (cr - 128)), 0, 255);
        o[p + 1] = (byte)Math.Clamp((int)Math.Round(y - 0.344136 * (cb - 128) - 0.714136 * (cr - 128)), 0, 255);
        o[p + 2] = (byte)Math.Clamp((int)Math.Round(y + 1.772 * (cb - 128)), 0, 255);
    }
}
