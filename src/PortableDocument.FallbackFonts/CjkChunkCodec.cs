using System.IO.Compression;
using PortableDocument.Content;

namespace PortableDocument.FallbackFonts;

/// <summary>
/// Chunk format of the lazily loaded CJK glyph data. A chunk holds the outlines (1000 units per em, integer coordinates) of the glyphs whose
/// Unicode scalar lies in one 256-scalar block, deflate-compressed: u16 glyph count, then per glyph its low scalar byte, a varint operation count
/// and the operations (0 move, 1 line, 2 quadratic, 3 cubic, 4 close) with zigzag-varint coordinates delta-coded against the previous point.
/// Quadratic segments (TrueType) are stored as one control point and elevated to cubic when read.
/// </summary>
public static class CjkChunkCodec
{
    const int MaxOps = 4096;

    public static byte[] Encode(IReadOnlyList<(int Low, PathData Path)> glyphs)
    {
        using var raw = new MemoryStream();
        var count = glyphs.Count;
        raw.WriteByte((byte)count); raw.WriteByte((byte)(count >> 8));
        foreach (var (low, path) in glyphs.OrderBy(g => g.Low))
        {
            raw.WriteByte((byte)low);
            WriteGlyph(raw, path);
        }
        using var packed = new MemoryStream();
        using (var z = new DeflateStream(packed, CompressionLevel.SmallestSize, true)) raw.WriteTo(z);
        return packed.ToArray();
    }

    static void WriteGlyph(Stream s, PathData path)
    {
        var ops = new List<(byte Op, long[] Pts)>();
        double sx = 0, sy = 0, cx = 0, cy = 0;
        var pi = 0;
        foreach (var op in path.Ops)
        {
            switch (op)
            {
                case PathOp.Move:
                    sx = cx = path.Pts[pi]; sy = cy = path.Pts[pi + 1];
                    ops.Add((0, [Round(cx), Round(cy)])); pi += 2; break;
                case PathOp.Line:
                    cx = path.Pts[pi]; cy = path.Pts[pi + 1];
                    ops.Add((1, [Round(cx), Round(cy)])); pi += 2; break;
                case PathOp.Curve:
                    double x1 = path.Pts[pi], y1 = path.Pts[pi + 1], x2 = path.Pts[pi + 2], y2 = path.Pts[pi + 3], x3 = path.Pts[pi + 4], y3 = path.Pts[pi + 5];
                    double qx1 = (3 * x1 - cx) / 2, qy1 = (3 * y1 - cy) / 2, qx2 = (3 * x2 - x3) / 2, qy2 = (3 * y2 - y3) / 2;
                    if (Math.Abs(qx1 - qx2) < 0.02 && Math.Abs(qy1 - qy2) < 0.02)
                        ops.Add((2, [Round(qx1), Round(qy1), Round(x3), Round(y3)]));
                    else
                        ops.Add((3, [Round(x1), Round(y1), Round(x2), Round(y2), Round(x3), Round(y3)]));
                    cx = x3; cy = y3; pi += 6; break;
                case PathOp.Close:
                    ops.Add((4, [])); cx = sx; cy = sy; break;
            }
        }
        WriteVarint(s, (ulong)ops.Count);
        long px = 0, py = 0;
        foreach (var (op, pts) in ops)
        {
            s.WriteByte(op);
            for (var i = 0; i < pts.Length; i += 2)
            {
                WriteVarint(s, Zigzag(pts[i] - px)); WriteVarint(s, Zigzag(pts[i + 1] - py));
                px = pts[i]; py = pts[i + 1];
            }
        }
    }

    static long Round(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);
    static ulong Zigzag(long v) => (ulong)(v << 1 ^ v >> 63);

    static void WriteVarint(Stream s, ulong v)
    {
        while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
        s.WriteByte((byte)v);
    }

    /// <summary>Decodes a chunk to its glyphs keyed by the low byte of the Unicode scalar; malformed data yields an empty result.</summary>
    public static Dictionary<int, PathData> Decode(byte[] chunk)
    {
        var result = new Dictionary<int, PathData>();
        try
        {
            using var z = new DeflateStream(new MemoryStream(chunk), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            var d = ms.GetBuffer().AsSpan(0, (int)ms.Length);
            var pos = 0;
            if (d.Length < 2) return result;
            var count = d[0] | d[1] << 8; pos = 2;
            for (var g = 0; g < count; g++)
            {
                var low = d[pos++];
                var n = (int)ReadVarint(d, ref pos);
                if (n > MaxOps) return [];
                var path = new PathData();
                long px = 0, py = 0;
                for (var i = 0; i < n; i++)
                {
                    var op = d[pos++];
                    switch (op)
                    {
                        case 0: Next(d, ref pos, ref px, ref py); path.MoveTo(px, py); break;
                        case 1: Next(d, ref pos, ref px, ref py); path.LineTo(px, py); break;
                        case 2:
                            {
                                Next(d, ref pos, ref px, ref py);
                                double qx = px, qy = py;
                                Next(d, ref pos, ref px, ref py);
                                var (x0, y0) = path.Current;
                                path.CurveTo(x0 + 2 * (qx - x0) / 3, y0 + 2 * (qy - y0) / 3, px + 2 * (qx - px) / 3, py + 2 * (qy - py) / 3, px, py);
                                break;
                            }
                        case 3:
                            {
                                Next(d, ref pos, ref px, ref py); double x1 = px, y1 = py;
                                Next(d, ref pos, ref px, ref py); double x2 = px, y2 = py;
                                Next(d, ref pos, ref px, ref py);
                                path.CurveTo(x1, y1, x2, y2, px, py);
                                break;
                            }
                        case 4: path.Close(); break;
                        default: return [];
                    }
                }
                result[low] = path;
            }
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
        {
            return [];
        }
        return result;
    }

    static void Next(ReadOnlySpan<byte> d, ref int pos, ref long px, ref long py)
    {
        px += Unzig(ReadVarint(d, ref pos)); py += Unzig(ReadVarint(d, ref pos));
    }

    static long Unzig(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);

    static ulong ReadVarint(ReadOnlySpan<byte> d, ref int pos)
    {
        ulong v = 0;
        for (var shift = 0; ; shift += 7)
        {
            if (shift > 63) throw new InvalidDataException("Varint too long.");
            var b = d[pos++];
            v |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80) return v;
        }
    }
}
