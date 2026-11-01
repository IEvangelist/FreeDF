using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;
using PortableDocument.Document;

/// <summary>
/// Worker-pool benchmark and smoke: renders the same rasters with one PDF worker and with render replicas, compares decoded RGBA hashes,
/// and checks that a boot failure, a replica crash and low-core devices all fall back to correct single-worker rendering.
/// Usage: --pool &lt;published-wwwroot&gt; &lt;output-dir&gt; &lt;samples-dir&gt; [cores]
/// </summary>
static class EditorPool
{
    sealed record Scenario(string Name, string Query, int Cores, string? Route, int? Memory = null, int BandFactor = 1, int Throttle = 1);

    public static async Task Run(string root, string output, string samples, int cores)
    {
        Directory.CreateDirectory(output);
        var fixture = CreateFixture(samples);
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
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var pw = await Playwright.CreateAsync();
            await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
            Scenario[] scenarios =
            [
                new("single-worker", "?pool=0", cores, null),
                new("pool", "", cores, null),
                new("pool-boot-failure", "", cores, "bootfail"),
                new("pool-replica-crash", "", cores, "crash"),
                new("pool-2-cores", "", 2, null),
                new("pool-low-memory", "", cores, null, 2),
                new("pool-2-bands-per-worker", "", cores, null, null, 2),
                new("single-worker-cpu-4x", "?pool=0", cores, null, null, 1, 4),
                new("pool-cpu-4x", "", cores, null, null, 1, 4),
            ];
            var results = new List<JsonElement>();
            foreach (var s in scenarios)
            {
                await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 } });
                await context.AddInitScriptAsync(
                    $"Object.defineProperty(Navigator.prototype,'hardwareConcurrency',{{get:()=>{s.Cores}}});" +
                    $"window.poolBandFactor={s.BandFactor};" + (s.Memory is { } m ? $"Object.defineProperty(Navigator.prototype,'deviceMemory',{{get:()=>{m}}});" : ""));
                if (s.Route == "bootfail")
                    await context.RouteAsync("**/replica-worker.js", r => r.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = "self.postMessage({ startupError: 'simulated boot failure' });" }));
                if (s.Route == "crash")
                    await context.RouteAsync("**/replica-worker.js", r => r.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = "self.onmessage = () => { throw new Error('simulated replica crash'); }; self.postMessage({ ready: true });" }));
                var page = await context.NewPageAsync();
                if (s.Throttle > 1)
                {
                    var cdp = await context.NewCDPSessionAsync(page);
                    await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = s.Throttle });
                }
                page.Console += (_, m) => { if (m.Type is "warning" or "error") Console.WriteLine($"  [{s.Name}] {m.Text}"); };
                page.SetDefaultTimeout(180000);
                await page.GotoAsync(address + "/csharp-pdf/" + s.Query);
                await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
                await page.Locator("#open").SetInputFilesAsync(fixture);
                await page.WaitForFunctionAsync("document.getElementById('base')?.naturalWidth > 0 && document.getElementById('status').textContent.startsWith('Ready.')");
                await page.WaitForFunctionAsync("document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
                await page.WaitForTimeoutAsync(1500);
                var result = await page.EvaluateAsync<JsonElement>(Script);
                var named = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(result.GetRawText())!;
                named["scenario"] = JsonSerializer.SerializeToElement(s.Name);
                var element = JsonSerializer.SerializeToElement(named);
                results.Add(element);
                Console.WriteLine($"{s.Name}: pool={element.GetProperty("pool")} cold240={element.GetProperty("cold240Ms"):0} warm240={element.GetProperty("warm240Ms"):0} nav137={element.GetProperty("nav137Ms"):0} thumbs={element.GetProperty("thumbsMs"):0} print12={element.GetProperty("printMs"):0} maxFrameGap={element.GetProperty("maxFrameGapMs"):0} engine={element.GetProperty("engine")} tiles={element.GetProperty("timings")}");
            }
            await File.WriteAllTextAsync(Path.Combine(output, "pool.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            var baseline = results[0].GetProperty("hashes").EnumerateArray().Select(h => h.GetString()).ToArray();
            foreach (var r in results.Skip(1))
            {
                var hashes = r.GetProperty("hashes").EnumerateArray().Select(h => h.GetString()).ToArray();
                if (!baseline.SequenceEqual(hashes)) throw new Exception($"{r.GetProperty("scenario")}: decoded pixels differ from the single-worker render.");
            }
            string Pool(int i) => results[i].GetProperty("pool").GetString()!;
            if (!Pool(1).Contains("spawned")) throw new Exception("The pool never spawned a replica: " + Pool(1));
            if (!Pool(1).Contains("cancelled=0")) throw new Exception("Unrelated work was cancelled (print batch must complete): " + Pool(1));
            foreach (var i in new[] { 2, 3 })
                if (!Pool(i).Contains("disabled=true")) throw new Exception($"{results[i].GetProperty("scenario")} should disable the pool and fall back: {Pool(i)}");
            foreach (var i in new[] { 4, 5 })
                if (Pool(i) != "none") throw new Exception($"{results[i].GetProperty("scenario")} must stay single-worker: {Pool(i)}");
            Console.WriteLine("Pool smoke passed: all scenarios produced identical decoded pixels; failure and low-resource scenarios fell back.");
        }
        finally { await app.StopAsync(); File.Delete(fixture); }
    }

    static string CreateFixture(string samples)
    {
        var reportDoc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(samples, "20-report.pdf")));
        var imageDoc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(samples, "09-images.pdf")));
        var editor = PdfEditor.Open(reportDoc);
        for (var i = 0; i < 6; i++) editor.ImportPages(imageDoc, [0]);
        var path = Path.Combine(Path.GetTempPath(), $"freedf-editor-perf-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, editor.Save());
        return path;
    }

    const string Script = """
        async () => {
          const host = window.pdfHost;
          const state = await (await host.request('/api/state')).json();
          const v = state.version, pages = state.pageCount;
          const hash = async image => {
            const bitmap = await createImageBitmap(await (await fetch(image)).blob());
            const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
            const context = canvas.getContext('2d'); context.drawImage(bitmap, 0, 0); bitmap.close();
            const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
            return `${canvas.width}x${canvas.height}:` + [...new Uint8Array(await crypto.subtle.digest('SHA-256', pixels))].map(x => x.toString(16).padStart(2, '0')).join('');
          };
          let maxFrameGapMs = 0, last = performance.now(), running = true;
          const frame = () => { const t = performance.now(); maxFrameGapMs = Math.max(maxFrameGapMs, t - last); last = t; if (running) requestAnimationFrame(frame); };
          requestAnimationFrame(frame);
          const hashes = [];
          const timed = async url => { const t = performance.now(); const image = await host.image(url); const ms = performance.now() - t; hashes.push(await hash(image)); return ms; };
          const url = (page, dpi) => `/api/page/${page}.png?dpi=${dpi}&v=${v}&bare=true`;
          const cold240Ms = await timed(url(0, 239));
          window.profileWorkerStages = [];
                    const warm240Ms = await timed(url(1, 239));
                    const engine = (window.profileWorkerStages ?? []).map(s => s.engine).filter(Boolean);
          let nav137Ms = 0;
          for (let page = 2; page < Math.min(pages, 6); page++) nav137Ms += await timed(url(page, 137));
          const start = performance.now();
          const thumbs = await Promise.all(Array.from({ length: pages }, (_, page) => host.image(`/api/page/${page}.png?dpi=41&v=${v}`)));
          const thumbsMs = performance.now() - start;
          for (const t of thumbs) hashes.push(await hash(t));
          // Print path: every page at 150 dpi in one Promise.all must all complete (no cancellation of unrelated work).
          const printStart = performance.now();
          const printed = await Promise.all(Array.from({ length: pages }, (_, page) => host.image(`/api/page/${page}.png?dpi=150&v=${v}&bare=false`)));
          const printMs = performance.now() - printStart;
          for (const p of printed) hashes.push(await hash(p));
          running = false;
          const pool = host.renderPool;
          const stats = pool ? pool.scheduler.stats : null;
          return { pages, printMs, cold240Ms, warm240Ms, nav137Ms, thumbsMs, maxFrameGapMs, hashes,
            engine, timings: pool ? pool.timings.slice(0, 4) : null,
            pool: pool ? `slots=${pool.maxSlots} spawned=${stats.spawned} completed=${stats.completed} failures=${stats.failures} cancelled=${stats.cancelled} disabled=${pool.scheduler.disabled}` : 'none' };
        }
        """;
}
