namespace PortableDocument.Content;

/// <summary>MSB-first bit reader used by JBIG2 Huffman-coded data.</summary>
internal sealed class Jb2BitReader(byte[] d, int pos, int end)
{
    int _bit;
    public int Pos = pos;
    public int End = end;

    public int ReadBit()
    {
        if (Pos >= End) throw new InvalidOperationException("JBIG2 data truncated");
        var b = d[Pos] >> (7 - _bit) & 1;
        if (++_bit == 8) { _bit = 0; Pos++; }
        return b;
    }

    public int ReadBits(int n)
    {
        var v = 0;
        for (var i = 0; i < n; i++) v = v << 1 | ReadBit();
        return v;
    }

    public void Align() { if (_bit != 0) { _bit = 0; Pos++; } }
}

/// <summary>JBIG2 Huffman table (ITU-T T.88 Annex B), standard or custom.</summary>
internal sealed class Jb2HuffTable
{
    const byte Normal = 0, Lower = 1, Upper = 2, Oob = 3;
    readonly List<(int Pref, int RLen, int Low, byte Kind)> _lines = [];
    Dictionary<ulong, int> _codes = [];

    public void Add(int pref, int rlen, int low) => _lines.Add((pref, rlen, low, Normal));
    public void AddLower(int pref, int low) => _lines.Add((pref, 32, low, Lower));
    public void AddUpper(int pref, int low) => _lines.Add((pref, 32, low, Upper));
    public void AddOob(int pref) => _lines.Add((pref, 0, 0, Oob));

    public Jb2HuffTable Build()
    {
        var maxLen = 0;
        foreach (var l in _lines) maxLen = Math.Max(maxLen, l.Pref);
        var count = new int[maxLen + 2];
        foreach (var l in _lines) if (l.Pref > 0) count[l.Pref]++;
        var first = 0L;
        _codes = [];
        for (var len = 1; len <= maxLen; len++)
        {
            first = (first + count[len - 1]) << 1;
            var code = first;
            for (var i = 0; i < _lines.Count; i++)
                if (_lines[i].Pref == len) _codes[(ulong)len << 32 | (uint)code++] = i;
        }
        return this;
    }

    /// <summary>Decodes a value; returns false for the out-of-band symbol.</summary>
    public bool Decode(Jb2BitReader br, out int value)
    {
        uint code = 0;
        for (var len = 1; len <= 32; len++)
        {
            code = code << 1 | (uint)br.ReadBit();
            if (!_codes.TryGetValue((ulong)len << 32 | code, out var li)) continue;
            var (_, rlen, low, kind) = _lines[li];
            switch (kind)
            {
                case Oob: value = 0; return false;
                case Lower: value = (int)(low - (uint)ReadWide(br, 32)); return true;
                case Upper: value = (int)(low + (uint)ReadWide(br, 32)); return true;
                default: value = low + (rlen > 0 ? br.ReadBits(rlen) : 0); return true;
            }
        }
        throw new InvalidOperationException("bad JBIG2 Huffman code");
    }

    static long ReadWide(Jb2BitReader br, int n)
    {
        long v = 0;
        for (var i = 0; i < n; i++) v = v << 1 | (uint)br.ReadBit();
        return v;
    }

    /// <summary>Parses a custom table segment (type 53), T.88 B.2.</summary>
    public static Jb2HuffTable Parse(byte[] d, int off, int end)
    {
        var flags = d[off];
        var oob = (flags & 1) != 0; var ps = (flags >> 1 & 7) + 1; var rs = (flags >> 4 & 7) + 1;
        var low = ReadI32(d, off + 1); var high = ReadI32(d, off + 5);
        var br = new Jb2BitReader(d, off + 9, end);
        var t = new Jb2HuffTable();
        var cur = (long)low;
        while (cur < high)
        {
            var pref = br.ReadBits(ps); var rl = br.ReadBits(rs);
            t.Add(pref, rl, (int)cur);
            cur += 1L << rl;
        }
        t.AddLower(br.ReadBits(ps), low - 1);
        t.AddUpper(br.ReadBits(ps), high);
        if (oob) t.AddOob(br.ReadBits(ps));
        return t.Build();
    }

    static int ReadI32(byte[] d, int o) => d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3];

    static Jb2HuffTable Make(int[] lines, int lowerPref, int lowerLow, int upperPref, int upperLow, int oobPref)
    {
        var t = new Jb2HuffTable();
        for (var i = 0; i < lines.Length; i += 3) t.Add(lines[i], lines[i + 1], lines[i + 2]);
        if (lowerPref >= 0) t.AddLower(lowerPref, lowerLow);
        if (upperPref >= 0) t.AddUpper(upperPref, upperLow);
        if (oobPref >= 0) t.AddOob(oobPref);
        return t.Build();
    }

    static readonly Jb2HuffTable[] Std = BuildStandard();

    /// <summary>Standard table B.n, n in 1..15.</summary>
    public static Jb2HuffTable Standard(int n) => Std[n - 1];

    static Jb2HuffTable[] BuildStandard() =>
    [
        Make([1, 4, 0, 2, 8, 16, 3, 16, 272], -1, 0, 3, 65808, -1),
        Make([1, 0, 0, 2, 0, 1, 3, 0, 2, 4, 3, 3, 5, 6, 11], -1, 0, 6, 75, 6),
        Make([8, 8, -256, 1, 0, 0, 2, 0, 1, 3, 0, 2, 4, 3, 3, 5, 6, 11], 8, -257, 7, 75, 6),
        Make([1, 0, 1, 2, 0, 2, 3, 0, 3, 4, 3, 4, 5, 6, 12], -1, 0, 5, 76, -1),
        Make([7, 8, -255, 1, 0, 1, 2, 0, 2, 3, 0, 3, 4, 3, 4, 5, 6, 12], 7, -256, 6, 76, -1),
        Make([5, 10, -2048, 4, 9, -1024, 4, 8, -512, 4, 7, -256, 5, 6, -128, 5, 5, -64, 4, 5, -32, 2, 7, 0, 3, 7, 128, 3, 8, 256, 4, 9, 512, 4, 10, 1024], 6, -2049, 6, 2048, -1),
        Make([4, 9, -1024, 3, 8, -512, 4, 7, -256, 5, 6, -128, 5, 5, -64, 4, 5, -32, 4, 5, 0, 5, 5, 32, 5, 6, 64, 4, 7, 128, 3, 8, 256, 3, 9, 512, 3, 10, 1024], 5, -1025, 5, 2048, -1),
        Make([8, 3, -15, 9, 1, -7, 8, 1, -5, 9, 0, -3, 7, 0, -2, 4, 0, -1, 2, 1, 0, 5, 0, 2, 6, 0, 3, 3, 4, 4, 6, 1, 20, 4, 4, 22, 4, 5, 38, 5, 6, 70, 5, 7, 134, 6, 7, 262, 7, 8, 390, 6, 10, 646], 9, -16, 9, 1670, 2),
        Make([8, 4, -31, 9, 2, -15, 8, 2, -11, 9, 1, -7, 7, 1, -5, 4, 1, -3, 3, 1, -1, 3, 1, 1, 5, 1, 3, 6, 1, 5, 3, 5, 7, 6, 2, 39, 4, 5, 43, 4, 6, 75, 5, 7, 139, 5, 8, 267, 6, 8, 523, 7, 9, 779, 6, 11, 1291], 9, -32, 9, 3339, 2),
        Make([7, 4, -21, 8, 0, -5, 7, 0, -4, 5, 0, -3, 2, 2, -2, 5, 0, 2, 6, 0, 3, 7, 0, 4, 8, 0, 5, 2, 6, 6, 5, 5, 70, 6, 5, 102, 6, 6, 134, 6, 7, 198, 6, 8, 326, 6, 9, 582, 6, 10, 1094, 7, 11, 2118], 8, -22, 8, 4166, 2),
        Make([1, 0, 1, 2, 1, 2, 4, 0, 4, 4, 1, 5, 5, 1, 7, 5, 2, 9, 6, 2, 13, 7, 2, 17, 7, 3, 21, 7, 4, 29, 7, 5, 45, 7, 6, 77], -1, 0, 7, 141, -1),
        Make([1, 0, 1, 2, 0, 2, 3, 1, 3, 5, 0, 5, 5, 1, 6, 6, 1, 8, 7, 0, 10, 7, 1, 11, 7, 2, 13, 7, 3, 17, 7, 4, 25, 8, 5, 41], -1, 0, 8, 73, -1),
        Make([1, 0, 1, 3, 0, 2, 4, 0, 3, 5, 0, 4, 4, 1, 5, 3, 3, 7, 6, 1, 15, 6, 2, 17, 6, 3, 21, 6, 4, 29, 6, 5, 45, 7, 6, 77], -1, 0, 7, 141, -1),
        Make([3, 0, -2, 3, 0, -1, 1, 0, 0, 3, 0, 1, 3, 0, 2], -1, 0, -1, 0, -1),
        Make([7, 4, -24, 6, 2, -8, 5, 1, -4, 4, 0, -2, 3, 0, -1, 1, 0, 0, 3, 0, 1, 4, 0, 2, 5, 1, 3, 6, 2, 5, 7, 4, 9], 7, -25, 7, 25, -1),
    ];
}
