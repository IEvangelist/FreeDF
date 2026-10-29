namespace AdoBye.Content;

// Arithmetic-coded DCT JPEG (SOF9 sequential, SOF10 progressive) per ITU-T T.81 Annex D/F/G.
public static partial class JpegDecoder
{
    // Table D.2 (QM-coder probability estimation), flattened as { Qe, next-LPS, next-MPS, switch-MPS }.
    static readonly int[] QmRaw = [0x5a1d, 1, 1, 1, 0x2586, 14, 2, 0, 0x1114, 16, 3, 0, 0x080b, 18, 4, 0, 0x03d8, 20, 5, 0, 0x01da, 23, 6, 0, 0x00e5, 25, 7, 0, 0x006f, 28, 8, 0, 0x0036, 30, 9, 0, 0x001a, 33, 10, 0, 0x000d, 35, 11, 0, 0x0006, 9, 12, 0, 0x0003, 10, 13, 0, 0x0001, 12, 13, 0, 0x5a7f, 15, 15, 1, 0x3f25, 36, 16, 0, 0x2cf2, 38, 17, 0, 0x207c, 39, 18, 0, 0x17b9, 40, 19, 0, 0x1182, 42, 20, 0, 0x0cef, 43, 21, 0, 0x09a1, 45, 22, 0, 0x072f, 46, 23, 0, 0x055c, 48, 24, 0, 0x0406, 49, 25, 0, 0x0303, 51, 26, 0, 0x0240, 52, 27, 0, 0x01b1, 54, 28, 0, 0x0144, 56, 29, 0, 0x00f5, 57, 30, 0, 0x00b7, 59, 31, 0, 0x008a, 60, 32, 0, 0x0068, 62, 33, 0, 0x004e, 63, 34, 0, 0x003b, 32, 35, 0, 0x002c, 33, 9, 0, 0x5ae1, 37, 37, 1, 0x484c, 64, 38, 0, 0x3a0d, 65, 39, 0, 0x2ef1, 67, 40, 0, 0x261f, 68, 41, 0, 0x1f33, 69, 42, 0, 0x19a8, 70, 43, 0, 0x1518, 72, 44, 0, 0x1177, 73, 45, 0, 0x0e74, 74, 46, 0, 0x0bfb, 75, 47, 0, 0x09f8, 77, 48, 0, 0x0861, 78, 49, 0, 0x0706, 79, 50, 0, 0x05cd, 48, 51, 0, 0x04de, 50, 52, 0, 0x040f, 50, 53, 0, 0x0363, 51, 54, 0, 0x02d4, 52, 55, 0, 0x025c, 53, 56, 0, 0x01f8, 54, 57, 0, 0x01a4, 55, 58, 0, 0x0160, 56, 59, 0, 0x0125, 57, 60, 0, 0x00f6, 58, 61, 0, 0x00cb, 59, 62, 0, 0x00ab, 61, 63, 0, 0x008f, 61, 32, 0, 0x5b12, 65, 65, 1, 0x4d04, 80, 66, 0, 0x412c, 81, 67, 0, 0x37d8, 82, 68, 0, 0x2fe8, 83, 69, 0, 0x293c, 84, 70, 0, 0x2379, 86, 71, 0, 0x1edf, 87, 72, 0, 0x1aa9, 87, 73, 0, 0x174e, 72, 74, 0, 0x1424, 72, 75, 0, 0x119c, 74, 76, 0, 0x0f6b, 74, 77, 0, 0x0d51, 75, 78, 0, 0x0bb6, 77, 79, 0, 0x0a40, 77, 48, 0, 0x5832, 80, 81, 1, 0x4d1c, 88, 82, 0, 0x438e, 89, 83, 0, 0x3bdd, 90, 84, 0, 0x34ee, 91, 85, 0, 0x2eae, 92, 86, 0, 0x299a, 93, 87, 0, 0x2516, 86, 71, 0, 0x5570, 88, 89, 1, 0x4ca9, 95, 90, 0, 0x44d9, 96, 91, 0, 0x3e22, 97, 92, 0, 0x3824, 99, 93, 0, 0x32b4, 99, 94, 0, 0x2e17, 93, 86, 0, 0x56a8, 95, 96, 1, 0x4f46, 101, 97, 0, 0x47e5, 102, 98, 0, 0x41cf, 103, 99, 0, 0x3c3d, 104, 100, 0, 0x375e, 99, 93, 0, 0x5231, 105, 102, 0, 0x4c0f, 106, 103, 0, 0x4639, 107, 104, 0, 0x415e, 103, 99, 0, 0x5627, 105, 106, 1, 0x50e7, 108, 107, 0, 0x4b85, 109, 103, 0, 0x5597, 110, 109, 0, 0x504f, 111, 107, 0, 0x5a10, 110, 111, 1, 0x5522, 112, 109, 0, 0x59eb, 112, 111, 1, 0x5a1d, 113, 113, 0];

    // Packed as (Qe << 16) | (nextMps << 8) | (switch << 7) | nextLps, with the state byte holding (mps << 7) | index.
    static readonly int[] QmTab = BuildQm();

    static int[] BuildQm()
    {
        var t = new int[QmRaw.Length / 4];
        for (var i = 0; i < t.Length; i++) t[i] = QmRaw[i * 4] << 16 | QmRaw[i * 4 + 2] << 8 | QmRaw[i * 4 + 3] << 7 | QmRaw[i * 4 + 1];
        return t;
    }

    sealed class ArithDec(byte[] d, int pos)
    {
        long _c, _a;
        int _ct = -16;
        bool _marker;
        public int Pos = pos, MarkerPos = -1, MarkerCode;
        public bool Overflow;

        public bool AtMarker => _marker;

        public void Reset() { _c = 0; _a = 0; _ct = -16; Overflow = false; }

        public int Decode(Span<byte> st, int i)
        {
            while (_a < 0x8000)
            {
                if (--_ct < 0)
                {
                    int data;
                    if (_marker) data = 0;
                    else
                    {
                        data = Pos < d.Length ? d[Pos++] : -1;
                        if (data == 0xFF)
                        {
                            var at = Pos - 1;
                            do data = Pos < d.Length ? d[Pos++] : 0xD9; while (data == 0xFF);
                            if (data == 0) data = 0xFF;
                            else { _marker = true; MarkerPos = at; MarkerCode = data; data = 0; }
                        }
                        else if (data < 0) { _marker = true; MarkerPos = d.Length; MarkerCode = 0xD9; data = 0; }
                    }
                    _c = _c << 8 | (uint)data;
                    if ((_ct += 8) < 0 && ++_ct == 0) _a = 0x8000;
                }
                _a <<= 1;
            }
            int sv = st[i];
            var t = QmTab[sv & 0x7F];
            long qe = t >> 16; var nl = t & 0xFF; var nm = (t >> 8) & 0xFF;
            var temp = _a - qe;
            _a = temp;
            temp <<= _ct;
            if (_c >= temp)
            {
                _c -= temp;
                if (_a < qe) { _a = qe; st[i] = (byte)((sv & 0x80) ^ nm); }
                else { _a = qe; st[i] = (byte)((sv & 0x80) ^ nl); sv ^= 0x80; }
            }
            else if (_a < 0x8000)
            {
                if (_a < qe) { st[i] = (byte)((sv & 0x80) ^ nl); sv ^= 0x80; }
                else st[i] = (byte)((sv & 0x80) ^ nm);
            }
            return sv >> 7;
        }

        public bool Restart()
        {
            if (_marker)
            {
                if (MarkerCode is not (>= 0xD0 and <= 0xD7)) return false;
                _marker = false;
            }
            else
            {
                while (Pos + 1 < d.Length && !(d[Pos] == 0xFF && d[Pos + 1] is >= 0xD0 and <= 0xD7)) Pos++;
                Pos += 2;
            }
            Reset();
            return true;
        }
    }

    static void DecodeArithScan(byte[] d, int start, Comp[] sc, bool prog, int ss, int se, int ah, int al, int restart,
        int hmax, int vmax, int w, int h, int[] dcL, int[] dcU, int[] acK, out int endPos)
    {
        var ad = new ArithDec(d, start);
        var dcStats = new byte[4][]; var acStats = new byte[4][];
        for (var i = 0; i < 4; i++) { dcStats[i] = new byte[64]; acStats[i] = new byte[256]; }
        var fixedBin = new byte[4];
        fixedBin[0] = 113;
        var last = new int[sc.Length]; var ctx = new int[sc.Length];

        void ResetStats()
        {
            foreach (var s in dcStats) Array.Clear(s);
            foreach (var s in acStats) Array.Clear(s);
            Array.Clear(last); Array.Clear(ctx);
        }

        void Block(int ci, int bx, int by)
        {
            var c = sc[ci];
            var coef = c.Coef.AsSpan((by * c.BW + bx) * 64, 64);
            if (ad.Overflow) return;
            if (!prog)
            {
                ArithDc(ad, dcStats[c.Td], dcL[c.Td], dcU[c.Td], ref last[ci], ref ctx[ci], coef, 0);
                if (!ad.Overflow) ArithAcFirst(ad, acStats[c.Ta], acK[c.Ta], fixedBin, coef, 1, 63, 0);
            }
            else if (ss == 0)
            {
                if (ah == 0) ArithDc(ad, dcStats[c.Td], dcL[c.Td], dcU[c.Td], ref last[ci], ref ctx[ci], coef, al);
                else if (ad.Decode(fixedBin, 0) != 0) coef[0] |= (short)(1 << al);
            }
            else if (ah == 0) ArithAcFirst(ad, acStats[c.Ta], acK[c.Ta], fixedBin, coef, ss, se, al);
            else ArithAcRefine(ad, acStats[c.Ta], fixedBin, coef, ss, se, al);
        }

        void Interval(ref int mcu)
        {
            if (restart > 0 && mcu > 0 && mcu % restart == 0) { if (ad.Restart()) ResetStats(); }
        }

        var n = 0;
        if (sc.Length == 1)
        {
            var c = sc[0];
            for (var by = 0; by < c.RealBH; by++)
                for (var bx = 0; bx < c.RealBW; bx++, n++)
                {
                    Interval(ref n);
                    Block(0, bx, by);
                }
        }
        else
        {
            var mx = (w + 8 * hmax - 1) / (8 * hmax); var my = (h + 8 * vmax - 1) / (8 * vmax);
            for (var r = 0; r < my; r++)
                for (var col = 0; col < mx; col++, n++)
                {
                    Interval(ref n);
                    for (var ci = 0; ci < sc.Length; ci++)
                    {
                        var c = sc[ci];
                        for (var v = 0; v < c.V; v++)
                            for (var hh = 0; hh < c.H; hh++)
                                Block(ci, col * c.H + hh, r * c.V + v);
                    }
                }
        }
        endPos = ad.AtMarker ? ad.MarkerPos : ad.Pos;
    }

    // Figures F.19, F.21-F.24: DC difference with conditioning.
    static void ArithDc(ArithDec ad, byte[] stats, int dcL, int dcU, ref int last, ref int ctx, Span<short> coef, int al)
    {
        var st = ctx;
        if (ad.Decode(stats, st) == 0) ctx = 0;
        else
        {
            var sign = ad.Decode(stats, st + 1);
            st += 2 + sign;
            var m = ad.Decode(stats, st);
            if (m != 0)
            {
                st = 20;
                while (ad.Decode(stats, st) != 0)
                {
                    if ((m <<= 1) == 0x8000) { ad.Overflow = true; return; }
                    st++;
                }
            }
            if (m < (1 << dcL) >> 1) ctx = 0;
            else if (m > (1 << dcU) >> 1) ctx = 12 + sign * 4;
            else ctx = 4 + sign * 4;
            var v = m;
            st += 14;
            while ((m >>= 1) != 0) if (ad.Decode(stats, st) != 0) v |= m;
            v++;
            if (sign != 0) v = -v;
            last = (last + v) & 0xFFFF;
        }
        coef[0] = (short)(last << al);
    }

    // Figure F.20 / G.1: AC coefficients ss..se (sequential uses 1..63 with al = 0).
    static void ArithAcFirst(ArithDec ad, byte[] stats, int kx, byte[] fixedBin, Span<short> coef, int ss, int se, int al)
    {
        for (var k = ss; k <= se; k++)
        {
            var st = 3 * (k - 1);
            if (ad.Decode(stats, st) != 0) break;
            while (ad.Decode(stats, st + 1) == 0)
            {
                st += 3; k++;
                if (k > se) { ad.Overflow = true; return; }
            }
            var sign = ad.Decode(fixedBin, 0);
            st += 2;
            var m = ad.Decode(stats, st);
            if (m != 0 && ad.Decode(stats, st) != 0)
            {
                m <<= 1;
                st = k <= kx ? 189 : 217;
                while (ad.Decode(stats, st) != 0)
                {
                    if ((m <<= 1) == 0x8000) { ad.Overflow = true; return; }
                    st++;
                }
            }
            var v = m;
            st += 14;
            while ((m >>= 1) != 0) if (ad.Decode(stats, st) != 0) v |= m;
            v++;
            if (sign != 0) v = -v;
            coef[Zig[k]] = (short)(v << al);
        }
    }

    // Figure G.10: successive-approximation refinement of AC coefficients.
    static void ArithAcRefine(ArithDec ad, byte[] stats, byte[] fixedBin, Span<short> coef, int ss, int se, int al)
    {
        int p1 = 1 << al, m1 = -1 << al;
        var kex = se;
        while (kex > 0 && coef[Zig[kex]] == 0) kex--;
        for (var k = ss; k <= se; k++)
        {
            var st = 3 * (k - 1);
            if (k > kex && ad.Decode(stats, st) != 0) break;
            for (; ; )
            {
                ref var cf = ref coef[Zig[k]];
                if (cf != 0)
                {
                    if (ad.Decode(stats, st + 2) != 0) cf += (short)(cf < 0 ? m1 : p1);
                    break;
                }
                if (ad.Decode(stats, st + 1) != 0)
                {
                    cf = (short)(ad.Decode(fixedBin, 0) != 0 ? m1 : p1);
                    break;
                }
                st += 3; k++;
                if (k > se) { ad.Overflow = true; return; }
            }
        }
    }
}
