using System.Runtime.CompilerServices;

namespace Pdf.Content;

/// <summary>A codeword segment of a code-block (a run of coding passes sharing one terminated bit stream).</summary>
internal sealed class JpxSeg
{
    public int Passes, MaxPasses, Len;
}

internal sealed class JpxCodeBlock
{
    public int X0, Y0, X1, Y1;
    public bool Included;
    public int Zbp, LBlock = 3, NumPasses;
    public byte[] Buf = [];
    public int BufLen;
    public readonly List<JpxSeg> Segs = [];

    public void Append(ReadOnlySpan<byte> s)
    {
        if (BufLen + s.Length > Buf.Length) Array.Resize(ref Buf, Math.Max(BufLen + s.Length, Buf.Length * 2));
        s.CopyTo(Buf.AsSpan(BufLen));
        BufLen += s.Length;
    }
}

/// <summary>EBCOT tier-1 code-block decoder (ITU-T T.800 Annex D). Coefficients are produced in half-units (value * 2).</summary>
internal sealed class JpxTier1
{
    const int SigNE = 1, SigSE = 2, SigSW = 4, SigNW = 8, SigN = 0x10, SigE = 0x20, SigS = 0x40, SigW = 0x80;
    const int SgnN = 0x100, SgnE = 0x200, SgnS = 0x400, SgnW = 0x800;
    const int Sig = 0x1000, Refine = 0x2000, Visit = 0x4000;
    const int CtxRun = 17, CtxUni = 18;

    static readonly byte[][] Zc = BuildZc();
    static readonly byte[] Sc = BuildSc();

    static byte[][] BuildZc()
    {
        var t = new byte[4][];
        for (var o = 0; o < 4; o++)
        {
            t[o] = new byte[256];
            for (var f = 0; f < 256; f++)
            {
                int h = (f >> 7 & 1) + (f >> 5 & 1), v = (f >> 4 & 1) + (f >> 6 & 1);
                int d = (f & 1) + (f >> 1 & 1) + (f >> 2 & 1) + (f >> 3 & 1);
                if (o == 1) (h, v) = (v, h);
                int c;
                if (o == 3)
                {
                    var hv = h + v;
                    c = d >= 3 ? 8 : d == 2 ? (hv >= 1 ? 7 : 6) : d == 1 ? (hv >= 2 ? 5 : hv == 1 ? 4 : 3) : (hv >= 2 ? 2 : hv);
                }
                else
                {
                    c = h == 2 ? 8 : h == 1 ? (v >= 1 ? 7 : d >= 1 ? 6 : 5)
                        : v == 2 ? 4 : v == 1 ? 3 : d >= 2 ? 2 : d;
                }
                t[o][f] = (byte)c;
            }
        }
        return t;
    }

    static byte[] BuildSc()
    {
        var t = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            int Contrib(int sigBit, int sgnBit) => (i >> sigBit & 1) == 0 ? 0 : (i >> sgnBit & 1) != 0 ? -1 : 1;
            var v = Math.Clamp(Contrib(0, 4) + Contrib(2, 6), -1, 1);
            var h = Math.Clamp(Contrib(3, 7) + Contrib(1, 5), -1, 1);
            int ctx, xor = 0;
            switch (h, v)
            {
                case (1, 1): ctx = 13; break;
                case (1, 0): ctx = 12; break;
                case (1, -1): ctx = 11; break;
                case (0, 1): ctx = 10; break;
                case (0, 0): ctx = 9; break;
                case (0, -1): ctx = 10; xor = 1; break;
                case (-1, 1): ctx = 11; xor = 1; break;
                case (-1, 0): ctx = 12; xor = 1; break;
                default: ctx = 13; xor = 1; break;
            }
            t[i] = (byte)(ctx | xor << 7);
        }
        return t;
    }

    int[] _flags = new int[66 * 66];
    int[] _data = new int[64 * 64];
    readonly byte[] _cx = new byte[19];
    int _w, _h, _stride;
    MqDecoder _mq = null!;
    byte[] _rb = [];
    int _rpos, _rend, _rct, _rc;

    public int[] Data => _data;

    void ResetCx()
    {
        Array.Clear(_cx);
        _cx[0] = 4 << 1;
        _cx[CtxRun] = 3 << 1;
        _cx[CtxUni] = 46 << 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int RawBit()
    {
        if (_rct == 0)
        {
            if (_rc == 0xFF)
            {
                if (_rpos < _rend && _rb[_rpos] > 0x8F) { _rc = 0xFF; _rct = 8; }
                else { _rc = _rpos < _rend ? _rb[_rpos++] : 0xFF; _rct = 7; }
            }
            else { _rc = _rpos < _rend ? _rb[_rpos++] : 0xFF; _rct = 8; }
        }
        _rct--;
        return _rc >> _rct & 1;
    }

    /// <param name="orient">0 LL, 1 HL, 2 LH, 3 HH.</param>
    /// <param name="numbps">Number of coded magnitude bit planes.</param>
    /// <param name="style">Code-block style flags (bypass 1, reset 2, termall 4, vcausal 8, pterm 0x10, segsym 0x20).</param>
    public void Decode(JpxCodeBlock cb, int w, int h, int orient, int numbps, int style)
    {
        _w = w; _h = h; _stride = w + 2;
        var fl = (w + 2) * (h + 2);
        if (_flags.Length < fl) _flags = new int[fl]; else Array.Clear(_flags, 0, fl);
        if (_data.Length < w * h) _data = new int[w * h]; else Array.Clear(_data, 0, w * h);
        ResetCx();
        var zc = Zc[orient];
        bool bypass = (style & 1) != 0, reset = (style & 2) != 0, vc = (style & 8) != 0, segsym = (style & 0x20) != 0;
        int bp1 = numbps, passType = 2, segIdx = 0, inSeg = 0, off = 0;
        var raw = false;
        for (var pass = 0; pass < cb.NumPasses && bp1 >= 1 && segIdx < cb.Segs.Count; pass++)
        {
            var seg = cb.Segs[segIdx];
            if (inSeg == 0)
            {
                raw = bypass && pass >= 10 && passType < 2;
                if (raw) { _rb = cb.Buf; _rpos = off; _rend = Math.Min(off + seg.Len, cb.BufLen); _rct = 0; _rc = 0; }
                else _mq = new MqDecoder(cb.Buf, off, Math.Min(off + seg.Len, cb.BufLen));
            }
            switch (passType)
            {
                case 0: SigPass(bp1, zc, raw, vc); break;
                case 1: RefPass(bp1, raw); break;
                default:
                    CleanPass(bp1, zc, vc);
                    if (segsym) { _mq.Decode(_cx, CtxUni); _mq.Decode(_cx, CtxUni); _mq.Decode(_cx, CtxUni); _mq.Decode(_cx, CtxUni); }
                    break;
            }
            if (++inSeg >= seg.Passes) { off += seg.Len; segIdx++; inSeg = 0; }
            if (reset) ResetCx();
            if (++passType == 3) { passType = 0; bp1--; }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void SetSig(int fi, bool neg, bool noNorth)
    {
        var fl = _flags;
        var s = _stride;
        fl[fi] |= Sig;
        if (!noNorth)
        {
            fl[fi - s - 1] |= SigSE;
            fl[fi - s] |= neg ? SigS | SgnS : SigS;
            fl[fi - s + 1] |= SigSW;
        }
        fl[fi - 1] |= neg ? SigE | SgnE : SigE;
        fl[fi + 1] |= neg ? SigW | SgnW : SigW;
        fl[fi + s - 1] |= SigNE;
        fl[fi + s] |= neg ? SigN | SgnN : SigN;
        fl[fi + s + 1] |= SigNW;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int MqSign(int f)
    {
        int s = Sc[f >> 4 & 0xFF];
        return _mq.Decode(_cx, s & 0x7F) ^ s >> 7;
    }

    void SigPass(int bp1, byte[] zc, bool raw, bool vc)
    {
        int one = 1 << bp1, half = one >> 1, oph = one | half;
        for (var k = 0; k < _h; k += 4)
        {
            var ye = Math.Min(k + 4, _h);
            for (var x = 0; x < _w; x++)
                for (var y = k; y < ye; y++)
                {
                    var fi = (y + 1) * _stride + x + 1;
                    var f = _flags[fi];
                    if ((f & (Sig | Visit)) != 0 || (f & 0xFF) == 0) continue;
                    var bit = raw ? RawBit() : _mq.Decode(_cx, zc[f & 0xFF]);
                    if (bit != 0)
                    {
                        var neg = (raw ? RawBit() : MqSign(f)) != 0;
                        _data[y * _w + x] = neg ? -oph : oph;
                        SetSig(fi, neg, vc && (y & 3) == 0);
                    }
                    _flags[fi] |= Visit;
                }
        }
    }

    void RefPass(int bp1, bool raw)
    {
        var half = (1 << bp1) >> 1;
        for (var k = 0; k < _h; k += 4)
        {
            var ye = Math.Min(k + 4, _h);
            for (var x = 0; x < _w; x++)
                for (var y = k; y < ye; y++)
                {
                    var fi = (y + 1) * _stride + x + 1;
                    var f = _flags[fi];
                    if ((f & (Sig | Visit)) != Sig) continue;
                    var bit = raw ? RawBit() : _mq.Decode(_cx, (f & Refine) != 0 ? 16 : (f & 0xFF) != 0 ? 15 : 14);
                    var t = bit != 0 ? half : -half;
                    ref var dv = ref _data[y * _w + x];
                    dv += dv < 0 ? -t : t;
                    _flags[fi] = f | Refine;
                }
        }
    }

    void CleanPass(int bp1, byte[] zc, bool vc)
    {
        int one = 1 << bp1, half = one >> 1, oph = one | half;
        for (var k = 0; k < _h; k += 4)
        {
            var ye = Math.Min(k + 4, _h);
            for (var x = 0; x < _w; x++)
            {
                var fi0 = (k + 1) * _stride + x + 1;
                var start = k;
                if (k + 3 < _h)
                {
                    const int m = Sig | Visit | 0xFF;
                    if (((_flags[fi0] | _flags[fi0 + _stride] | _flags[fi0 + 2 * _stride] | _flags[fi0 + 3 * _stride]) & m) == 0)
                    {
                        if (_mq.Decode(_cx, CtxRun) == 0) continue;
                        var r = _mq.Decode(_cx, CtxUni) << 1;
                        r |= _mq.Decode(_cx, CtxUni);
                        var y = k + r;
                        var fi = fi0 + r * _stride;
                        var neg = MqSign(_flags[fi]) != 0;
                        _data[y * _w + x] = neg ? -oph : oph;
                        SetSig(fi, neg, vc && (y & 3) == 0);
                        start = y + 1;
                    }
                }
                for (var y = start; y < ye; y++)
                {
                    var fi = (y + 1) * _stride + x + 1;
                    var f = _flags[fi];
                    if ((f & (Sig | Visit)) == 0 && _mq.Decode(_cx, zc[f & 0xFF]) != 0)
                    {
                        var neg = MqSign(f) != 0;
                        _data[y * _w + x] = neg ? -oph : oph;
                        SetSig(fi, neg, vc && (y & 3) == 0);
                    }
                    _flags[fi] &= ~Visit;
                }
            }
        }
    }
}
