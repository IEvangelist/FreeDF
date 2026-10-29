using System.Buffers.Binary;
using System.Text;
using Pdf.Content;

/// <summary>One installed font face: TrueType (glyf) or CFF-flavoured OpenType, from a single-font file or a .ttc/.otc collection member (<paramref name="Face"/> ≥ 0).</summary>
public sealed record FontFace(string Family, bool Bold, bool Italic, string Path, string PostScript, int Face = -1)
{
    public string Key => Face < 0 ? Path : $"{Path}#{Face}";
}

/// <summary>Installed system fonts grouped by family (name ID 1), found by reading only each file's table directory, name, head and OS/2 tables.</summary>
public sealed class FontCatalog
{
    public static readonly string[] Standard = ["Helvetica", "Times", "Courier", "Symbol", "ZapfDingbats"];

    readonly Dictionary<string, List<FontFace>> _families = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (byte[] Data, TrueTypeFont Font)> _loaded = [];

    public static FontCatalog Scan(IEnumerable<string>? extraDirs = null)
    {
        var c = new FontCatalog();
        var dirs = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.Fonts) };
        dirs.AddRange((Environment.GetEnvironmentVariable("PDF_EDITOR_FONT_DIRS") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        if (extraDirs is not null) dirs.AddRange(extraDirs);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dirs.Add(System.IO.Path.Combine(home, "Microsoft", "Windows", "Fonts"));
        dirs.AddRange(["/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts", "/System/Library/Fonts",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")]);
        foreach (var dir in dirs.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".ttf" or ".otf" or ".ttc" or ".otc")) continue;
                try { foreach (var face in Read(f)) c.Add(face); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        return c;
    }

    void Add(FontFace f)
    {
        if (!_families.TryGetValue(f.Family, out var l)) _families[f.Family] = l = [];
        if (!l.Any(x => x.Bold == f.Bold && x.Italic == f.Italic)) l.Add(f);
    }

    /// <summary>Family names with the variants each offers, standard PDF fonts first.</summary>
    public IEnumerable<object> List() =>
        Standard.Select(s => (object)new { family = s, standard = true, bold = s != "Symbol" && s != "ZapfDingbats", italic = s != "Symbol" && s != "ZapfDingbats" })
        .Concat(_families.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .Select(k => (object)new { family = k.Key, standard = false, bold = k.Value.Any(v => v.Bold), italic = k.Value.Any(v => v.Italic) }));

    public bool Has(string? family) => family is not null && _families.ContainsKey(family);

    /// <summary>Best face for the request: exact style, then the closest available.</summary>
    public FontFace? Resolve(string? family, bool bold, bool italic)
    {
        if (family is null || !_families.TryGetValue(family, out var l)) return null;
        return l.OrderBy(v => (v.Bold != bold ? 2 : 0) + (v.Italic != italic ? 1 : 0)).First();
    }

    /// <summary>The font as a standalone sfnt (a collection member is extracted) and its parsed form.</summary>
    public (byte[] Data, TrueTypeFont Font) Load(FontFace f)
    {
        lock (_loaded)
        {
            if (!_loaded.TryGetValue(f.Key, out var v))
            {
                var raw = File.ReadAllBytes(f.Path);
                _loaded[f.Key] = v = (f.Face >= 0 ? ExtractFace(raw, f.Face) : raw, null!);
            }
            if (v.Font is null) _loaded[f.Key] = v = (v.Data, new TrueTypeFont(v.Data));
            return v;
        }
    }

    /// <summary>Copies one member of a TrueType collection into a standalone sfnt so the rest of the pipeline needs no collection awareness.</summary>
    public static byte[] ExtractFace(byte[] ttc, int face)
    {
        uint U32(int o) => BinaryPrimitives.ReadUInt32BigEndian(ttc.AsSpan(o));
        var count = (int)U32(8);
        if (face < 0 || face >= count) throw new ArgumentException("No such font in the collection.");
        var dir = (int)U32(12 + face * 4);
        var n = BinaryPrimitives.ReadUInt16BigEndian(ttc.AsSpan(dir + 4));
        var hdrLen = 12 + n * 16;
        var output = new MemoryStream();
        var hdr = new byte[hdrLen];
        Array.Copy(ttc, dir, hdr, 0, hdrLen);
        var off = hdrLen;
        var body = new MemoryStream();
        for (var i = 0; i < n; i++)
        {
            var e = 12 + i * 16;
            var o = (int)U32(dir + e + 8); var l = (int)U32(dir + e + 12);
            BinaryPrimitives.WriteUInt32BigEndian(hdr.AsSpan(e + 8), (uint)(off + body.Length));
            body.Write(ttc, o, l);
            while (body.Length % 4 != 0) body.WriteByte(0);
        }
        output.Write(hdr); body.WriteTo(output);
        return output.ToArray();
    }

    static readonly string[] FallbackFamilies =
    [
        "Segoe UI", "Arial", "Segoe UI Symbol", "Microsoft YaHei", "Malgun Gothic", "MS Gothic", "Yu Gothic", "Noto Sans", "Noto Sans CJK SC",
        "DejaVu Sans", "Liberation Sans", "Arial Unicode MS", "Cambria Math",
    ];
    readonly Dictionary<(int, bool, bool), FontFace?> _fallback = [];

    bool Covers(FontFace f, int rune)
    {
        try { return Load(f).Font.LookupAny(rune) > 0; }
        catch (Exception e) when (e is IOException or ArgumentException or IndexOutOfRangeException or NotSupportedException or InvalidDataException) { return false; }
    }

    /// <summary>An installed face that has a glyph for <paramref name="rune"/>: well-known broad-coverage families first, then any face.</summary>
    public FontFace? Fallback(int rune, bool bold, bool italic)
    {
        lock (_fallback)
        {
            if (_fallback.TryGetValue((rune, bold, italic), out var hit)) return hit;
            FontFace? found = null;
            foreach (var fam in FallbackFamilies)
                if (Resolve(fam, bold, italic) is { } f && Covers(f, rune)) { found = f; break; }
            found ??= _families.Values.SelectMany(l => l).FirstOrDefault(f => Covers(f, rune));
            return _fallback[(rune, bold, italic)] = found;
        }
    }

    static IEnumerable<FontFace> Read(string path)
    {
        using var fs = File.OpenRead(path);
        var hdr = new byte[12];
        if (fs.Read(hdr) < 12) yield break;
        if (BinaryPrimitives.ReadUInt32BigEndian(hdr) == 0x74746366)
        {
            var count = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(hdr.AsSpan(8)), 256u);
            var offsets = new int[count];
            var raw = new byte[count * 4]; fs.ReadExactly(raw);
            for (var i = 0; i < count; i++) offsets[i] = (int)BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(i * 4));
            for (var i = 0; i < count; i++)
                if (ReadFace(fs, offsets[i], path, i) is { } f) yield return f;
            yield break;
        }
        if (ReadFace(fs, 0, path, -1) is { } single) yield return single;
    }

    static FontFace? ReadFace(FileStream fs, long dirOffset, string path, int faceIndex)
    {
        fs.Seek(dirOffset, SeekOrigin.Begin);
        Span<byte> hdr = stackalloc byte[12];
        if (fs.Read(hdr) < 12) return null;
        var tag = BinaryPrimitives.ReadUInt32BigEndian(hdr);
        if (tag is not (0x00010000 or 0x74727565 or 0x4F54544F)) return null;
        var n = BinaryPrimitives.ReadUInt16BigEndian(hdr[4..]);
        var dir = new byte[n * 16]; fs.ReadExactly(dir);
        var t = new Dictionary<string, (int Off, int Len)>();
        for (var i = 0; i < n; i++)
            t[Encoding.ASCII.GetString(dir, i * 16, 4)] = ((int)BinaryPrimitives.ReadUInt32BigEndian(dir.AsSpan(i * 16 + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(dir.AsSpan(i * 16 + 12)));
        if (!(t.ContainsKey("glyf") || t.ContainsKey("CFF ")) || !t.TryGetValue("name", out var nm) || !t.TryGetValue("head", out var hd)) return null;
        byte[] Get((int Off, int Len) r) { var b = new byte[Math.Min(r.Len, 1 << 20)]; fs.Seek(r.Off, SeekOrigin.Begin); fs.ReadExactly(b); return b; }
        var name = Get(nm); var head = Get(hd);
        var mac = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(44));
        var family = Name(name, 1); var ps = Name(name, 6);
        if (string.IsNullOrWhiteSpace(family)) return null;
        var style = Name(name, 2) ?? "";
        var bold = (mac & 1) != 0 || style.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        var italic = (mac & 2) != 0 || style.Contains("Italic", StringComparison.OrdinalIgnoreCase) || style.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        return new FontFace(family.Trim(), bold, italic, path, ps ?? family.Replace(" ", ""), faceIndex);
    }

    static string? Name(byte[] d, int id)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(2)); var so = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(4));
        string? mac = null;
        for (var i = 0; i < count; i++)
        {
            var p = 6 + i * 12;
            if (p + 12 > d.Length) break;
            var plat = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p)); var nid = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 6));
            if (nid != id) continue;
            var len = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 8)); var off = so + BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 10));
            if (off + len > d.Length) continue;
            if (plat is 3 or 0) return Encoding.BigEndianUnicode.GetString(d, off, len);
            if (plat == 1) mac ??= Encoding.Latin1.GetString(d, off, len);
        }
        return mac;
    }
}

/// <summary>Reduces a glyf-flavoured TrueType font to the glyphs in use. Glyph IDs are preserved so Identity-H text keeps working.</summary>
public static class FontSubsetter
{
    public static int TableOffset(byte[] d, string tag) => Find(d, tag).Off;
    public static int TableLength(byte[] d, string tag) => Find(d, tag).Len;
    static (int Off, int Len) Find(byte[] d, string tag)
    {
        var n = d[4] << 8 | d[5];
        for (var i = 0; i < n; i++)
        {
            var p = 12 + i * 16;
            if (Encoding.ASCII.GetString(d, p, 4) == tag)
                return ((int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 12)));
        }
        return (-1, 0);
    }

    public static byte[] Subset(byte[] d, IReadOnlySet<int> keep)
    {
        int U16(int o) => d[o] << 8 | d[o + 1];
        uint U32(int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
        var n = U16(4);
        var t = new Dictionary<string, (int Off, int Len)>();
        for (var i = 0; i < n; i++) t[Encoding.ASCII.GetString(d, 12 + i * 16, 4)] = ((int)U32(12 + i * 16 + 8), (int)U32(12 + i * 16 + 12));
        var head = t["head"]; var numGlyphs = U16(t["maxp"].Off + 4); var longLoca = U16(head.Off + 50) != 0;
        var loca = t["loca"]; var glyf = t["glyf"];
        int Loc(int g) => longLoca ? (int)U32(loca.Off + g * 4) : U16(loca.Off + g * 2) * 2;

        var used = new SortedSet<int> { 0 };
        var work = new Stack<int>();
        foreach (var g in keep) if (g > 0 && g < numGlyphs && used.Add(g)) work.Push(g);
        work.Push(0);
        while (work.Count > 0)
        {
            var g = work.Pop(); var s = Loc(g); var e = Loc(g + 1);
            if (e - s < 10 || (short)U16(glyf.Off + s) >= 0) continue;
            var p = glyf.Off + s + 10;
            while (true)
            {
                var flags = U16(p); var comp = U16(p + 2);
                if (comp < numGlyphs && used.Add(comp)) work.Push(comp);
                p += 4 + ((flags & 1) != 0 ? 4 : 2) + ((flags & 8) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);
                if ((flags & 0x20) == 0) break;
            }
        }

        // Glyph IDs are kept, but nothing past the highest glyph in use needs an entry.
        var newN = used.Max + 1;
        using var gl = new MemoryStream();
        var newLoca = new byte[(newN + 1) * 4];
        for (var g = 0; g < newN; g++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(g * 4), (uint)gl.Length);
            if (!used.Contains(g)) continue;
            var s = Loc(g); var e = Loc(g + 1);
            gl.Write(d, glyf.Off + s, e - s);
            while (gl.Length % 4 != 0) gl.WriteByte(0);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(newN * 4), (uint)gl.Length);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var tag in new[] { "head", "hhea", "maxp", "hmtx", "cvt ", "fpgm", "prep" })
            if (t.TryGetValue(tag, out var r)) tables[tag] = d.AsSpan(r.Off, r.Len).ToArray();
        tables["glyf"] = gl.ToArray(); tables["loca"] = newLoca;
        tables["head"][8] = tables["head"][9] = tables["head"][10] = tables["head"][11] = 0;
        tables["head"][50] = 0; tables["head"][51] = 1;
        if (newN < numGlyphs && tables.ContainsKey("hhea") && tables.ContainsKey("hmtx"))
        {
            tables["maxp"][4] = (byte)(newN >> 8); tables["maxp"][5] = (byte)newN;
            var nh = tables["hhea"][34] << 8 | tables["hhea"][35];
            var newNh = Math.Min(nh, newN);
            tables["hhea"][34] = (byte)(newNh >> 8); tables["hhea"][35] = (byte)newNh;
            var hm = tables["hmtx"];
            var nhm = new byte[newNh * 4 + (newN - newNh) * 2];
            Array.Copy(hm, nhm, newNh * 4);
            if (newN > newNh) Array.Copy(hm, nh * 4, nhm, newNh * 4, (newN - newNh) * 2);
            tables["hmtx"] = nhm;
        }

        var count = tables.Count;
        var outp = new MemoryStream();
        var hdr = new byte[12 + count * 16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hdr, 0x00010000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(4), (ushort)count);
        var sr = 1; var es = 0; while (sr * 2 <= count) { sr *= 2; es++; }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(6), (ushort)(sr * 16));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(8), (ushort)es);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(10), (ushort)(count * 16 - sr * 16));
        var off = hdr.Length; var idx = 0;
        foreach (var (tag, data) in tables)
        {
            uint sum = 0;
            for (var i = 0; i < data.Length; i += 4)
            {
                uint w = 0;
                for (var k = 0; k < 4; k++) w = w << 8 | (i + k < data.Length ? data[i + k] : (uint)0);
                sum += w;
            }
            var e = 12 + idx++ * 16;
            Encoding.ASCII.GetBytes(tag).CopyTo(hdr, e);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hdr.AsSpan(e + 4), sum);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hdr.AsSpan(e + 8), (uint)off);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hdr.AsSpan(e + 12), (uint)data.Length);
            off += (data.Length + 3) & ~3;
        }
        outp.Write(hdr);
        foreach (var data in tables.Values) { outp.Write(data); for (var i = data.Length; i % 4 != 0; i++) outp.WriteByte(0); }
        return outp.ToArray();
    }
}

/// <summary>
/// Shrinks a name-keyed CFF table by replacing the charstrings of unused glyphs with a bare <c>endchar</c>; glyph IDs, charset and the
/// subroutines are untouched. CID-keyed CFF is returned as null (caller embeds it whole).
/// </summary>
public static class CffSubsetter
{
    static (int Count, int[] Offs, int DataStart, int End) Index(byte[] d, int p)
    {
        var count = d[p] << 8 | d[p + 1];
        if (count == 0) return (0, [], p + 2, p + 2);
        int os = d[p + 2];
        var offs = new int[count + 1];
        for (var i = 0; i <= count; i++) { var v = 0; for (var k = 0; k < os; k++) v = v << 8 | d[p + 3 + i * os + k]; offs[i] = v; }
        var start = p + 3 + (count + 1) * os - 1;
        return (count, offs, start, start + offs[count]);
    }

    static byte[] BuildIndex(IReadOnlyList<byte[]> items)
    {
        var total = items.Sum(i => i.Length) + 1;
        var os = total < 256 ? 1 : total < 65536 ? 2 : total < 1 << 24 ? 3 : 4;
        var ms = new MemoryStream();
        ms.WriteByte((byte)(items.Count >> 8)); ms.WriteByte((byte)items.Count);
        if (items.Count == 0) return ms.ToArray();
        ms.WriteByte((byte)os);
        var cur = 1;
        void Off(int v) { for (var k = os - 1; k >= 0; k--) ms.WriteByte((byte)(v >> (8 * k))); }
        Off(cur);
        foreach (var i in items) { cur += i.Length; Off(cur); }
        foreach (var i in items) ms.Write(i);
        return ms.ToArray();
    }

    static byte[] Int5(int v) => [29, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    sealed record Entry(int Op, int Start, int OpStart, int End, double[] Vals);

    static List<Entry>? ParseDict(byte[] d, int s, int e)
    {
        var list = new List<Entry>(); var vals = new List<double>(); var p = s; var st = s;
        while (p < e)
        {
            int b = d[p];
            if (b <= 21)
            {
                var op = b; var opStart = p; p++;
                if (b == 12) { op = 1200 + d[p]; p++; }
                list.Add(new Entry(op, st, opStart, p, vals.ToArray())); vals.Clear(); st = p;
            }
            else if (b == 28) { vals.Add((short)(d[p + 1] << 8 | d[p + 2])); p += 3; }
            else if (b == 29) { vals.Add(d[p + 1] << 24 | d[p + 2] << 16 | d[p + 3] << 8 | d[p + 4]); p += 5; }
            else if (b == 30) { p++; while (p < e) { var x = d[p++]; if ((x & 0xF) == 0xF || x >> 4 == 0xF) break; } vals.Add(0); }
            else if (b is >= 32 and <= 246) { vals.Add(b - 139); p++; }
            else if (b is >= 247 and <= 250) { vals.Add((b - 247) * 256 + d[p + 1] + 108); p += 2; }
            else if (b is >= 251 and <= 254) { vals.Add(-(b - 251) * 256 - d[p + 1] - 108); p += 2; }
            else return null;
        }
        return list;
    }

    public static byte[]? Subset(byte[] cff, IReadOnlySet<int> keep)
    {
        try
        {
            var name = Index(cff, cff[2]);
            var top = Index(cff, name.End);
            if (top.Count != 1) return null;
            var ts = top.DataStart + top.Offs[0]; var te = top.DataStart + top.Offs[1];
            var entries = ParseDict(cff, ts, te);
            if (entries is null || entries.Any(x => x.Op == 1230)) return null;
            var csEntry = entries.FirstOrDefault(x => x.Op == 17);
            if (csEntry is null) return null;
            var csStart = (int)csEntry.Vals[0];
            var cs = Index(cff, csStart);
            if (cs.End > cff.Length) return null;
            var items = new byte[cs.Count][];
            for (var g = 0; g < cs.Count; g++)
                items[g] = g == 0 || keep.Contains(g) ? cff[(cs.DataStart + cs.Offs[g])..(cs.DataStart + cs.Offs[g + 1])] : [0x0E];
            var newCs = BuildIndex(items);
            var csDelta = newCs.Length - (cs.End - csStart);

            byte[] MakeTop(int deltaTop)
            {
                int Shift(int o) => o >= cs.End ? o + deltaTop + csDelta : o + deltaTop;
                var ms = new MemoryStream();
                foreach (var e in entries)
                {
                    var op = cff[e.OpStart..e.End];
                    switch (e.Op)
                    {
                        case 15: ms.Write(Int5(e.Vals[0] > 2 ? Shift((int)e.Vals[0]) : (int)e.Vals[0])); ms.Write(op); break;
                        case 16: ms.Write(Int5(e.Vals[0] > 1 ? Shift((int)e.Vals[0]) : (int)e.Vals[0])); ms.Write(op); break;
                        case 17: ms.Write(Int5(csStart + deltaTop)); ms.Write(op); break;
                        case 18: ms.Write(Int5((int)e.Vals[0])); ms.Write(Int5(Shift((int)e.Vals[1]))); ms.Write(op); break;
                        default: ms.Write(cff, e.Start, e.End - e.Start); break;
                    }
                }
                return ms.ToArray();
            }
            var oldTopLen = top.End - name.End;
            var deltaTop = BuildIndex([MakeTop(0)]).Length - oldTopLen;
            var newTop = BuildIndex([MakeTop(deltaTop)]);
            if (newTop.Length - oldTopLen != deltaTop) return null;

            var priv = entries.FirstOrDefault(x => x.Op == 18);
            if (priv is not null)
            {
                var pOff = (int)priv.Vals[1]; var pSize = (int)priv.Vals[0];
                var pd = ParseDict(cff, pOff, pOff + pSize);
                if (pd?.FirstOrDefault(x => x.Op == 19) is { } subrs)
                {
                    int Sh(int o) => o >= cs.End ? o + deltaTop + csDelta : o + deltaTop;
                    if (Sh(pOff + (int)subrs.Vals[0]) - Sh(pOff) != (int)subrs.Vals[0]) return null;
                }
            }

            var outp = new MemoryStream();
            outp.Write(cff, 0, name.End);
            outp.Write(newTop);
            outp.Write(cff, top.End, csStart - top.End);
            outp.Write(newCs);
            outp.Write(cff, cs.End, cff.Length - cs.End);
            return outp.ToArray();
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or OverflowException) { return null; }
    }
}
