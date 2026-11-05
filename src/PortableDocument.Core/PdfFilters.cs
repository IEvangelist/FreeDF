using System.Buffers;
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
        => GetFilters(d, resolve, strictMetadataResolver: null);

    internal static List<(string Name, PdfDictionary? Parms)> GetFilters(PdfDictionary d,
        Func<PdfObject?, PdfObject?> resolve, Func<PdfObject?, PdfObject?>? strictMetadataResolver)
    {
        var resolveMetadata = strictMetadataResolver ?? resolve;
        var strictMetadata = strictMetadataResolver is not null;
        var f = resolveMetadata(d.Get("Filter") ?? d.Get("F"));
        var p = resolveMetadata(d.Get("DecodeParms") ?? d.Get("DP"));
        if (strictMetadata && p is not null and not PdfNull and not PdfDictionary and not PdfArray)
            throw new PdfParseException("stream DecodeParms must be a dictionary or an array of dictionaries");
        var list = new List<(string, PdfDictionary?)>();
        if (f is PdfName n)
        {
            var parms = p is PdfArray pa0 ? pa0.Count > 0
                ? ResolveDecodeParms(pa0[0], resolveMetadata, strictMetadata) : null
                : ResolveDecodeParms(p, resolveMetadata, strictMetadata);
            list.Add((Canon(n.Value), parms));
        }
        else if (f is PdfArray fa)
            for (var i = 0; i < fa.Count; i++)
            {
                var nm = resolveMetadata(fa[i]).AsName();
                if (nm is null)
                {
                    if (strictMetadata) throw new PdfParseException("stream Filter array entries must be names");
                    continue;
                }
                var pp = p is PdfArray pa && i < pa.Count
                    ? ResolveDecodeParms(pa[i], resolveMetadata, strictMetadata)
                    : fa.Count == 1 ? p as PdfDictionary : null;
                list.Add((Canon(nm), pp));
            }
        else if (strictMetadata && f is not null and not PdfNull)
            throw new PdfParseException("stream Filter must be a name or an array of names");
        if (strictMetadata && p is PdfArray parameters)
            for (var i = list.Count; i < parameters.Count; i++)
                ResolveDecodeParms(parameters[i], resolveMetadata, strictMetadata);
        return list;
    }

    static PdfDictionary? ResolveDecodeParms(PdfObject? value, Func<PdfObject?, PdfObject?> resolve, bool strictMetadata)
    {
        var resolved = resolve(value);
        if (strictMetadata && resolved is not null and not PdfNull and not PdfDictionary)
            throw new PdfParseException("stream DecodeParms array entries must be dictionaries or null");
        return resolved as PdfDictionary;
    }

    /// <summary>Decodes until an image codec filter is reached; returns the remaining image filter name, if any.</summary>
    public static (byte[] Data, string? ImageFilter, PdfDictionary? ImageParms) Decode(byte[] raw, List<(string Name, PdfDictionary? Parms)> filters, Func<PdfObject?, PdfObject?> resolve)
        => Decode(raw, filters, resolve, strictContentFilters: false);

    internal static (byte[] Data, string? ImageFilter, PdfDictionary? ImageParms) Decode(byte[] raw,
        List<(string Name, PdfDictionary? Parms)> filters, Func<PdfObject?, PdfObject?> resolve, bool strictContentFilters)
    {
        var data = raw;
        foreach (var (name, parms) in filters)
        {
            if (ImageCodecs.Contains(name)) return (data, name, parms);
            data = name switch
            {
                "FlateDecode" => strictContentFilters ? StrictPredict(StrictFlate(data), parms, resolve)
                    : Predict(Flate(data), parms, resolve),
                "LZWDecode" => strictContentFilters ? StrictPredict(StrictLzw(data, parms, resolve), parms, resolve)
                    : Predict(Lzw(data, (int)(resolve(parms?.Get("EarlyChange")).AsNumber() ?? 1)), parms, resolve),
                "ASCIIHexDecode" => strictContentFilters ? StrictAsciiHex(data) : AsciiHex(data),
                "ASCII85Decode" => strictContentFilters ? StrictAscii85(data) : Ascii85(data),
                "RunLengthDecode" => strictContentFilters ? StrictRunLength(data) : RunLength(data),
                "Crypt" => data,
                _ => throw new NotSupportedException($"Filter {name}")
            };
        }
        return (data, null, null);
    }

    const long MaxInflated = 1L << 30;

    static void InflateInto(Stream z, MemoryStream output)
    {
        const int bufferSize = 81920;
        var buf = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int n;
            while ((n = z.Read(buf, 0, bufferSize)) > 0)
            {
                output.Write(buf, 0, n);
                if (output.Length > MaxInflated) throw new PdfParseException("Decompressed stream exceeds the size limit");
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buf, clearArray: true); }
    }

    public static byte[] Flate(byte[] src)
    {
        var output = new MemoryStream(Math.Min(src.Length, 65536));
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

    static byte[] StrictFlate(byte[] src)
    {
        if (src.Length < 6 || (src[0] & 15) != 8 || src[0] >> 4 > 7
            || (src[0] * 256 + src[1]) % 31 != 0 || (src[1] & 32) != 0)
            throw new InvalidDataException("Invalid zlib header in content stream");
        using var output = new MemoryStream(Math.Min(src.Length, 65536));
        using var input = new StrictFlateInput(src);
        using (var z = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true))
            InflateInto(z, output);
        if (input.Position != src.Length)
            throw new InvalidDataException("Unexpected bytes after zlib content stream");
        return output.ToArray();
    }

    sealed class StrictFlateInput(byte[] data) : MemoryStream(data, writable: false)
    {
        // The inflater requests input only while unfinished; ordinary EOF would hide truncation.
        public override int Read(byte[] buffer, int offset, int count) =>
            RequireData(base.Read(buffer, offset, ReadCount(count)), count);
        public override int Read(Span<byte> buffer) => RequireData(base.Read(buffer[..ReadCount(buffer.Length)]), buffer.Length);

        // Keep the last byte separate so inflater read-ahead cannot hide excess data.
        int ReadCount(int requested) => (int)Math.Min(requested, Math.Max(1L, Length - Position - 1));

        static int RequireData(int read, int requested) => read == 0 && requested != 0
            ? throw new InvalidDataException("Incomplete zlib content stream") : read;
    }

    public static byte[] DeflateEncode(byte[] data)
    {
        // Empty zlib stream: header, final fixed-Huffman block, Adler-32 of empty input.
        if (data.Length == 0) return [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    static byte[] StrictAsciiHex(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c == '>')
            {
                if (i + 1 != data.Length)
                    throw new InvalidDataException("Excess data after ASCIIHex content-stream end marker");
                return AsciiHex(data);
            }
            if (!PdfLexer.IsWhite(c) && c is not (>= (byte)'0' and <= (byte)'9'
                or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F'))
                throw new InvalidDataException("Invalid ASCIIHex content-stream character");
        }
        throw new InvalidDataException("ASCIIHex content-stream end marker was not found");
    }

    static byte[] StrictAscii85(byte[] data)
    {
        ulong group = 0;
        var count = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (PdfLexer.IsWhite(c)) continue;
            if (c == '~')
            {
                if (i + 1 >= data.Length || data[i + 1] != '>' || count == 1)
                    throw new InvalidDataException("Invalid ASCII85 content-stream end marker or final group");
                if (i + 2 != data.Length)
                    throw new InvalidDataException("Excess data after ASCII85 content-stream end marker");
                if (count > 1)
                {
                    for (var n = count; n < 5; n++) group = group * 85 + 84;
                    if (group > uint.MaxValue)
                        throw new InvalidDataException("ASCII85 content-stream final group overflows");
                }
                return Ascii85(data);
            }
            if (c == 'z' && count == 0) continue;
            if (c is < (byte)'!' or > (byte)'u')
                throw new InvalidDataException("Invalid ASCII85 content-stream character or zero group");
            group = group * 85 + (uint)(c - '!');
            if (++count == 5)
            {
                if (group > uint.MaxValue)
                    throw new InvalidDataException("ASCII85 content-stream group overflows");
                count = 0;
                group = 0;
            }
        }
        throw new InvalidDataException("ASCII85 content-stream end marker was not found");
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

    static byte[] StrictRunLength(byte[] data)
    {
        long length = 0;
        for (var i = 0; i < data.Length;)
        {
            var run = data[i++];
            if (run == 128)
            {
                if (i != data.Length)
                    throw new InvalidDataException("Excess data after RunLength content-stream end marker");
                return RunLength(data);
            }
            var input = run < 128 ? run + 1 : 1;
            if (input > data.Length - i)
                throw new InvalidDataException("Incomplete RunLength content-stream packet");
            i += input;
            length += run < 128 ? run + 1 : 257 - run;
            if (length > MaxInflated)
                throw new PdfParseException("Decompressed stream exceeds the size limit");
        }
        throw new InvalidDataException("RunLength content-stream end marker was not found");
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

    static byte[] StrictLzw(byte[] data, PdfDictionary? parms, Func<PdfObject?, PdfObject?> resolve)
    {
        var value = resolve(parms?.Get("EarlyChange"));
        var early = value is null or PdfNull ? 1 : value is PdfInteger { Value: 0 or 1 } number
            ? (int)number.Value : throw new InvalidDataException("Invalid LZW EarlyChange parameter");
        return Lzw(data, early, strict: true);
    }

    static byte[] Lzw(byte[] s, int early, bool strict = false)
    {
        var o = new List<byte>();
        var table = new List<byte[]>();
        void Reset() { table.Clear(); for (var i = 0; i < 256; i++) table.Add([(byte)i]); table.Add([]); table.Add([]); }
        // Strict streams initialize at the required first clear code.
        if (!strict) Reset();
        var bits = 9; var bitBuf = 0; var bitCnt = 0; var pos = 0;
        var first = true;
        byte[]? prev = null;
        while (true)
        {
            while (bitCnt < bits && pos < s.Length) { bitBuf = (bitBuf << 8) | s[pos++]; bitCnt += 8; }
            if (bitCnt < bits)
            {
                if (strict) throw new InvalidDataException("LZW end marker was not found");
                break;
            }
            var code = (bitBuf >> (bitCnt - bits)) & ((1 << bits) - 1);
            bitCnt -= bits;
            bitBuf &= (1 << bitCnt) - 1;
            if (strict && first && code != 256)
                throw new InvalidDataException("LZW stream must start with a clear-table code");
            first = false;
            if (code == 256) { Reset(); bits = 9; prev = null; continue; }
            if (code == 257)
            {
                if (strict && bitBuf != 0) throw new InvalidDataException("LZW final byte padding must be zero");
                if (strict && pos != s.Length) throw new InvalidDataException("Unexpected bytes after LZW end marker");
                break;
            }
            byte[] entry;
            if (code < table.Count) entry = table[code];
            else if (prev is not null && (!strict || code == table.Count)) entry = [.. prev, prev[0]];
            else
            {
                if (strict) throw new InvalidDataException("Invalid LZW dictionary code");
                break;
            }
            if (strict && entry.Length > MaxInflated - o.Count)
                throw new PdfParseException("Decompressed stream exceeds the size limit");
            o.AddRange(entry);
            if (prev is not null)
            {
                if (strict && table.Count >= 4096)
                    throw new InvalidDataException("LZW table must be cleared when full");
                table.Add([.. prev, entry[0]]);
            }
            prev = entry;
            var n = table.Count + early;
            bits = n >= 4096 ? 12 : n >= 2048 ? 12 : n >= 1024 ? 11 : n >= 512 ? 10 : 9;
        }
        return o.ToArray();
    }

    static byte[] StrictPredict(byte[] data, PdfDictionary? parameters, Func<PdfObject?, PdfObject?> resolve)
    {
        long Integer(string key, int fallback)
        {
            var value = resolve(parameters?.Get(key));
            if (value is null or PdfNull) return fallback;
            if (value is PdfInteger number) return number.Value;
            throw new InvalidDataException($"Invalid content-stream predictor {key} parameter");
        }
        var predictor = Integer("Predictor", 1);
        if (predictor == 1) return data;
        if (predictor is not (2 or >= 10 and <= 15))
            throw new InvalidDataException("Unknown content-stream predictor");
        var colors = Integer("Colors", 1);
        var bits = Integer("BitsPerComponent", 8);
        var columns = Integer("Columns", 1);
        const long maxRowBits = 1 << 28;
        if (colors < 1 || columns < 1 || bits is not (1 or 2 or 4 or 8 or 16)
            || colors > maxRowBits / bits || columns > maxRowBits / (colors * bits))
            throw new InvalidDataException("Invalid or oversized content-stream predictor parameters");
        var rowBytes = (int)((colors * bits * columns + 7) / 8);
        var frameBytes = rowBytes + (predictor == 2 ? 0 : 1);
        if (data.Length % frameBytes != 0)
            throw new InvalidDataException("Incomplete content-stream predictor row");
        if (predictor != 2)
            for (var offset = 0; offset < data.Length; offset += frameBytes)
                if (data[offset] > 4)
                    throw new InvalidDataException("Unknown content-stream PNG predictor row tag");
        return Predict(data, parameters, resolve);
    }

    static byte[] Predict(byte[] data, PdfDictionary? p, Func<PdfObject?, PdfObject?> r)
    {
        if (p is null) return data;
        var pred = (int)(r(p.Get("Predictor")).AsNumber() ?? 1);
        if (pred <= 1) return data;
        var colors = (int)(r(p.Get("Colors")).AsNumber() ?? 1);
        var bpc = (int)(r(p.Get("BitsPerComponent")).AsNumber() ?? 8);
        var columns = (int)(r(p.Get("Columns")).AsNumber() ?? 1);
        var componentBits = (long)colors * bpc;
        if (colors < 1 || columns < 1 || bpc is not (1 or 2 or 4 or 8 or 16) ||
            componentBits > 1 << 28 || componentBits * columns > 1 << 28)
            throw new PdfParseException("Invalid or oversized predictor parameters");
        var bpp = Math.Max(1, (colors * bpc + 7) / 8);
        var rowBytes = (colors * bpc * columns + 7) / 8;
        if (pred == 2)
        {
            if (bpc != 8) { TiffPredictSubByteOr16(data, colors, bpc, columns, rowBytes); return data; }
            for (var row = 0; row + rowBytes <= data.Length; row += rowBytes)
                for (var i = bpp; i < rowBytes; i++) data[row + i] += data[row + i - bpp];
            return data;
        }
        var rows = data.Length / (rowBytes + 1) + (data.Length % (rowBytes + 1) > 1 ? 1 : 0);
        var res = new byte[rows * rowBytes];
        var zeros = new byte[rowBytes];
        for (var y = 0; y < rows; y++)
        {
            var off = y * (rowBytes + 1);
            if (off >= data.Length) break;
            var ft = data[off];
            var avail = Math.Min(rowBytes, data.Length - off - 1);
            var cur = res.AsSpan(y * rowBytes, rowBytes);
            data.AsSpan(off + 1, avail).CopyTo(cur);
            var prev = y == 0 ? zeros : res.AsSpan((y - 1) * rowBytes, rowBytes);
            switch (ft)
            {
                case 1:
                    for (var i = bpp; i < cur.Length; i++) cur[i] += cur[i - bpp];
                    break;
                case 2:
                    for (var i = 0; i < cur.Length; i++) cur[i] += prev[i];
                    break;
                case 3:
                    for (var i = 0; i < cur.Length; i++) cur[i] += (byte)(((i >= bpp ? cur[i - bpp] : 0) + prev[i]) / 2);
                    break;
                case 4:
                    for (var i = 0; i < cur.Length; i++)
                        cur[i] += (byte)Paeth(i >= bpp ? cur[i - bpp] : 0, prev[i], i >= bpp ? prev[i - bpp] : 0);
                    break;
            }
        }
        return res;
    }

    // TIFF predictor 2 (T.2 differencing per colour component) for bit depths other than 8; rows are byte-padded.
    static void TiffPredictSubByteOr16(byte[] data, int colors, int bpc, int columns, int rowBytes)
    {
        for (var row = 0; row + rowBytes <= data.Length; row += rowBytes)
        {
            if (bpc == 16)
            {
                for (var i = colors * 2; i + 1 < rowBytes; i += 2)
                {
                    var v = (data[row + i] << 8 | data[row + i + 1]) + (data[row + i - colors * 2] << 8 | data[row + i - colors * 2 + 1]);
                    data[row + i] = (byte)(v >> 8); data[row + i + 1] = (byte)v;
                }
                continue;
            }
            var mask = (1 << bpc) - 1; var n = colors * columns;
            for (var s = 0; s < n; s++)
            {
                var bit = s * bpc; var idx = row + (bit >> 3); var sh = 8 - bpc - (bit & 7);
                var previous = 0;
                if (s >= colors)
                {
                    var previousBit = (s - colors) * bpc;
                    previous = data[row + (previousBit >> 3)] >> (8 - bpc - (previousBit & 7)) & mask;
                }
                var v = ((data[idx] >> sh & mask) + previous) & mask;
                data[idx] = (byte)(data[idx] & ~(mask << sh) | v << sh);
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
