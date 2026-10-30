using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

// Usage:
//   AdoBye.Samples generate <out-dir> [name-fragment]   author the samples (and the merged 27-full-collection) with this repo's own writer/editor
//   AdoBye.Samples validate <samples-dir> [--out dir]   open + render every page of every sample, fail on exceptions or renderer warnings
//   AdoBye.Samples repack <in.pdf> <out.pdf>            rewrite a sample with PdfEditor/PdfWriter (compressed streams + object/xref streams)
// Passwords for the encrypted samples are listed in samples/README.md.

var passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["17-encrypted-user-password"] = "user123",
    ["18-encrypted-owner-restricted"] = "owner123",
};

if (args.Length >= 2 && args[0] == "generate")
{
    // generate <out-dir> [name-fragment]: authors the samples with this repo's own writer (see Kit/ and Gen/).
    var only = args.Length > 2 ? args[2] : "";
    var gens = typeof(Program).Assembly.GetTypes()
        .Where(t => t.Namespace == "AdoBye.Samples.Gen" && t.GetField("Out") is not null && t.GetMethod("Build") is not null)
        .Select(t => (Out: (string)t.GetField("Out")!.GetRawConstantValue()!, Build: t.GetMethod("Build")!))
        .Where(g => g.Out.Contains(only, StringComparison.Ordinal)).OrderBy(g => g.Out, StringComparer.Ordinal);
    foreach (var g in gens)
    {
        g.Build.Invoke(null, [Path.Combine(args[1], g.Out)]);
        Console.WriteLine("built " + g.Out);
    }
    return 0;
}

if (args.Length >= 3 && args[0] == "repack")
{
    var ed = PdfEditor.Open(File.ReadAllBytes(args[1]));
    File.WriteAllBytes(args[2], ed.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true }));
    Console.WriteLine($"wrote {args[2]}");
    return 0;
}

if (args.Length < 2 || args[0] != "validate") { Console.Error.WriteLine("usage: generate <out-dir> [fragment] | validate <dir> [--out dir] | repack <in> <out>"); return 2; }
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
            var r = DocumentRenderer.RenderPage(doc.Pages[p], 96);
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
