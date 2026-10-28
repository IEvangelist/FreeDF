using Microsoft.Playwright;
using Pdf.Document;
using Pdf.Render;

// Usage: Pdf.RenderCompare <pdf|dir>... [--out dir] [--dpi 96] [--page-tolerance 0.02] [--channel msedge|chrome] [--no-browser]
// Renders each PDF with our renderer and with the browser's built-in PDF viewer (PDFium on Chrome/Edge),
// then diffs the two and writes PNGs + report.json. Exit code 1 if any fixture exceeds its tolerance.

var inputs = new List<string>(); var outDir = "compare-out"; double dpi = 96, tol = 0.02; var channel = "msedge"; var noBrowser = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out": outDir = args[++i]; break;
        case "--dpi": dpi = double.Parse(args[++i]); break;
        case "--page-tolerance": tol = double.Parse(args[++i]); break;
        case "--channel": channel = args[++i]; break;
        case "--no-browser": noBrowser = true; break;
        default: inputs.Add(args[i]); break;
    }
}
var files = inputs.SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p, "*.pdf", SearchOption.AllDirectories) : [p]).OrderBy(x => x).ToList();
if (files.Count == 0) { Console.Error.WriteLine("No PDFs given."); return 2; }
Directory.CreateDirectory(outDir);

IPlaywright? pw = null; IBrowser? browser = null;
if (!noBrowser)
{
    pw = await Playwright.CreateAsync();
    browser = await pw.Chromium.LaunchAsync(new() { Channel = channel, Headless = false, Args = ["--headless=new", "--hide-scrollbars"] });
}

var results = new List<object>(); var failed = 0;
foreach (var file in files)
{
    var name = Path.GetFileNameWithoutExtension(file);
    var doc = PdfDocument.Open(file);
    var ours = PdfRenderer.RenderPage(doc.Pages[0], dpi);
    File.WriteAllBytes(Path.Combine(outDir, name + ".ours.png"), ours.ToPng());
    if (browser is null) { results.Add(new { file = name, warnings = ours.Warnings }); continue; }

    var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1400, Height = 1800 } });
    await page.GotoAsync(new Uri(Path.GetFullPath(file)).AbsoluteUri + "#toolbar=0&navpanes=0&scrollbar=0&zoom=" + (int)(dpi / 96 * 100 * 2));
    await page.WaitForTimeoutAsync(2500);
    var shot = await page.ScreenshotAsync();
    await page.CloseAsync();
    File.WriteAllBytes(Path.Combine(outDir, name + ".viewer.png"), shot);
    var (rw, rh, rrgba) = PngWriter.Decode(shot);
    var crop = ImageDiff.CropPage(rw, rh, rrgba);
    if (crop is null) { results.Add(new { file = name, error = "could not locate page in viewer screenshot" }); failed++; Console.WriteLine($"FAIL {name}: page not found in viewer screenshot"); continue; }
    var refRgba = ImageDiff.Resize(crop.Value.Rgba, crop.Value.W, crop.Value.H, ours.Width, ours.Height);
    File.WriteAllBytes(Path.Combine(outDir, name + ".ref.png"), PngWriter.Encode(ours.Width, ours.Height, refRgba));
    var d = ImageDiff.Compare(ours.Rgba, refRgba, ours.Width, ours.Height);
    File.WriteAllBytes(Path.Combine(outDir, name + ".diff.png"), PngWriter.Encode(ours.Width, ours.Height, d.DiffImage));
    var ok = d.BadFraction <= tol;
    if (!ok) failed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: bad={d.BadFraction:P2} meanAbs={d.MeanAbs:F2} (tol {tol:P1})");
    results.Add(new { file = name, pass = ok, badFraction = d.BadFraction, meanAbs = d.MeanAbs, warnings = ours.Warnings });
}
File.WriteAllText(Path.Combine(outDir, "report.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
if (browser is not null) await browser.CloseAsync();
pw?.Dispose();
return failed == 0 ? 0 : 1;

static class ImageDiff
{
    // The viewer shows the page as a light rectangle on a darker background; find it by differing from the corner colour.
    public static (int W, int H, byte[] Rgba)? CropPage(int w, int h, byte[] px)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        var bgR = px[0]; var bgG = px[1]; var bgB = px[2];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                if (Math.Abs(px[o] - bgR) + Math.Abs(px[o + 1] - bgG) + Math.Abs(px[o + 2] - bgB) < 24) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        if (maxX < 0) return null;
        minX += 1; minY += 1; maxX -= 1; maxY -= 1;
        var cw = maxX - minX + 1; var ch = maxY - minY + 1;
        if (cw < 8 || ch < 8) return null;
        var o2 = new byte[cw * ch * 4];
        for (var y = 0; y < ch; y++) Buffer.BlockCopy(px, ((minY + y) * w + minX) * 4, o2, y * cw * 4, cw * 4);
        return (cw, ch, o2);
    }

    public static byte[] Resize(byte[] src, int sw, int sh, int dw, int dh)
    {
        var o = new byte[dw * dh * 4];
        for (var y = 0; y < dh; y++)
            for (var x = 0; x < dw; x++)
            {
                int x0 = x * sw / dw, x1 = Math.Max(x0 + 1, (x + 1) * sw / dw), y0 = y * sh / dh, y1 = Math.Max(y0 + 1, (y + 1) * sh / dh);
                int r = 0, g = 0, b = 0, n = 0;
                for (var yy = y0; yy < y1 && yy < sh; yy++)
                    for (var xx = x0; xx < x1 && xx < sw; xx++) { var s = (yy * sw + xx) * 4; r += src[s]; g += src[s + 1]; b += src[s + 2]; n++; }
                var d = (y * dw + x) * 4;
                o[d] = (byte)(r / n); o[d + 1] = (byte)(g / n); o[d + 2] = (byte)(b / n); o[d + 3] = 255;
            }
        return o;
    }

    public record Result(double BadFraction, double MeanAbs, byte[] DiffImage);

    // A pixel is "bad" if no pixel in its 3x3 neighbourhood of the other image is within 48/255 per channel;
    // this tolerates the sub-pixel anti-aliasing differences expected between two independent rasterizers.
    public static Result Compare(byte[] a, byte[] b, int w, int h)
    {
        var diff = new byte[w * h * 4]; long bad = 0; double sum = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                sum += (Math.Abs(a[o] - b[o]) + Math.Abs(a[o + 1] - b[o + 1]) + Math.Abs(a[o + 2] - b[o + 2])) / 3.0;
                var isBad = !Near(a, b, w, h, x, y) && !Near(b, a, w, h, x, y);
                if (isBad) bad++;
                var g = isBad ? (byte)0 : (byte)(255 - Math.Min(255, Math.Abs(a[o] - b[o]) / 2));
                diff[o] = isBad ? (byte)255 : g; diff[o + 1] = g; diff[o + 2] = g; diff[o + 3] = 255;
            }
        return new Result((double)bad / (w * h), sum / (w * h), diff);
    }

    static bool Near(byte[] a, byte[] b, int w, int h, int x, int y)
    {
        var o = (y * w + x) * 4;
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                var p = (yy * w + xx) * 4;
                if (Math.Abs(a[o] - b[p]) <= 48 && Math.Abs(a[o + 1] - b[p + 1]) <= 48 && Math.Abs(a[o + 2] - b[p + 2]) <= 48) return true;
            }
        return false;
    }
}
