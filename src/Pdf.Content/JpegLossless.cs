namespace Pdf.Content;

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
                var src = raw.AsSpan(y * c.CW, c.CW);
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
