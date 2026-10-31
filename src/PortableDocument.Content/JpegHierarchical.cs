namespace PortableDocument.Content;

// Hierarchical JPEG (ITU-T T.81 Annex J): a DHP-introduced sequence of non-differential and differential frames
// (SOF5-7, SOF13-15) with EXP bilinear resolution doubling. Frames are decoded into full-precision component planes
// (not 8-bit), so this path is separate from the single-frame decoder. Differential DCT frames use a double-precision IDCT
// without level shift (T.81 J.2.3.1); the DC coefficient of a differential block is decoded without prediction.
public static partial class JpegDecoder
{
    const int MaxHierarchicalFrames = 64;

    sealed class HPlane(int w, int h)
    {
        public readonly int W = w, H = h;
        public readonly int[] S = new int[w * h];
    }

    sealed class HFrame
    {
        public int Marker, Prec, W, H, Hmax = 1, Vmax = 1, Eh, Ev, Pt;
        public bool Diff, Lossless, Arith, Prog;
        public Comp[] Comps = [];
    }

    static byte[]? DecodeHierarchical(byte[] d, out int width, out int height, out int ncomp)
    {
        width = height = ncomp = 0;
        var qt = new int[4][];
        var hdc = new Huff?[4]; var hac = new Huff?[4];
        int[] dcL = new int[4], dcU = [1, 1, 1, 1], acK = [5, 5, 5, 5];
        int pos = 2, restart = 0, adobe = -1, frames = 0;
        int dprec = 0, dw = 0, dh = 0, pendingEh = 0, pendingEv = 0;
        Comp[]? dhp = null;
        long budget = 0;
        var refs = new Dictionary<int, HPlane>();
        HFrame? fr = null;

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
                case 0xCC:
                    for (var q = seg; q + 1 < end; q += 2)
                    {
                        var tb = d[q] & 3; var cs = d[q + 1];
                        if (d[q] >> 4 == 0) { dcL[tb] = cs & 15; dcU[tb] = cs >> 4; } else acK[tb] = cs;
                    }
                    break;
                case 0xDD: restart = d[seg] << 8 | d[seg + 1]; break;
                case 0xEE: if (len >= 14 && d[seg] == 'A' && d[seg + 1] == 'd') adobe = d[seg + 11]; break;
                case 0xDE:
                    {
                        if (dhp is not null) return null;
                        dprec = d[seg]; dh = d[seg + 1] << 8 | d[seg + 2]; dw = d[seg + 3] << 8 | d[seg + 4];
                        var n = d[seg + 5];
                        if (dw <= 0 || dh <= 0 || n is < 1 or > 4 || dprec is < 2 or > 16 || (long)dw * dh * n > MaxLosslessSamples) return null;
                        dhp = new Comp[n];
                        for (var i = 0; i < n; i++)
                        {
                            var o = seg + 6 + i * 3;
                            dhp[i] = new Comp { Id = d[o], H = Math.Max(1, d[o + 1] >> 4), V = Math.Max(1, d[o + 1] & 15) };
                        }
                        break;
                    }
                case 0xDF: pendingEh = d[seg] >> 4; pendingEv = d[seg] & 15; break;
                case 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF:
                    {
                        if (dhp is null || ++frames > MaxHierarchicalFrames) return null;
                        if (fr is not null && !FinishFrame(fr, qt, refs, ref budget)) return null;
                        fr = StartFrame(d, seg, m, dprec, dw, dh, ref budget);
                        if (fr is null) return null;
                        fr.Eh = pendingEh; fr.Ev = pendingEv; pendingEh = pendingEv = 0;
                        break;
                    }
                case 0xDA:
                    {
                        if (fr is null) return null;
                        var ns = d[seg];
                        var sc = new Comp[ns];
                        for (var i = 0; i < ns; i++)
                        {
                            var cid = d[seg + 1 + i * 2]; var tt = d[seg + 2 + i * 2];
                            var c = Array.Find(fr.Comps, x => x.Id == cid) ?? throw new InvalidOperationException("bad component");
                            c.Td = tt >> 4 & 3; c.Ta = tt & 3; c.NoPred = fr.Diff; c.Q ??= qt[c.Tq]; sc[i] = c;
                        }
                        var ss = d[seg + 1 + ns * 2]; var se = d[seg + 2 + ns * 2];
                        var ah = d[seg + 3 + ns * 2] >> 4; var al = d[seg + 3 + ns * 2] & 15;
                        if (fr.Lossless)
                        {
                            fr.Pt = al;
                            if (fr.Diff ? ss != 0 : ss is < 1 or > 7) return null;
                            pos = DecodeLosslessGeneral(d, end, fr.Arith, sc, hdc, ss, al, fr.Prec, restart, fr.Hmax, fr.Vmax, fr.W, fr.H, dcL, dcU);
                            continue;
                        }
                        if (!fr.Prog) { ss = 0; se = 63; ah = al = 0; }
                        if (fr.Arith)
                        {
                            DecodeArithScan(d, end, sc, fr.Prog, ss, se, ah, al, restart, fr.Hmax, fr.Vmax, fr.W, fr.H, dcL, dcU, acK, out pos);
                            continue;
                        }
                        var br = new BitReader(d, end);
                        DecodeScan(br, sc, hdc, hac, fr.Prog, ss, se, ah, al, restart, fr.Hmax, fr.Vmax, fr.W, fr.H);
                        pos = br.Pos;
                        continue;
                    }
            }
            pos = end;
        }
        if (dhp is null || fr is null || !FinishFrame(fr, qt, refs, ref budget)) return null;

        width = dw; height = dh; ncomp = dhp.Length;
        int hmax = 1, vmax = 1;
        foreach (var c in dhp) { hmax = Math.Max(hmax, c.H); vmax = Math.Max(vmax, c.V); }
        var planes = new byte[dhp.Length][];
        var mask = (1 << dprec) - 1;
        for (var i = 0; i < dhp.Length; i++)
        {
            var c = dhp[i];
            if (!refs.TryGetValue(c.Id, out var p)) return null;
            var cw = (dw * c.H + hmax - 1) / hmax; var ch = (dh * c.V + vmax - 1) / vmax;
            c.BW = (cw + 7) / 8;
            var stride = c.BW * 8;
            var plane = planes[i] = new byte[stride * ch];
            for (var y = 0; y < ch; y++)
            {
                var sy = p.H == ch ? y : Math.Min(p.H - 1, (int)((long)y * p.H / ch));
                for (var x = 0; x < cw; x++)
                {
                    var sx = p.W == cw ? x : Math.Min(p.W - 1, (int)((long)x * p.W / cw));
                    var s = p.S[sy * p.W + sx] & mask;
                    plane[y * stride + x] = (byte)(dprec == 8 ? s : dprec > 8 ? s >> (dprec - 8) : (s * 255 + (mask >> 1)) / mask);
                }
            }
        }
        return Assemble(dhp, planes, dw, dh, hmax, vmax, adobe, (long)dw * dh > 40_000 && !OperatingSystem.IsBrowser());
    }

    static HFrame? StartFrame(byte[] d, int seg, int m, int dprec, int dw, int dh, ref long budget)
    {
        var f = new HFrame
        {
            Marker = m, Prec = d[seg],
            Diff = m is 0xC5 or 0xC6 or 0xC7 or 0xCD or 0xCE or 0xCF,
            Lossless = m is 0xC3 or 0xC7 or 0xCB or 0xCF,
            Arith = m is 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF,
            Prog = m is 0xC2 or 0xC6 or 0xCA or 0xCE,
        };
        // T.81 B.3.1: precision is constant over all frames and equals the DHP value; frames never exceed the DHP size.
        if (f.Prec != dprec || (!f.Lossless && f.Prec is not (8 or 12))) return null;
        f.H = d[seg + 1] << 8 | d[seg + 2]; f.W = d[seg + 3] << 8 | d[seg + 4];
        var n = d[seg + 5];
        if (f.W <= 0 || f.H <= 0 || f.W > dw || f.H > dh || n is < 1 or > 4) return null;
        f.Comps = new Comp[n];
        for (var i = 0; i < n; i++)
        {
            var o = seg + 6 + i * 3;
            f.Comps[i] = new Comp { Id = d[o], H = Math.Max(1, d[o + 1] >> 4), V = Math.Max(1, d[o + 1] & 15), Tq = d[o + 2] & 3 };
            f.Hmax = Math.Max(f.Hmax, f.Comps[i].H); f.Vmax = Math.Max(f.Vmax, f.Comps[i].V);
        }
        var mx = (f.W + 8 * f.Hmax - 1) / (8 * f.Hmax); var my = (f.H + 8 * f.Vmax - 1) / (8 * f.Vmax);
        foreach (var c in f.Comps)
        {
            c.BW = mx * c.H; c.BH = my * c.V;
            c.RealBW = ((f.W * c.H + f.Hmax - 1) / f.Hmax + 7) / 8; c.RealBH = ((f.H * c.V + f.Vmax - 1) / f.Vmax + 7) / 8;
            c.CW = (f.W * c.H + f.Hmax - 1) / f.Hmax; c.CH = (f.H * c.V + f.Vmax - 1) / f.Vmax;
            long cells;
            if (f.Lossless)
            {
                c.Stride = (f.W + f.Hmax - 1) / f.Hmax * c.H;
                var rows = (f.H + f.Vmax - 1) / f.Vmax * c.V;
                cells = (long)c.Stride * rows;
                budget += cells;
                if (budget > MaxLosslessSamples) return null;
                c.Raw = new ushort[cells];
            }
            else
            {
                cells = (long)c.BW * c.BH * 64;
                budget += cells;
                if (budget > MaxLosslessSamples) return null;
                c.Coef = new short[cells];
            }
        }
        return f;
    }

    // Converts a decoded frame to sample planes and folds it into the reference components (T.81 J.2.1).
    static bool FinishFrame(HFrame f, int[][] qt, Dictionary<int, HPlane> refs, ref long budget)
    {
        var max = (1 << f.Prec) - 1;
        foreach (var c in f.Comps)
        {
            var p = new HPlane(c.CW, c.CH);
            if (f.Lossless)
            {
                for (var y = 0; y < c.CH; y++)
                    for (var x = 0; x < c.CW; x++)
                    {
                        int v = c.Raw![y * c.Stride + x];
                        p.S[y * c.CW + x] = f.Diff ? (short)v << f.Pt : (v << f.Pt) & max;
                    }
            }
            else DctSamples(f, c, c.Q, p);
            if (!f.Diff) { refs[c.Id] = p; continue; }
            if (!refs.TryGetValue(c.Id, out var rf)) return false;
            var up = rf;
            if (f.Eh != 0) up = ExpandHorizontal(up);
            if (f.Ev != 0) up = ExpandVertical(up);
            if (up.W < p.W || up.H < p.H || f.Eh > 1 || f.Ev > 1) return false;
            budget += (long)p.W * p.H;
            if (budget > 4L * MaxLosslessSamples) return false;
            var res = new HPlane(p.W, p.H);
            for (var y = 0; y < p.H; y++)
                for (var x = 0; x < p.W; x++)
                {
                    var s = up.S[y * up.W + x] + p.S[y * p.W + x];
                    res.S[y * p.W + x] = f.Lossless ? s & 0xFFFF : Math.Clamp(s, 0, max);
                }
            refs[c.Id] = res;
        }
        return true;
    }

    // J.1.1.2: bilinear doubling; out[2i] = in[i], out[2i+1] = (in[i] + in[i+1]) / 2 with the last input replicated.
    static HPlane ExpandHorizontal(HPlane a)
    {
        var o = new HPlane(a.W * 2, a.H);
        for (var y = 0; y < a.H; y++)
            for (var x = 0; x < a.W; x++)
            {
                var l = a.S[y * a.W + x]; var r = a.S[y * a.W + Math.Min(x + 1, a.W - 1)];
                o.S[y * o.W + 2 * x] = l; o.S[y * o.W + 2 * x + 1] = (l + r) >> 1;
            }
        return o;
    }

    static HPlane ExpandVertical(HPlane a)
    {
        var o = new HPlane(a.W, a.H * 2);
        for (var y = 0; y < a.H; y++)
            for (var x = 0; x < a.W; x++)
            {
                var l = a.S[y * a.W + x]; var r = a.S[Math.Min(y + 1, a.H - 1) * a.W + x];
                o.S[2 * y * a.W + x] = l; o.S[(2 * y + 1) * a.W + x] = (l + r) >> 1;
            }
        return o;
    }

    static readonly double[] DctBasis = BuildDctBasis();

    static double[] BuildDctBasis()
    {
        var t = new double[64];
        for (var u = 0; u < 8; u++)
            for (var x = 0; x < 8; x++)
                t[u * 8 + x] = (u == 0 ? Math.Sqrt(0.125) : 0.5) * Math.Cos((2 * x + 1) * u * Math.PI / 16);
        return t;
    }

    // T.81 A.3.3 separable IDCT in double precision; differential frames are neither level shifted nor clamped (J.2.3.1).
    static void DctSamples(HFrame f, Comp c, int[]? q, HPlane p)
    {
        var shift = f.Diff ? 0 : 1 << (f.Prec - 1);
        var max = (1 << f.Prec) - 1;
        Span<double> inp = stackalloc double[64];
        Span<double> tmp = stackalloc double[64];
        for (var by = 0; by * 8 < c.CH; by++)
            for (var bx = 0; bx * 8 < c.CW; bx++)
            {
                var coef = c.Coef.AsSpan((by * c.BW + bx) * 64, 64);
                for (var i = 0; i < 64; i++) inp[i] = coef[i] * (q?[i] ?? 1);
                for (var v = 0; v < 8; v++)
                    for (var x = 0; x < 8; x++)
                    {
                        double s = 0;
                        for (var u = 0; u < 8; u++) s += DctBasis[u * 8 + x] * inp[v * 8 + u];
                        tmp[v * 8 + x] = s;
                    }
                for (var y = 0; y < 8 && by * 8 + y < c.CH; y++)
                    for (var x = 0; x < 8 && bx * 8 + x < c.CW; x++)
                    {
                        double s = 0;
                        for (var v = 0; v < 8; v++) s += DctBasis[v * 8 + y] * tmp[v * 8 + x];
                        var r = (int)Math.Floor(s + 0.5) + shift;
                        p.S[(by * 8 + y) * c.CW + bx * 8 + x] = f.Diff ? r : Math.Clamp(r, 0, max);
                    }
            }
    }
}
