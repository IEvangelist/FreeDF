using System.IO.Compression;

namespace PortableDocument.Core;

/// <summary>Stream filters (ISO 32000-2 §7.4). DCT/JPX/CCITT/JBIG2 are image codecs returned undecoded and flagged.</summary>
public static class PdfFilters
{
    public static readonly HashSet<string> ImageCodecs = ["DCTDecode", "DCT", "JPXDecode", "CCITTFaxDecode", "CCF", "JBIG2Decode"];

    static string Canon(string n) => n switch
    {
        "AHx" => "ASCIIHexDecode", "A85" => "ASCII85Decode", "LZW" => "LZWDecode",
        "Fl" => "FlateDecode", "RL" => "RunLengthDecode", "CCF" => "CCITTFaxDecode", "DCT" => "DCTDecode",
        _ => n
    };

    public static List<(string Name, PdfDictionary? Parms)> GetFilters(PdfDictionary d, Func<PdfObject?, PdfObject?> resolve)
    {
        var f = resolve(d.Get("Filter") ?? d.Get("F"));
        var p = resolve(d.Get("DecodeParms") ?? d.Get("DP"));
        var list = new List<(string, PdfDictionary?)>();
        if (f is PdfName n) list.Add((Canon(n.Value), resolve(p is PdfArray pa0 && pa0.Count > 0 ? pa0[0] : p) as PdfDictionary));
        else if (f is PdfArray fa)
            for (var i = 0; i < fa.Count; i++)
            {
                var nm = resolve(fa[i]).AsName();
                if (nm is null) continue;
                var pp = p is PdfArray pa && i < pa.Count ? resolve(pa[i]) as PdfDictionary : null;
                list.Add((Canon(nm), pp));
            }
        return list;
    }

    /// <summary>Decodes until an image codec filter is reached; returns the remaining image filter name, if any.</summary>
    public static (byte[] Data, string? ImageFilter, PdfDictionary? ImageParms) Decode(byte[] raw, List<(string Name, PdfDictionary? Parms)> filters, Func<PdfObject?, PdfObject?> resolve)
    {
        var data = raw;
        foreach (var (name, parms) in filters)
        {
            if (ImageCodecs.Contains(name)) return (data, name, parms);
            data = name switch
            {
                "FlateDecode" => Predict(Flate(data), parms, resolve),
                "LZWDecode" => Predict(Lzw(data, (int)(resolve(parms?.Get("EarlyChange")).AsNumber() ?? 1)), parms, resolve),
                "ASCIIHexDecode" => AsciiHex(data),
                "ASCII85Decode" => Ascii85(data),
                "RunLengthDecode" => RunLength(data),
                "Crypt" => data,
                _ => throw new NotSupportedException($"Filter {name}")
            };
        }
        return (data, null, null);
    }

    const long MaxInflated = 1L << 30;

    static void InflateInto(Stream z, MemoryStream output)
    {
        var buf = new byte[81920];
        int n;
        while ((n = z.Read(buf, 0, buf.Length)) > 0)
        {
            output.Write(buf, 0, n);
            if (output.Length > MaxInflated) throw new PdfParseException("Decompressed stream exceeds the size limit");
        }
    }

    public static byte[] Flate(byte[] src)
    {
        var output = new MemoryStream();
        try
        {
            using var z = new ZLibStream(new MemoryStream(src), CompressionMode.Decompress);
            InflateInto(z, output);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            // Truncated/corrupt data: keep what was decoded, which matches common reader behaviour.
        }
        if (output.Length == 0 && src.Length > 2)
        {
            try
            {
                using var df = new DeflateStream(new MemoryStream(src, 2, src.Length - 2), CompressionMode.Decompress);
                InflateInto(df, output);
            }
            catch (Exception e) when (e is InvalidDataException or IOException) { }
        }
        return output.ToArray();
    }

    public static byte[] DeflateEncode(byte[] data)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    static byte[] AsciiHex(byte[] s)
    {
        var o = new List<byte>();
        var hi = -1;
        foreach (var c in s)
        {
            if (c == '>') break;
            var h = c switch { >= (byte)'0' and <= (byte)'9' => c - '0', >= (byte)'a' and <= (byte)'f' => c - 'a' + 10, >= (byte)'A' and <= (byte)'F' => c - 'A' + 10, _ => -1 };
            if (h < 0) continue;
            if (hi < 0) hi = h; else { o.Add((byte)(hi * 16 + h)); hi = -1; }
        }
        if (hi >= 0) o.Add((byte)(hi * 16));
        return o.ToArray();
    }

    static byte[] Ascii85(byte[] s)
    {
        var o = new List<byte>();
        var grp = new uint[5];
        var n = 0;
        var i = 0;
        if (s.Length >= 2 && s[0] == '<' && s[1] == '~') i = 2;
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '~') break;
            if (PdfLexer.IsWhite(c)) continue;
            if (c == 'z' && n == 0) { o.AddRange(new byte[4]); continue; }
            if (c < '!' || c > 'u') continue;
            grp[n++] = (uint)(c - '!');
            if (n == 5) { Flush(5); n = 0; }
        }
        if (n > 1) { for (var k = n; k < 5; k++) grp[k] = 84; Flush(n); }
        return o.ToArray();

        void Flush(int count)
        {
            var v = 0u;
            for (var k = 0; k < 5; k++) v = unchecked(v * 85 + grp[k]);
            for (var k = 0; k < count - 1; k++) o.Add((byte)(v >> (24 - 8 * k)));
        }
    }

    static byte[] RunLength(byte[] s)
    {
        var o = new List<byte>();
        for (var i = 0; i < s.Length;)
        {
            int l = s[i++];
            if (l == 128) break;
            if (l < 128) { for (var k = 0; k <= l && i < s.Length; k++) o.Add(s[i++]); }
            else if (i < s.Length) { var b = s[i++]; for (var k = 0; k < 257 - l; k++) o.Add(b); }
        }
        return o.ToArray();
    }

    static byte[] Lzw(byte[] s, int early)
    {
        var o = new List<byte>();
        var table = new List<byte[]>();
        void Reset() { table.Clear(); for (var i = 0; i < 256; i++) table.Add([(byte)i]); table.Add([]); table.Add([]); }
        Reset();
        var bits = 9; var bitBuf = 0; var bitCnt = 0; var pos = 0;
        byte[]? prev = null;
        while (true)
        {
            while (bitCnt < bits && pos < s.Length) { bitBuf = (bitBuf << 8) | s[pos++]; bitCnt += 8; }
            if (bitCnt < bits) break;
            var code = (bitBuf >> (bitCnt - bits)) & ((1 << bits) - 1);
            bitCnt -= bits;
            bitBuf &= (1 << bitCnt) - 1;
            if (code == 256) { Reset(); bits = 9; prev = null; continue; }
            if (code == 257) break;
            byte[] entry;
            if (code < table.Count) entry = table[code];
            else if (prev is not null) entry = [.. prev, prev[0]];
            else break;
            o.AddRange(entry);
            if (prev is not null) table.Add([.. prev, entry[0]]);
            prev = entry;
            var n = table.Count + early;
            bits = n >= 4096 ? 12 : n >= 2048 ? 12 : n >= 1024 ? 11 : n >= 512 ? 10 : 9;
        }
        return o.ToArray();
    }

    static byte[] Predict(byte[] data, PdfDictionary? p, Func<PdfObject?, PdfObject?> r)
    {
        if (p is null) return data;
        var pred = (int)(r(p.Get("Predictor")).AsNumber() ?? 1);
        if (pred <= 1) return data;
        var colors = (int)(r(p.Get("Colors")).AsNumber() ?? 1);
        var bpc = (int)(r(p.Get("BitsPerComponent")).AsNumber() ?? 8);
        var columns = (int)(r(p.Get("Columns")).AsNumber() ?? 1);
        var bpp = Math.Max(1, colors * bpc / 8);
        var rowBytes = (colors * bpc * columns + 7) / 8;
        if (pred == 2)
        {
            if (rowBytes == 0) return data;
            for (var row = 0; row + rowBytes <= data.Length; row += rowBytes) TiffRow(data, row, colors, bpc, columns, bpp);
            return data;
        }
        var rows = data.Length / (rowBytes + 1) + (data.Length % (rowBytes + 1) > 1 ? 1 : 0);
        var res = new byte[rows * rowBytes];
        var prev = new byte[rowBytes];
        var cur = new byte[rowBytes];
        for (var y = 0; y < rows; y++)
        {
            var off = y * (rowBytes + 1);
            if (off >= data.Length) break;
            var ft = data[off];
            var avail = Math.Min(rowBytes, data.Length - off - 1);
            Array.Clear(cur);
            Array.Copy(data, off + 1, cur, 0, avail);
            for (var i = 0; i < rowBytes; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = ft switch
                {
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + (a + b) / 2),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => cur[i]
                };
            }
            Array.Copy(cur, 0, res, y * rowBytes, rowBytes);
            (prev, cur) = (cur, prev);
        }
        return res;
    }

    /// <summary>Undoes the TIFF horizontal differencing predictor (ISO 32000-2 7.4.4.4) for one row of 1, 2, 4, 8 or 16 bits per component.</summary>
    static void TiffRow(byte[] data, int row, int colors, int bpc, int columns, int bpp)
    {
        var rowBytes = (colors * bpc * columns + 7) / 8;
        switch (bpc)
        {
            case 8:
                for (var i = bpp; i < rowBytes; i++) data[row + i] += data[row + i - bpp];
                break;
            case 16:
                for (var i = colors * 2; i + 1 < rowBytes; i += 2)
                {
                    var v = ((data[row + i] << 8) | data[row + i + 1]) + ((data[row + i - colors * 2] << 8) | data[row + i - colors * 2 + 1]);
                    data[row + i] = (byte)(v >> 8); data[row + i + 1] = (byte)v;
                }
                break;
            case 1 or 2 or 4:
                {
                    var mask = (1 << bpc) - 1;
                    var count = Math.Min(colors * columns, rowBytes * 8 / bpc);
                    var samples = new int[count];
                    for (var s = 0; s < count; s++)
                    {
                        var bit = s * bpc;
                        samples[s] = (data[row + (bit >> 3)] >> (8 - bpc - (bit & 7))) & mask;
                    }
                    for (var s = colors; s < count; s++) samples[s] = (samples[s] + samples[s - colors]) & mask;
                    for (var s = 0; s < count; s++)
                    {
                        var bit = s * bpc; var shift = 8 - bpc - (bit & 7);
                        ref var b = ref data[row + (bit >> 3)];
                        b = (byte)((b & ~(mask << shift)) | (samples[s] << shift));
                    }
                    break;
                }
        }
    }

    static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
