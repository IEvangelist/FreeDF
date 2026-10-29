namespace Pdf.Content;

/// <summary>CCITT Group 3 (1D, mixed 2D) and Group 4 decoder per ITU-T T.4/T.6 as used by /CCITTFaxDecode (ISO 32000-2 §7.4.6).</summary>
public static class CcittDecoder
{
    static readonly string[] WhiteTerm =
    ("00110101 000111 0111 1000 1011 1100 1110 1111 10011 10100 00111 01000 001000 000011 110100 110101 101010 101011 0100111 0001100 0001000 0010111 0000011 0000100 0101000 0101011 0010011 0100100 0011000 00000010 00000011 00011010 00011011 00010010 00010011 00010100 00010101 00010110 00010111 00101000 00101001 00101010 00101011 00101100 00101101 00000100 00000101 00001010 00001011 01010010 01010011 01010100 01010101 00100100 00100101 01011000 01011001 01011010 01011011 01001010 01001011 00110010 00110011 00110100").Split(' ');
    static readonly string[] WhiteMakeup =
    ("11011 10010 010111 0110111 00110110 00110111 01100100 01100101 01101000 01100111 011001100 011001101 011010010 011010011 011010100 011010101 011010110 011010111 011011000 011011001 011011010 011011011 010011000 010011001 010011010 011000 010011011").Split(' ');
    static readonly string[] BlackTerm =
    ("0000110111 010 11 10 011 0011 0010 00011 000101 000100 0000100 0000101 0000111 00000100 00000111 000011000 0000010111 0000011000 0000001000 00001100111 00001101000 00001101100 00000110111 00000101000 00000010111 00000011000 000011001010 000011001011 000011001100 000011001101 000001101000 000001101001 000001101010 000001101011 000011010010 000011010011 000011010100 000011010101 000011010110 000011010111 000001101100 000001101101 000011011010 000011011011 000001010100 000001010101 000001010110 000001010111 000001100100 000001100101 000001010010 000001010011 000000100100 000000110111 000000111000 000000100111 000000101000 000001011000 000001011001 000000101011 000000101100 000001011010 000001100110 000001100111").Split(' ');
    static readonly string[] BlackMakeup =
    ("0000001111 000011001000 000011001001 000001011011 000000110011 000000110100 000000110101 0000001101100 0000001101101 0000001001010 0000001001011 0000001001100 0000001001101 0000001110010 0000001110011 0000001110100 0000001110101 0000001110110 0000001110111 0000001010010 0000001010011 0000001010100 0000001010101 0000001011010 0000001011011 0000001100100 0000001100101").Split(' ');
    static readonly string[] ExtMakeup =
    ("00000001000 00000001100 00000001101 000000010010 000000010011 000000010100 000000010101 000000010110 000000010111 000000011100 000000011101 000000011110 000000011111").Split(' ');

    static readonly Dictionary<int, int> White = Build(WhiteTerm, WhiteMakeup);
    static readonly Dictionary<int, int> Black = Build(BlackTerm, BlackMakeup);

    static Dictionary<int, int> Build(string[] term, string[] makeup)
    {
        var d = new Dictionary<int, int>();
        void Add(string code, int run) => d[(code.Length << 16) | Convert.ToInt32(code, 2)] = run;
        for (var i = 0; i < term.Length; i++) Add(term[i], i);
        for (var i = 0; i < makeup.Length; i++) Add(makeup[i], 64 * (i + 1));
        for (var i = 0; i < ExtMakeup.Length; i++) Add(ExtMakeup[i], 1792 + 64 * i);
        return d;
    }

    sealed class Bits(byte[] d)
    {
        public long Pos;
        public bool Eof => Pos >= (long)d.Length * 8;
        public int Peek(int n)
        {
            var v = 0;
            for (var i = 0; i < n; i++)
            {
                var p = Pos + i; var by = (int)(p >> 3);
                v = v << 1 | (by < d.Length ? d[by] >> (7 - (int)(p & 7)) & 1 : 0);
            }
            return v;
        }
        public void Skip(int n) => Pos += n;
        public int Read(int n) { var v = Peek(n); Pos += n; return v; }
        public void Align() => Pos = (Pos + 7) & ~7L;
    }

    /// <summary>Returns packed 1-bit rows (MSB first). Black pixels are 0 unless <paramref name="blackIs1"/>.</summary>
    public static byte[] Decode(byte[] data, int k, int columns, int rows, bool blackIs1, bool byteAlign, int heightHint) =>
        Decode(data, k, columns, rows, blackIs1, byteAlign, heightHint, out _);

    /// <summary>As above, also reporting the bytes consumed (including a trailing EOFB for K &lt; 0), for concatenated MMR bitmaps as used by JBIG2.</summary>
    public static byte[] Decode(byte[] data, int k, int columns, int rows, bool blackIs1, bool byteAlign, int heightHint, out int consumedBytes)
    {
        if (columns <= 0) columns = 1728;
        var maxRows = rows > 0 ? rows : heightHint > 0 ? heightHint : int.MaxValue;
        var stride = (columns + 7) / 8;
        var output = new List<byte[]>();
        var br = new Bits(data);
        var refLine = new List<int>();
        var cur = new List<int>();

        while (output.Count < maxRows && !br.Eof)
        {
            if (byteAlign) br.Align();
            var is1D = k == 0;
            if (k >= 0)
            {
                // Skip fill bits and EOL (000000000001); two consecutive EOLs mark RTC.
                var eols = 0;
                while (true)
                {
                    var p = br.Peek(12);
                    if (p == 1) { br.Skip(12); eols++; if (k > 0) is1D = br.Read(1) == 1; if (eols >= 2) break; continue; }
                    if (p == 0 && !br.Eof) { br.Skip(1); continue; }
                    break;
                }
                if (eols >= 2) break;
            }
            else if (br.Peek(12) == 1 && br.Peek(24) == 0x001001) break; // EOFB

            cur.Clear();
            var ok = is1D ? Row1D(br, cur, columns) : Row2D(br, cur, refLine, columns);
            output.Add(Render(cur, columns, stride, blackIs1));
            (refLine, cur) = (cur.ToList(), refLine);
            if (!ok) break;
        }
        if (k < 0 && br.Peek(24) == 0x001001) br.Skip(24);
        br.Align();
        consumedBytes = (int)Math.Min(data.Length, br.Pos >> 3);
        var res = new byte[stride * (rows > 0 ? rows : Math.Max(output.Count, heightHint))];
        Array.Fill(res, blackIs1 ? (byte)0 : (byte)0xFF);
        for (var i = 0; i < output.Count && (i + 1) * stride <= res.Length; i++) output[i].CopyTo(res, i * stride);
        return res;
    }

    static int ReadRun(Bits br, bool black)
    {
        var table = black ? Black : White; var total = 0;
        while (true)
        {
            var found = -1;
            for (var len = 2; len <= 13; len++)
                if (table.TryGetValue((len << 16) | br.Peek(len), out var run)) { br.Skip(len); found = run; break; }
            if (found < 0) return -1;
            total += found;
            if (found < 64) return total;
        }
    }

    static bool Row1D(Bits br, List<int> cur, int columns)
    {
        var a0 = 0; var black = false;
        while (a0 < columns)
        {
            var run = ReadRun(br, black);
            if (run < 0) return false;
            a0 = Math.Min(columns, a0 + run);
            cur.Add(a0); black = !black;
        }
        return true;
    }

    static bool Row2D(Bits br, List<int> cur, List<int> refLine, int columns)
    {
        var a0 = -1; var black = false;
        while (a0 < columns)
        {
            // b1: first changing element right of a0 whose colour is opposite to the current one.
            var i = black ? 1 : 0;
            while (i < refLine.Count && refLine[i] <= a0) i += 2;
            var b1 = i < refLine.Count ? refLine[i] : columns;
            var b2 = i + 1 < refLine.Count ? refLine[i + 1] : columns;
            int a1;
            if (br.Peek(1) == 1) { br.Skip(1); a1 = b1; }
            else if (br.Peek(3) == 0b011) { br.Skip(3); a1 = b1 + 1; }
            else if (br.Peek(3) == 0b010) { br.Skip(3); a1 = b1 - 1; }
            else if (br.Peek(3) == 0b001)
            {
                br.Skip(3);
                var r1 = ReadRun(br, black); var r2 = ReadRun(br, !black);
                if (r1 < 0 || r2 < 0) return false;
                var s = Math.Max(a0, 0);
                cur.Add(Math.Min(columns, s + r1)); cur.Add(Math.Min(columns, s + r1 + r2));
                a0 = Math.Min(columns, s + r1 + r2);
                if (a0 >= columns) break;
                continue;
            }
            else if (br.Peek(4) == 0b0001) { br.Skip(4); a0 = b2; continue; }
            else if (br.Peek(6) == 0b000011) { br.Skip(6); a1 = b1 + 2; }
            else if (br.Peek(6) == 0b000010) { br.Skip(6); a1 = b1 - 2; }
            else if (br.Peek(7) == 0b0000011) { br.Skip(7); a1 = b1 + 3; }
            else if (br.Peek(7) == 0b0000010) { br.Skip(7); a1 = b1 - 3; }
            else return false;
            a1 = Math.Clamp(a1, Math.Max(a0, 0), columns);
            cur.Add(a1); a0 = a1; black = !black;
            if (a0 >= columns) break;
        }
        return true;
    }

    static byte[] Render(List<int> changes, int columns, int stride, bool blackIs1)
    {
        var row = new byte[stride];
        if (!blackIs1) Array.Fill(row, (byte)0xFF);
        for (var i = 0; i < changes.Count; i += 2)
        {
            var s = changes[i]; var e = i + 1 < changes.Count ? changes[i + 1] : columns;
            for (var x = s; x < e && x < columns; x++)
            {
                if (blackIs1) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                else row[x >> 3] &= (byte)~(0x80 >> (x & 7));
            }
        }
        return row;
    }
}
