using System.Text;

namespace PortableDocument.Core;

public readonly record struct XrefEntry(bool InUse, bool Compressed, long OffsetOrStream, int GenOrIndex);

/// <summary>Reads a PDF file: header, xref tables/streams (incl. hybrid), object streams, incremental updates, and repair.</summary>
public sealed class PdfFile
{
    public byte[] Data { get; }
    public Dictionary<int, XrefEntry> Xref { get; } = [];
    public PdfDictionary Trailer { get; private set; } = new();
    public string Version { get; private set; } = "1.0";
    public bool Repaired { get; private set; }
    readonly Dictionary<int, PdfObject> _cache = [];
    readonly Dictionary<int, ObjStmIndex> _objStmCache = [];
    readonly HashSet<PdfDictionary> _resolvingObjectStreamTypes = [];
    readonly HashSet<int> _readingObjectStreamIndexes = [];
    readonly HashSet<long> _loadingObjectOffsets = [];

    PdfFile(byte[] data) { Data = data; }

    /// <summary>Re-reads the first <paramref name="length"/> bytes of this file (an earlier revision) with the credentials this file was opened with.</summary>
    public PdfFile LoadRevision(long length) => Load(Data[..(int)length], _password, _cert);

    PdfDecryptor? _dec;
    string _password = "";
    System.Security.Cryptography.X509Certificates.X509Certificate2? _cert;
    int _encObj = -1;
    public bool IsEncrypted => _dec is not null;
    public PdfDecryptor? Decryptor => _dec;

    public static PdfFile Open(string path, string password = "", System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null) => Load(File.ReadAllBytes(path), password, certificate);

    public static PdfFile Load(byte[] data, string password = "", System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null)
    {
        var f = new PdfFile(data) { _password = password, _cert = certificate };
        f.ParseHeader();
        try
        {
            f.ReadXrefChain();
            f.SetupEncryption(password);
            if (f.Trailer.Get("Root") is null || f.Resolve(f.Trailer.Get("Root")) is not PdfDictionary) throw new PdfParseException("bad root");
        }
        catch (PdfPasswordException) { throw; }
        catch (Exception e) when (e is PdfParseException or IndexOutOfRangeException or ArgumentException or FormatException or InvalidCastException or KeyNotFoundException or InvalidOperationException or NullReferenceException or OverflowException)
        {
            f.Repair();
        }
        return f;
    }

    void ParseHeader()
    {
        var limit = Math.Min(1024, Data.Length);
        var s = Encoding.Latin1.GetString(Data, 0, limit);
        var i = s.IndexOf("%PDF-", StringComparison.Ordinal);
        if (i < 0) throw new PdfParseException("Missing %PDF- header");
        var end = i + 5;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
        Version = s[(i + 5)..end];
    }

    public PdfDictionary Catalog => Resolve(Trailer.Get("Root")) as PdfDictionary ?? throw new PdfParseException("Document catalog is missing or not a dictionary");

    public PdfObject? Resolve(PdfObject? o)
    {
        var depth = 0;
        while (o is PdfRef r && depth++ < 32) o = GetObject(r);
        return o is PdfRef ? PdfNull.Instance : o;
    }

    /// <summary>Reads one indirect reference, returning null-object for a missing, free or different-generation target without following further references.</summary>
    public PdfObject GetObject(PdfRef reference)
    {
        bool IsCurrent() => Xref.TryGetValue(reference.Number, out var entry) && entry.InUse
            && reference.Generation == (entry.Compressed ? 0 : entry.GenOrIndex);
        if (!IsCurrent()) return PdfNull.Instance;
        var value = GetObject(reference.Number);
        // Loading may repair the xref and discover a different generation.
        return IsCurrent() ? value : PdfNull.Instance;
    }

    public PdfObject GetObject(int num)
    {
        if (_cache.TryGetValue(num, out var c)) return c;
        if (!Xref.TryGetValue(num, out var e) || !e.InUse) return PdfNull.Instance;
        PdfObject result;
        try { result = e.Compressed ? LoadCompressed((int)e.OffsetOrStream, e.GenOrIndex, num) : LoadAt(e.OffsetOrStream, num, e.GenOrIndex); }
        catch (PdfParseException) when (!Repaired) { Repair(); return GetObject(num); }
        if (_dec is not null && !e.Compressed && num != _encObj) result = _dec.Decrypt(result, num, e.GenOrIndex);
        _cache[num] = result;
        return result;
    }

    void SetupEncryption(string password)
    {
        _dec = null; _encObj = -1;
        var raw = Trailer.Get("Encrypt");
        if (raw is null) return;
        if (raw is PdfRef r)
        {
            _encObj = r.Number;
            if (r.Generation == 0 && Xref.TryGetValue(r.Number, out var entry)
                && entry is { InUse: true, Compressed: true })
                throw new PdfParseException("encryption dictionary cannot be compressed");
        }
        var enc = Resolve(raw) as PdfDictionary ?? throw new PdfParseException("bad /Encrypt");
        var id = Resolve(Trailer.Get("ID")) is PdfArray { Items.Count: > 0 } ia && Resolve(ia.Items[0]) is PdfString s ? s.Bytes : [];
        _dec = PdfDecryptor.Create(enc, id, Resolve, password, _cert);
        // Drop anything parsed before the key was known (strings/streams were still ciphertext).
        var keep = _encObj >= 0 && _cache.TryGetValue(_encObj, out var e) ? e : null;
        _cache.Clear(); _objStmCache.Clear();
        if (keep is not null) _cache[_encObj] = keep;
    }

    PdfObject LoadAt(long offset, int expectNum, int? expectGeneration = null)
    {
        if (!_loadingObjectOffsets.Add(offset))
            throw new PdfParseException("cyclic indirect object loading");
        try { return LoadAtCore(offset, expectNum, expectGeneration); }
        finally { _loadingObjectOffsets.Remove(offset); }
    }

    PdfObject LoadAtCore(long offset, int expectNum, int? expectGeneration)
    {
        if (offset < 0 || offset >= Data.Length) throw new PdfParseException("offset out of range");
        var lx = new PdfLexer(Data, (int)offset);
        var n = lx.ReadObject(false) as PdfInteger;
        var g = lx.ReadObject(false) as PdfInteger;
        var kw = lx.ReadObject(false) as PdfKeyword;
        if (n is null || g is null || kw?.Value != "obj" || (expectNum >= 0 && n.Value != expectNum)
            || (expectGeneration is { } generation && g.Value != generation)) throw new PdfParseException($"object header mismatch at {offset}");
        var obj = lx.ReadObject() ?? throw new PdfParseException("eof in object");
        if (obj is PdfDictionary d)
        {
            var save = lx.Pos;
            if (lx.ReadObject() is PdfKeyword { Value: "stream" })
                return ReadStream(d, lx);
            lx.Pos = save;
        }
        return obj is PdfKeyword ? PdfNull.Instance : obj;
    }

    /// <summary>Returns the dictionary of the stream object at <paramref name="offset"/> without copying its data, or null for non-stream or malformed objects.</summary>
    PdfDictionary? PeekStreamDict(long offset, int expectNum, int? expectGeneration = null)
    {
        if (offset < 0 || offset >= Data.Length) return null;
        try
        {
            var lx = new PdfLexer(Data, (int)offset);
            if (lx.ReadObject(false) is not PdfInteger n || lx.ReadObject(false) is not PdfInteger g
                || lx.ReadObject(false) is not PdfKeyword { Value: "obj" } || n.Value != expectNum
                || expectGeneration is { } generation && g.Value != generation) return null;
            return lx.ReadObject() is PdfDictionary d && lx.ReadObject() is PdfKeyword { Value: "stream" } ? d : null;
        }
        catch (PdfParseException) { return null; }
    }

    PdfStream ReadStream(PdfDictionary d, PdfLexer lx)
    {
        var p = lx.Pos;
        if (p < Data.Length && Data[p] == 13) p++;
        if (p < Data.Length && Data[p] == 10) p++;
        if ((p == lx.Pos || Data[p - 1] != 10) && IsObjectStream(d))
            throw new PdfParseException("object stream keyword must be followed by LF or CRLF");
        var length = d.Get("Length");
        if (length is PdfRef) length = Resolve(length);
        long len = length switch
        {
            PdfInteger integer => integer.Value,
            PdfReal real => (long)real.Value,
            _ => -1
        };
        if (len >= 0 && len <= Data.Length - p && EndStreamFollows(p + (int)len))
        {
            if (!ExactEndStreamFollows(p + (int)len) && IsObjectStream(d))
                throw new PdfParseException("object stream endstream must follow Length with at most one EOL");
            return new PdfStream(d, Data.AsSpan(p, (int)len).ToArray());
        }
        var idx = IndexOf(Data, "endstream"u8, p);
        if (idx < 0) idx = Data.Length;
        var end = idx;
        if (end > p && Data[end - 1] == 10) end--;
        if (end > p && Data[end - 1] == 13) end--;
        if (length is PdfInteger { Value: >= 0 } && IsObjectStream(d))
        {
            if (len != end - p)
                throw new PdfParseException("object stream recovered byte count disagrees with Length");
            throw new PdfParseException("object stream is missing a valid endstream marker");
        }
        return new PdfStream(d, Data.AsSpan(p, end - p).ToArray());
    }

    bool ExactEndStreamFollows(int pos)
    {
        if (pos < Data.Length && Data[pos] == 13) pos++;
        if (pos < Data.Length && Data[pos] == 10) pos++;
        return Data.AsSpan(pos).StartsWith("endstream"u8)
            && (pos + 9 == Data.Length || !PdfLexer.IsRegular(Data[pos + 9]));
    }

    bool EndStreamFollows(int pos)
    {
        var lx = new PdfLexer(Data, pos);
        return lx.ReadObject(false) is PdfKeyword { Value: "endstream" };
    }

    static int IndexOf(byte[] d, ReadOnlySpan<byte> pat, int from) { var i = d.AsSpan(from).IndexOf(pat); return i < 0 ? -1 : i + from; }

    (int Count, int First) ObjectStreamBounds(PdfStream stream, int length)
    {
        if (Resolve(stream.Dict.Get("N")) is not PdfInteger { Value: >= 0 and <= int.MaxValue } count
            || Resolve(stream.Dict.Get("First")) is not PdfInteger { Value: >= 0 } first
            || first.Value > length || count.Value > first.Value / 4)
            throw new PdfParseException("bad object stream header");
        return ((int)count.Value, (int)first.Value);
    }

    PdfObject LoadCompressed(int stmNum, int index, int objNum)
    {
        if (!_objStmCache.TryGetValue(stmNum, out var map))
        {
            if (Xref.TryGetValue(stmNum, out var container) && container is { InUse: true, Compressed: false, GenOrIndex: 0 }
                && PeekStreamDict(container.OffsetOrStream, stmNum)?.Get("Length") is PdfRef { Generation: 0 } ownLength
                && ownLength.Number == objNum)
                throw new PdfParseException("object stream Length value cannot be compressed");
            if (GetObject(new PdfRef(stmNum, 0)) is not PdfStream s) throw new PdfParseException("bad object stream");
            map = ReadObjectStreamIndex(s, stmNum);
            _objStmCache[stmNum] = map;
        }
        if (!map.Members.TryGetValue(objNum, out var member) || member.Index != index)
            throw new PdfParseException("object stream member index mismatch");
        if (map.Parsed.TryGetValue(objNum, out var hit)) return hit;
        var start = map.First + member.Offset;
        if (member.Offset > 0 && PdfLexer.IsRegular(map.Data[start - 1]) && PdfLexer.IsRegular(map.Data[start]))
            throw new PdfParseException("object stream member starts inside a token");
        // Objects are parsed on demand; a malformed member only affects requests for that member.
        var lexer = new PdfLexer(map.Data, start, objectStreamMember: true,
            end: map.First + member.End);
        var parsed = lexer.ReadObject();
        if (lexer.Pos > map.First + member.End)
            throw new PdfParseException("object stream member exceeds its boundary");
        if (parsed is null or PdfKeyword)
            throw new PdfParseException("object stream member must contain an object value");
        if (HasKeywordValue(parsed))
            throw new PdfParseException("object stream member contains a keyword value");
        if (parsed is PdfRef)
            throw new PdfParseException("object stream member cannot be an indirect reference");
        if (!lexer.SkipWhite(map.First + member.End))
            throw new PdfParseException("object stream member comment crosses its boundary");
        if (lexer.Pos < map.First + member.End)
        {
            if (parsed is PdfDictionary && lexer.ReadObject(false) is PdfKeyword { Value: "stream" }
                && lexer.Pos <= map.First + member.End)
                throw new PdfParseException("stream objects cannot be stored in an object stream");
            throw new PdfParseException("extra data after object stream member value");
        }
        map.Parsed[objNum] = parsed;
        return parsed;
    }

    static bool HasKeywordValue(PdfObject value) => value switch
    {
        PdfKeyword => true,
        PdfArray array => array.Items.Any(HasKeywordValue),
        PdfDictionary dictionary => dictionary.Entries.Values.Any(HasKeywordValue),
        _ => false
    };

    bool IsObjectStream(PdfDictionary dictionary)
    {
        if (!_resolvingObjectStreamTypes.Add(dictionary))
            throw new PdfParseException("cyclic object stream type");
        try { return Resolve(dictionary.Get("Type")) is PdfName { Value: "ObjStm" }; }
        finally { _resolvingObjectStreamTypes.Remove(dictionary); }
    }

    ObjStmIndex ReadObjectStreamIndex(PdfStream stream, int streamNumber)
    {
        if (!_readingObjectStreamIndexes.Add(streamNumber))
            throw new PdfParseException("cyclic object stream metadata");
        try { return ReadObjectStreamIndexCore(stream, streamNumber); }
        finally { _readingObjectStreamIndexes.Remove(streamNumber); }
    }

    ObjStmIndex ReadObjectStreamIndexCore(PdfStream stream, int streamNumber)
    {
        if (!IsObjectStream(stream.Dict))
            throw new PdfParseException("bad object stream type");
        ValidateObjectStreamCollection(stream.Dict);
        var data = DecodeStream(stream);
        var (n, first) = ObjectStreamBounds(stream, data.Length);
        var lx = new PdfLexer(data, 0, objectStreamMember: true);
        var map = new ObjStmIndex(data, first);
        long previousOffset = -1;
        var previousNumber = -1;
        for (var i = 0; i < n; i++)
        {
            if (lx.ReadObject(false) is not PdfInteger on || lx.ReadObject(false) is not PdfInteger off ||
                lx.Pos > first || on.Value <= 0 || on.Value > int.MaxValue ||
                off.Value < 0 || off.Value >= data.Length - first)
                throw new PdfParseException("bad object stream member offset");
            if (on.Value == streamNumber)
                throw new PdfParseException("object stream cannot contain its own stream object");
            if (i == 0 && off.Value != 0)
                throw new PdfParseException("first object stream member offset must be zero");
            if (off.Value <= previousOffset)
                throw new PdfParseException("object stream member offsets must increase");
            previousOffset = off.Value;
            if (!map.Members.TryAdd((int)on.Value, (i, (int)off.Value, data.Length - first)))
                throw new PdfParseException("duplicate object stream member number");
            if (previousNumber >= 0)
            {
                var previous = map.Members[previousNumber];
                map.Members[previousNumber] = (previous.Index, previous.Offset, (int)off.Value);
            }
            previousNumber = (int)on.Value;
        }
        if (!lx.SkipWhite(first) && n > 0)
            throw new PdfParseException("object stream header comment crosses First");
        if (lx.Pos != first)
            throw new PdfParseException("extra data after object stream header pairs");
        if (stream.Dict.Get("Length") is PdfRef { Generation: 0 } length
            && (map.Members.ContainsKey(length.Number)
                || Xref.TryGetValue(length.Number, out var lengthEntry) && lengthEntry is { InUse: true, Compressed: true }))
            throw new PdfParseException("object stream Length value cannot be compressed");
        if (Resolve(stream.Dict.Get("Length")) is not PdfInteger { Value: >= 0 })
            throw new PdfParseException("object stream Length must be a nonnegative integer");
        return map;
    }

    void ValidateObjectStreamCollection(PdfDictionary dictionary)
    {
        var seen = new HashSet<int>();
        while (dictionary.Get("Extends") is { } extends && extends is not PdfNull)
        {
            if (extends is not PdfRef { Generation: 0 } reference
                || !Xref.TryGetValue(reference.Number, out var entry)
                || entry is not { InUse: true, Compressed: false, GenOrIndex: 0 }
                || PeekStreamDict(entry.OffsetOrStream, reference.Number, 0) is not { } parent
                || !IsObjectStream(parent))
                throw new PdfParseException("object stream Extends must reference an object stream");
            if (!seen.Add(reference.Number))
                throw new PdfParseException("cyclic object stream Extends links");
            dictionary = parent;
        }
    }

    sealed class ObjStmIndex(byte[] data, int first)
    {
        public readonly byte[] Data = data;
        public readonly int First = first;
        public readonly Dictionary<int, (int Index, int Offset, int End)> Members = [];
        public readonly Dictionary<int, PdfObject> Parsed = [];
    }

    public byte[] DecodeStream(PdfStream s) => DecodeStream(s, out _, out _);

    public byte[] DecodeStream(PdfStream s, out string? imageFilter, out PdfDictionary? imageParms)
    {
        var filters = PdfFilters.GetFilters(s.Dict, Resolve);
        var (data, f, p) = PdfFilters.Decode(s.RawData, filters, Resolve);
        imageFilter = f; imageParms = p;
        return data;
    }

    void ReadXrefChain()
    {
        long? ChainOffset(PdfObject? raw, string key)
        {
            if (raw is null or PdfNull) return null;
            if (raw is not PdfInteger n || n.Value < 0 || n.Value >= Data.Length)
                throw new PdfParseException($"bad /{key} xref offset");
            return n.Value;
        }

        var tail = Math.Max(0, Data.Length - 2048);
        var idx = Data.AsSpan(tail).LastIndexOf("startxref"u8);
        if (idx < 0) throw new PdfParseException("no startxref");
        var lx = new PdfLexer(Data, tail + idx + 9);
        if (lx.ReadObject(false) is not PdfInteger so) throw new PdfParseException("bad startxref");
        var visited = new HashSet<long>();
        var pos = so.Value;
        var first = true;
        while (pos >= 0 && visited.Add(pos))
        {
            var trailer = ReadXrefSection(pos);
            if (first) { Trailer = trailer; first = false; }
            else foreach (var kv in trailer.Entries) Trailer.Entries.TryAdd(kv.Key, kv.Value);
            if (ChainOffset(trailer.Get("XRefStm"), "XRefStm") is { } hx && visited.Add(hx))
            {
                // Hybrid files (7.5.8.4): the table marks compressed objects free; the XRefStm of the same section supplies them.
                var hidden = _tableFree; _tableFree = null;
                ReadXrefSection(hx, hidden);
            }
            pos = ChainOffset(trailer.Get("Prev"), "Prev") ?? -1;
        }
    }

    bool TryFixedXrefEntry(int p, out long offset, out int gen, out bool inUse)
    {
        offset = 0; gen = 0; inUse = false;
        if (p + 19 > Data.Length || Data[p + 10] != ' ' || Data[p + 16] != ' ') return false;
        var kind = Data[p + 17];
        if ((kind != 'n' && kind != 'f') || Data[p + 18] is not (32 or 10 or 13)) return false;
        long o = 0;
        for (var k = 0; k < 10; k++) { var c = Data[p + k] - '0'; if ((uint)c > 9) return false; o = o * 10 + c; }
        var g = 0;
        for (var k = 11; k < 16; k++) { var c = Data[p + k] - '0'; if ((uint)c > 9) return false; g = g * 10 + c; }
        offset = o; gen = g; inUse = kind == 'n';
        return true;
    }

    HashSet<int>? _tableFree;

    PdfDictionary ReadXrefSection(long pos, HashSet<int>? overridable = null)
    {
        if (pos < 0 || pos >= Data.Length) throw new PdfParseException("xref offset out of range");
        var lx = new PdfLexer(Data, (int)pos);
        lx.SkipWhite();
        if (lx.Pos + 4 <= Data.Length && Data.AsSpan(lx.Pos, 4).SequenceEqual("xref"u8))
        {
            lx.Pos += 4;
            _tableFree = [];
            var sectionNumbers = new HashSet<int>();
            while (true)
            {
                var save = lx.Pos;
                var a = lx.ReadObject(false);
                if (a is PdfKeyword { Value: "trailer" }) break;
                if (a is not PdfInteger start || lx.ReadObject(false) is not PdfInteger count) { lx.Pos = save; throw new PdfParseException("bad xref subsection"); }
                if (start.Value < 0 || start.Value > int.MaxValue || count.Value < 0 ||
                    count.Value > (long)int.MaxValue - start.Value + 1)
                    throw new PdfParseException("xref subsection range exceeds supported object numbers");
                if (count.Value is > 0 and < 8_000_000)
                    Xref.EnsureCapacity(Xref.Count + (int)Math.Min(count.Value, (Data.Length - lx.Pos) / 6));
                for (long i = 0; i < count.Value; i++)
                {
                    var num = (int)(start.Value + i);
                    if (!sectionNumbers.Add(num)) throw new PdfParseException("duplicate object number in xref section");
                    lx.SkipWhite();
                    if (TryFixedXrefEntry(lx.Pos, out var foff, out var fgen, out var fuse))
                    {
                        lx.Pos += 18;
                        if (!Xref.ContainsKey(num))
                        {
                            Xref[num] = new XrefEntry(fuse, false, foff, fgen);
                            if (!fuse) _tableFree.Add(num);
                        }
                        continue;
                    }
                    if (lx.ReadObject(false) is not PdfInteger off || lx.ReadObject(false) is not PdfInteger gen
                        || lx.ReadObject(false) is not PdfKeyword k || k.Value is not ("n" or "f"))
                        throw new PdfParseException("bad xref entry");
                    if (gen.Value < 0 || gen.Value > int.MaxValue)
                        throw new PdfParseException("xref generation exceeds supported range");
                    if (!Xref.ContainsKey(num)) { Xref[num] = new XrefEntry(k.Value == "n", false, off.Value, (int)gen.Value); if (k.Value != "n") _tableFree.Add(num); }
                }
            }
            return lx.ReadObject() as PdfDictionary ?? throw new PdfParseException("bad trailer");
        }
        var obj = LoadAt(pos, -1);
        if (obj is not PdfStream xs || xs.Dict.Get("Type").AsName() != "XRef") throw new PdfParseException("not an xref stream");
        var data = DecodeStream(xs);
        if (xs.Dict.Get("W") is not PdfArray { Items.Count: >= 3 } widths)
            throw new PdfParseException("bad xref stream /W");
        var w = widths.Items.Select(x => x is PdfInteger { Value: >= 0 and <= 8 } n
            ? (int)n.Value : throw new PdfParseException("bad xref stream /W field width")).ToArray();
        if (xs.Dict.Get("Size") is not PdfInteger { Value: > 0 and <= (long)int.MaxValue + 1 } size)
            throw new PdfParseException("bad xref stream /Size");
        var rawIndex = xs.Dict.Get("Index");
        if (rawIndex is not (null or PdfNull or PdfArray))
            throw new PdfParseException("bad xref stream /Index");
        var index = rawIndex is PdfArray ia ? ia.Items.Select(x => x is PdfInteger n
            ? n.Value : throw new PdfParseException("non-integer xref stream /Index")).ToArray() : [0L, size.Value];
        if (index.Length % 2 != 0) throw new PdfParseException("unpaired xref stream /Index");
        long end = 0;
        long requiredRows = 0;
        for (var s = 0; s < index.Length; s += 2)
        {
            var start = index[s];
            var count = index[s + 1];
            if (start < end || start < 0 || start > int.MaxValue || count < 0 || count > size.Value - start)
                throw new PdfParseException("bad xref stream /Index range");
            end = start + count;
            requiredRows += count;
        }
        var rowLen = w.Sum();
        if (rowLen == 0) throw new PdfParseException("empty xref stream rows");
        if (requiredRows > data.Length / rowLen) throw new PdfParseException("truncated xref stream rows");
        if (rowLen > 0 && data.Length / rowLen is > 0 and < 8_000_000) Xref.EnsureCapacity(Xref.Count + data.Length / rowLen);
        var woff = new int[w.Length];
        for (var wi = 1; wi < w.Length; wi++) woff[wi] = woff[wi - 1] + w[wi - 1];
        var p = 0;
        for (var s = 0; s + 1 < index.Length; s += 2)
            for (long i = 0; i < index[s + 1]; i++, p += rowLen)
            {
                ulong Field(int idx2, ulong def)
                {
                    if (w[idx2] == 0) return def;
                    var off = p + woff[idx2];
                    ulong v = 0;
                    for (var k = 0; k < w[idx2]; k++) v = (v << 8) | data[off + k];
                    return v;
                }
                var type = Field(0, 1);
                var num = (int)(index[s] + i);
                if (Xref.ContainsKey(num) && !(overridable?.Contains(num) ?? false)) continue;
                var value = Field(1, 0);
                var generationOrIndex = Field(2, 0);
                if (type <= 2 && (generationOrIndex > int.MaxValue
                    || (type == 1 && value > long.MaxValue)
                    || (type == 2 && value > int.MaxValue)))
                    throw new PdfParseException("xref stream entry exceeds supported field range");
                Xref[num] = type switch
                {
                    0 => new XrefEntry(false, false, 0, (int)generationOrIndex),
                    1 => new XrefEntry(true, false, (long)value, (int)generationOrIndex),
                    2 => new XrefEntry(true, true, (long)value, (int)generationOrIndex),
                    _ => new XrefEntry(false, false, 0, 0)
                };
            }
        return xs.Dict;
    }

    /// <summary>Rebuilds the xref by scanning raw bytes for "N G obj".</summary>
    void Repair()
    {
        Repaired = true;
        Xref.Clear(); _cache.Clear(); _objStmCache.Clear();
        var at = 0;
        var from = 0;
        while (from < Data.Length)
        {
            var rel = Data.AsSpan(from).IndexOf("obj"u8);
            if (rel < 0) break;
            var objAt = from + rel;
            from = objAt + 3;
            if (objAt + 3 < Data.Length && char.IsAsciiLetter((char)Data[objAt + 3])) continue;
            var q = objAt;
            var wsEnd = q;
            while (q > at && Data[q - 1] is 32 or 9 or 13 or 10) q--;
            if (q == wsEnd) continue;
            var genEnd = q;
            while (q > at && char.IsAsciiDigit((char)Data[q - 1])) q--;
            var genLen = genEnd - q;
            if (genLen is < 1 or > 5) continue;
            var genStart = q;
            var ws2End = q;
            while (q > at && Data[q - 1] is 32 or 9 or 13 or 10) q--;
            if (q == ws2End) continue;
            var numEnd = q;
            while (q > at && char.IsAsciiDigit((char)Data[q - 1])) q--;
            var numLen = numEnd - q;
            if (numLen is < 1 or > 10 || (q > 0 && char.IsAsciiDigit((char)Data[q - 1]))) continue;
            if (!int.TryParse(Encoding.ASCII.GetString(Data, q, numLen), out var num)
                || !int.TryParse(Encoding.ASCII.GetString(Data, genStart, genLen), out var gen))
            {
                at = objAt + 3;
                continue;
            }
            Xref[num] = new XrefEntry(true, false, q, gen);
            at = objAt + 3;
        }

        var trailers = new List<PdfDictionary>();
        var tpos = 0;
        while ((tpos = IndexOf(Data, "trailer"u8, tpos)) >= 0)
        {
            tpos += 7;
            try { if (new PdfLexer(Data, tpos).ReadObject() is PdfDictionary td) trailers.Add(td); } catch (PdfParseException) { }
        }
        var merged = new PdfDictionary();
        foreach (var td in trailers) foreach (var kv in td.Entries) merged.Entries[kv.Key] = kv.Value;

        // Object streams and xref streams: index compressed objects and pick up trailer keys.
        // XRef streams first (they are never encrypted) so /Encrypt is known before object streams are read.
        foreach (var num in Xref.Keys.ToList())
        {
            if (PeekStreamDict(Xref[num].OffsetOrStream, num) is { } xd && xd.Get("Type").AsName() == "XRef") foreach (var kv in xd.Entries) merged.Entries.TryAdd(kv.Key, kv.Value);
        }
        Trailer = merged;
        SetupEncryption(_password);
        foreach (var num in Xref.Keys.ToList())
        {
            var entry = Xref[num];
            if (entry is not { InUse: true, Compressed: false, GenOrIndex: 0 }
                || PeekStreamDict(entry.OffsetOrStream, num, 0) is not { } candidate) continue;
            PdfObject o;
            try
            {
                if (!IsObjectStream(candidate)) continue;
                o = LoadAt(entry.OffsetOrStream, num, 0);
            }
            catch (PdfParseException) { continue; }
            if (o is not PdfStream st) continue;
            if (_dec is not null && num != _encObj) st = (PdfStream)_dec.Decrypt(st, num, 0);
            try
            {
                var map = ReadObjectStreamIndex(st, num);
                foreach (var member in map.Members)
                    Xref[member.Key] = new XrefEntry(true, true, num, member.Value.Index);
            }
            catch (Exception e) when (e is PdfParseException or NotSupportedException or InvalidOperationException or ArgumentException or OverflowException) { }
        }
        if (merged.Get("Root") is null)
            foreach (var num in Xref.Keys)
                if (GetObject(num) is PdfDictionary { } d && d.Get("Type").AsName() == "Catalog") { merged.Entries["Root"] = new PdfRef(num, 0); break; }
        Trailer = merged;
        if (Trailer.Get("Root") is null) throw new PdfParseException("Unable to locate document catalog");
    }
}
