// Out-of-process, one-engine-per-run profiling harness. Emits one JSON line per operation.
// Usage: AdoBye.Profile <file.pdf> [--engine adobye|pdfpig] [--ops open,text,render,thumb,save,validate]
//        [--iters 5] [--pages 8] [--dpi 72,150] [--budget 60]
using System.Diagnostics;
using System.Text.Json;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

var file = args[0];
string Opt(string name, string def) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
var engine = Opt("--engine", "adobye");
var ops = Opt("--ops", "open,text,render,thumb,save").Split(',');
var iters = int.Parse(Opt("--iters", "5"));
var samplePages = int.Parse(Opt("--pages", "8"));
var dpis = Opt("--dpi", "72,150").Split(',').Select(double.Parse).ToArray();
var budgetMs = double.Parse(Opt("--budget", "60")) * 1000;

var data = File.ReadAllBytes(file);
var name = Path.GetFileName(file);
var proc = Process.GetCurrentProcess();

void Emit(string op, List<double> ms, object? extra = null, string? error = null)
{
    var s = ms.OrderBy(x => x).ToList();
    proc.Refresh();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        engine, file = name, bytes = data.Length, op, n = s.Count,
        first_ms = ms.Count > 0 ? Math.Round(ms[0], 3) : (double?)null,
        median_ms = s.Count > 0 ? Math.Round(s[s.Count / 2], 3) : (double?)null,
        min_ms = s.Count > 0 ? Math.Round(s[0], 3) : (double?)null,
        max_ms = s.Count > 0 ? Math.Round(s[^1], 3) : (double?)null,
        peak_ws_mb = Math.Round(proc.PeakWorkingSet64 / 1048576.0, 1),
        gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2),
        gc_pause_ms = Math.Round(GC.GetTotalPauseDuration().TotalMilliseconds, 1),
        alloc_mb = Math.Round(GC.GetTotalAllocatedBytes() / 1048576.0, 1),
        cpu_ms = Math.Round(proc.TotalProcessorTime.TotalMilliseconds, 0),
        extra, error,
    }));
}

List<double> Run(Func<object?> body, out object? last, int n)
{
    var ms = new List<double>(); last = null;
    var total = Stopwatch.StartNew();
    for (var i = 0; i < n; i++)
    {
        var t = Stopwatch.GetTimestamp();
        last = body();
        ms.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        if (total.Elapsed.TotalMilliseconds > budgetMs) break;
    }
    return ms;
}

int[] Sample(int count) => count <= samplePages
    ? Enumerable.Range(0, count).ToArray()
    : Enumerable.Range(0, samplePages).Select(i => i * (count - 1) / (samplePages - 1)).Distinct().ToArray();

void Guard(string op, Action a)
{
    try { a(); }
    catch (Exception e) { Emit(op, [], null, e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
}

if (engine == "adobye")
{
    PdfDocument? doc = null;
    if (ops.Contains("open")) Guard("open", () =>
    {
        var ms = Run(() => { doc = PdfDocument.Load(data); return doc.Pages.Count; }, out var pc, iters);
        Emit("open", ms, new { pages = pc });
    });
    Guard("setup", () => doc ??= PdfDocument.Load(data));
    if (doc is null) return;
    var pages = doc.Pages.Count;
    if (ops.Contains("fingerprint")) Guard("fingerprint", () =>
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in doc.File.Xref.OrderBy(k => k.Key)) sb.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
        var h = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        Emit("fingerprint", [0], new { xref_entries = doc.File.Xref.Count, xref_sha = Convert.ToHexString(h)[..16], pages, repaired = doc.File.Repaired });
    });
    if (ops.Contains("text")) Guard("text", () =>
    {
        var ms = Run(() =>
        {
            var d = PdfDocument.Load(data); long chars = 0;
            var idx = Sample(d.Pages.Count);
            foreach (var i in idx) foreach (var sp in Pdf.ExtractText(d.Pages[i])) chars += sp.Text.Length;
            return chars;
        }, out var chars, Math.Min(iters, 3));
        Emit("text_sampled_pages_with_open", ms, new { pages = Sample(pages).Length, chars });
    });
    if (ops.Contains("text_all")) Guard("text_all", () =>
    {
        var ms = Run(() =>
        {
            var d = PdfDocument.Load(data); long chars = 0;
            foreach (var p in d.Pages) foreach (var sp in Pdf.ExtractText(p)) chars += sp.Text.Length;
            return chars;
        }, out var chars, Math.Min(iters, 2));
        Emit("text_all_pages_with_open", ms, new { pages, chars });
    });
    foreach (var dpi in dpis)
    {
        if (ops.Contains("render")) Guard("render", () =>
        {
            var idx = Sample(pages); var perPage = new List<double>();
            foreach (var i in idx)
            {
                var t = Stopwatch.GetTimestamp();
                var r = Pdf.RenderPage(doc.Pages[i], dpi);
                perPage.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            }
            Emit($"render_{dpi}dpi_per_page", perPage, new { pages = idx.Length });
        });
    }
    if (ops.Contains("thumb")) Guard("thumb", () =>
    {
        var perPage = new List<double>();
        foreach (var i in Sample(pages))
        {
            var t = Stopwatch.GetTimestamp();
            Pdf.RenderPage(doc.Pages[i], 24);
            perPage.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        Emit("thumb_24dpi_per_page", perPage, new { pages = perPage.Count });
    });
    if (ops.Contains("save")) Guard("save", () =>
    {
        var ms = Run(() => PdfEditor.Open(data).Save().Length, out var len, Math.Min(iters, 3));
        Emit("save_full", ms, new { out_bytes = len });
    });
    if (ops.Contains("save_incr")) Guard("save_incr", () =>
    {
        var ms = Run(() => { var e = PdfEditor.Open(data); e.SetInfo("Title", "x"); return e.SaveIncremental().Length; }, out var len, Math.Min(iters, 3));
        Emit("save_incremental", ms, new { out_bytes = len });
    });
    if (ops.Contains("validate")) Guard("validate", () =>
    {
        var ms = Run(() => PdfValidator.ValidatePdfA(doc, 2).ToString(), out _, 1);
        Emit("validate_pdfa2", ms);
    });
}
else
{
    UglyToad.PdfPig.PdfDocument? doc = null;
    if (ops.Contains("open")) Guard("open", () =>
    {
        var ms = Run(() => { doc?.Dispose(); doc = UglyToad.PdfPig.PdfDocument.Open(data); return doc.NumberOfPages; }, out var pc, iters);
        Emit("open", ms, new { pages = pc });
    });
    Guard("setup", () => doc ??= UglyToad.PdfPig.PdfDocument.Open(data));
    if (doc is null) return;
    var pages = doc.NumberOfPages;
    if (ops.Contains("text")) Guard("text", () =>
    {
        var ms = Run(() =>
        {
            using var d = UglyToad.PdfPig.PdfDocument.Open(data); long chars = 0;
            foreach (var i in Sample(d.NumberOfPages)) foreach (var l in d.GetPage(i + 1).Letters) chars += l.Value.Length;
            return chars;
        }, out var chars, Math.Min(iters, 3));
        Emit("text_sampled_pages_with_open", ms, new { pages = Sample(pages).Length, chars });
    });
    if (ops.Contains("text_all")) Guard("text_all", () =>
    {
        var ms = Run(() =>
        {
            using var d = UglyToad.PdfPig.PdfDocument.Open(data); long chars = 0;
            foreach (var p in d.GetPages()) foreach (var l in p.Letters) chars += l.Value.Length;
            return chars;
        }, out var chars, Math.Min(iters, 2));
        Emit("text_all_pages_with_open", ms, new { pages, chars });
    });
}
