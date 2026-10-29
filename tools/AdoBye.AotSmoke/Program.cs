using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using PortableDocument.Document;
using PortableDocument.Render;

if (RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("Run the published NativeAOT executable, not the managed build.");

var iterations = 1;
var requireSatori = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--iterations" when i + 1 < args.Length:
            iterations = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
            break;
        case "--require-satori":
            requireSatori = true;
            break;
        default:
            throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
    }
}

var satori = GC.GetConfigurationVariables().ContainsKey("SatoriGC");
if (requireSatori && !satori)
    throw new InvalidOperationException("Satori GC was requested but is not active.");

var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "cjk-rksj.pdf"));
Smoke(bytes);
var before = GC.GetAllocatedBytesForCurrentThread();
var timer = Stopwatch.StartNew();
for (var i = 0; i < iterations; i++) Smoke(bytes);
timer.Stop();
var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
Console.WriteLine($"PASS NativeAOT: dynamic-code={RuntimeFeature.IsDynamicCodeSupported}, SatoriGC={satori}, " +
    $"iterations={iterations}, elapsed-ms={timer.Elapsed.TotalMilliseconds:F3}, allocated-bytes={allocated}.");

static void Smoke(byte[] bytes)
{
    var document = PdfDocument.Load(bytes);
    GlyphNameSmoke.Run(document.File);
    Check(document.Pages.Count == 1, "page count");
    Check(Text(document) == "AB\u3042", "embedded CJK text and predefined CMap resources");
    var rendered = DocumentRenderer.RenderPage(document.Pages[0], 72);
    Check(rendered.Width > 0 && rendered.Height > 0, "raster dimensions");
    Check(rendered.Rgba.Length == rendered.Width * rendered.Height * 4, "raster length");
    Check(Enumerable.Range(0, rendered.Rgba.Length / 4).Any(i => rendered.Rgba[i * 4] < 128), "visible glyph pixels");
    Check(rendered.ToPng().AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG encoding");

    var editor = PdfEditor.Open(document);
    editor.SetInfo("Title", "NativeAOT smoke");
    var saved = editor.Save();
    var reloaded = PdfDocument.Load(saved);
    Check(reloaded.Info("Title") == "NativeAOT smoke", "edited metadata");
    Check(Text(reloaded) == "AB\u3042", "saved text");
    Check(DocumentRenderer.RenderPage(reloaded.Pages[0], 72).Rgba.AsSpan().SequenceEqual(rendered.Rgba), "saved rendering");

    // Exercise xref repair and the native byte-pattern dependency as well as ordinary parsing.
    var damaged = (byte[])saved.Clone();
    var offset = damaged.AsSpan().LastIndexOf("startxref"u8);
    Check(offset >= 0, "saved startxref");
    offset += "startxref".Length;
    while (offset < damaged.Length && damaged[offset] is 10 or 13 or 32) offset++;
    while (offset < damaged.Length && damaged[offset] is >= (byte)'0' and <= (byte)'9') damaged[offset++] = (byte)'0';
    Check(Text(PdfDocument.Load(damaged)) == "AB\u3042", "xref repair text");
}

static string Text(PdfDocument document) =>
    string.Concat(DocumentRenderer.ExtractText(document.Pages[0]).Select(span => span.Text));

static void Check(bool success, string operation)
{
    if (!success) throw new InvalidOperationException($"NativeAOT smoke failed: {operation}.");
}
