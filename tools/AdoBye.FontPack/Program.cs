using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PortableDocument.Content;
using PortableDocument.FallbackFonts;

// Rebuilds the fallback font data from the pinned upstream sources:
//   AdoBye.FontPack <sourceRoot> <repoRoot>
// sourceRoot holds: liberation-fonts-ttf-2.1.5\, dejavu-fonts-ttf-2.37\, afm\*.afm, notojp.ttf, notosc.ttf, nototc.ttf, notokr.ttf, OFL.txt (Noto)
// Outputs: src\PortableDocument.Content\Resources\std14-widths.txt, src\PortableDocument.FallbackFonts\Resources\cjk-index.bin and fallback-fonts\**.
if (args.Length != 2) { Console.Error.WriteLine("usage: AdoBye.FontPack <sourceRoot> <repoRoot>"); return 2; }
var src = args[0]; var repo = args[1];
var outDir = Path.Combine(repo, "src", "PortableDocument.FallbackFonts", "fallback-fonts");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

WriteStd14(Path.Combine(src, "afm"), Path.Combine(repo, "src", "PortableDocument.Content", "Resources", "std14-widths.txt"));
CopyLatin();
PackCjk();
CopyLicences();
WriteManifest();
return 0;

void WriteStd14(string afmDir, string target)
{
    string[] fonts = ["Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic", "Symbol", "ZapfDingbats"];
    var sb = new StringBuilder("# Advance widths (1/1000 em) from the Adobe Core 14 AFM metrics: code (-1 when unencoded) name width.\n");
    foreach (var f in fonts)
    {
        sb.Append('@').Append(f).Append('\n');
        var symbolic = f is "Symbol" or "ZapfDingbats";
        foreach (var line in File.ReadLines(Path.Combine(afmDir, f + ".afm")))
        {
            if (!line.StartsWith("C ", StringComparison.Ordinal)) continue;
            var parts = line.Split(';', StringSplitOptions.TrimEntries);
            var code = int.Parse(parts[0].AsSpan(2), CultureInfo.InvariantCulture);
            var w = int.Parse(parts[1].AsSpan(3), CultureInfo.InvariantCulture);
            var name = parts[2][2..];
            sb.Append(symbolic ? code : -1).Append(' ').Append(name).Append(' ').Append(w).Append('\n');
        }
    }
    File.WriteAllText(target, sb.ToString());
}

void CopyLatin()
{
    var dir = Path.Combine(outDir, "latin"); Directory.CreateDirectory(dir);
    foreach (var f in Directory.GetFiles(Path.Combine(src, "liberation-fonts-ttf-2.1.5"), "Liberation*.ttf")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
    File.Copy(Path.Combine(src, "dejavu-fonts-ttf-2.37", "ttf", "DejaVuSans.ttf"), Path.Combine(dir, "DejaVuSans.ttf"), true);
}

void CopyLicences()
{
    var dir = Path.Combine(outDir, "licenses"); Directory.CreateDirectory(dir);
    File.Copy(Path.Combine(src, "liberation-fonts-ttf-2.1.5", "LICENSE"), Path.Combine(dir, "Liberation-OFL-1.1.txt"), true);
    File.Copy(Path.Combine(src, "liberation-fonts-ttf-2.1.5", "AUTHORS"), Path.Combine(dir, "Liberation-AUTHORS.txt"), true);
    File.Copy(Path.Combine(src, "dejavu-fonts-ttf-2.37", "LICENSE"), Path.Combine(dir, "DejaVu-LICENSE.txt"), true);
    File.Copy(Path.Combine(src, "dejavu-fonts-ttf-2.37", "AUTHORS"), Path.Combine(dir, "DejaVu-AUTHORS.txt"), true);
    File.Copy(Path.Combine(src, "OFL.txt"), Path.Combine(dir, "NotoSansCJK-OFL-1.1.txt"), true);
}

void PackCjk()
{
    var index = new byte[CjkIndex.Orderings.Length * 32];
    var total = 0L; var files = 0;
    foreach (var (ordering, fontFile, cover) in new (string, string, Func<SortedSet<int>>)[]
    {
        ("Japan1", "notojp.ttf", () => Cover(932, [(0x81, 0x9F), (0xE0, 0xFC)], [(0x40, 0xFC)], [(0xA1, 0xDF)])),
        ("GB1", "notosc.ttf", () => Cover(936, [(0xA1, 0xF7)], [(0xA1, 0xFE)], [])),
        ("CNS1", "nototc.ttf", () => Cover(950, [(0xA1, 0xF9)], [(0x40, 0x7E), (0xA1, 0xFE)], [])),
        ("Korea1", "notokr.ttf", () => Cover(949, [(0xA1, 0xAC), (0xB0, 0xC8), (0xCA, 0xFD)], [(0xA1, 0xFE)], [])),
    })
    {
        var font = new TrueTypeFont(File.ReadAllBytes(Path.Combine(src, fontFile)));
        var dir = Path.Combine(outDir, "cjk", ordering);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var byBlock = new SortedDictionary<int, List<(int, PathData)>>();
        var missing = 0;
        foreach (var cp in cover())
        {
            var gid = font.LookupAny(cp);
            if (gid == 0) { missing++; continue; }
            var path = font.GetOutline(gid);
            if (path is null || path.IsEmpty) continue;
            if (!byBlock.TryGetValue(cp >> 8, out var list)) byBlock[cp >> 8] = list = [];
            list.Add((cp & 0xFF, path));
        }
        long bytes = 0; var glyphs = 0;
        foreach (var (block, list) in byBlock)
        {
            var data = CjkChunkCodec.Encode(list);
            File.WriteAllBytes(Path.Combine(dir, $"{block:x2}.bin"), data);
            bytes += data.Length; glyphs += list.Count; files++;
            index[Array.IndexOf(CjkIndex.Orderings, ordering) * 32 + (block >> 3)] |= (byte)(1 << (block & 7));
        }
        total += bytes;
        Console.WriteLine($"{ordering}: {glyphs} glyphs in {byBlock.Count} chunks, {bytes:N0} bytes, {missing} code points absent from {fontFile}");
    }
    File.WriteAllBytes(Path.Combine(repo, "src", "PortableDocument.FallbackFonts", "Resources", "cjk-index.bin"), index);
    Console.WriteLine($"CJK total {total:N0} bytes in {files} chunks");
}

SortedSet<int> Cover(int codePage, (int, int)[] leads, (int, int)[] trails, (int, int)[] singles)
{
    var enc = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
    var set = new SortedSet<int>();
    for (var b = 0x20; b <= 0x7E; b++) set.Add(b);
    for (var c = 0xFF01; c <= 0xFF5E; c++) set.Add(c);
    for (var c = 0xFFE0; c <= 0xFFE5; c++) set.Add(c);
    foreach (var (lo, hi) in singles)
        for (var b = lo; b <= hi; b++) Add(enc.GetString([(byte)b]));
    foreach (var (llo, lhi) in leads)
        for (var l = llo; l <= lhi; l++)
            foreach (var (tlo, thi) in trails)
                for (var t = tlo; t <= thi; t++) Add(enc.GetString([(byte)l, (byte)t]));
    return set;

    void Add(string s)
    {
        if (s.Length == 1 && s[0] != '\uFFFD' && !char.IsControl(s[0])) set.Add(s[0]);
    }
}

void WriteManifest()
{
    var assets = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories)
        .Where(f => !(Path.GetFileName(f).Equals("manifest.json", StringComparison.Ordinal) && Path.GetDirectoryName(f) == outDir))
        .Select(f => Path.GetRelativePath(outDir, f).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .Select(p => new { path = p, size = new FileInfo(Path.Combine(outDir, p)).Length, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(outDir, p)))).ToLowerInvariant() })
        .ToArray();
    var opts = new JsonSerializerOptions { WriteIndented = true };
    // Precache list for hosts with a service worker: only the small index; fonts and glyph chunks are fetched on first use.
    File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(new { version = 1, lazy = true, assets }, opts) + "\n");
    Console.WriteLine($"manifest: {assets.Length} assets, {assets.Sum(a => a.size):N0} bytes");
}
