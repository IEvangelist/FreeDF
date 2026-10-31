using System.Runtime.CompilerServices;

namespace PortableDocument.Content;

/// <summary>
/// JBIG2 decoder (ITU-T T.88 / ISO 32000-2 §7.4.7): generic regions (MMR and arithmetic, templates 0-3, TPGD),
/// generic refinement, symbol dictionaries (arithmetic, and Huffman including refinement/aggregation), text regions (arithmetic and Huffman), pattern dictionaries, halftone regions,
/// custom Huffman tables and /JBIG2Globals. Amendment 2 12-AT extended generic templates (GBTEMPLATE 0 + EXTTEMPLATE) are decoded. Amendment 3 colour regions (COLEXTFLAG) are decoded as monochrome ink:
/// the trailing GBFGCOLID / colour section is skipped, the background is transparent (the region is OR-ed onto the page) and the palette segment is ignored, because PDF only allows monochrome JBIG2.
/// Works on one byte per pixel internally; output is packed 1 bpp.
/// </summary>
public static class Jbig2Decoder
{
    sealed class Bitmap(int w, int h)
    {
        public readonly int W = w, H = h;
        public readonly byte[] D = new byte[checked(w * h)];
    }

    sealed class Seg
    {
        public int Num, Type, Page;
        public int[] Refs = [];
        public int Off, Len;
        public bool UnknownLen;
    }

    sealed class SymDict
    {
        public Bitmap[] Exported = [];
        public byte[]? Gb, Gr;
    }

    sealed class IntDec
    {
        readonly byte[] _cx = new byte[512];

        static int Bit(MqDecoder mq, byte[] cx, ref int prev)
        {
            var d = mq.Decode(cx, prev);
            prev = prev < 256 ? prev << 1 | d : (prev << 1 | d) & 511 | 256;
            return d;
        }

        /// <summary>Annex A.2 integer decoding; false means OOB.</summary>
        public bool Decode(MqDecoder mq, out int value)
        {
            var prev = 1;
            var s = Bit(mq, _cx, ref prev);
            int n, off;
            if (Bit(mq, _cx, ref prev) == 0) { n = 2; off = 0; }
            else if (Bit(mq, _cx, ref prev) == 0) { n = 4; off = 4; }
            else if (Bit(mq, _cx, ref prev) == 0) { n = 6; off = 20; }
            else if (Bit(mq, _cx, ref prev) == 0) { n = 8; off = 84; }
            else if (Bit(mq, _cx, ref prev) == 0) { n = 12; off = 340; }
            else { n = 32; off = 4436; }
            long v = 0;
            for (var i = 0; i < n; i++) v = v << 1 | (uint)Bit(mq, _cx, ref prev);
            v += off;
            if (s == 0) { value = (int)v; return true; }
            if (v == 0) { value = 0; return false; }
            value = (int)-v;
            return true;
        }
    }

    sealed class TextArith(int codeLen)
    {
        public readonly IntDec Dt = new(), Fs = new(), Ds = new(), It = new(), Ri = new(), Rdw = new(), Rdh = new(), Rdx = new(), Rdy = new();
        public readonly byte[] Iaid = new byte[1 << (Math.Min(codeLen, 24) + 1)];
        public byte[] Gr = new byte[8192];

        public int DecodeId(MqDecoder mq, int len)
        {
            var prev = 1;
            for (var i = 0; i < len; i++) prev = prev << 1 | mq.Decode(Iaid, prev);
            return prev - (1 << len);
        }
    }

    sealed class TextParams
    {
        public bool Huff, Refine, Transposed, FixedSymCodes;
        public byte[]? GrCtx;
        public int W, H, NumInstances, LogStrips, RefCorner, CombOp, DefPixel, DsOffset, RTemplate, CodeLen;
        public sbyte[] RAt = [-1, -1, -1, -1];
        public Bitmap[] Syms = [];
        public Jb2HuffTable? FS, DS, DT, RDW, RDH, RDX, RDY, RSize, SymCodes;
    }

    sealed class State
    {
        public byte[] Data = [];
        public readonly Dictionary<int, object> Results = [];
        public Bitmap? Page;
        public int PageDefault;
        public bool UnknownHeight;
        public int PageNum = -1, IgnoredPage = -1;
    }

    static readonly int[] SltpGeneric = [0x9B25, 0x0795, 0x00E5, 0x0195];

    /// <summary>Decodes an embedded JBIG2 stream (plus optional globals). Returns packed rows of <paramref name="width"/>x<paramref name="height"/> with 0 = black (PDF convention), or null.</summary>
    public static byte[]? Decode(byte[] data, byte[]? globals, int width, int height, out string? warning)
    {
        warning = null;
        var st = new State();
        try
        {
            if (globals is { Length: > 0 }) Process(st, globals);
            Process(st, data);
        }
        catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException or OverflowException or NotSupportedException or OutOfMemoryException)
        {
            warning = "JBIG2: " + e.Message;
        }
        if (st.Page is null) return null;
        var stride = (width + 7) / 8;
        var res = new byte[stride * height];
        Array.Fill(res, (byte)0xFF);
        var pg = st.Page;
        for (var y = 0; y < Math.Min(height, pg.H); y++)
        {
            var row = pg.D.AsSpan(y * pg.W, pg.W);
            var o = y * stride;
            for (var x = 0; x < Math.Min(width, pg.W); x++)
                if (row[x] != 0) res[o + (x >> 3)] &= (byte)~(0x80 >> (x & 7));
        }
        return res;
    }

    static uint U32(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
    static int I32(byte[] d, int o) => (int)U32(d, o);
    static int U16(byte[] d, int o) => d[o] << 8 | d[o + 1];

    static void Process(State st, byte[] d)
    {
        st.Data = d;
        var pos = 0;
        var fileSequential = true;
        var segs = new List<Seg>();
        if (d.Length > 9 && d[0] == 0x97 && d[1] == 0x4A && d[2] == 0x42 && d[3] == 0x32 && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A)
        {
            var fl = d[8]; pos = 9;
            fileSequential = (fl & 1) != 0;
            if ((fl & 2) == 0) pos += 4;
        }
        if (fileSequential)
        {
            while (pos < d.Length)
            {
                var s = ParseHeader(d, ref pos);
                if (s is null) break;
                s.Off = pos;
                if (s.UnknownLen) s.Len = FindGenericEnd(d, pos);
                if ((long)pos + s.Len > d.Length) s.Len = d.Length - pos;
                pos += s.Len;
                if (s.Type == 51) break;
                segs.Add(s);
            }
        }
        else
        {
            while (pos < d.Length)
            {
                var s = ParseHeader(d, ref pos);
                if (s is null) break;
                segs.Add(s);
                if (s.Type == 51) break;
            }
            foreach (var s in segs)
            {
                s.Off = pos;
                if (s.UnknownLen) s.Len = FindGenericEnd(d, pos);
                if ((long)pos + s.Len > d.Length) s.Len = d.Length - pos;
                pos += s.Len;
            }
        }
        Exception? first = null;
        foreach (var s in segs)
        {
            try { Handle(st, s); }
            catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException or OverflowException or NotSupportedException)
            {
                first ??= e;
            }
        }
        if (first is not null && st.Page is null) throw first;
    }

    static Seg? ParseHeader(byte[] d, ref int pos)
    {
        if (pos + 11 > d.Length) return null;
        var s = new Seg { Num = I32(d, pos) };
        var flags = d[pos + 4];
        s.Type = flags & 63;
        var bigPage = (flags & 0x40) != 0;
        pos += 5;
        int count = d[pos] >> 5;
        if (count == 7)
        {
            count = (int)(U32(d, pos) & 0x1FFFFFFF);
            pos += 4 + (count + 8) / 8;
        }
        else pos++;
        if (count < 0 || count > 100_000) return null;
        var sz = s.Num <= 256 ? 1 : s.Num <= 65536 ? 2 : 4;
        s.Refs = new int[count];
        for (var i = 0; i < count; i++)
        {
            s.Refs[i] = sz == 1 ? d[pos] : sz == 2 ? U16(d, pos) : I32(d, pos);
            pos += sz;
        }
        if (bigPage) { s.Page = I32(d, pos); pos += 4; } else { s.Page = d[pos]; pos++; }
        var len = U32(d, pos); pos += 4;
        if (len == 0xFFFFFFFF) s.UnknownLen = true; else s.Len = len > int.MaxValue ? int.MaxValue : (int)len;
        return s;
    }

    // Unknown-length immediate generic region: the data ends with an end marker followed by a 4-byte row count.
    static int FindGenericEnd(byte[] d, int start)
    {
        var mmr = (d[start + 17] & 1) != 0;
        byte m0 = mmr ? (byte)0x00 : (byte)0xFF, m1 = mmr ? (byte)0x00 : (byte)0xAC;
        for (var i = start + 18; i + 1 < d.Length; i++)
            if (d[i] == m0 && d[i + 1] == m1) return Math.Min(d.Length - start, i + 2 + 4 - start);
        return d.Length - start;
    }

    static void Handle(State st, Seg s)
    {
        var d = st.Data;
        var o = s.Off; var end = s.Off + s.Len;
        if (s.Page != 0 && s.Page == st.IgnoredPage) return;
        switch (s.Type)
        {
            case 48:
                {
                    if (st.Page is not null && s.Page != st.PageNum) { st.IgnoredPage = s.Page; break; }
                    var w = (int)U32(d, o); var h = U32(d, o + 4);
                    var unknown = h == 0xFFFFFFFF;
                    st.UnknownHeight = unknown;
                    st.PageNum = s.Page;
                    st.PageDefault = d[o + 16] >> 2 & 1;
                    st.Page = NewBitmap(w, unknown ? 0 : (int)h);
                    if (st.PageDefault != 0) Array.Fill(st.Page.D, (byte)1);
                    break;
                }
            case 50:
                if (st.UnknownHeight && st.Page is not null && s.Len >= 4) GrowPage(st, (long)U32(d, o) + 1);
                break;
            case 53: st.Results[s.Num] = Jb2HuffTable.Parse(d, o, end); break;
            case 0: st.Results[s.Num] = DecodeSymbolDict(st, s); break;
            case 16: st.Results[s.Num] = DecodePatternDict(st, s); break;
            case 4 or 6 or 7: Region(st, s, DecodeTextSeg(st, s)); break;
            case 20 or 22 or 23: Region(st, s, DecodeHalftoneSeg(st, s)); break;
            case 36 or 38 or 39: Region(st, s, DecodeGenericSeg(st, s)); break;
            case 40 or 42 or 43: Region(st, s, DecodeRefineSeg(st, s)); break;
        }
    }

    // Striped pages of unknown height grow as stripes arrive; end-of-stripe segments fix the final row count.
    static void GrowPage(State st, long rows)
    {
        var pg = st.Page!;
        if (rows <= pg.H) return;
        if (rows > int.MaxValue) throw new InvalidOperationException("bad JBIG2 page height");
        var np = NewBitmap(pg.W, (int)rows);
        if (st.PageDefault != 0) Array.Fill(np.D, (byte)1);
        pg.D.CopyTo(np.D, 0);
        st.Page = np;
    }

    static void Region(State st, Seg s, (Bitmap Bm, int X, int Y, int Op) r)
    {
        if (s.Type is 4 or 20 or 36 or 40) { st.Results[s.Num] = r.Bm; return; }
        var pg = st.Page ?? throw new InvalidOperationException("region before page info");
        if (st.UnknownHeight && r.Y >= 0 && (long)r.Y + r.Bm.H > pg.H) { GrowPage(st, (long)r.Y + r.Bm.H); pg = st.Page!; }
        Compose(pg, r.Bm, r.X, r.Y, r.Op);
    }

    static void Compose(Bitmap dst, Bitmap src, int x0, int y0, int op)
    {
        var xs = Math.Max(0, -x0); var xe = Math.Min(src.W, dst.W - x0);
        if (xe <= xs) return;
        for (var y = Math.Max(0, -y0); y < src.H && y0 + y < dst.H; y++)
        {
            var s = src.D.AsSpan(y * src.W + xs, xe - xs);
            var t = dst.D.AsSpan((y0 + y) * dst.W + x0 + xs, xe - xs);
            switch (op)
            {
                case 0: for (var i = 0; i < s.Length; i++) t[i] |= s[i]; break;
                case 1: for (var i = 0; i < s.Length; i++) t[i] &= s[i]; break;
                case 2: for (var i = 0; i < s.Length; i++) t[i] ^= s[i]; break;
                case 3: for (var i = 0; i < s.Length; i++) t[i] = (byte)(t[i] ^ s[i] ^ 1); break;
                default: s.CopyTo(t); break;
            }
        }
    }

    static (int W, int H, int X, int Y, int Op) RegionInfo(byte[] d, int o) =>
        (I32(d, o), I32(d, o + 4), I32(d, o + 8), I32(d, o + 12), d[o + 16] & 7);

    static Bitmap NewBitmap(int w, int h)
    {
        if (w < 0 || h < 0 || (long)w * h > 100_000_000) throw new InvalidOperationException("bad JBIG2 bitmap size");
        return new Bitmap(w, h);
    }

    // ---------------------------------------------------------------- generic region

    static (Bitmap, int, int, int) DecodeGenericSeg(State st, Seg s)
    {
        var d = st.Data; var o = s.Off; var end = o + s.Len;
        var (w, h, x, y, op) = RegionInfo(d, o);
        var colour = (d[o + 16] & 8) != 0;
        if (colour) { op = 0; if (!s.UnknownLen && end - o > 21) end -= 4; }
        o += 17;
        var flags = d[o++];
        var mmr = (flags & 1) != 0; var tmpl = flags >> 1 & 3; var tpgd = (flags & 8) != 0;
        var ext = !mmr && tmpl == 0 && (flags & 0x10) != 0;
        if (s.UnknownLen) { h = I32(d, end - 4); end -= 4; }
        var bm = NewBitmap(w, h);
        if (mmr) { DecodeMmr(d, o, end, bm); return (bm, x, y, op); }
        var at = ReadAt(d, ref o, ext ? 12 : tmpl == 0 ? 4 : 1);
        var mq = new MqDecoder(d, o, end);
        if (ext) DecodeGenericExt(mq, new byte[65536], bm, tpgd, at);
        else DecodeGeneric(mq, new byte[65536], bm, tmpl, tpgd, at, null);
        return (bm, x, y, op);
    }

    // Bit of the 16-bit context occupied by each extended-template AT pixel A1..A12, i.e. the bit of the pixel at its nominal position (T.88 Amd.2 Figure 3(b), shared with the 4-AT layout).
    static readonly int[] ExtAtBit = [1, 13, 9, 14, 12, 5, 2, 3, 11, 4, 15, 10];

    /// <summary>T.88 Amd.2 generic region decoding with GBTEMPLATE 0 and EXTTEMPLATE 1 (four fixed pixels plus twelve adaptive ones); separate from <see cref="DecodeGeneric"/> to keep that hot loop untouched.</summary>
    static void DecodeGenericExt(MqDecoder mq, byte[] cx, Bitmap bm, bool tpgd, sbyte[] at)
    {
        int w = bm.W, h = bm.H;
        var D = bm.D;
        var ltp = 0;
        for (var y = 0; y < h; y++)
        {
            if (tpgd)
            {
                ltp ^= mq.Decode(cx, SltpGeneric[0]);
                if (ltp != 0)
                {
                    if (y > 0) Array.Copy(D, (y - 1) * w, D, y * w, w);
                    continue;
                }
            }
            for (var x = 0; x < w; x++)
            {
                var c = Px(bm, x - 1, y) | Px(bm, x + 1, y - 1) << 6 | Px(bm, x, y - 1) << 7 | Px(bm, x - 1, y - 1) << 8;
                for (var k = 0; k < 12; k++) c |= Px(bm, x + at[2 * k], y + at[2 * k + 1]) << ExtAtBit[k];
                D[y * w + x] = (byte)mq.Decode(cx, c);
            }
        }
    }

    static sbyte[] ReadAt(byte[] d, ref int o, int pairs)
    {
        var at = new sbyte[pairs * 2];
        for (var i = 0; i < at.Length; i++) at[i] = (sbyte)d[o++];
        return at;
    }

    static void DecodeMmr(byte[] d, int o, int end, Bitmap bm)
    {
        var rows = CcittDecoder.Decode(d.AsSpan(o, end - o).ToArray(), -1, bm.W, bm.H, true, false, bm.H);
        var stride = (bm.W + 7) / 8;
        for (var y = 0; y < bm.H; y++)
            for (var x = 0; x < bm.W; x++)
                bm.D[y * bm.W + x] = (byte)(rows[y * stride + (x >> 3)] >> (7 - (x & 7)) & 1);
    }

    /// <summary>Arithmetic generic region decoding procedure (T.88 6.2) for templates 0-3.</summary>
    static void DecodeGeneric(MqDecoder mq, byte[] cx, Bitmap bm, int tmpl, bool tpgd, sbyte[] at, Bitmap? skip)
    {
        int w = bm.W, h = bm.H;
        var zero = new byte[w + 4];
        // Template geometry: bits of row 0, row -1 window (lookahead/width/shift), row -2 window.
        int n0, l1, n1, s1, l2, n2, s2;
        Span<int> atBit = stackalloc int[4];
        switch (tmpl)
        {
            case 0: n0 = 4; l1 = 2; n1 = 5; s1 = 5; l2 = 1; n2 = 3; s2 = 12; atBit[0] = 4; atBit[1] = 10; atBit[2] = 11; atBit[3] = 15; break;
            case 1: n0 = 3; l1 = 2; n1 = 5; s1 = 4; l2 = 2; n2 = 4; s2 = 9; atBit[0] = 3; break;
            case 2: n0 = 2; l1 = 1; n1 = 4; s1 = 3; l2 = 1; n2 = 3; s2 = 7; atBit[0] = 2; break;
            default: n0 = 4; l1 = 1; n1 = 5; s1 = 5; l2 = 0; n2 = 0; s2 = 0; atBit[0] = 4; break;
        }
        var nat = tmpl == 0 ? 4 : 1;
        int m0 = (1 << n0) - 1, m1 = (1 << n1) - 1, m2 = (1 << n2) - 1;
        var ltp = 0;
        var D = bm.D;
        for (var y = 0; y < h; y++)
        {
            if (tpgd)
            {
                ltp ^= mq.Decode(cx, SltpGeneric[tmpl]);
                if (ltp != 0)
                {
                    if (y > 0) Array.Copy(D, (y - 1) * w, D, y * w, w);
                    continue;
                }
            }
            var row = D.AsSpan(y * w, w);
            var r1 = y >= 1 ? D.AsSpan((y - 1) * w, w) : zero.AsSpan(0, w);
            var r2 = y >= 2 ? D.AsSpan((y - 2) * w, w) : zero.AsSpan(0, w);
            int w0 = 0, w1 = 0, w2 = 0;
            // Windows hold pixels up to x+l-1 before the lookahead pixel x+l is shifted in.
            w1 = Prime(r1, l1, w);
            w2 = Prime(r2, l2, w);
            for (var x = 0; x < w; x++)
            {
                w1 = (w1 << 1 | (x + l1 < w ? r1[x + l1] : 0)) & m1;
                if (n2 > 0) w2 = (w2 << 1 | (x + l2 < w ? r2[x + l2] : 0)) & m2;
                var c = w0 | w1 << s1 | w2 << s2;
                for (var k = 0; k < nat; k++)
                {
                    int ax = x + at[2 * k], ay = y + at[2 * k + 1];
                    if ((uint)ax < (uint)w && (uint)ay < (uint)h && D[ay * w + ax] != 0) c |= 1 << atBit[k];
                }
                int bit;
                if (skip is not null && skip.D[y * w + x] != 0) bit = 0;
                else bit = mq.Decode(cx, c);
                row[x] = (byte)bit;
                w0 = (w0 << 1 | bit) & m0;
            }
        }
    }

    static int Prime(ReadOnlySpan<byte> r, int l, int w)
    {
        var v = 0;
        for (var i = 0; i < l; i++) v = v << 1 | (i < w ? r[i] : 0);
        return v;
    }

    // ---------------------------------------------------------------- refinement

    static (Bitmap, int, int, int) DecodeRefineSeg(State st, Seg s)
    {
        var d = st.Data; var o = s.Off; var end = o + s.Len;
        var (w, h, x, y, op) = RegionInfo(d, o);
        o += 17;
        var flags = d[o++];
        var tmpl = flags & 1; var tpgr = (flags & 2) != 0;
        var at = tmpl == 0 ? ReadAt(d, ref o, 2) : [-1, -1, -1, -1];
        Bitmap? refBm = null;
        foreach (var r in s.Refs) if (st.Results.TryGetValue(r, out var obj) && obj is Bitmap b) refBm = b;
        int dx = 0, dy = 0;
        if (refBm is null)
        {
            var pg = st.Page ?? throw new InvalidOperationException("refinement without reference");
            refBm = NewBitmap(w, h);
            for (var yy = 0; yy < h; yy++)
                for (var xx = 0; xx < w; xx++)
                    if ((uint)(x + xx) < (uint)pg.W && (uint)(y + yy) < (uint)pg.H) refBm.D[yy * w + xx] = pg.D[(y + yy) * pg.W + x + xx];
        }
        var bm = NewBitmap(w, h);
        var mq = new MqDecoder(d, o, end);
        DecodeRefinement(mq, new byte[8192], bm, refBm, dx, dy, tmpl, tpgr, at);
        return (bm, x, y, op);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Px(Bitmap b, int x, int y) => (uint)x < (uint)b.W && (uint)y < (uint)b.H ? b.D[y * b.W + x] : 0;

    static void DecodeRefinement(MqDecoder mq, byte[] cx, Bitmap bm, Bitmap rf, int dx, int dy, int tmpl, bool tpgr, sbyte[] at)
    {
        var ltp = 0;
        var sltp = tmpl == 0 ? 0x100 : 0x80;
        for (var y = 0; y < bm.H; y++)
        {
            if (tpgr) ltp ^= mq.Decode(cx, sltp);
            for (var x = 0; x < bm.W; x++)
            {
                int rx = x - dx, ry = y - dy;
                if (ltp != 0)
                {
                    var v = Px(rf, rx, ry);
                    if (Px(rf, rx - 1, ry - 1) == v && Px(rf, rx, ry - 1) == v && Px(rf, rx + 1, ry - 1) == v &&
                        Px(rf, rx - 1, ry) == v && Px(rf, rx + 1, ry) == v &&
                        Px(rf, rx - 1, ry + 1) == v && Px(rf, rx, ry + 1) == v && Px(rf, rx + 1, ry + 1) == v)
                    {
                        bm.D[y * bm.W + x] = (byte)v;
                        continue;
                    }
                }
                int c;
                if (tmpl == 0)
                {
                    c = Px(bm, x - 1, y) | Px(bm, x + 1, y - 1) << 1 | Px(bm, x, y - 1) << 2 | Px(bm, x + at[0], y + at[1]) << 3
                        | Px(rf, rx + 1, ry + 1) << 4 | Px(rf, rx, ry + 1) << 5 | Px(rf, rx - 1, ry + 1) << 6
                        | Px(rf, rx + 1, ry) << 7 | Px(rf, rx, ry) << 8 | Px(rf, rx - 1, ry) << 9
                        | Px(rf, rx + 1, ry - 1) << 10 | Px(rf, rx, ry - 1) << 11 | Px(rf, rx + at[2], ry + at[3]) << 12;
                }
                else
                {
                    c = Px(bm, x - 1, y) | Px(bm, x + 1, y - 1) << 1 | Px(bm, x, y - 1) << 2 | Px(bm, x - 1, y - 1) << 3
                        | Px(rf, rx + 1, ry + 1) << 4 | Px(rf, rx, ry + 1) << 5 | Px(rf, rx + 1, ry) << 6
                        | Px(rf, rx, ry) << 7 | Px(rf, rx - 1, ry) << 8 | Px(rf, rx, ry - 1) << 9;
                }
                bm.D[y * bm.W + x] = (byte)mq.Decode(cx, c);
            }
        }
    }

    // ---------------------------------------------------------------- symbol dictionary

    static SymDict DecodeSymbolDict(State st, Seg s)
    {
        var d = st.Data; var o = s.Off; var end = o + s.Len;
        var flags = U16(d, o); o += 2;
        var huff = (flags & 1) != 0; var refAgg = (flags & 2) != 0;
        var selDh = flags >> 2 & 3; var selDw = flags >> 4 & 3; var selBm = flags >> 6 & 1; var selAgg = flags >> 7 & 1;
        var ctxUsed = (flags & 0x100) != 0; var ctxRetained = (flags & 0x200) != 0;
        var tmpl = flags >> 10 & 3; var rtmpl = flags >> 12 & 1;
        sbyte[] at = [3, -1, -3, -1, 2, -2, -2, -2], rat = [-1, -1, -1, -1];
        if (!huff) at = ReadAt(d, ref o, tmpl == 0 ? 4 : 1);
        if (refAgg && rtmpl == 0) rat = ReadAt(d, ref o, 2);
        var numEx = (int)U32(d, o); var numNew = (int)U32(d, o + 4); o += 8;
        if (numNew < 0 || numNew > 10_000_000 || numEx < 0) throw new InvalidOperationException("bad symbol count");

        var inSyms = new List<Bitmap>();
        var tables = new List<Jb2HuffTable>();
        SymDict? lastDict = null;
        foreach (var r in s.Refs)
        {
            if (!st.Results.TryGetValue(r, out var obj)) continue;
            if (obj is SymDict sd) { inSyms.AddRange(sd.Exported); lastDict = sd; }
            else if (obj is Jb2HuffTable t) tables.Add(t);
        }
        var ti = 0;
        Jb2HuffTable Pick(int sel, params int[] std) => sel < std.Length && std[sel] > 0 ? Jb2HuffTable.Standard(std[sel]) : tables[ti++];
        Jb2HuffTable? tDh = null, tDw = null, tBm = null, tAgg = null;
        if (huff)
        {
            tDh = Pick(selDh, 4, 5, -1, -1); tDw = Pick(selDw, 2, 3, -1, -1);
            tBm = selBm == 0 ? Jb2HuffTable.Standard(1) : tables[ti++];
            tAgg = selAgg == 0 ? Jb2HuffTable.Standard(1) : tables[ti++];
        }

        var numIn = inSyms.Count;
        var codeLen = 0;
        while ((1L << codeLen) < numIn + (long)numNew) codeLen++;
        if (huff) codeLen = Math.Max(codeLen, 1);
        var all = new Bitmap[numIn + numNew];
        inSyms.CopyTo(all);

        MqDecoder? mq = null; Jb2BitReader? br = null;
        var gb = new byte[65536]; var gr = new byte[8192];
        if (ctxUsed && lastDict is { Gb: not null }) { gb = (byte[])lastDict.Gb.Clone(); if (lastDict.Gr is not null) gr = (byte[])lastDict.Gr.Clone(); }
        var iadh = new IntDec(); var iadw = new IntDec(); var iaex = new IntDec(); var iaai = new IntDec();
        TextArith? ta = null;
        if (huff) br = new Jb2BitReader(d, o, end);
        else { mq = new MqDecoder(d, o, end); ta = new TextArith(codeLen) { Gr = gr }; }

        var widths = new int[numNew];
        int hcHeight = 0, done = 0;
        long symPixels = 0;
        while (done < numNew)
        {
            int dh;
            if (huff) { if (!tDh!.Decode(br!, out dh)) throw new InvalidOperationException("OOB height"); }
            else if (!iadh.Decode(mq!, out dh)) throw new InvalidOperationException("OOB height");
            hcHeight += dh;
            if (hcHeight < 0) throw new InvalidOperationException("bad height class");
            int symW = 0, totW = 0, firstInClass = done;
            while (true)
            {
                int dw;
                bool ok = huff ? tDw!.Decode(br!, out dw) : iadw.Decode(mq!, out dw);
                if (!ok) break;
                if (done >= numNew) throw new InvalidOperationException("too many symbols");
                symW += dw; totW += symW;
                if (symW < 0) throw new InvalidOperationException("bad symbol width");
                symPixels += (long)symW * hcHeight;
                if (symPixels > 50_000_000) throw new InvalidOperationException("symbol dictionary too large");
                if (!huff || refAgg)
                {
                    var bm = NewBitmap(symW, hcHeight);
                    if (!refAgg) DecodeGeneric(mq!, gb, bm, tmpl, false, at, null);
                    else
                    {
                        int ninst;
                        if (huff) { if (!tAgg!.Decode(br!, out ninst)) throw new InvalidOperationException("OOB aggregate instance count"); }
                        else iaai.Decode(mq!, out ninst);
                        if (ninst is < 1 or > (1 << 20)) throw new InvalidOperationException("bad aggregate instance count");
                        if (ninst > 1)
                        {
                            var tp = new TextParams
                            {
                                Huff = huff, Refine = true, W = symW, H = hcHeight, NumInstances = ninst, RefCorner = 1, RTemplate = rtmpl, RAt = rat,
                                CodeLen = codeLen, Syms = all, CombOp = 0, GrCtx = gr, FixedSymCodes = huff,
                            };
                            if (huff)
                            {
                                tp.FS = Jb2HuffTable.Standard(6); tp.DS = Jb2HuffTable.Standard(8); tp.DT = Jb2HuffTable.Standard(11);
                                tp.RDW = tp.RDH = tp.RDX = tp.RDY = Jb2HuffTable.Standard(15); tp.RSize = Jb2HuffTable.Standard(1);
                            }
                            bm = DecodeText(tp, d, mq, ta, br);
                        }
                        else if (huff)
                        {
                            var id = br!.ReadBits(codeLen);
                            var h15 = Jb2HuffTable.Standard(15);
                            if (!h15.Decode(br, out var rdx) || !h15.Decode(br, out var rdy) || !Jb2HuffTable.Standard(1).Decode(br, out var size))
                                throw new InvalidOperationException("OOB refinement data");
                            br.Align();
                            if ((uint)id >= (uint)(numIn + done) || size < 0 || br.Pos + size > end) throw new InvalidOperationException("bad refinement symbol");
                            var ibo = all[id] ?? throw new InvalidOperationException("bad symbol id");
                            DecodeRefinement(new MqDecoder(d, br.Pos, br.Pos + size), gr, bm, ibo, rdx, rdy, rtmpl, false, rat);
                            br.Pos += size;
                        }
                        else
                        {
                            var id = ta!.DecodeId(mq!, codeLen);
                            ta.Rdx.Decode(mq!, out var rdx); ta.Rdy.Decode(mq!, out var rdy);
                            var ibo = all[id] ?? throw new InvalidOperationException("bad symbol id");
                            DecodeRefinement(mq!, gr, bm, ibo, rdx, rdy, rtmpl, false, rat);
                        }
                    }
                    all[numIn + done] = bm;
                }
                else widths[done] = symW;
                done++;
            }
            if (huff && !refAgg)
            {
                tBm!.Decode(br!, out var bmSize);
                br!.Align();
                var coll = NewBitmap(totW, hcHeight);
                if (bmSize == 0)
                {
                    var stride = (totW + 7) / 8;
                    for (var y = 0; y < hcHeight; y++)
                        for (var x = 0; x < totW; x++)
                            coll.D[y * totW + x] = (byte)(d[br.Pos + y * stride + (x >> 3)] >> (7 - (x & 7)) & 1);
                    br.Pos += stride * hcHeight;
                }
                else { DecodeMmr(d, br.Pos, br.Pos + bmSize, coll); br.Pos += bmSize; }
                var xo = 0;
                for (var i = firstInClass; i < done; i++)
                {
                    var sb = NewBitmap(widths[i], hcHeight);
                    for (var y = 0; y < hcHeight; y++) Array.Copy(coll.D, y * totW + xo, sb.D, y * widths[i], widths[i]);
                    all[numIn + i] = sb; xo += widths[i];
                }
            }
        }

        // Export flags (6.5.10)
        var export = new List<Bitmap>();
        int idx = 0; var cur = false;
        var total = numIn + numNew;
        while (idx < total)
        {
            int run;
            if (huff) Jb2HuffTable.Standard(1).Decode(br!, out run); else iaex.Decode(mq!, out run);
            if (run < 0 || idx + run > total) throw new InvalidOperationException("bad export run");
            if (cur) for (var i = 0; i < run; i++) export.Add(all[idx + i]);
            idx += run; cur = !cur;
        }
        return new SymDict { Exported = export.ToArray(), Gb = ctxRetained ? gb : null, Gr = ctxRetained ? gr : null };
    }

    // ---------------------------------------------------------------- text region

    static (Bitmap, int, int, int) DecodeTextSeg(State st, Seg s)
    {
        var d = st.Data; var o = s.Off; var end = o + s.Len;
        var (w, h, x, y, op) = RegionInfo(d, o);
        if ((d[o + 16] & 8) != 0)
        {
            // Amd.3 colour section: trailing T.45 colour palette IDs whose total length (including this 4-byte field) is in the last four bytes.
            op = 0;
            var sect = end - o > 25 ? U32(d, end - 4) : 0;
            if (sect >= 4 && sect <= end - o - 21) end -= (int)sect;
        }
        o += 17;
        var f = U16(d, o); o += 2;
        var tp = new TextParams
        {
            W = w, H = h,
            Huff = (f & 1) != 0, Refine = (f & 2) != 0, LogStrips = f >> 2 & 3, RefCorner = f >> 4 & 3, Transposed = (f & 0x40) != 0,
            CombOp = f >> 7 & 3, DefPixel = f >> 9 & 1, DsOffset = (f >> 10 & 31) - ((f >> 14 & 1) << 5), RTemplate = f >> 15 & 1,
        };
        var hf = 0;
        if (tp.Huff) { hf = U16(d, o); o += 2; }
        if (tp.Refine && tp.RTemplate == 0) tp.RAt = ReadAt(d, ref o, 2);
        tp.NumInstances = (int)U32(d, o); o += 4;

        var syms = new List<Bitmap>(); var tables = new List<Jb2HuffTable>();
        foreach (var r in s.Refs)
        {
            if (!st.Results.TryGetValue(r, out var obj)) continue;
            if (obj is SymDict sd) syms.AddRange(sd.Exported);
            else if (obj is Jb2HuffTable t) tables.Add(t);
        }
        tp.Syms = syms.ToArray();
        var codeLen = 0;
        while ((1L << codeLen) < tp.Syms.Length) codeLen++;
        tp.CodeLen = codeLen;
        var bm = NewBitmap(w, h);

        if (tp.Huff)
        {
            var ti = 0;
            Jb2HuffTable Pick(int sel, params int[] std) => sel < std.Length && std[sel] > 0 ? Jb2HuffTable.Standard(std[sel]) : tables[ti++];
            tp.FS = Pick(hf & 3, 6, 7, -1, -1); tp.DS = Pick(hf >> 2 & 3, 8, 9, 10, -1); tp.DT = Pick(hf >> 4 & 3, 11, 12, 13, -1);
            tp.RDW = Pick(hf >> 6 & 3, 14, 15, -1, -1); tp.RDH = Pick(hf >> 8 & 3, 14, 15, -1, -1);
            tp.RDX = Pick(hf >> 10 & 3, 14, 15, -1, -1); tp.RDY = Pick(hf >> 12 & 3, 14, 15, -1, -1);
            tp.RSize = (hf >> 14 & 1) == 0 ? Jb2HuffTable.Standard(1) : tables[ti++];
            var br = new Jb2BitReader(d, o, end);
            // Symbol ID code table (7.4.3.1.7)
            var runCodes = new Jb2HuffTable();
            for (var i = 0; i < 35; i++) runCodes.Add(br.ReadBits(4), 0, i);
            runCodes.Build();
            var sid = new Jb2HuffTable();
            var n = 0; var lastLen = 0;
            var lens = new int[tp.Syms.Length];
            while (n < lens.Length)
            {
                runCodes.Decode(br, out var code);
                if (code < 32) { lens[n++] = lastLen = code; continue; }
                int rep, val = 0;
                if (code == 32) { rep = 3 + br.ReadBits(2); val = lastLen; }
                else if (code == 33) rep = 3 + br.ReadBits(3);
                else rep = 11 + br.ReadBits(7);
                for (var i = 0; i < rep && n < lens.Length; i++) lens[n++] = val;
                if (code != 32) lastLen = 0;
            }
            br.Align();
            for (var i = 0; i < lens.Length; i++) sid.Add(lens[i], 0, i);
            tp.SymCodes = sid.Build();
            var res = DecodeText(tp, d, null, null, br);
            return (res, x, y, op);
        }
        var mq = new MqDecoder(d, o, end);
        return (DecodeText(tp, d, mq, new TextArith(codeLen), null), x, y, op);
    }

    static Bitmap DecodeText(TextParams p, byte[] data, MqDecoder? mq, TextArith? ta, Jb2BitReader? br)
    {
        var bm = NewBitmap(p.W, p.H);
        if (p.DefPixel != 0) Array.Fill(bm.D, (byte)1);
        var strips = 1 << p.LogStrips;
        byte[]? huffGr = p.Huff && p.Refine ? p.GrCtx ?? new byte[8192] : null;

        bool Read(IntDec? ia, Jb2HuffTable? ht, out int v) => p.Huff ? ht!.Decode(br!, out v) : ia!.Decode(mq!, out v);

        Read(ta?.Dt, p.DT, out var dt0);
        long stripT = -(long)dt0 * strips;
        long firstS = 0;
        var n = 0;
        while (n < p.NumInstances)
        {
            if (!Read(ta?.Dt, p.DT, out var dt)) throw new InvalidOperationException("OOB strip delta");
            stripT += (long)dt * strips;
            Read(ta?.Fs, p.FS, out var dfs);
            firstS += dfs;
            var curS = firstS;
            var first = true;
            while (true)
            {
                if (!first)
                {
                    if (!Read(ta?.Ds, p.DS, out var ids)) break;
                    curS += ids + p.DsOffset;
                }
                first = false;
                if (n >= p.NumInstances) break;
                var curT = 0;
                if (strips > 1)
                {
                    if (p.Huff) curT = br!.ReadBits(p.LogStrips);
                    else ta!.It.Decode(mq!, out curT);
                }
                var ti = stripT + curT;
                int id;
                if (p.Huff)
                {
                    if (p.FixedSymCodes) id = br!.ReadBits(p.CodeLen);
                    else if (!p.SymCodes!.Decode(br!, out id)) throw new InvalidOperationException("OOB symbol");
                }
                else id = ta!.DecodeId(mq!, p.CodeLen);
                if ((uint)id >= (uint)p.Syms.Length) throw new InvalidOperationException("bad symbol id");
                var ib = p.Syms[id] ?? throw new InvalidOperationException("undefined symbol");
                var ri = 0;
                if (p.Refine)
                {
                    if (p.Huff) ri = br!.ReadBit(); else ta!.Ri.Decode(mq!, out ri);
                }
                if (ri != 0)
                {
                    Read(ta?.Rdw, p.RDW, out var rdw); Read(ta?.Rdh, p.RDH, out var rdh);
                    Read(ta?.Rdx, p.RDX, out var rdx); Read(ta?.Rdy, p.RDY, out var rdy);
                    var rb = NewBitmap(ib.W + rdw, ib.H + rdh);
                    var dx = (rdw >> 1) + rdx; var dy = (rdh >> 1) + rdy;
                    if (p.Huff)
                    {
                        p.RSize!.Decode(br!, out var size);
                        br!.Align();
                        var rmq = new MqDecoder(data, br.Pos, br.Pos + size);
                        DecodeRefinement(rmq, huffGr!, rb, ib, dx, dy, p.RTemplate, false, p.RAt);
                        br.Pos += size;
                    }
                    else DecodeRefinement(mq!, ta!.Gr, rb, ib, dx, dy, p.RTemplate, false, p.RAt);
                    ib = rb;
                }
                int wi = ib.W, hi = ib.H;
                if (!p.Transposed && p.RefCorner is 2 or 3) curS += wi - 1;
                else if (p.Transposed && p.RefCorner is 0 or 2) curS += hi - 1;
                var si = curS;
                long px, py;
                if (!p.Transposed)
                {
                    px = p.RefCorner is 2 or 3 ? si - wi + 1 : si;
                    py = p.RefCorner is 0 or 2 ? ti - hi + 1 : ti;
                }
                else
                {
                    px = p.RefCorner is 2 or 3 ? ti - wi + 1 : ti;
                    py = p.RefCorner is 0 or 2 ? si - hi + 1 : si;
                }
                if (px > int.MinValue / 2 && px < int.MaxValue / 2 && py > int.MinValue / 2 && py < int.MaxValue / 2)
                    Compose(bm, ib, (int)px, (int)py, p.CombOp);
                if (!p.Transposed && p.RefCorner is 0 or 1) curS += wi - 1;
                else if (p.Transposed && p.RefCorner is 1 or 3) curS += hi - 1;
                n++;
            }
        }
        return bm;
    }

    // ---------------------------------------------------------------- pattern dictionary and halftone

    static Bitmap[] DecodePatternDict(State st, Seg s)
    {
        var d = st.Data; var o = s.Off; var end = o + s.Len;
        var flags = d[o]; var pw = d[o + 1]; var ph = d[o + 2]; var grayMax = (int)U32(d, o + 3); o += 7;
        if (pw == 0 || ph == 0 || grayMax < 0 || grayMax > 65535) throw new InvalidOperationException("bad pattern dictionary");
        var mmr = (flags & 1) != 0; var tmpl = flags >> 1 & 3;
        var coll = NewBitmap((grayMax + 1) * pw, ph);
        if (mmr) DecodeMmr(d, o, end, coll);
        else
        {
            sbyte[] at = [(sbyte)-pw, 0, -3, -1, 2, -2, -2, -2];
            DecodeGeneric(new MqDecoder(d, o, end), new byte[65536], coll, tmpl, false, at, null);
        }
        var pats = new Bitmap[grayMax + 1];
        for (var i = 0; i <= grayMax; i++)
        {
            pats[i] = new Bitmap(pw, ph);
            for (var y = 0; y < ph; y++) Array.Copy(coll.D, y * coll.W + i * pw, pats[i].D, y * pw, pw);
        }
        return pats;
    }

    static (Bitmap, int, int, int) DecodeHalftoneSeg(State st, Seg s)
    {
        var d = st.Data; var o = s.Off; var end = o + s.Len;
        var (w, h, x, y, op) = RegionInfo(d, o);
        o += 17;
        var flags = d[o++];
        bool mmr = (flags & 1) != 0, enableSkip = (flags & 8) != 0;
        int tmpl = flags >> 1 & 3, combOp = flags >> 4 & 7, defPixel = flags >> 7 & 1;
        int gw = (int)U32(d, o), gh = (int)U32(d, o + 4), gx = I32(d, o + 8), gy = I32(d, o + 12);
        int rx = U16(d, o + 16), ry = U16(d, o + 18);
        o += 20;
        Bitmap[]? pats = null;
        foreach (var r in s.Refs) if (st.Results.TryGetValue(r, out var obj) && obj is Bitmap[] pa) pats = pa;
        if (pats is null || pats.Length == 0) throw new InvalidOperationException("halftone without patterns");
        var bm = NewBitmap(w, h);
        if (defPixel != 0) Array.Fill(bm.D, (byte)1);
        if (gw < 0 || gh < 0 || (long)gw * gh > 100_000_000) throw new InvalidOperationException("bad halftone grid");
        int pw = pats[0].W, ph = pats[0].H;

        Bitmap? skip = null;
        if (enableSkip)
        {
            skip = new Bitmap(gw, gh);
            for (var m = 0; m < gh; m++)
                for (var n = 0; n < gw; n++)
                {
                    var xx = (gx + (long)m * ry + (long)n * rx) >> 8;
                    var yy = (gy + (long)m * rx - (long)n * ry) >> 8;
                    if (xx + pw <= 0 || xx >= w || yy + ph <= 0 || yy >= h) skip.D[m * gw + n] = 1;
                }
        }
        var bpp = 0;
        while ((1 << bpp) < pats.Length) bpp++;
        var gray = new int[gw * gh];
        if (bpp > 0)
        {
            var planes = new Bitmap[bpp];
            MqDecoder? mq = null; byte[]? cx = null;
            var mmrPos = o;
            if (!mmr) { mq = new MqDecoder(d, o, end); cx = new byte[65536]; }
            sbyte[] at = [(sbyte)(tmpl <= 1 ? 3 : 2), -1, -3, -1, 2, -2, -2, -2];
            for (var j = bpp - 1; j >= 0; j--)
            {
                var pl = new Bitmap(gw, gh);
                if (mmr) mmrPos = DecodeMmrSeq(d, mmrPos, end, pl);
                else DecodeGeneric(mq!, cx!, pl, tmpl, false, at, skip);
                if (j < bpp - 1) for (var i = 0; i < pl.D.Length; i++) pl.D[i] ^= planes[j + 1].D[i];
                planes[j] = pl;
                for (var i = 0; i < pl.D.Length; i++) gray[i] |= pl.D[i] << j;
            }
        }
        for (var m = 0; m < gh; m++)
            for (var n = 0; n < gw; n++)
            {
                var xx = (gx + (long)m * ry + (long)n * rx) >> 8;
                var yy = (gy + (long)m * rx - (long)n * ry) >> 8;
                var g = Math.Min(gray[m * gw + n], pats.Length - 1);
                if (xx > -100_000 && xx < 100_000_000 && yy > -100_000 && yy < 100_000_000) Compose(bm, pats[g], (int)xx, (int)yy, combOp);
            }
        return (bm, x, y, op);
    }

    // MMR bitplane inside a sequence of planes: returns the offset after the plane (EOFB-terminated, byte aligned).
    static int DecodeMmrSeq(byte[] d, int o, int end, Bitmap bm)
    {
        var rows = CcittDecoder.Decode(d.AsSpan(o, end - o).ToArray(), -1, bm.W, bm.H, true, false, bm.H, out var consumed);
        var stride = (bm.W + 7) / 8;
        for (var y = 0; y < bm.H; y++)
            for (var x = 0; x < bm.W; x++)
                bm.D[y * bm.W + x] = (byte)(rows[y * stride + (x >> 3)] >> (7 - (x & 7)) & 1);
        return o + consumed;
    }
}
