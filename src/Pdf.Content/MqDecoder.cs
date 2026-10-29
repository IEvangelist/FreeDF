using System.Runtime.CompilerServices;

namespace Pdf.Content;

/// <summary>
/// MQ arithmetic decoder shared by JBIG2 (ITU-T T.88 Annex E) and JPEG 2000 (ITU-T T.800 Annex C).
/// Context states are stored as one byte each: (state index &lt;&lt; 1) | MPS.
/// </summary>
internal sealed class MqDecoder
{
    static readonly ushort[] Qe =
    [
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
        0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
        0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085, 0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
    ];
    static readonly byte[] Nmps =
    [
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46,
    ];
    static readonly byte[] Nlps =
    [
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46,
    ];
    static readonly bool[] Switch = BuildSwitch();

    static bool[] BuildSwitch()
    {
        var s = new bool[47];
        s[0] = s[6] = s[14] = true;
        return s;
    }

    readonly byte[] _d;
    readonly int _end;
    int _bp;
    uint _c, _a;
    int _ct;

    public MqDecoder(byte[] data, int start, int end)
    {
        _d = data; _bp = start; _end = Math.Min(end, data.Length);
        _c = (uint)B(_bp) << 16;
        ByteIn();
        _c <<= 7; _ct -= 7; _a = 0x8000;
    }

    /// <summary>Index of the next unread byte (useful for locating the end of a coded segment).</summary>
    public int Position => _bp;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int B(int i) => i < _end ? _d[i] : 0xFF;

    void ByteIn()
    {
        if (B(_bp) == 0xFF)
        {
            if (B(_bp + 1) > 0x8F) { _c += 0xFF00; _ct = 8; }
            else { _bp++; _c += (uint)B(_bp) << 9; _ct = 7; }
        }
        else { _bp++; _c += (uint)B(_bp) << 8; _ct = 8; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(byte[] cx, int i)
    {
        int s = cx[i], idx = s >> 1, mps = s & 1, d;
        uint qe = Qe[idx];
        _a -= qe;
        if ((_c >> 16) < qe)
        {
            if (_a < qe) { d = mps; idx = Nmps[idx]; }
            else { d = 1 - mps; if (Switch[idx]) mps = 1 - mps; idx = Nlps[idx]; }
            _a = qe;
        }
        else
        {
            _c -= qe << 16;
            if ((_a & 0x8000) != 0) return mps;
            if (_a < qe) { d = 1 - mps; if (Switch[idx]) mps = 1 - mps; idx = Nlps[idx]; }
            else { d = mps; idx = Nmps[idx]; }
        }
        do
        {
            if (_ct == 0) ByteIn();
            _a <<= 1; _c <<= 1; _ct--;
        } while ((_a & 0x8000) == 0);
        cx[i] = (byte)(idx << 1 | mps);
        return d;
    }
}
