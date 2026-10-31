using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PortableDocument.Content;
using PortableDocument.FallbackFonts;

// Rebuilds the fallback font data from the pinned upstream sources:
//   AdoBye.FontPack <sourceRoot> <repoRoot>
// sourceRoot holds: liberation-fonts-ttf-2.1.5\, dejavu-fonts-ttf-2.37\, afm\*.afm, notojp.ttf, notosc.ttf, nototc.ttf, notokr.ttf, OFL.txt (Noto)
// Outputs: src\PortableDocument.Content\Resources\std14-widths.bin (notice in std14-notice.txt), src\PortableDocument.FallbackFonts\Resources\cjk-index.bin and fallback-fonts\**.
if (args.Length == 2 && args[0] == "fixtures") { WriteFixtures(args[1]); return 0; }
if (args.Length != 2) { Console.Error.WriteLine("usage: AdoBye.FontPack <sourceRoot> <repoRoot> | fixtures <dir>"); return 2; }
var src = args[0]; var repo = args[1];
var outDir = Path.Combine(repo, "src", "PortableDocument.FallbackFonts", "fallback-fonts");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

WriteStd14(Path.Combine(src, "afm"), Path.Combine(repo, "src", "PortableDocument.Content", "Resources", "std14-widths.bin"));
CopyLatin();
PackCjk();
CopyLicences();
WriteManifest();
return 0;

void WriteFixtures(string dir)
{
    Directory.CreateDirectory(dir);
    static string Hex(string s) => string.Concat(s.Select(c => ((int)c).ToString("X4")));
    string T(string font, double y, string text, double size = 22) => $"BT /{font} {size} Tf 24 {y} Td ({text}) Tj ET\n";
    string H(string font, double y, string text, double size = 26) => $"BT /{font} {size} Tf 24 {y} Td <{Hex(text)}> Tj ET\n";
    string TypeOne(string f, string baseFont, string extra = "") => $"/{f} << /Type /Font /Subtype /Type1 /BaseFont /{baseFont} {extra} >>";
    string True(string f, string baseFont) => $"/{f} << /Type /Font /Subtype /TrueType /BaseFont /{baseFont} /Encoding /WinAnsiEncoding >>";
    string Cjk(string f, string baseFont, string ordering, string cmap) =>
        $"/{f} << /Type /Font /Subtype /Type0 /BaseFont /{baseFont} /Encoding /{cmap} /DescendantFonts [<< /Type /Font /Subtype /CIDFontType0 /BaseFont /{baseFont} /CIDSystemInfo << /Registry (Adobe) /Ordering ({ordering}) /Supplement 2 >> /DW 1000 >>] >>";

    Pdf("nonembedded-latin.pdf",
        T("F1", 270, "Arial: Hamburgefonstiv 0123456789") + T("F2", 235, "Arial Bold Italic: Hamburgefonstiv") + T("F3", 200, "Times New Roman: Hamburgefonstiv") +
        T("F4", 165, "Times Bold: Hamburgefonstiv") + T("F5", 130, "Courier New: Hamburgefonstiv 0123") + T("F6", 95, "Courier Oblique: Hamburgefonstiv") + T("F7", 60, "Helvetica Bold: Hamburgefonstiv"),
        $"{True("F1", "ArialMT")} {True("F2", "Arial-BoldItalicMT")} {True("F3", "TimesNewRomanPSMT")} {TypeOne("F4", "Times-Bold")} {True("F5", "CourierNewPSMT")} {TypeOne("F6", "Courier-Oblique")} {TypeOne("F7", "Helvetica-Bold")}");
    Pdf("nonembedded-symbol-dingbats.pdf",
        T("F1", 250, "abgdezhq ABGDEZ \\256\\245\\345") + T("F2", 190, "abcdefghijk 12345 !\"#$%", 24) + T("F3", 130, "Mixed Symbol: a b g", 18),
        $"{TypeOne("F1", "Symbol")} {TypeOne("F2", "ZapfDingbats")} {TypeOne("F3", "Helvetica")}");
    Pdf("nonembedded-cjk.pdf",
        H("F1", 260, "日本語のテキスト") + H("F2", 200, "中文简体文本") + H("F3", 140, "한국어 텍스트") + H("F4", 80, "繁體中文文字"),
        $"{Cjk("F1", "MS-Gothic", "Japan1", "UniJIS-UTF16-H")} {Cjk("F2", "SimHei", "GB1", "UniGB-UTF16-H")} {Cjk("F3", "Batang", "Korea1", "UniKS-UTF16-H")} {Cjk("F4", "MingLiU", "CNS1", "UniCNS-UTF16-H")}");

    void Pdf(string name, string content, string fonts)
    {
        var sb = new StringBuilder("%PDF-1.7\n");
        var offs = new List<int>();
        void Obj(string body) { offs.Add(sb.Length); sb.Append($"{offs.Count} 0 obj\n{body}\nendobj\n"); }
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 300] /Contents 4 0 R /Resources << /Font << {fonts} >> >> >>");
        Obj($"<< /Length {content.Length} >>\nstream\n{content}endstream");
        var xref = sb.Length;
        sb.Append($"xref\n0 {offs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {offs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(Path.Combine(dir, name), new UTF8Encoding(false).GetBytes(sb.ToString()));
    }
}

void WriteStd14(string afmDir, string target)
{
    string[] fonts = ["Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic", "Symbol", "ZapfDingbats"];
    var notice = File.ReadAllText(Path.Combine(repo, "src", "PortableDocument.Content", "Resources", "std14-notice.txt"));
    var sb = new StringBuilder();
    foreach (var l in notice.Split('\n')) sb.Append("# ").Append(l.TrimEnd('\r')).Append('\n');
    sb.Append("# Derived data: advance widths (1/1000 em) and the built-in Symbol/ZapfDingbats encodings only, one entry per glyph: code (-1 when unencoded) name width.\n");
    sb.Append("# Fonts whose widths equal an earlier font are aliases (@Name=Canonical).\n");
    var seen = new Dictionary<string, string>();
    foreach (var f in fonts)
    {
        var body = new StringBuilder();
        var symbolic = f is "Symbol" or "ZapfDingbats";
        foreach (var line in File.ReadLines(Path.Combine(afmDir, f + ".afm")))
        {
            if (!line.StartsWith("C ", StringComparison.Ordinal)) continue;
            var parts = line.Split(';', StringSplitOptions.TrimEntries);
            var code = int.Parse(parts[0].AsSpan(2), CultureInfo.InvariantCulture);
            var w = int.Parse(parts[1].AsSpan(3), CultureInfo.InvariantCulture);
            body.Append(symbolic ? code : -1).Append(' ').Append(parts[2][2..]).Append(' ').Append(w).Append('\n');
        }
        var text = body.ToString();
        if (seen.TryGetValue(text, out var canon)) { sb.Append('@').Append(f).Append('=').Append(canon).Append('\n'); continue; }
        seen[text] = f;
        sb.Append('@').Append(f).Append('\n').Append(text);
    }
    using var packed = new MemoryStream();
    using (var z = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.SmallestSize, true)) z.Write(Encoding.UTF8.GetBytes(sb.ToString()));
    File.WriteAllBytes(target, packed.ToArray());
    Console.WriteLine($"std14 widths: {sb.Length:N0} bytes text, {packed.Length:N0} bytes embedded");
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
    File.Copy(Path.Combine(repo, "src", "PortableDocument.Content", "Resources", "std14-notice.txt"), Path.Combine(dir, "Adobe-Core14-AFM-NOTICE.txt"), true);
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
        long bytes = 0; var glyphs = 0; double maxDev = 0; var structural = 0;
        foreach (var (block, list) in byBlock)
        {
            var data = CjkChunkCodec.Encode(list);
            var back = CjkChunkCodec.Decode(data);
            foreach (var (low, orig) in list)
            {
                if (!back.TryGetValue(low, out var dec) || dec.Pts.Count != orig.Pts.Count || !dec.Ops.SequenceEqual(orig.Ops)) { structural++; continue; }
                for (var i = 0; i < orig.Pts.Count; i++) maxDev = Math.Max(maxDev, Math.Abs(orig.Pts[i] - dec.Pts[i]));
            }
            File.WriteAllBytes(Path.Combine(dir, $"{block:x2}.bin"), data);
            bytes += data.Length; glyphs += list.Count; files++;
            index[Array.IndexOf(CjkIndex.Orderings, ordering) * 32 + (block >> 3)] |= (byte)(1 << (block & 7));
        }
        total += bytes;
        Console.WriteLine($"{ordering}: {glyphs} glyphs in {byBlock.Count} chunks, {bytes:N0} bytes, {missing} code points absent from {fontFile}; re-encoding max deviation {maxDev:F2} units/1000 em, {structural} glyphs with changed structure");
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
