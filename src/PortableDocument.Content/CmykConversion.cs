namespace PortableDocument.Content;

/// <summary>
/// DeviceCMYK to sRGB for on-screen output. ISO 32000-2 10.3.5 only gives the naive R = 1 - min(1, C + K) formula as a
/// device-dependent fallback; every conforming viewer substitutes a press-like characterisation (Acrobat: SWOP). This uses a
/// 9x9x9x9 grid (node values 0, 32, ... 224, 255 per component) interpolated multilinearly, which matches PDFium/Chrome (the
/// reference renderer of tools/AdoBye.RenderCompare) to a mean of under one 8-bit level; see docs/colour-conversion.md for
/// provenance, licence and measured error. The grid is an embedded resource, so it is trimming/AOT/browser safe, and the
/// conversion is allocation-free.
/// </summary>
static class CmykConversion
{
    const int Nodes = 9;
    static readonly byte[] Table = Load();

    static byte[] Load()
    {
        using var s = typeof(CmykConversion).Assembly.GetManifestResourceStream("PortableDocument.Content.Resources.cmyk-srgb-9x9x9x9.bin")
            ?? throw new InvalidOperationException("Missing embedded CMYK table.");
        var data = new byte[Nodes * Nodes * Nodes * Nodes * 3];
        s.ReadExactly(data);
        return data;
    }

    /// <summary>Components are clamped to 0...1; the result is in 0...1.</summary>
    public static void ToRgb(double c, double m, double y, double k, out double r, out double g, out double b)
    {
        var t = Table;
        Span<float> v = stackalloc float[48];
        var pc = Cl(c) * 8; var pm = Cl(m) * 8; var py = Cl(y) * 8; var pk = Cl(k) * 8;
        int ic = Math.Min((int)pc, 7), im = Math.Min((int)pm, 7), iy = Math.Min((int)py, 7), ik = Math.Min((int)pk, 7);
        var baseOff = (((ic * Nodes + im) * Nodes + iy) * Nodes + ik) * 3;
        const int sK = 3, sY = 27, sM = 243, sC = 2187;
        for (var i = 0; i < 16; i++)
        {
            var o = baseOff + ((i & 8) != 0 ? sC : 0) + ((i & 4) != 0 ? sM : 0) + ((i & 2) != 0 ? sY : 0) + ((i & 1) != 0 ? sK : 0);
            v[i * 3] = t[o]; v[i * 3 + 1] = t[o + 1]; v[i * 3 + 2] = t[o + 2];
        }
        // Corner index bits are c, m, y, k (k least significant); collapse k first, then y, m, c.
        Reduce(v, 8, pk - ik);
        Reduce(v, 4, py - iy);
        Reduce(v, 2, pm - im);
        Reduce(v, 1, pc - ic);
        r = v[0] / 255.0; g = v[1] / 255.0; b = v[2] / 255.0;
    }

    static float Cl(double v) => v > 0 ? (v < 1 ? (float)v : 1f) : 0f; // NaN maps to 0

    static void Reduce(Span<float> v, int pairs, float f)
    {
        for (var j = 0; j < pairs; j++)
            for (var ch = 0; ch < 3; ch++)
            {
                var a = v[2 * j * 3 + ch];
                v[j * 3 + ch] = a + (v[(2 * j + 1) * 3 + ch] - a) * f;
            }
    }
}
