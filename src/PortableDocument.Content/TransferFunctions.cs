using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>
/// An ExtGState /TR or /TR2 transfer function (ISO 32000-2 Table 58, 10.4) tabulated per RGB component. A single function applies to
/// all components; an array supplies one per red, green, blue (and gray, unused on an RGB surface). Evaluation is a table lookup.
/// </summary>
sealed class TransferFunctions
{
    const int N = 1024;
    readonly float[][] _lut = new float[3][];
    readonly byte[][] _bytes = new byte[3][];

    TransferFunctions(PdfFunction?[] fns)
    {
        for (var c = 0; c < 3; c++)
        {
            var fn = fns[c];
            var t = new float[N + 1];
            for (var i = 0; i <= N; i++)
            {
                var x = (double)i / N;
                var y = x;
                if (fn is not null)
                {
                    var o = fn.Evaluate([x]);
                    if (o.Length > 0 && !double.IsNaN(o[0])) y = o[0];
                }
                t[i] = (float)Math.Clamp(y, 0, 1);
            }
            _lut[c] = t;
            var bytes = new byte[256];
            for (var i = 0; i < 256; i++) bytes[i] = (byte)Math.Clamp((int)Math.Round(t[(int)Math.Round(i * (double)N / 255)] * 255), 0, 255);
            _bytes[c] = bytes;
        }
    }

    /// <summary>
    /// Parses a /TR or /TR2 value. Returns false when the value is invalid. A null <paramref name="result"/> means the identity transfer
    /// (the name /Identity, or /Default which denotes the page's initial transfer, identity here).
    /// </summary>
    public static bool TryParse(PdfFile f, PdfObject? value, bool allowDefault, out TransferFunctions? result)
    {
        result = null;
        value = f.Resolve(value);
        if (value is PdfName n) return n.Value == "Identity" || (allowDefault && n.Value == "Default");
        var fns = new PdfFunction?[3];
        if (value is PdfArray a)
        {
            if (a.Count != 4) return false;
            for (var i = 0; i < 3; i++)
            {
                var e = f.Resolve(a[i]);
                if (e is PdfName { Value: "Identity" }) continue;
                if (e is PdfName || PdfFunction.Parse(f, e) is not { } fn) return false;
                fns[i] = fn;
            }
            if (fns.All(x => x is null)) return true;
        }
        else
        {
            if (value is null || PdfFunction.Parse(f, value) is not { } single) return false;
            fns[0] = fns[1] = fns[2] = single;
        }
        result = new TransferFunctions(fns);
        return true;
    }

    float Map(int c, double v)
    {
        var p = Math.Clamp(v, 0, 1) * N;
        var i = (int)p;
        if (i >= N) return _lut[c][N];
        var t = _lut[c];
        return t[i] + (t[i + 1] - t[i]) * (float)(p - i);
    }

    public RgbColor Apply(RgbColor c) => new(Map(0, c.R), Map(1, c.G), Map(2, c.B));

    /// <summary>Maps the RGB bytes of straight-alpha RGBA pixels, preserving alpha, into a new buffer.</summary>
    public ImageData Apply(ImageData img)
    {
        var src = img.Rgba;
        var dst = GC.AllocateUninitializedArray<byte>(src.Length);
        var r = _bytes[0]; var g = _bytes[1]; var b = _bytes[2];
        for (var i = 0; i + 3 < src.Length; i += 4)
        {
            dst[i] = r[src[i]]; dst[i + 1] = g[src[i + 1]]; dst[i + 2] = b[src[i + 2]]; dst[i + 3] = src[i + 3];
        }
        return img with { Rgba = dst };
    }
}
