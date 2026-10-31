using System.Text.Json;
using PortableDocument.Document;

// Usage: AdoBye.ValidateDump <out.jsonl> <dir-or-pdf>...
// Writes one JSON line per PDF with our PDF/A-1b/2b/3b and PDF/UA-1 verdicts, for comparison with veraPDF (tools/validation/compare.py).
var profiles = new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b, PdfValidationProfile.PdfUA1 };
var files = args.Skip(1).SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p, "*.pdf", SearchOption.AllDirectories) : [p]).OrderBy(p => p, StringComparer.Ordinal).ToList();
using var w = new StreamWriter(args[0]);
foreach (var path in files)
{
    var result = new Dictionary<string, object>();
    try
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(path));
        foreach (var p in profiles)
        {
            var r = PdfValidator.Validate(doc, p);
            result[p.ToString()] = new
            {
                compliant = r.IsCompliant,
                findings = r.Errors.Select(f => new { rule = f.RuleId, clause = f.Clause, message = f.Message }).ToArray(),
            };
        }
    }
    catch (Exception ex) { result["loadError"] = ex.GetType().Name + ": " + ex.Message; }
    w.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFullPath(path), result }));
}
Console.WriteLine($"validated {files.Count} file(s) -> {Path.GetFullPath(args[0])}");
