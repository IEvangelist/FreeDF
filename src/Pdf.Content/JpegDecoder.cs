namespace Pdf.Content;

/// <summary>Huffman JPEG decoder: baseline, extended sequential and progressive (SOF0/1/2). Arithmetic/lossless are unsupported.</summary>
public static class JpegDecoder
{
    sealed class Comp
    {
        public int Id, H, V, Tq, Td, Ta, Pred, BW, BH, RealBW, RealBH;
        public int[] Coef = [];
    }

    sealed class Huff { public int[] MinCode = new int[17], MaxCode = new int[17], ValPtr = new int[17]; public byte[] Vals = []; }

    static readonly int[] Zig =
    [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];

    public static byte[]? Decode(byte[] d, out int width, out int height, out int ncomp)
    {
        width = height = ncomp = 0;
        try { return DecodeCore(d, out width, out height, out ncomp); }
        catch (Exception e) when (e is IndexOutOfRangeException or InvalidOperationException or ArgumentException or OverflowException) { return null; }
    }

    static byte[]? DecodeCore(byte[] d, out int width, out int height, out int ncomp)
    {
        width = height = ncomp = 0;
        var qt = new int[4][];
        var hdc = new Huff?[4]; var hac = new Huff?[4];
        Comp[] comps = [];
        int pos = 2, restart = 0, adobe = -1;
        int hmax = 1, vmax = 1;
        var progressive = false;
        var scans = 0;
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return null;
        while (pos + 4 <= d.Length)
        {
            if (d[pos] != 0xFF) { pos++; continue; }
            var m = d[pos + 1]; pos += 2;
            if (m == 0xFF) { pos--; continue; }
            if (m is 0xD8 or 0x01 or 0x00 || m is >= 0xD0 and <= 0xD7) continue;
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
                        if (tc == 0) hdc[th] = h; else hac[th] = h;
                    }
                    break;
                case 0xC0 or 0xC1 or 0xC2:
                    progressive = m == 0xC2;
                    if (d[seg] != 8) return null;
                    height = d[seg + 1] << 8 | d[seg + 2]; width = d[seg + 3] << 8 | d[seg + 4];
                    ncomp = d[seg + 5];
                    if (width <= 0 || height <= 0 || ncomp is < 1 or > 4) return null;
                    comps = new Comp[ncomp];
                    for (var i = 0; i < ncomp; i++)
                    {
                        var o = seg + 6 + i * 3;
                        comps[i] = new Comp { Id = d[o], H = Math.Max(1, d[o + 1] >> 4), V = Math.Max(1, d[o + 1] & 15), Tq = d[o + 2] & 3 };
                        hmax = Math.Max(hmax, comps[i].H); vmax = Math.Max(vmax, comps[i].V);
                    }
                    var mx = (width + 8 * hmax - 1) / (8 * hmax); var my = (height + 8 * vmax - 1) / (8 * vmax);
                    foreach (var c in comps)
                    {
                        c.BW = mx * c.H; c.BH = my * c.V;
                        c.RealBW = ((width * c.H + hmax - 1) / hmax + 7) / 8; c.RealBH = ((height * c.V + vmax - 1) / vmax + 7) / 8;
                        c.Coef = new int[c.BW * c.BH * 64];
                    }
                    break;
                case 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF: return null;
                case 0xDD: restart = d[seg] << 8 | d[seg + 1]; break;
                case 0xEE: if (len >= 14 && d[seg] == 'A' && d[seg + 1] == 'd') adobe = d[seg + 11]; break;
                case 0xDA:
                    {
                        if (comps.Length == 0) return null;
                        var ns = d[seg];
                        var sc = new Comp[ns];
                        for (var i = 0; i < ns; i++)
                        {
                            var cid = d[seg + 1 + i * 2]; var tt = d[seg + 2 + i * 2];
                            var c = comps.First(x => x.Id == cid); c.Td = tt >> 4 & 3; c.Ta = tt & 3; sc[i] = c;
                        }
                        var ss = d[seg + 1 + ns * 2]; var se = d[seg + 2 + ns * 2];
                        var ah = d[seg + 3 + ns * 2] >> 4; var al = d[seg + 3 + ns * 2] & 15;
                        if (!progressive) { ss = 0; se = 63; ah = al = 0; }
                        var br = new BitReader(d, end);
                        DecodeScan(br, sc, hdc, hac, progressive, ss, se, ah, al, restart, hmax, vmax, width, height);
                        pos = br.Pos; scans++;
                        continue;
                    }
            }
            pos = end;
        }
        if (scans == 0) return null;
        var planes = new byte[comps.Length][];
        for (var i = 0; i < comps.Length; i++) planes[i] = Reconstruct(comps[i], qt[comps[i].Tq] ?? Enumerable.Repeat(1, 64).ToArray());
        return Assemble(comps, planes, width, height, hmax, vmax, adobe);
    }

    sealed class BitReader(byte[] d, int pos)
    {
        int _buf, _cnt; bool _marker;
        public int Pos = pos;
        public int EobRun;
        public int Bit()
        {
            if (_cnt == 0)
            {
                var b = 0;
                if (!_marker && Pos < d.Length)
                {
                    b = d[Pos++];
                    if (b == 0xFF)
                    {
                        var n = Pos < d.Length ? d[Pos] : 0xD9;
                        if (n == 0) Pos++; else { Pos--; _marker = true; b = 0; }
                    }
                }
                _buf = b; _cnt = 8;
            }
            _cnt--;
            return _buf >> _cnt & 1;
        }
        public int Bits(int n) { var v = 0; for (var i = 0; i < n; i++) v = v << 1 | Bit(); return v; }
        public void Restart()
        {
            _cnt = 0; EobRun = 0;
            while (Pos + 1 < d.Length && !(d[Pos] == 0xFF && d[Pos + 1] is >= 0xD0 and <= 0xD7)) Pos++;
            Pos += 2; _marker = false;
        }
    }

    static void DecodeScan(BitReader br, Comp[] sc, Huff?[] hdc, Huff?[] hac, bool prog, int ss, int se, int ah, int al, int restart, int hmax, int vmax, int w, int h)
    {
        foreach (var c in sc) c.Pred = 0;
        void Block(Comp c, int bx, int by)
        {
            var o = (by * c.BW + bx) * 64;
            if (!prog) Sequential(br, c, hdc[c.Td], hac[c.Ta], c.Coef, o);
            else if (ss == 0) { if (ah == 0) DcFirst(br, c, hdc[c.Td], c.Coef, o, al); else if (br.Bit() == 1) c.Coef[o] |= 1 << al; }
            else if (ah == 0) AcFirst(br, hac[c.Ta]!, c.Coef, o, ss, se, al);
            else AcRefine(br, hac[c.Ta]!, c.Coef, o, ss, se, al);
        }
        var mcu = 0;
        if (sc.Length == 1)
        {
            var c = sc[0];
            for (var by = 0; by < c.RealBH; by++)
                for (var bx = 0; bx < c.RealBW; bx++, mcu++)
                {
                    if (restart > 0 && mcu > 0 && mcu % restart == 0) { br.Restart(); c.Pred = 0; }
                    Block(c, bx, by);
                }
            return;
        }
        var mx = (w + 8 * hmax - 1) / (8 * hmax); var my = (h + 8 * vmax - 1) / (8 * vmax);
        for (var r = 0; r < my; r++)
            for (var col = 0; col < mx; col++, mcu++)
            {
                if (restart > 0 && mcu > 0 && mcu % restart == 0) { br.Restart(); foreach (var c in sc) c.Pred = 0; }
                foreach (var c in sc)
                    for (var v = 0; v < c.V; v++)
                        for (var hh = 0; hh < c.H; hh++)
                            Block(c, col * c.H + hh, r * c.V + v);
            }
    }

    static int Sym(BitReader br, Huff? h)
    {
        if (h is null) throw new InvalidOperationException("missing Huffman table");
        var code = 0;
        for (var l = 1; l <= 16; l++)
        {
            code = code << 1 | br.Bit();
            if (h.MaxCode[l] >= 0 && code <= h.MaxCode[l] && code >= h.MinCode[l]) return h.Vals[h.ValPtr[l] + code - h.MinCode[l]];
        }
        return 0;
    }

    static int Extend(int v, int t) => t == 0 ? 0 : v < 1 << (t - 1) ? v - (1 << t) + 1 : v;

    static void Sequential(BitReader br, Comp c, Huff? dc, Huff? ac, int[] coef, int o)
    {
        var t = Sym(br, dc);
        c.Pred += Extend(br.Bits(t), t);
        coef[o] = c.Pred;
        for (var k = 1; k < 64;)
        {
            var rs = Sym(br, ac); int r = rs >> 4, s = rs & 15;
            if (s == 0) { if (r == 15) { k += 16; continue; } break; }
            k += r;
            if (k > 63) break;
            coef[o + Zig[k]] = Extend(br.Bits(s), s);
            k++;
        }
    }

    static void DcFirst(BitReader br, Comp c, Huff? dc, int[] coef, int o, int al)
    {
        var t = Sym(br, dc);
        c.Pred += Extend(br.Bits(t), t);
        coef[o] = c.Pred << al;
    }

    static void AcFirst(BitReader br, Huff ac, int[] coef, int o, int ss, int se, int al)
    {
        if (br.EobRun > 0) { br.EobRun--; return; }
        for (var k = ss; k <= se;)
        {
            var rs = Sym(br, ac); int r = rs >> 4, s = rs & 15;
            if (s == 0)
            {
                if (r < 15) { br.EobRun = (1 << r) - 1; if (r > 0) br.EobRun += br.Bits(r); break; }
                k += 16; continue;
            }
            k += r;
            if (k > 63) break;
            coef[o + Zig[k]] = Extend(br.Bits(s), s) * (1 << al);
            k++;
        }
    }

    static void AcRefine(BitReader br, Huff ac, int[] coef, int o, int ss, int se, int al)
    {
        int p1 = 1 << al, m1 = -1 << al;
        var k = ss;
        if (br.EobRun <= 0)
        {
            for (; k <= se; k++)
            {
                var rs = Sym(br, ac); var r = rs >> 4; var s = rs & 15; var nv = 0;
                if (s == 0)
                {
                    if (r < 15) { br.EobRun = 1 << r; if (r > 0) br.EobRun += br.Bits(r); break; }
                }
                else nv = br.Bit() == 1 ? p1 : m1;
                while (k <= se)
                {
                    ref var cf = ref coef[o + Zig[k]];
                    if (cf != 0)
                    {
                        if (br.Bit() == 1 && (cf & p1) == 0) cf += cf >= 0 ? p1 : m1;
                    }
                    else if (--r < 0) break;
                    k++;
                }
                if (nv != 0 && k <= se) coef[o + Zig[k]] = nv;
            }
        }
        if (br.EobRun > 0)
        {
            for (; k <= se; k++)
            {
                ref var cf = ref coef[o + Zig[k]];
                if (cf != 0 && br.Bit() == 1 && (cf & p1) == 0) cf += cf >= 0 ? p1 : m1;
            }
            br.EobRun--;
        }
    }

    static readonly double[] Cos = BuildCos();
    static double[] BuildCos()
    {
        var t = new double[64];
        for (var x = 0; x < 8; x++) for (var u = 0; u < 8; u++) t[x * 8 + u] = (u == 0 ? Math.Sqrt(0.125) : 0.5) * Math.Cos((2 * x + 1) * u * Math.PI / 16);
        return t;
    }

    static byte[] Reconstruct(Comp c, int[] q)
    {
        var stride = c.BW * 8;
        var data = new byte[stride * c.BH * 8];
        var blk = new double[64]; var tmp = new double[64];
        for (var by = 0; by < c.BH; by++)
            for (var bx = 0; bx < c.BW; bx++)
            {
                var o = (by * c.BW + bx) * 64;
                for (var i = 0; i < 64; i++) blk[i] = (double)c.Coef[o + i] * q[i];
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
                        data[(by * 8 + y) * stride + bx * 8 + x] = (byte)Math.Clamp((int)Math.Round(s + 128), 0, 255);
                    }
            }
        return data;
    }

    static byte[] Assemble(Comp[] comps, byte[][] planes, int w, int h, int hmax, int vmax, int adobe)
    {
        var n = comps.Length;
        var o = new byte[w * h * n];
        var ycc = n == 3 ? adobe != 0 && !(comps[0].Id == 'R' && comps[1].Id == 'G' && comps[2].Id == 'B') : n == 4 && adobe == 2;
        var v = new int[4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                for (var i = 0; i < n; i++)
                {
                    var c = comps[i];
                    v[i] = planes[i][(y * c.V / vmax) * (c.BW * 8) + x * c.H / hmax];
                }
                var p = (y * w + x) * n;
                if (ycc)
                {
                    YCC(v[0], v[1], v[2], o, p);
                    if (n == 4) { o[p] = (byte)(255 - o[p]); o[p + 1] = (byte)(255 - o[p + 1]); o[p + 2] = (byte)(255 - o[p + 2]); o[p + 3] = (byte)v[3]; }
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
