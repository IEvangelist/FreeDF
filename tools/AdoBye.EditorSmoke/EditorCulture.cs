using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

/// <summary>Browser locale must not change PDF-affecting culture behaviour or which ICU data is downloaded.</summary>
static class EditorCulture
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Culture: " + message); }

    public static async Task Run(string root)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        using var files = new PhysicalFileProvider(Path.GetFullPath(root));
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".dat"] = "application/octet-stream";
        app.UsePathBase("/csharp-pdf");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/csharp-pdf/";
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
        var summaries = new List<string>();
        foreach (var locale in new[] { "tr-TR", "de-DE", "ja-JP" })
        {
            await using var context = await browser.NewContextAsync(new() { Locale = locale, ServiceWorkers = ServiceWorkerPolicy.Block });
            var requested = new ConcurrentQueue<string>();
            context.Request += (_, request) => requested.Enqueue(new Uri(request.Url).AbsolutePath);
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(90000);
            await page.GotoAsync(url);
            await page.WaitForFunctionAsync("document.getElementById('status')?.textContent.startsWith('Ready.')");
            using var diagnostics = JsonDocument.Parse(await page.EvaluateAsync<string>("window.pdfHost.request('/api/_diagnostics').then(r => r.text())"));
            var d = diagnostics.RootElement;
            Check(await page.EvaluateAsync<string>("navigator.language") == locale, $"{locale}: browser locale applied.");
            Check(d.GetProperty("culture").GetString() == "", $"{locale}: engine culture is invariant.");
            Check(d.GetProperty("decimalFormat").GetString() == "1234.5", $"{locale}: decimals use a point.");
            Check(d.GetProperty("upper").GetString() == "I", $"{locale}: no Turkish casing.");
            Check(d.GetProperty("normalizesDecomposed").GetBoolean() && d.GetProperty("decomposes").GetBoolean(), $"{locale}: Unicode normalization works (redaction matches canonical equivalents).");
            Check(!d.GetProperty("invariantGlobalization").GetBoolean(), $"{locale}: ICU is loaded.");
            await page.SetInputFilesAsync("#open", new FilePayload { Name = "culture.pdf", MimeType = "application/pdf", Buffer = EditorServiceWorker.MinimalPdf() });
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'culture.pdf' && document.getElementById('base')?.dataset.loadedUrl && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
            var pooledMatches = await page.EvaluateAsync<bool>("""
                async () => {
                  const url = new URL(document.getElementById('base').dataset.targetUrl, location.href);
                  url.searchParams.set('dpi', '240');
                  const path = url.pathname + url.search;
                  const primary = await pdfHost.imageBlob(path, {pool: false});
                  const pooled = await pdfHost.renderPool?.tryRender(path);
                  if (!pooled) return false;
                  const hash = async blob => {
                    const bitmap = await createImageBitmap(blob), canvas = document.createElement('canvas');
                    canvas.width = bitmap.width; canvas.height = bitmap.height;
                    const ctx = canvas.getContext('2d'); ctx.drawImage(bitmap, 0, 0); bitmap.close();
                    return [...new Uint8Array(await crypto.subtle.digest('SHA-256', ctx.getImageData(0, 0, canvas.width, canvas.height).data))].join(',');
                  };
                  return await hash(primary) === await hash(pooled) && pdfHost.renderPool.scheduler.stats.failures === 0;
                }
                """);
            Check(pooledMatches, $"{locale}: replica boots with the same ICU selection and matches primary pixels.");
            var icu = requested.Where(p => p.Contains("icudt_", StringComparison.Ordinal)).Distinct().ToArray();
            Check(icu.Length == 1 && icu[0].Contains("icudt_EFIGS", StringComparison.Ordinal), $"{locale}: only the EFIGS ICU file is fetched ({string.Join(", ", icu)}).");
            summaries.Add(locale);
        }
        Console.WriteLine($"PASS: culture-independent engine (invariant culture, normalization, no Turkish casing, single EFIGS ICU file) for {string.Join(", ", summaries)} browser locales.");
    }
}
