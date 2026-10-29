using AdoBye.Core;
using AdoBye.Document;
using AdoBye.Render;

// Usage:
//   AdoBye.Samples validate <samples-dir> [--out dir]   open + render every page of every sample, fail on exceptions or renderer warnings
//   AdoBye.Samples repack <in.pdf> <out.pdf>            rewrite a sample with PdfEditor/PdfWriter (compressed streams + object/xref streams)
// Passwords for the encrypted samples are listed in samples/README.md.

var passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["17-encrypted-user-password"] = "user123",
    ["18-encrypted-owner-restricted"] = "owner123",
};

if (args.Length >= 3 && args[0] == "repack")
{
    var ed = PdfEditor.Open(File.ReadAllBytes(args[1]));
    File.WriteAllBytes(args[2], ed.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true }));
    Console.WriteLine($"wrote {args[2]}");
    return 0;
}

if (args.Length < 2 || args[0] != "validate") { Console.Error.WriteLine("usage: validate <dir> [--out dir] | repack <in> <out>"); return 2; }
string? outDir = null;
for (var i = 2; i < args.Length - 1; i++) if (args[i] == "--out") outDir = args[i + 1];
if (outDir != null) Directory.CreateDirectory(outDir);

var bad = 0;
foreach (var file in Directory.EnumerateFiles(args[1], "*.pdf").OrderBy(f => f))
{
    var name = Path.GetFileNameWithoutExtension(file);
    try
    {
        var doc = PdfDocument.Open(file, passwords.FirstOrDefault(kv => name.StartsWith(kv.Key[..3], StringComparison.Ordinal)).Value ?? "");
        var warnings = new List<string>();
        for (var p = 0; p < doc.Pages.Count; p++)
        {
            var r = PdfRenderer.RenderPage(doc.Pages[p], 96);
            warnings.AddRange(r.Warnings.Select(w => $"p{p + 1}: {w}"));
            if (outDir != null) File.WriteAllBytes(Path.Combine(outDir, $"{name}.p{p + 1}.png"), r.ToPng());
        }
        Console.WriteLine($"{(warnings.Count == 0 ? "OK  " : "WARN")} {name} ({doc.Pages.Count} pages)");
        foreach (var w in warnings.Distinct().Take(8)) Console.WriteLine("       " + w);
        if (warnings.Count > 0) bad++;
    }
    catch (Exception e) { Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); bad++; }
}
return bad == 0 ? 0 : 1;
