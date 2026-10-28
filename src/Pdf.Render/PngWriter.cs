using System.IO.Compression;
using System.IO.Hashing;

namespace Pdf.Render;

public static class PngWriter
{
    public static byte[] Encode(int w, int h, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w); WriteBE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr);
        var raw = new byte[(w * 4 + 1) * h];
        for (var y = 0; y < h; y++) Buffer.BlockCopy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4);
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) zs.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        var crc = new Crc32(); crc.Append(t); crc.Append(data);
        var c = crc.GetCurrentHash(); // little-endian
        s.Write([c[3], c[2], c[1], c[0]]);
    }

    static void WriteBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    public static (int W, int H, byte[] Rgba) Decode(byte[] png)
    {
        var pos = 8; int w = 0, h = 0, ct = 6; using var idat = new MemoryStream();
        while (pos < png.Length)
        {
            var len = png[pos] << 24 | png[pos + 1] << 16 | png[pos + 2] << 8 | png[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IHDR") { w = png[pos + 8] << 24 | png[pos + 9] << 16 | png[pos + 10] << 8 | png[pos + 11]; h = png[pos + 12] << 24 | png[pos + 13] << 16 | png[pos + 14] << 8 | png[pos + 15]; ct = png[pos + 17]; }
            else if (type == "IDAT") idat.Write(png, pos + 8, len);
            pos += 12 + len;
        }
        var bpp = ct == 6 ? 4 : ct == 2 ? 3 : throw new NotSupportedException($"PNG colour type {ct}");
        idat.Position = 0;
        using var zs = new ZLibStream(idat, CompressionMode.Decompress);
        var stride = w * bpp; var raw = new byte[(stride + 1) * h];
        zs.ReadExactly(raw);
        var cur = new byte[stride]; var prev = new byte[stride];
        var o = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var f = raw[y * (stride + 1)];
            for (var i = 0; i < stride; i++)
            {
                int x = raw[y * (stride + 1) + 1 + i];
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = (byte)(f switch
                {
                    1 => x + a, 2 => x + b, 3 => x + (a + b) / 2,
                    4 => x + Paeth(a, b, c), _ => x
                });
            }
            for (var xp = 0; xp < w; xp++)
            {
                o[(y * w + xp) * 4] = cur[xp * bpp]; o[(y * w + xp) * 4 + 1] = cur[xp * bpp + 1]; o[(y * w + xp) * 4 + 2] = cur[xp * bpp + 2];
                o[(y * w + xp) * 4 + 3] = bpp == 4 ? cur[xp * bpp + 3] : (byte)255;
            }
            (prev, cur) = (cur, prev);
        }
        return (w, h, o);
    }

    static int Paeth(int a, int b, int c)
    {
        var p = a + b - c; var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
