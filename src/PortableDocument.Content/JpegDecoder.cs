using System.Runtime.CompilerServices;

namespace PortableDocument.Content;

/// <summary>
/// Huffman JPEG decoder: baseline, extended sequential and progressive (SOF0/1/2), plus arithmetic-coded DCT (SOF9/10, JpegArith.cs)
/// and lossless Huffman (SOF3, JpegLossless.cs). SOF1/2 support 8- and 12-bit precision; other DCT modes require 8 bits.
/// Arithmetic lossless (SOF11), hierarchical and subsampled interleaved lossless JPEG remain unsupported.
/// Uses a 64-bit bit buffer, 9-bit Huffman lookahead tables, libjpeg's integer "islow" IDCT and parallel reconstruction.
/// </summary>
public static partial class JpegDecoder
{
    sealed class Comp
    {
        public int Id, H, V, Tq, Td, Ta, Pred, BW, BH, RealBW, RealBH, CW, CH;
        public short[] Coef = [];
        public ushort[]? Raw;
    }

    sealed class Huff
    {
        public readonly int[] MaxCode = new int[18], ValOffset = new int[17];
        public readonly short[] Look = new short[512]; // (len << 8) | symbol for codes of <= 9 bits
        public byte[] Vals = [];
    }

    static readonly byte[] Zig =
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
        bool arith = false, lossless = false;
        var prec = 8; var lpt = 0;
        int[] dcL = new int[4], dcU = [1, 1, 1, 1], acK = [5, 5, 5, 5];
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
                        var h = BuildHuff(counts, d.AsSpan(seg, total).ToArray()); seg += total;
                        if (tc == 0) hdc[th] = h; else hac[th] = h;
                    }
                    break;
                case 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC9 or 0xCA:
                    progressive = m is 0xC2 or 0xCA;
                    arith = m is 0xC9 or 0xCA;
                    lossless = m == 0xC3;
                    prec = d[seg];
                    if (lossless ? prec is < 2 or > 16 : prec != 8 && !(prec == 12 && m is 0xC1 or 0xC2)) return null;
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
                        if (lossless)
                        {
                            c.CW = (width * c.H + hmax - 1) / hmax; c.CH = (height * c.V + vmax - 1) / vmax;
                            c.Raw = new ushort[c.CW * c.CH];
                        }
                        else c.Coef = new short[c.BW * c.BH * 64];
                    }
                    break;
                case 0xC5 or 0xC6 or 0xC7 or 0xCB or 0xCD or 0xCE or 0xCF: return null;
                case 0xCC:
                    for (var q = seg; q + 1 < end; q += 2)
                    {
                        var tb = d[q] & 3; var cs = d[q + 1];
                        if (d[q] >> 4 == 0) { dcL[tb] = cs & 15; dcU[tb] = cs >> 4; } else acK[tb] = cs;
                    }
                    break;
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
                            var c = Array.Find(comps, x => x.Id == cid) ?? throw new InvalidOperationException("bad component");
                            c.Td = tt >> 4 & 3; c.Ta = tt & 3; sc[i] = c;
                        }
                        var ss = d[seg + 1 + ns * 2]; var se = d[seg + 2 + ns * 2];
                        var ah = d[seg + 3 + ns * 2] >> 4; var al = d[seg + 3 + ns * 2] & 15;
                        if (lossless)
                        {
                            var lbr = new BitReader(d, end);
                            DecodeLosslessScan(lbr, sc, hdc, ss, al, prec, restart); lpt = al;
                            pos = lbr.Pos; scans++;
                            continue;
                        }
                        if (!progressive) { ss = 0; se = 63; ah = al = 0; }
                        if (arith)
                        {
                            DecodeArithScan(d, end, sc, progressive, ss, se, ah, al, restart, hmax, vmax, width, height, dcL, dcU, acK, out pos);
                            scans++;
                            continue;
                        }
                        var br = new BitReader(d, end);
                        DecodeScan(br, sc, hdc, hac, progressive, ss, se, ah, al, restart, hmax, vmax, width, height);
                        pos = br.Pos; scans++;
                        continue;
                    }
            }
            pos = end;
        }
        if (scans == 0) return null;
        if (lossless) return FinishLossless(comps, width, height, hmax, vmax, adobe, prec, lpt);
        if (prec == 12) return FinishDct12(comps, qt, width, height, hmax, vmax, adobe);
        var planes = new byte[comps.Length][];
        var quant = new int[comps.Length][];
        for (var i = 0; i < comps.Length; i++)
        {
            quant[i] = qt[comps[i].Tq] ?? Array.ConvertAll(new int[64], _ => 1);
            planes[i] = new byte[comps[i].BW * 8 * comps[i].BH * 8];
        }
        // Reconstruct block rows of all components in parallel.
        var jobs = new List<(int C, int Row)>();
        for (var i = 0; i < comps.Length; i++) for (var r = 0; r < comps[i].BH; r++) jobs.Add((i, r));
        var par = !OperatingSystem.IsBrowser() && (long)width * height > 40_000;
        if (par) Parallel.ForEach(jobs, j => ReconstructRow(comps[j.C], quant[j.C], planes[j.C], j.Row));
        else foreach (var j in jobs) ReconstructRow(comps[j.C], quant[j.C], planes[j.C], j.Row);
        return Assemble(comps, planes, width, height, hmax, vmax, adobe, par);
    }

    static Huff BuildHuff(int[] counts, byte[] vals)
    {
        var h = new Huff { Vals = vals };
        int code = 0, k = 0;
        for (var l = 1; l <= 16; l++)
        {
            h.ValOffset[l] = k - code;
            for (var i = 0; i < counts[l]; i++)
            {
                if (l <= 9)
                {
                    var first = code << (9 - l);
                    var ent = (short)(l << 8 | vals[k + i]);
                    for (var f = 0; f < 1 << (9 - l); f++) h.Look[first + f] = ent;
                }
                code++;
            }
            k += counts[l];
            h.MaxCode[l] = counts[l] > 0 ? code - 1 : -1;
            code <<= 1;
        }
        h.MaxCode[17] = int.MaxValue;
        return h;
    }

    sealed class BitReader(byte[] d, int pos)
    {
        ulong _acc; int _n; bool _marker;
        public int Pos = pos;
        public int EobRun;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Fill()
        {
            while (_n <= 56)
            {
                int b = 0;
                if (!_marker && Pos < d.Length)
                {
                    b = d[Pos];
                    if (b == 0xFF)
                    {
                        var nx = Pos + 1 < d.Length ? d[Pos + 1] : 0xD9;
                        if (nx == 0) Pos += 2; else { _marker = true; b = 0; }
                    }
                    else Pos++;
                }
                _acc = _acc << 8 | (uint)b; _n += 8;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Bit() { if (_n < 1) Fill(); _n--; return (int)(_acc >> _n) & 1; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Bits(int n)
        {
            if (n == 0) return 0;
            if (_n < n) Fill();
            _n -= n;
            return (int)(_acc >> _n) & ((1 << n) - 1);
        }

        public int Sym(Huff? h)
        {
            if (h is null) throw new InvalidOperationException("missing Huffman table");
            if (_n < 16) Fill();
            var look = h.Look[(int)(_acc >> (_n - 9)) & 511];
            if (look != 0) { _n -= look >> 8; return look & 255; }
            for (var l = 10; l <= 16; l++)
            {
                var code = (int)(_acc >> (_n - l)) & ((1 << l) - 1);
                if (code <= h.MaxCode[l])
                {
                    _n -= l;
                    return h.Vals[Math.Min(h.Vals.Length - 1, code + h.ValOffset[l])];
                }
            }
            _n -= 16; return 0;
        }

        public void Restart()
        {
            _n = 0; _acc = 0; EobRun = 0;
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
            var coef = c.Coef.AsSpan(o, 64);
            if (!prog) Sequential(br, c, hdc[c.Td], hac[c.Ta], coef);
            else if (ss == 0) { if (ah == 0) DcFirst(br, c, hdc[c.Td], coef, al); else if (br.Bit() == 1) coef[0] |= (short)(1 << al); }
            else if (ah == 0) AcFirst(br, hac[c.Ta]!, coef, ss, se, al);
            else AcRefine(br, hac[c.Ta]!, coef, ss, se, al);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Extend(int v, int t) => v < 1 << (t - 1) ? v - (1 << t) + 1 : v;

    static void Sequential(BitReader br, Comp c, Huff? dc, Huff? ac, Span<short> coef)
    {
        var t = br.Sym(dc);
        if (t != 0) c.Pred += Extend(br.Bits(t), t);
        coef[0] = (short)c.Pred;
        var zig = Zig;
        for (var k = 1; k < 64;)
        {
            var rs = br.Sym(ac); int r = rs >> 4, s = rs & 15;
            if (s == 0) { if (r == 15) { k += 16; continue; } break; }
            k += r;
            if (k > 63) break;
            coef[zig[k]] = (short)Extend(br.Bits(s), s);
            k++;
        }
    }

    static void DcFirst(BitReader br, Comp c, Huff? dc, Span<short> coef, int al)
    {
        var t = br.Sym(dc);
        if (t != 0) c.Pred += Extend(br.Bits(t), t);
        coef[0] = (short)(c.Pred << al);
    }

    static void AcFirst(BitReader br, Huff ac, Span<short> coef, int ss, int se, int al)
    {
        if (br.EobRun > 0) { br.EobRun--; return; }
        for (var k = ss; k <= se;)
        {
            var rs = br.Sym(ac); int r = rs >> 4, s = rs & 15;
            if (s == 0)
            {
                if (r < 15) { br.EobRun = (1 << r) - 1; if (r > 0) br.EobRun += br.Bits(r); break; }
                k += 16; continue;
            }
            k += r;
            if (k > 63) break;
            coef[Zig[k]] = (short)(Extend(br.Bits(s), s) * (1 << al));
            k++;
        }
    }

    static void AcRefine(BitReader br, Huff ac, Span<short> coef, int ss, int se, int al)
    {
        int p1 = 1 << al, m1 = -1 << al;
        var k = ss;
        if (br.EobRun <= 0)
        {
            for (; k <= se; k++)
            {
                var rs = br.Sym(ac); var r = rs >> 4; var s = rs & 15; var nv = 0;
                if (s == 0)
                {
                    if (r < 15) { br.EobRun = 1 << r; if (r > 0) br.EobRun += br.Bits(r); break; }
                }
                else nv = br.Bit() == 1 ? p1 : m1;
                while (k <= se)
                {
                    ref var cf = ref coef[Zig[k]];
                    if (cf != 0)
                    {
                        if (br.Bit() == 1 && (cf & p1) == 0) cf += (short)(cf >= 0 ? p1 : m1);
                    }
                    else if (--r < 0) break;
                    k++;
                }
                if (nv != 0 && k <= se) coef[Zig[k]] = (short)nv;
            }
        }
        if (br.EobRun > 0)
        {
            for (; k <= se; k++)
            {
                ref var cf = ref coef[Zig[k]];
                if (cf != 0 && br.Bit() == 1 && (cf & p1) == 0) cf += (short)(cf >= 0 ? p1 : m1);
            }
            br.EobRun--;
        }
    }

    const int ConstBits = 13, Pass1Bits = 2;
    const int F0298 = 2446, F0390 = 3196, F0541 = 4433, F0765 = 6270, F0899 = 7373, F1175 = 9633,
              F1501 = 12299, F1847 = 15137, F1961 = 16069, F2053 = 16819, F2562 = 20995, F3072 = 25172;

    static void ReconstructRow(Comp c, int[] q, byte[] plane, int by)
    {
        var stride = c.BW * 8;
        Span<int> ws = stackalloc int[64];
        Span<int> inp = stackalloc int[64];
        for (var bx = 0; bx < c.BW; bx++)
        {
            var coef = c.Coef.AsSpan((by * c.BW + bx) * 64, 64);
            for (var i = 0; i < 64; i++) inp[i] = coef[i] * q[i];
            Idct(inp, ws, plane.AsSpan(by * 8 * stride + bx * 8), stride);
        }
    }

    // libjpeg jidctint ("islow") so output matches libjpeg bit-for-bit.
    static void Idct(ReadOnlySpan<int> inp, Span<int> ws, Span<byte> dst, int stride)
    {
        for (var col = 0; col < 8; col++)
        {
            if (inp[col + 8] == 0 && inp[col + 16] == 0 && inp[col + 24] == 0 && inp[col + 32] == 0 && inp[col + 40] == 0 && inp[col + 48] == 0 && inp[col + 56] == 0)
            {
                var dc = inp[col] << Pass1Bits;
                for (var r = 0; r < 8; r++) ws[col + r * 8] = dc;
                continue;
            }
            int z2 = inp[col + 16], z3 = inp[col + 48];
            var z1 = (z2 + z3) * F0541;
            var tmp2 = z1 + z3 * -F1847; var tmp3 = z1 + z2 * F0765;
            z2 = inp[col]; z3 = inp[col + 32];
            var tmp0 = (z2 + z3) << ConstBits; var tmp1 = (z2 - z3) << ConstBits;
            var t10 = tmp0 + tmp3; var t13 = tmp0 - tmp3; var t11 = tmp1 + tmp2; var t12 = tmp1 - tmp2;
            tmp0 = inp[col + 56]; tmp1 = inp[col + 40]; tmp2 = inp[col + 24]; tmp3 = inp[col + 8];
            z1 = tmp0 + tmp3; z2 = tmp1 + tmp2; z3 = tmp0 + tmp2; var z4 = tmp1 + tmp3; var z5 = (z3 + z4) * F1175;
            tmp0 *= F0298; tmp1 *= F2053; tmp2 *= F3072; tmp3 *= F1501;
            z1 *= -F0899; z2 *= -F2562; z3 *= -F1961; z4 *= -F0390;
            z3 += z5; z4 += z5;
            tmp0 += z1 + z3; tmp1 += z2 + z4; tmp2 += z2 + z3; tmp3 += z1 + z4;
            const int sh = ConstBits - Pass1Bits, rnd = 1 << (sh - 1);
            ws[col] = (t10 + tmp3 + rnd) >> sh; ws[col + 56] = (t10 - tmp3 + rnd) >> sh;
            ws[col + 8] = (t11 + tmp2 + rnd) >> sh; ws[col + 48] = (t11 - tmp2 + rnd) >> sh;
            ws[col + 16] = (t12 + tmp1 + rnd) >> sh; ws[col + 40] = (t12 - tmp1 + rnd) >> sh;
            ws[col + 24] = (t13 + tmp0 + rnd) >> sh; ws[col + 32] = (t13 - tmp0 + rnd) >> sh;
        }
        for (var row = 0; row < 8; row++)
        {
            var w = ws.Slice(row * 8, 8);
            var o = dst.Slice(row * stride, 8);
            const int sh = ConstBits + Pass1Bits + 3, rnd = 1 << (sh - 1);
            if (w[1] == 0 && w[2] == 0 && w[3] == 0 && w[4] == 0 && w[5] == 0 && w[6] == 0 && w[7] == 0)
            {
                var dc = (byte)Clamp(((w[0] + (1 << (Pass1Bits + 2))) >> (Pass1Bits + 3)) + 128);
                o.Fill(dc);
                continue;
            }
            int z2 = w[2], z3 = w[6];
            var z1 = (z2 + z3) * F0541;
            var tmp2 = z1 + z3 * -F1847; var tmp3 = z1 + z2 * F0765;
            var tmp0 = (w[0] + w[4]) << ConstBits; var tmp1 = (w[0] - w[4]) << ConstBits;
            var t10 = tmp0 + tmp3; var t13 = tmp0 - tmp3; var t11 = tmp1 + tmp2; var t12 = tmp1 - tmp2;
            tmp0 = w[7]; tmp1 = w[5]; tmp2 = w[3]; tmp3 = w[1];
            z1 = tmp0 + tmp3; z2 = tmp1 + tmp2; z3 = tmp0 + tmp2; var z4 = tmp1 + tmp3; var z5 = (z3 + z4) * F1175;
            tmp0 *= F0298; tmp1 *= F2053; tmp2 *= F3072; tmp3 *= F1501;
            z1 *= -F0899; z2 *= -F2562; z3 *= -F1961; z4 *= -F0390;
            z3 += z5; z4 += z5;
            tmp0 += z1 + z3; tmp1 += z2 + z4; tmp2 += z2 + z3; tmp3 += z1 + z4;
            o[0] = (byte)Clamp(((t10 + tmp3 + rnd) >> sh) + 128); o[7] = (byte)Clamp(((t10 - tmp3 + rnd) >> sh) + 128);
            o[1] = (byte)Clamp(((t11 + tmp2 + rnd) >> sh) + 128); o[6] = (byte)Clamp(((t11 - tmp2 + rnd) >> sh) + 128);
            o[2] = (byte)Clamp(((t12 + tmp1 + rnd) >> sh) + 128); o[5] = (byte)Clamp(((t12 - tmp1 + rnd) >> sh) + 128);
            o[3] = (byte)Clamp(((t13 + tmp0 + rnd) >> sh) + 128); o[4] = (byte)Clamp(((t13 - tmp0 + rnd) >> sh) + 128);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Clamp(int v) => (uint)v <= 255 ? v : v < 0 ? 0 : 255;

    static readonly int[] CrR = BuildTab(x => (91881 * x + 32768) >> 16);
    static readonly int[] CbB = BuildTab(x => (116130 * x + 32768) >> 16);
    static readonly int[] CrG = BuildTab(x => -46802 * x);
    static readonly int[] CbG = BuildTab(x => -22554 * x + 32768);
    static int[] BuildTab(Func<int, int> f) { var t = new int[256]; for (var i = 0; i < 256; i++) t[i] = f(i - 128); return t; }

    static byte[] Assemble(Comp[] comps, byte[][] planes, int w, int h, int hmax, int vmax, int adobe, bool parallel)
    {
        var n = comps.Length;
        var o = new byte[w * h * n];
        var ycc = n == 3 ? adobe != 0 && !(comps[0].Id == 'R' && comps[1].Id == 'G' && comps[2].Id == 'B') : n == 4 && adobe == 2;
        var xmap = new int[n][];
        for (var i = 0; i < n; i++)
        {
            xmap[i] = new int[w];
            for (var x = 0; x < w; x++) xmap[i][x] = x * comps[i].H / hmax;
        }
        void Row(int y)
        {
            var dst = o.AsSpan(y * w * n, w * n);
            if (n == 1)
            {
                var c = comps[0]; var src = planes[0].AsSpan((y * c.V / vmax) * c.BW * 8);
                if (c.H == hmax) src[..w].CopyTo(dst); else for (var x = 0; x < w; x++) dst[x] = src[xmap[0][x]];
                return;
            }
            Span<int> rowBase = stackalloc int[4];
            for (var i = 0; i < n; i++) rowBase[i] = (y * comps[i].V / vmax) * comps[i].BW * 8;
            for (var x = 0; x < w; x++)
            {
                var p = x * n;
                if (ycc)
                {
                    int yy = planes[0][rowBase[0] + xmap[0][x]], cb = planes[1][rowBase[1] + xmap[1][x]], cr = planes[2][rowBase[2] + xmap[2][x]];
                    var r = Clamp(yy + CrR[cr]); var g = Clamp(yy + ((CbG[cb] + CrG[cr]) >> 16)); var b = Clamp(yy + CbB[cb]);
                    if (n == 4) { dst[p] = (byte)(255 - r); dst[p + 1] = (byte)(255 - g); dst[p + 2] = (byte)(255 - b); dst[p + 3] = planes[3][rowBase[3] + xmap[3][x]]; }
                    else { dst[p] = (byte)r; dst[p + 1] = (byte)g; dst[p + 2] = (byte)b; }
                }
                else for (var i = 0; i < n; i++) dst[p + i] = planes[i][rowBase[i] + xmap[i][x]];
            }
        }
        if (parallel) Parallel.For(0, h, Row); else for (var y = 0; y < h; y++) Row(y);
        return o;
    }
}
