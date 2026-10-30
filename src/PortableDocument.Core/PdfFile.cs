using System.Text;
using GregsEx;

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
    readonly Dictionary<int, Dictionary<int, PdfObject>> _objStmCache = [];

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
        while (o is PdfRef r && depth++ < 32) o = GetObject(r.Number);
        return o is PdfRef ? PdfNull.Instance : o;
    }

    public PdfObject GetObject(int num)
    {
        if (_cache.TryGetValue(num, out var c)) return c;
        if (!Xref.TryGetValue(num, out var e) || !e.InUse) return PdfNull.Instance;
        PdfObject result;
        try { result = e.Compressed ? LoadCompressed((int)e.OffsetOrStream, e.GenOrIndex, num) : LoadAt(e.OffsetOrStream, num); }
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
        if (raw is PdfRef r) _encObj = r.Number;
        var enc = Resolve(raw) as PdfDictionary ?? throw new PdfParseException("bad /Encrypt");
        var id = Resolve(Trailer.Get("ID")) is PdfArray { Items.Count: > 0 } ia && Resolve(ia.Items[0]) is PdfString s ? s.Bytes : [];
        _dec = PdfDecryptor.Create(enc, id, Resolve, password, _cert);
        // Drop anything parsed before the key was known (strings/streams were still ciphertext).
        var keep = _encObj >= 0 && _cache.TryGetValue(_encObj, out var e) ? e : null;
        _cache.Clear(); _objStmCache.Clear();
        if (keep is not null) _cache[_encObj] = keep;
    }

    PdfObject LoadAt(long offset, int expectNum)
    {
        if (offset < 0 || offset >= Data.Length) throw new PdfParseException("offset out of range");
        var lx = new PdfLexer(Data, (int)offset);
        var n = lx.ReadObject(false) as PdfInteger;
        var g = lx.ReadObject(false) as PdfInteger;
        var kw = lx.ReadObject(false) as PdfKeyword;
        if (n is null || g is null || kw?.Value != "obj" || (expectNum >= 0 && n.Value != expectNum)) throw new PdfParseException($"object header mismatch at {offset}");
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

    PdfStream ReadStream(PdfDictionary d, PdfLexer lx)
    {
        var p = lx.Pos;
        if (p < Data.Length && Data[p] == 13) p++;
        if (p < Data.Length && Data[p] == 10) p++;
        long len = -1;
        if (d.Get("Length") is { } lo)
        {
            // Avoid resolving Length via a ref that points at this very object.
            var rv = lo is PdfRef ? Resolve(lo) : lo;
            if (rv.AsNumber() is { } dl) len = (long)dl;
        }
        if (len >= 0 && p + len <= Data.Length && EndStreamFollows(p + (int)len))
            return new PdfStream(d, Data.AsSpan(p, (int)len).ToArray());
        var idx = IndexOf(Data, "endstream"u8, p);
        if (idx < 0) idx = Data.Length;
        var end = idx;
        if (end > p && Data[end - 1] == 10) end--;
        if (end > p && Data[end - 1] == 13) end--;
        return new PdfStream(d, Data.AsSpan(p, end - p).ToArray());
    }

    bool EndStreamFollows(int pos)
    {
        var lx = new PdfLexer(Data, pos);
        return lx.ReadObject(false) is PdfKeyword { Value: "endstream" };
    }

    static int IndexOf(byte[] d, ReadOnlySpan<byte> pat, int from) { var i = d.AsSpan(from).IndexOf(pat); return i < 0 ? -1 : i + from; }

    PdfObject LoadCompressed(int stmNum, int index, int objNum)
    {
        if (!_objStmCache.TryGetValue(stmNum, out var map))
        {
            map = [];
            if (GetObject(stmNum) is not PdfStream s) throw new PdfParseException("bad object stream");
            var data = DecodeStream(s);
            var n = (int)(Resolve(s.Dict.Get("N")).AsNumber() ?? 0);
            var first = (int)(Resolve(s.Dict.Get("First")).AsNumber() ?? 0);
            var lx = new PdfLexer(data);
            var pairs = new List<(int, int)>();
            for (var i = 0; i < n; i++)
            {
                if (lx.ReadObject(false) is not PdfInteger on || lx.ReadObject(false) is not PdfInteger off) break;
                pairs.Add(((int)on.Value, (int)off.Value));
            }
            foreach (var (on, off) in pairs)
            {
                var ol = new PdfLexer(data, first + off);
                map[on] = ol.ReadObject() ?? PdfNull.Instance;
            }
            _objStmCache[stmNum] = map;
        }
        return map.GetValueOrDefault(objNum) ?? PdfNull.Instance;
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
            if (trailer.Get("XRefStm") is PdfInteger hx && visited.Add(hx.Value)) ReadXrefSection(hx.Value);
            pos = trailer.Get("Prev") is { } pv && pv.AsNumber() is { } pn ? (long)pn : -1;
        }
    }

    PdfDictionary ReadXrefSection(long pos)
    {
        if (pos < 0 || pos >= Data.Length) throw new PdfParseException("xref offset out of range");
        var lx = new PdfLexer(Data, (int)pos);
        lx.SkipWhite();
        if (lx.Pos + 4 <= Data.Length && Data.AsSpan(lx.Pos, 4).SequenceEqual("xref"u8))
        {
            lx.Pos += 4;
            while (true)
            {
                var save = lx.Pos;
                var a = lx.ReadObject(false);
                if (a is PdfKeyword { Value: "trailer" }) break;
                if (a is not PdfInteger start || lx.ReadObject(false) is not PdfInteger count) { lx.Pos = save; throw new PdfParseException("bad xref subsection"); }
                for (var i = 0; i < count.Value; i++)
                {
                    if (lx.ReadObject(false) is not PdfInteger off || lx.ReadObject(false) is not PdfInteger gen || lx.ReadObject(false) is not PdfKeyword k)
                        throw new PdfParseException("bad xref entry");
                    var num = (int)(start.Value + i);
                    if (!Xref.ContainsKey(num)) Xref[num] = new XrefEntry(k.Value == "n", false, off.Value, (int)gen.Value);
                }
            }
            return lx.ReadObject() as PdfDictionary ?? throw new PdfParseException("bad trailer");
        }
        var obj = LoadAt(pos, -1);
        if (obj is not PdfStream xs || xs.Dict.Get("Type").AsName() != "XRef") throw new PdfParseException("not an xref stream");
        var data = DecodeStream(xs);
        var w = ((PdfArray)xs.Dict.Get("W")!).Items.Select(x => (int)x.AsNumber()!.Value).ToArray();
        var size = (int)xs.Dict.Get("Size").AsNumber()!.Value;
        var index = xs.Dict.Get("Index") is PdfArray ia ? ia.Items.Select(x => (int)x.AsNumber()!.Value).ToArray() : [0, size];
        var rowLen = w.Sum();
        var p = 0;
        for (var s = 0; s + 1 < index.Length; s += 2)
            for (var i = 0; i < index[s + 1]; i++, p += rowLen)
            {
                if (p + rowLen > data.Length) break;
                long Field(int idx2, long def)
                {
                    if (w[idx2] == 0) return def;
                    var off = p + w.Take(idx2).Sum();
                    long v = 0;
                    for (var k = 0; k < w[idx2]; k++) v = (v << 8) | data[off + k];
                    return v;
                }
                var type = Field(0, 1);
                var num = index[s] + i;
                if (Xref.ContainsKey(num)) continue;
                Xref[num] = type switch
                {
                    0 => new XrefEntry(false, false, 0, (int)Field(2, 0)),
                    1 => new XrefEntry(true, false, Field(1, 0), (int)Field(2, 0)),
                    2 => new XrefEntry(true, true, Field(1, 0), (int)Field(2, 0)),
                    _ => new XrefEntry(false, false, 0, 0)
                };
            }
        return xs.Dict;
    }

    /// <summary>Rebuilds the xref by scanning raw bytes for "N G obj" using a GregsEx PCRE2 byte pattern.</summary>
    void Repair()
    {
        Repaired = true;
        Xref.Clear(); _cache.Clear(); _objStmCache.Clear();
        var objPat = RegexPattern.Parse(@"(?<![0-9])([0-9]{1,10})[ \t\r\n]+([0-9]{1,5})[ \t\r\n]+obj(?![a-zA-Z])", RegexProfiles.Pcre2Raw8, new RegexCompileOptions(RegexInputKind.Bytes));
        using var ws = objPat.CreateWorkspace();
        var caps = new RegexCapture[objPat.GroupCount];
        var at = 0;
        ReadOnlySpan<byte> span = Data;
        while (at < Data.Length && ws.FindCaptures(span, at, caps, out var range) == RegexSearchStatus.Match)
        {
            if (!int.TryParse(Encoding.ASCII.GetString(Data, caps[1].Index, caps[1].Length), out var num)
                || !int.TryParse(Encoding.ASCII.GetString(Data, caps[2].Index, caps[2].Length), out var gen))
            {
                at = Math.Max(range.Index + range.Length, at + 1);
                continue;
            }
            Xref[num] = new XrefEntry(true, false, range.Index, gen);
            at = Math.Max(range.Index + range.Length, at + 1);
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
            PdfObject o;
            try { o = LoadAt(Xref[num].OffsetOrStream, num); } catch (PdfParseException) { continue; }
            if (o is PdfStream { } xs && xs.Dict.Get("Type").AsName() == "XRef") foreach (var kv in xs.Dict.Entries) merged.Entries.TryAdd(kv.Key, kv.Value);
        }
        Trailer = merged;
        SetupEncryption(_password);
        foreach (var num in Xref.Keys.ToList())
        {
            PdfObject o;
            try { o = LoadAt(Xref[num].OffsetOrStream, num); } catch (PdfParseException) { continue; }
            if (o is not PdfStream st) continue;
            if (_dec is not null && num != _encObj) st = (PdfStream)_dec.Decrypt(st, num, Xref[num].GenOrIndex);
            var type = st.Dict.Get("Type").AsName();
            if (type == "ObjStm")
            {
                try
                {
                    var data = DecodeStream(st);
                    var n = (int)(st.Dict.Get("N").AsNumber() ?? 0);
                    var lx = new PdfLexer(data);
                    for (var i = 0; i < n; i++)
                    {
                        if (lx.ReadObject(false) is not PdfInteger on || lx.ReadObject(false) is not PdfInteger) break;
                        Xref[(int)on.Value] = new XrefEntry(true, true, num, i);
                    }
                }
                catch (Exception e) when (e is PdfParseException or NotSupportedException or InvalidOperationException or ArgumentException or OverflowException) { }
            }
        }
        if (merged.Get("Root") is null)
            foreach (var num in Xref.Keys)
                if (GetObject(num) is PdfDictionary { } d && d.Get("Type").AsName() == "Catalog") { merged.Entries["Root"] = new PdfRef(num, 0); break; }
        Trailer = merged;
        if (Trailer.Get("Root") is null) throw new PdfParseException("Unable to locate document catalog");
    }
}
