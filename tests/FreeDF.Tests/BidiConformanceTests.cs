using System.IO.Compression;
using System.Text;
using PortableDocument.Content.Shaping;
using Xunit;
using Xunit.Abstractions;

namespace FreeDF.Tests;

/// <summary>
/// Unicode Bidi Algorithm conformance: the official BidiTest.txt and BidiCharacterTest.txt (Unicode 16.0.0,
/// stored gzip-compressed in fixtures/ucd; Unicode License, see THIRD-PARTY-NOTICES.md) run through <see cref="Bidi"/>
/// for resolved paragraph level, per-character levels (rules X1-I2, L1) and visual order (rule L2).
/// </summary>
public class BidiConformanceTests(ITestOutputHelper output)
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "ucd");

    static IEnumerable<string> ReadGz(string name)
    {
        using var fs = File.OpenRead(Path.Combine(Dir, name));
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var sr = new StreamReader(gz, Encoding.UTF8);
        while (sr.ReadLine() is { } line) yield return line;
    }

    static readonly Dictionary<string, string> Representative = new()
    {
        ["L"] = "a", ["R"] = "\u05D0", ["AL"] = "\u0627", ["EN"] = "0", ["ES"] = "+", ["ET"] = "$", ["AN"] = "\u0660", ["CS"] = ",",
        ["NSM"] = "\u0300", ["BN"] = "\u00AD", ["B"] = "\u2029", ["S"] = "\t", ["WS"] = " ", ["ON"] = "!",
        ["LRE"] = "\u202A", ["LRO"] = "\u202D", ["RLE"] = "\u202B", ["RLO"] = "\u202E", ["PDF"] = "\u202C",
        ["LRI"] = "\u2066", ["RLI"] = "\u2067", ["FSI"] = "\u2068", ["PDI"] = "\u2069",
    };

    static string Compare(string text, BidiDirection dir, string?[] expectedLevels, int[] expectedOrder, int? expectedParagraph)
    {
        var r = Bidi.Resolve(text, dir);
        if (expectedParagraph is { } pl && r.ParagraphLevel != pl) return $"paragraph level {r.ParagraphLevel} != {pl}";
        var kept = new List<int>(); var lv = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (expectedLevels[i] == "x")
            {
                if (!r.Removed[i]) return $"char {i} should be removed";
                continue;
            }
            if (r.Removed[i]) return $"char {i} wrongly removed";
            if (r.Levels[i] != int.Parse(expectedLevels[i]!)) return $"level[{i}] {r.Levels[i]} != {expectedLevels[i]}";
            kept.Add(i); lv.Add(r.Levels[i]);
        }
        var order = Bidi.VisualOrder(lv).Select(k => kept[k]).ToArray();
        return order.SequenceEqual(expectedOrder) ? "" : $"order [{string.Join(' ', order)}] != [{string.Join(' ', expectedOrder)}]";
    }

    [Fact]
    public void BidiTest_txt_conformance()
    {
        string?[] levels = []; int[] reorder = [];
        int total = 0, failed = 0; var samples = new List<string>();
        foreach (var raw in ReadGz("BidiTest.txt.gz"))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("@Levels:")) { levels = line[8..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries); continue; }
            if (line.StartsWith("@Reorder:")) { reorder = line[9..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); continue; }
            if (line[0] == '@') continue;
            var parts = line.Split(';');
            var classes = parts[0].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var bits = Convert.ToInt32(parts[1].Trim());
            var text = string.Concat(classes.Select(c => Representative[c]));
            foreach (var (bit, dir) in new[] { (1, BidiDirection.Auto), (2, BidiDirection.LeftToRight), (4, BidiDirection.RightToLeft) })
            {
                if ((bits & bit) == 0) continue;
                total++;
                var err = Compare(text, dir, levels, reorder, null);
                if (err.Length == 0) continue;
                failed++;
                if (samples.Count < 20) samples.Add($"{parts[0].Trim()} dir={dir}: {err}");
            }
        }
        output.WriteLine($"BidiTest.txt: {total - failed}/{total} passed");
        Assert.True(failed == 0, $"BidiTest.txt: {total - failed}/{total} passed\n" + string.Join('\n', samples));
    }

    [Fact]
    public void BidiCharacterTest_txt_conformance()
    {
        int total = 0, failed = 0; var samples = new List<string>();
        foreach (var raw in ReadGz("BidiCharacterTest.txt.gz"))
        {
            if (raw.Length == 0 || raw[0] == '#') continue;
            var f = raw.Split(';');
            if (f.Length < 5) continue;
            var text = string.Concat(f[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => char.ConvertFromUtf32(Convert.ToInt32(h, 16))));
            var dir = f[1].Trim() switch { "0" => BidiDirection.LeftToRight, "1" => BidiDirection.RightToLeft, _ => BidiDirection.Auto };
            var levels = f[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var order = f[4].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            // indices in the file are per code point; expand to UTF-16 units when the text has supplementary characters
            if (text.Any(char.IsSurrogate))
            {
                var map = new List<int>(); var i = 0;
                foreach (var rune in text.EnumerateRunes()) { map.Add(i); i += rune.Utf16SequenceLength; }
                var expanded = new string?[text.Length];
                for (var k = 0; k < map.Count; k++) { expanded[map[k]] = levels[k]; if (text.Length > map[k] + 1 && char.IsHighSurrogate(text[map[k]])) expanded[map[k] + 1] = levels[k]; }
                var kept = order.Select(o => map[o]).ToArray();
                total++;
                var r = Bidi.Resolve(text, dir);
                var ok = r.ParagraphLevel == int.Parse(f[2]);
                for (var k = 0; ok && k < map.Count; k++) ok = levels[k] == "x" ? r.Removed[map[k]] : r.Levels[map[k]] == int.Parse(levels[k]);
                if (ok)
                {
                    var keptIdx = Enumerable.Range(0, map.Count).Where(k => levels[k] != "x").ToList();
                    var lv = keptIdx.Select(k => (int)r.Levels[map[k]]).ToList();
                    ok = Bidi.VisualOrder(lv).Select(k => keptIdx[k]).SequenceEqual(order);
                }
                if (!ok) { failed++; if (samples.Count < 20) samples.Add($"{f[0].Trim()} dir={dir}"); }
                _ = kept;
                continue;
            }
            total++;
            var err = Compare(text, dir, levels, order, int.Parse(f[2]));
            if (err.Length == 0) continue;
            failed++;
            if (samples.Count < 20) samples.Add($"{f[0].Trim()} dir={dir}: {err}");
        }
        output.WriteLine($"BidiCharacterTest.txt: {total - failed}/{total} passed");
        Assert.True(failed == 0, $"BidiCharacterTest.txt: {total - failed}/{total} passed\n" + string.Join('\n', samples));
    }

    [Fact]
    public void Joining_and_bidi_properties_match_the_ucd()
    {
        Assert.Equal(BidiClass.AL, UnicodeData.GetBidiClass(0x061B));
        Assert.Equal(BidiClass.AL, UnicodeData.GetBidiClass(0x061F));
        Assert.Equal(BidiClass.AL, UnicodeData.GetBidiClass(0x06D4));
        Assert.Equal(BidiClass.EN, UnicodeData.GetBidiClass(0xFF10));
        Assert.Equal(JoiningType.None, UnicodeData.GetJoiningType(0x0600));
        Assert.Equal(JoiningType.None, UnicodeData.GetJoiningType(0x06DD));
        Assert.Equal(JoiningType.Causing, UnicodeData.GetJoiningType(0x0640));
        Assert.Equal(JoiningType.Transparent, UnicodeData.GetJoiningType(0x064B));
        Assert.Equal(0x3009, UnicodeData.Mirrored(0x3008));
        Assert.Equal(0x2265, UnicodeData.Mirrored(0x2264));
        Assert.Equal(0x29, UnicodeData.Mirrored(0x28));
    }

    [Fact]
    public void Paragraphs_are_resolved_independently()
    {
        var text = "abc\n\u05D0\u05D1\u05D2\nxyz";
        var r = Bidi.Resolve(text);
        Assert.Equal(0, r.ParagraphLevel);
        Assert.Equal(1, r.Levels[4]);
        Assert.Equal(0, r.Levels[8]);
        var items = TextShaper.Itemize(text, BidiDirection.Auto, out _);
        Assert.Equal(items.OrderBy(i => i.Start).Select(i => i.Start), items.Select(i => i.Start));
    }
}
