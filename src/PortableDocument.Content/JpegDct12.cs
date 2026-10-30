namespace PortableDocument.Content;

public static partial class JpegDecoder
{
    // Keep the 8-bit fast path unchanged. Twelve-bit islow uses PASS1_BITS=1;
    // long intermediates also accommodate 16-bit quantizers without wrapping.
    static byte[] FinishDct12(Comp[] comps, int[][] qt, int w, int h, int hmax, int vmax, int adobe)
    {
        var planes = new ushort[comps.Length][];
        Span<long> ws = stackalloc long[64];
        Span<long> input = stackalloc long[8];
        Span<long> output = stackalloc long[8];
        for (var i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            var q = qt[c.Tq] ?? Array.ConvertAll(new int[64], _ => 1);
            var stride = c.BW * 8;
            var plane = planes[i] = new ushort[stride * c.BH * 8];
            for (var by = 0; by < c.BH; by++)
                for (var bx = 0; bx < c.BW; bx++)
                {
                    var coef = c.Coef.AsSpan((by * c.BW + bx) * 64, 64);
                    for (var col = 0; col < 8; col++)
                    {
                        for (var y = 0; y < 8; y++) input[y] = (long)coef[y * 8 + col] * q[y * 8 + col];
                        Idct12Pass(input, output);
                        for (var y = 0; y < 8; y++) ws[y * 8 + col] = (output[y] + (1 << 11)) >> 12;
                    }
                    for (var y = 0; y < 8; y++)
                    {
                        Idct12Pass(ws.Slice(y * 8, 8), output);
                        var dst = plane.AsSpan((by * 8 + y) * stride + bx * 8, 8);
                        for (var x = 0; x < 8; x++) dst[x] = (ushort)Clamp12(((output[x] + (1 << 16)) >> 17) + 2048);
                    }
                }
        }

        var n = comps.Length;
        var result = new byte[w * h * n];
        var ycc = n == 3 ? adobe != 0 && !(comps[0].Id == 'R' && comps[1].Id == 'G' && comps[2].Id == 'B') : n == 4 && adobe == 2;
        Span<int> samples = stackalloc int[4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                for (var i = 0; i < n; i++)
                {
                    var c = comps[i];
                    samples[i] = planes[i][(y * c.V / vmax) * c.BW * 8 + x * c.H / hmax];
                }
                var p = (y * w + x) * n;
                if (ycc)
                {
                    int yy = samples[0], cb = samples[1] - 2048, cr = samples[2] - 2048;
                    var r = Clamp12(yy + ((91881L * cr + 32768) >> 16));
                    var g = Clamp12(yy + ((-22554L * cb - 46802L * cr + 32768) >> 16));
                    var b = Clamp12(yy + ((116130L * cb + 32768) >> 16));
                    result[p] = (byte)((n == 4 ? 4095 - r : r) >> 4);
                    result[p + 1] = (byte)((n == 4 ? 4095 - g : g) >> 4);
                    result[p + 2] = (byte)((n == 4 ? 4095 - b : b) >> 4);
                    if (n == 4) result[p + 3] = (byte)(samples[3] >> 4);
                }
                else for (var i = 0; i < n; i++) result[p + i] = (byte)(samples[i] >> 4);
            }
        return result;
    }

    static int Clamp12(long value) => (int)Math.Clamp(value, 0, 4095);

    // One unscaled islow butterfly (ITU-T T.81 inverse DCT, libjpeg jidctint).
    static void Idct12Pass(ReadOnlySpan<long> input, Span<long> output)
    {
        long z2 = input[2], z3 = input[6];
        var z1 = (z2 + z3) * F0541;
        var tmp2 = z1 - z3 * F1847;
        var tmp3 = z1 + z2 * F0765;
        var tmp0 = (input[0] + input[4]) << ConstBits;
        var tmp1 = (input[0] - input[4]) << ConstBits;
        var t10 = tmp0 + tmp3; var t13 = tmp0 - tmp3;
        var t11 = tmp1 + tmp2; var t12 = tmp1 - tmp2;
        tmp0 = input[7]; tmp1 = input[5]; tmp2 = input[3]; tmp3 = input[1];
        z1 = tmp0 + tmp3; z2 = tmp1 + tmp2; z3 = tmp0 + tmp2;
        var z4 = tmp1 + tmp3; var z5 = (z3 + z4) * F1175;
        tmp0 *= F0298; tmp1 *= F2053; tmp2 *= F3072; tmp3 *= F1501;
        z1 *= -F0899; z2 *= -F2562; z3 *= -F1961; z4 *= -F0390;
        z3 += z5; z4 += z5;
        tmp0 += z1 + z3; tmp1 += z2 + z4; tmp2 += z2 + z3; tmp3 += z1 + z4;
        output[0] = t10 + tmp3; output[7] = t10 - tmp3;
        output[1] = t11 + tmp2; output[6] = t11 - tmp2;
        output[2] = t12 + tmp1; output[5] = t12 - tmp1;
        output[3] = t13 + tmp0; output[4] = t13 - tmp0;
    }
}
