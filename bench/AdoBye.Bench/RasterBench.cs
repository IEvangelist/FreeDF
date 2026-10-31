using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;

/// <summary>
/// Repeatable raster/decode throughput harness: renders each corpus page at a fixed dpi, reports best/median wall time, managed allocation and a
/// SHA-256 of the RGBA output. A previous run's output can be passed as a baseline to prove pixel identity.
/// Usage: --raster-bench &lt;dpi&gt; &lt;iterations&gt; [baseline.tsv] [filter] ; writes the table to stdout (TSV lines "name\thash\tbestMs\tmedianMs\tallocBytes").
/// </summary>
static class RasterBench
{
    static readonly string[] Groups =
    [
        "01-spec-showcase.pdf", "03-typography-embedded.pdf", "05-vector-graphics.pdf", "06-patterns-shadings.pdf", "07-transparency.pdf",
        "09-images.pdf", "20-report.pdf", "21-brochure.pdf", "24-poster.pdf", "25-dashboard-charts.pdf", "text.pdf", "vector.pdf", "image.pdf",
    ];

    static IEnumerable<(string Name, byte[] Bytes)> Corpus(string? filter)
    {
        var fx = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var n in Groups)
        {
            if (filter is not null && !n.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var p = Path.Combine(fx, n);
            if (File.Exists(p)) yield return (n, File.ReadAllBytes(p));
        }
        var repo = FindRepo();
        if (repo is null) yield break;
        foreach (var sub in new[] { "jbig2\\annexh-p1.pdf", "ccitt\\g4.pdf", "graphics\\transparency.pdf", "graphics\\mesh.pdf", "jpx\\cblk-small.pdf", "features\\features.pdf" })
        {
            var p = Path.Combine(repo, "tests", "fixtures", sub);
            var n = sub.Replace('\\', '/');
            if (File.Exists(p) && (filter is null || n.Contains(filter, StringComparison.OrdinalIgnoreCase))) yield return (n, File.ReadAllBytes(p));
        }
    }

    static string? FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "tests", "fixtures"))) return d.FullName;
        return null;
    }

    public static void Codecs()
    {
        var fx = Path.Combine(AppContext.BaseDirectory, "fixtures");
        double Best(Action a, int n) { a(); var b = double.MaxValue; for (var i = 0; i < n; i++) { var sw = Stopwatch.StartNew(); a(); b = Math.Min(b, sw.Elapsed.TotalMilliseconds); } return b; }
        foreach (var f in new[] { "photo.jpg", "photo-prog.jpg" })
        {
            var d = File.ReadAllBytes(Path.Combine(fx, f)); int w = 0, h = 0, c = 0;
            var ms = Best(() => PortableDocument.Content.JpegDecoder.Decode(d, out w, out h, out c), 10);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{f}\t{w}x{h}x{c}\t{ms:F1} ms\t{w * h / 1000.0 / ms:F1} Mpx/s"));
        }
        var repo = FindRepo()!;
        foreach (var rel in new[] { "jbig2/annexh-p1.pdf", "ccitt/g4.pdf", "ccitt/g3-1d.pdf", "jpx/cblk-small.pdf", "jpx/cblk-style-all.pdf" })
        {
            var b = File.ReadAllBytes(Path.Combine(repo, "tests", "fixtures", rel));
            var ms = Best(() => { var doc = PdfDocument.Load(b); foreach (var p in doc.Pages) DocumentRenderer.RenderPage(p, 72); }, 10);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{rel}\tload+render72\t{ms:F2} ms"));
        }
        var big = new byte[8 << 20]; new Random(1).NextBytes(big);
        for (var i = 0; i < big.Length; i++) big[i] &= 0x0F;
        var z = PortableDocument.Core.PdfFilters.DeflateEncode(big);
        var fl = Best(() => PortableDocument.Core.PdfFilters.Flate(z), 10);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"flate 8MB out ({z.Length} in)\t{fl:F1} ms\t{big.Length / 1048576.0 / fl * 1000:F0} MB/s"));
    }

    public static void Micro()
    {
        using var r = new PortableDocument.Render.SoftwareRasterizer(2040, 2640);
        var id = PortableDocument.Content.Matrix.Identity;
        PortableDocument.Content.PathData Rect(double x, double y, double w, double h) { var p = new PortableDocument.Content.PathData(); p.Rect(x, y, w, h); return p; }
        PortableDocument.Content.PathData Circle(double cx, double cy, double rad)
        {
            var p = new PortableDocument.Content.PathData();
            const double k = 0.5522847498;
            p.MoveTo(cx + rad, cy);
            p.CurveTo(cx + rad, cy + k * rad, cx + k * rad, cy + rad, cx, cy + rad);
            p.CurveTo(cx - k * rad, cy + rad, cx - rad, cy + k * rad, cx - rad, cy);
            p.CurveTo(cx - rad, cy - k * rad, cx - k * rad, cy - rad, cx, cy - rad);
            p.CurveTo(cx + k * rad, cy - rad, cx + rad, cy - k * rad, cx + rad, cy);
            p.Close();
            return p;
        }
        var col = new PortableDocument.Content.RgbColor(0.2, 0.4, 0.6);
        void Time(string label, int n, Action<int> body, double pixelsPerOp)
        {
            body(0);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < n; i++) body(i);
            var ms = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label}: {ms:F1} ms for {n} ops = {ms * 1000 / n:F1} us/op, {(pixelsPerOp > 0 ? ms * 1e6 / (n * pixelsPerOp) : 0):F2} ns/px"));
        }
        foreach (var grouped in new[] { false, true })
        {
            Console.WriteLine(grouped ? "-- inside isolated group (page layer path)" : "-- direct on page");
            if (grouped) r.BeginGroup(true, false, 1, "Normal");
            Time("rect 1000x1000 pixel-aligned", 20, i => r.Fill(Rect(100, 100, 1000, 1000), id, false, col, 1, "Normal"), 1000 * 1000);
            Time("rect 1000x1000 fractional", 20, i => r.Fill(Rect(100.3, 100.6, 1000, 1000), id, false, col, 1, "Normal"), 1000 * 1000);
            Time("circle r=400", 20, i => r.Fill(Circle(1000, 1000, 400), id, false, col, 1, "Normal"), Math.PI * 400 * 400);
            Time("rect 1000x1000 alpha .5", 20, i => r.Fill(Rect(100, 100, 1000, 1000), id, false, col, 0.5, "Normal"), 1000 * 1000);
            Time("small circle r=8 (glyph-sized)", 5000, i => r.Fill(Circle(50 + (i % 200) * 9.37, 50 + (i / 200) * 9.11, 8), id, false, col, 1, "Normal"), Math.PI * 64);
            var line = new PortableDocument.Content.PathData(); line.MoveTo(100, 100); line.LineTo(1900, 2500);
            var st = new PortableDocument.Content.StrokeStyle(2, default, default, 10, [], 0);
            Time("diagonal stroke w=2 (long)", 200, i => r.Stroke(line, id, st, col, 1, "Normal"), 0);
            if (grouped) r.EndGroup();
        }
    }

    public static void Phases(string filter, double dpi, int iterations)
    {
        foreach (var (name, bytes) in Corpus(filter))
        {
            double interp = double.MaxValue, full = double.MaxValue, ctor = double.MaxValue, rgba = double.MaxValue, load = double.MaxValue;
            for (var i = 0; i < iterations + 1; i++)
            {
                var sw = Stopwatch.StartNew();
                var doc = PdfDocument.Load(bytes);
                var tLoad = sw.Elapsed.TotalMilliseconds;
                double tInterp = 0, tFull = 0, tCtor = 0, tRgba = 0;
                foreach (var p in doc.Pages)
                {
                    var m = DocumentRenderer.PageMatrix(p, dpi / 72.0 * p.UserUnit, out var w, out var h);
                    sw.Restart();
                    new PortableDocument.Content.ContentInterpreter(p.Document.File, new NullBackend()).Render(p, m);
                    tInterp += sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    using (var r = new PortableDocument.Render.SoftwareRasterizer(w, h)) { r.ToRgba(); }
                    tCtor += sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    DocumentRenderer.RenderPage(p, dpi);
                    tFull += sw.Elapsed.TotalMilliseconds;
                }
                if (i == 0) continue;
                load = Math.Min(load, tLoad); interp = Math.Min(interp, tInterp); full = Math.Min(full, tFull); ctor = Math.Min(ctor, tCtor);
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}\tload {load:F1}\tinterpOnly {interp:F1}\tblankRaster+ToRgba {ctor:F1}\tfullRender {full:F1}"));
        }
    }

    sealed class NullBackend : PortableDocument.Content.IRenderBackend
    {
        public void Save() { }
        public void Restore() { }
        public void Clip(PortableDocument.Content.PathData path, PortableDocument.Content.Matrix ctm, bool evenOdd) { }
        public void Fill(PortableDocument.Content.PathData path, PortableDocument.Content.Matrix ctm, bool evenOdd, PortableDocument.Content.RgbColor color, double alpha, string blendMode) { }
        public void Stroke(PortableDocument.Content.PathData path, PortableDocument.Content.Matrix ctm, PortableDocument.Content.StrokeStyle style, PortableDocument.Content.RgbColor color, double alpha, string blendMode) { }
        public void DrawImage(PortableDocument.Content.ImageData image, PortableDocument.Content.Matrix ctm, double alpha, PortableDocument.Content.RgbColor maskColor, string blendMode) { }
        public void PaintShader(Func<double, double, PortableDocument.Content.RgbColor?> deviceToColor, double alpha, string blendMode) { }
        public void SetSoftMask(Action<PortableDocument.Content.IRenderBackend>? draw, bool luminosity, PortableDocument.Content.RgbColor backdrop) { }
    }

    public static void Run(double dpi, int iterations, string? baselinePath, string? filter)
    {
        var baseline = new Dictionary<string, string>();
        if (baselinePath is not null && File.Exists(baselinePath))
            foreach (var line in File.ReadAllLines(baselinePath)) { var f = line.Split('\t'); if (f.Length >= 2) baseline[f[0]] = f[1]; }
        var sb = new StringBuilder();
        var mismatches = 0;
        double total = 0;
        foreach (var (name, bytes) in Corpus(filter))
        {
            string hash = "";
            var times = new List<double>();
            long alloc = 0;
            try
            {
                for (var i = 0; i < iterations + 1; i++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var sw = Stopwatch.StartNew();
                    var h = new StringBuilder();
                    var doc = PdfDocument.Load(bytes);
                    foreach (var p in doc.Pages)
                    {
                        var r = DocumentRenderer.RenderPage(p, dpi);
                        if (i == 0) h.Append(Convert.ToHexString(SHA256.HashData(r.Rgba)));
                    }
                    sw.Stop();
                    if (i == 0) { hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(h.ToString()))); continue; } // warm-up pass
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    alloc = GC.GetAllocatedBytesForCurrentThread() - before;
                }
            }
            catch (Exception e) { hash = "ERR:" + e.GetType().Name; times.Add(0); }
            times.Sort();
            var best = times.Count > 0 ? times[0] : 0; var med = times.Count > 0 ? times[times.Count / 2] : 0;
            total += med;
            var status = baseline.TryGetValue(name, out var b) ? (b == hash ? "same" : "DIFF") : "";
            if (status == "DIFF") mismatches++;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name}\t{hash}\t{best:F1}\t{med:F1}\t{alloc}\t{status}"));
        }
        Console.Write(sb);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# total median ms {total:F1}; mismatches {mismatches}"));
    }
}
