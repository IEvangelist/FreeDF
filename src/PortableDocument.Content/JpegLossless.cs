namespace PortableDocument.Content;

// Lossless Huffman JPEG (SOF3) per ITU-T T.81 Annex H: predictors 1-7, point transform, 2-16 bit precision.
public static partial class JpegDecoder
{
    static void DecodeLosslessScan(BitReader br, Comp[] sc, Huff?[] hdc, int predictor, int pt, int prec, int restart)
    {
        var cw = sc[0].CW; var ch = sc[0].CH;
        foreach (var c in sc)
        {
            if (c.Raw is null) throw new InvalidOperationException("no lossless plane");
            if (sc.Length > 1 && (c.H != 1 || c.V != 1 || c.CW != cw || c.CH != ch)) throw new InvalidOperationException("subsampled interleaved lossless scan");
        }
        var total = cw * ch;
        var initial = 1 << (prec - pt - 1);
        int startIdx = 0, startRow = 0;
        var x = 0; var y = 0;
        for (var i = 0; i < total; i++)
        {
            if (restart > 0 && i > 0 && i % restart == 0) { br.Restart(); startIdx = i; startRow = y; }
            var o = y * cw + x;
            foreach (var c in sc)
            {
                var raw = c.Raw!;
                var t = br.Sym(hdc[c.Td]);
                var diff = t == 0 ? 0 : t == 16 ? 32768 : Extend(br.Bits(t), t);
                int pred;
                if (i == startIdx) pred = initial;
                else if (y == startRow) pred = raw[o - 1];
                else if (x == 0) pred = raw[o - cw];
                else
                {
                    int ra = raw[o - 1], rb = raw[o - cw], rc = raw[o - cw - 1];
                    pred = predictor switch
                    {
                        1 => ra,
                        2 => rb,
                        3 => rc,
                        4 => ra + rb - rc,
                        5 => ra + ((rb - rc) >> 1),
                        6 => rb + ((ra - rc) >> 1),
                        7 => (ra + rb) >> 1,
                        _ => 0,
                    };
                }
                raw[o] = (ushort)(pred + diff);
            }
            if (++x == cw) { x = 0; y++; }
        }
    }

    const long MaxLosslessSamples = 1L << 28;

    // Lossless scans that the fast Huffman loop above does not cover: arithmetic coding (SOF11, T.81 H.1.2.3) and
    // interleaved scans whose components have sampling factors above 1 (MCU = H x V samples per component, T.81 A.2.3).
    static int DecodeLosslessGeneral(byte[] d, int end, bool arith, Comp[] sc, Huff?[] hdc, int predictor, int pt, int prec, int restart,
        int hmax, int vmax, int width, int height, int[] dcL, int[] dcU)
    {
        var n = sc.Length;
        var single = n == 1;
        var mcux = single ? sc[0].CW : (width + hmax - 1) / hmax;
        var mcuy = single ? sc[0].CH : (height + vmax - 1) / vmax;
        var br = arith ? null : new BitReader(d, end);
        var ad = arith ? new ArithDec(d, end) : null;
        var initial = 1 << (prec - pt - 1);
        var stats = new byte[n][]; var cats = new byte[n][]; var first = new bool[n]; var startRow = new int[n];
        for (var i = 0; i < n; i++)
        {
            if (sc[i].Raw is null) throw new InvalidOperationException("no lossless plane");
            stats[i] = new byte[158]; cats[i] = new byte[sc[i].Raw!.Length]; first[i] = true;
        }

        void Sample(int ci, int x, int y)
        {
            var c = sc[ci]; var raw = c.Raw!; var stride = c.Stride; var o = y * stride + x;
            int diff;
            if (ad is not null)
            {
                var ca = x == 0 ? 0 : cats[ci][o - 1];
                var cb = y == startRow[ci] ? 0 : cats[ci][o - stride];
                diff = ArithLosslessDiff(ad, stats[ci], ca, cb, dcL[c.Td], dcU[c.Td], out var cat);
                cats[ci][o] = (byte)cat;
            }
            else
            {
                var t = br!.Sym(hdc[c.Td]);
                diff = t == 0 ? 0 : t == 16 ? 32768 : Extend(br.Bits(t), t);
            }
            int pred;
            if (predictor == 0) { pred = 0; first[ci] = false; } // differential frames code the sample value directly (T.81 J.1.3.2)
            else if (first[ci]) { pred = initial; first[ci] = false; }
            else if (y == startRow[ci]) pred = raw[o - 1];
            else if (x == 0) pred = raw[o - stride];
            else
            {
                int ra = raw[o - 1], rb = raw[o - stride], rc = raw[o - stride - 1];
                pred = predictor switch
                {
                    1 => ra, 2 => rb, 3 => rc, 4 => ra + rb - rc, 5 => ra + ((rb - rc) >> 1), 6 => rb + ((ra - rc) >> 1), 7 => (ra + rb) >> 1,
                    _ => 0,
                };
            }
            raw[o] = (ushort)(pred + diff);
        }

        var mcu = 0;
        for (var r = 0; r < mcuy; r++)
            for (var col = 0; col < mcux; col++, mcu++)
            {
                if (restart > 0 && mcu > 0 && mcu % restart == 0)
                {
                    if (ad is not null)
                    {
                        if (!ad.Restart()) return ad.AtMarker ? ad.MarkerPos : ad.Pos;
                        foreach (var s in stats) Array.Clear(s);
                    }
                    else br!.Restart();
                    for (var i = 0; i < n; i++) { first[i] = true; startRow[i] = single ? r : r * sc[i].V; }
                }
                if (ad is { Overflow: true }) return ad.AtMarker ? ad.MarkerPos : ad.Pos;
                if (single) Sample(0, col, r);
                else
                    for (var ci = 0; ci < n; ci++)
                    {
                        var c = sc[ci];
                        for (var v = 0; v < c.V; v++)
                            for (var h = 0; h < c.H; h++)
                                Sample(ci, col * c.H + h, r * c.V + v);
                    }
            }
        return ad is not null ? (ad.AtMarker ? ad.MarkerPos : ad.Pos) : br!.Pos;
    }

    // Figures H.2/H.3: difference conditioned on the classified differences to the left (ca) and above (cb).
    // Categories: 0 zero, 1 +small, 2 -small, 3 +large, 4 -large (S0 offsets 0, 4, 8, 12, 16).
    static int ArithLosslessDiff(ArithDec ad, byte[] st, int ca, int cb, int lo, int hi, out int cat)
    {
        cat = 0;
        var s0 = 4 * (5 * ca + cb);
        if (ad.Decode(st, s0) == 0) return 0;
        var sign = ad.Decode(st, s0 + 1);
        var s = s0 + 2 + sign;
        var m = ad.Decode(st, s);
        if (m != 0)
        {
            s = cb >= 3 ? 129 : 100;
            while (ad.Decode(st, s) != 0)
            {
                if ((m <<= 1) == 0x8000) { ad.Overflow = true; return 0; }
                s++;
            }
        }
        cat = m < (1 << lo) >> 1 ? 0 : m > (1 << hi) >> 1 ? 3 + sign : 1 + sign;
        var v = m;
        s += 14;
        while ((m >>= 1) != 0) if (ad.Decode(st, s) != 0) v |= m;
        v++;
        return sign != 0 ? -v : v;
    }

    static byte[] FinishLossless(Comp[] comps, int w, int h, int hmax, int vmax, int adobe, int prec, int pt)
    {
        var planes = new byte[comps.Length][];
        var mask = (1 << prec) - 1;
        for (var i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            var stride = c.BW * 8;
            var plane = planes[i] = new byte[stride * c.BH * 8];
            var raw = c.Raw!;
            for (var y = 0; y < c.CH; y++)
            {
                var src = raw.AsSpan(y * c.Stride, c.CW);
                var dst = plane.AsSpan(y * stride, c.CW);
                for (var x = 0; x < dst.Length; x++)
                {
                    var s = (src[x] << pt) & mask;
                    dst[x] = (byte)(prec == 8 ? s : prec > 8 ? s >> (prec - 8) : (s * 255 + (mask >> 1)) / mask);
                }
            }
        }
        return Assemble(comps, planes, w, h, hmax, vmax, adobe, (long)w * h > 40_000);
    }
}
