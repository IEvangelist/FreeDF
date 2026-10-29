using System.Buffers.Binary;
using System.Text;
using Pdf.Content;

/// <summary>One installed TrueType (glyf) font file.</summary>
public sealed record FontFace(string Family, bool Bold, bool Italic, string Path, string PostScript);

/// <summary>Installed system fonts grouped by family (name ID 1), found by reading only each file's table directory, name, head and OS/2 tables.</summary>
public sealed class FontCatalog
{
    public static readonly string[] Standard = ["Helvetica", "Times", "Courier", "Symbol", "ZapfDingbats"];

    readonly Dictionary<string, List<FontFace>> _families = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (byte[] Data, TrueTypeFont Font)> _loaded = [];

    public static FontCatalog Scan()
    {
        var c = new FontCatalog();
        var dirs = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.Fonts) };
        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dirs.Add(System.IO.Path.Combine(home, "Microsoft", "Windows", "Fonts"));
        dirs.AddRange(["/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts", "/System/Library/Fonts",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")]);
        foreach (var dir in dirs.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".ttf" or ".otf")) continue;
                try { if (Read(f) is { } face) c.Add(face); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
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

    public (byte[] Data, TrueTypeFont Font) Load(FontFace f)
    {
        lock (_loaded)
        {
            if (!_loaded.TryGetValue(f.Path, out var v)) _loaded[f.Path] = v = (File.ReadAllBytes(f.Path), null!);
            if (v.Font is null) _loaded[f.Path] = v = (v.Data, new TrueTypeFont(v.Data));
            return v;
        }
    }

    static FontFace? Read(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> hdr = stackalloc byte[12];
        if (fs.Read(hdr) < 12) return null;
        var tag = BinaryPrimitives.ReadUInt32BigEndian(hdr);
        if (tag is not (0x00010000 or 0x74727565 or 0x4F54544F)) return null;
        var n = BinaryPrimitives.ReadUInt16BigEndian(hdr[4..]);
        var dir = new byte[n * 16]; fs.ReadExactly(dir);
        var t = new Dictionary<string, (int Off, int Len)>();
        for (var i = 0; i < n; i++)
            t[Encoding.ASCII.GetString(dir, i * 16, 4)] = ((int)BinaryPrimitives.ReadUInt32BigEndian(dir.AsSpan(i * 16 + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(dir.AsSpan(i * 16 + 12)));
        if (!t.ContainsKey("glyf") || !t.TryGetValue("name", out var nm) || !t.TryGetValue("head", out var hd)) return null;
        byte[] Get((int Off, int Len) r) { var b = new byte[Math.Min(r.Len, 1 << 20)]; fs.Seek(r.Off, SeekOrigin.Begin); fs.ReadExactly(b); return b; }
        var name = Get(nm); var head = Get(hd);
        var mac = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(44));
        var family = Name(name, 1); var ps = Name(name, 6);
        if (string.IsNullOrWhiteSpace(family)) return null;
        var style = Name(name, 2) ?? "";
        var bold = (mac & 1) != 0 || style.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        var italic = (mac & 2) != 0 || style.Contains("Italic", StringComparison.OrdinalIgnoreCase) || style.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        return new FontFace(family.Trim(), bold, italic, path, ps ?? family.Replace(" ", ""));
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

        using var gl = new MemoryStream();
        var newLoca = new byte[(numGlyphs + 1) * 4];
        for (var g = 0; g < numGlyphs; g++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(g * 4), (uint)gl.Length);
            if (!used.Contains(g)) continue;
            var s = Loc(g); var e = Loc(g + 1);
            gl.Write(d, glyf.Off + s, e - s);
            while (gl.Length % 4 != 0) gl.WriteByte(0);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(numGlyphs * 4), (uint)gl.Length);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var tag in new[] { "head", "hhea", "maxp", "hmtx", "cvt ", "fpgm", "prep" })
            if (t.TryGetValue(tag, out var r)) tables[tag] = d.AsSpan(r.Off, r.Len).ToArray();
        tables["glyf"] = gl.ToArray(); tables["loca"] = newLoca;
        tables["head"][8] = tables["head"][9] = tables["head"][10] = tables["head"][11] = 0;
        tables["head"][50] = 0; tables["head"][51] = 1;

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
