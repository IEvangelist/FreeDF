using System.Diagnostics;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Content.Shaping;
using Xunit;

namespace FreeDF.Tests;

/// <summary>Hostile and degenerate layout data, using tiny synthetic sfnt fonts built in memory.</summary>
public class ShapingRobustnessTests
{
    static byte[] U(params int[] v)
    {
        var b = new byte[v.Length * 2];
        for (var i = 0; i < v.Length; i++) { b[i * 2] = (byte)(v[i] >> 8); b[i * 2 + 1] = (byte)v[i]; }
        return b;
    }

    static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>Concatenates tables and returns the bytes plus each table's offset.</summary>
    static (byte[] Bytes, int[] Offsets) Pack(params byte[][] tables)
    {
        var offs = new int[tables.Length]; var pos = 0;
        for (var i = 0; i < tables.Length; i++) { offs[i] = pos; pos += tables[i].Length; }
        return (Cat(tables), offs);
    }

    static byte[] Coverage(params int[] glyphs) => Cat(U(1, glyphs.Length), U(glyphs));

    static byte[] Lookup(int type, byte[][] subtables, int flag = 0)
    {
        var header = 6 + subtables.Length * 2;
        var offs = new List<int>(); var pos = header;
        foreach (var s in subtables) { offs.Add(pos); pos += s.Length; }
        return Cat(U(type, flag, subtables.Length), U([.. offs]), Cat(subtables));
    }

    static byte[] Gsub(string feature, params byte[][] lookups)
    {
        var featureIdx = Enumerable.Range(0, lookups.Length).ToArray();
        var langSys = Cat(U(0, 0xFFFF, 1, 0));
        var script = Cat(U(4, 0), langSys);
        var scriptList = Cat(U(1), Encoding.ASCII.GetBytes("DFLT"), U(8), script);
        var feat = Cat(U(0, lookups.Length), U(featureIdx));
        var featureList = Cat(U(1), Encoding.ASCII.GetBytes(feature), U(8), feat);
        var lookupHeader = 2 + lookups.Length * 2;
        var offs = new List<int>(); var pos = lookupHeader;
        foreach (var l in lookups) { offs.Add(pos); pos += l.Length; }
        var lookupList = Cat(U(lookups.Length), U([.. offs]), Cat(lookups));
        return Cat(U(1, 0, 10, 10 + scriptList.Length, 10 + scriptList.Length + featureList.Length), scriptList, featureList, lookupList);
    }

    static byte[] Font(int glyphs, byte[] gsub)
    {
        var head = new byte[54]; head[18] = 0x03; head[19] = 0xE8; // unitsPerEm 1000
        var maxp = Cat(U(0, 0, glyphs));
        var hhea = new byte[36]; hhea[34] = (byte)(glyphs >> 8); hhea[35] = (byte)glyphs;
        var hmtx = new byte[glyphs * 4]; for (var i = 0; i < glyphs; i++) { hmtx[i * 4] = 0x01; hmtx[i * 4 + 1] = 0xF4; }
        // cmap format 6: 'a' .. maps to glyph 1, 2, ...; U+0301 is not covered by this range, so use a second range via format 12.
        var groups = new List<int[]> { new[] { 0x61, 0x61, 1 }, new[] { 0x62, 0x62, 2 }, new[] { 0x301, 0x301, 3 } };
        var sub = new List<byte>(); void W32(int v) { sub.AddRange([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]); }
        sub.AddRange(U(12, 0)); W32(16 + groups.Count * 12); W32(0); W32(groups.Count);
        foreach (var g in groups) { W32(g[0]); W32(g[1]); W32(g[2]); }
        var cmap = Cat(U(0, 1, 3, 10), [0, 0, 0, 12], [.. sub]);
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["cmap"] = cmap, ["head"] = head, ["hhea"] = hhea, ["hmtx"] = hmtx, ["maxp"] = maxp, ["GSUB"] = gsub };
        var dir = 12 + tables.Count * 16; var pos = dir;
        var o = new List<byte>(); o.AddRange([0, 1, 0, 0]); o.AddRange(U(tables.Count, 0, 0, 0));
        var body = new List<byte>();
        foreach (var (tag, data) in tables)
        {
            o.AddRange(Encoding.ASCII.GetBytes(tag)); o.AddRange(new byte[4]);
            o.AddRange([(byte)(pos >> 24), (byte)(pos >> 16), (byte)(pos >> 8), (byte)pos]);
            o.AddRange([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
            body.AddRange(data); while (body.Count % 4 != 0) body.Add(0); pos = dir + body.Count;
        }
        o.AddRange(body);
        return [.. o];
    }

    [Fact]
    public void Self_referential_contextual_lookup_hits_the_budget_and_finishes_fast()
    {
        // type 5 format 3: input [a], 5000 substitution records all calling lookup 0 on position 0
        const int records = 5000;
        var recs = new List<int>(); for (var i = 0; i < records; i++) { recs.Add(0); recs.Add(0); }
        var covOffset = 8 + records * 4;
        var ctx = Cat(U(3, 1, records, covOffset), U([.. recs]), Coverage(1));
        var font = new TrueTypeFont(Font(6, Gsub("calt", Lookup(5, [ctx]))));
        var text = new string('a', 300);
        var sw = Stopwatch.StartNew();
        var run = TextShaper.Shape(font, text, 0, text.Length, false, ScriptId.Latin);
        sw.Stop();
        Assert.True(run.Truncated);
        Assert.Equal(300, run.Glyphs.Length);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Well_formed_font_is_not_truncated()
    {
        var run = TextShaper.Shape(new TrueTypeFont(Font(6, Gsub("calt", Lookup(1, [Cat(U(2, 8, 1, 4), Coverage(1))])))), "aab", 0, 3, false, ScriptId.Latin);
        Assert.False(run.Truncated);
        Assert.Equal([4, 4, 2], run.Glyphs.Select(g => g.GlyphId));
    }

    [Fact]
    public void Marks_keep_their_class_after_substitution_in_fonts_without_gdef_classes()
    {
        // ccmp replaces the combining acute (glyph 3) with glyph 5; without GDEF the mark class comes from Unicode and must survive
        var subst = Cat(U(2, 8, 1, 5), Coverage(3));
        var font = new TrueTypeFont(Font(6, Gsub("ccmp", Lookup(1, [subst]))));
        var run = TextShaper.Shape(font, "a\u0301", 0, 2, false, ScriptId.Latin);
        Assert.Equal([1, 5], run.Glyphs.Select(g => g.GlyphId));
        Assert.Equal(0, run.Glyphs[1].XAdvance);
        Assert.Equal(500, run.Glyphs[0].XAdvance);
    }
}
