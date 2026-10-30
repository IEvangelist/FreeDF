using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;
using PortableDocument.Document;

static class EditorPerformance
{
    const string Observe = """
        window.profileCommands = [];
        window.profileLongTasks = [];
        window.profileHeartbeatDelays = [];
        window.profileInputDelays = [];
        window.profileWorkerStages = [];
        window.profileRenderStages = [];
        window.profileInteractions = [];
        const heartbeatUrl = URL.createObjectURL(new Blob(['setInterval(()=>postMessage(performance.timeOrigin+performance.now()),16)'], {type:'text/javascript'}));
        const heartbeat = new Worker(heartbeatUrl);
        URL.revokeObjectURL(heartbeatUrl);
        heartbeat.onmessage = e => profileHeartbeatDelays.push(performance.timeOrigin + performance.now() - e.data);
        addEventListener('pointermove', e => profileInputDelays.push(performance.now()-e.timeStamp));
        new PerformanceObserver(list => {
          for (const e of list.getEntries()) window.profileLongTasks.push({start: e.startTime, ms: e.duration});
        }).observe({type:'longtask', buffered:true});
        """;
    const string Instrument = """
        () => {
          const host = window.pdfHost;
          for (const name of ['request', 'image']) {
            const original = host[name].bind(host);
            host[name] = name === 'request'
              ? async (url, options) => {
                  const start = performance.now();
                  try { return await original(url, options); }
                  finally { profileCommands.push({url, start, ms:performance.now()-start}); }
                }
              : url => {
                  const start = performance.now();
                  const record = () => profileCommands.push({url, start, ms:performance.now()-start});
                  try {
                    const value = original(url);
                    if (value && typeof value.then === 'function') return value.finally(record);
                    record(); return value;
                  } catch (e) { record(); throw e; }
                };
          }
        }
        """;

    public static async Task Run(string source, string output, string[] documents)
    {
        string? largeFixture = null;
        WebApplication? app = null;
        PhysicalFileProvider? files = null;
        var records = new List<object>();
        try
        {
            largeFixture = CreateLargeFixture(documents);
            documents = [.. documents, largeFixture];
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                app = builder.Build();
                files = new PhysicalFileProvider(Path.GetFullPath(source));
                app.UsePathBase("/csharp-pdf");
                var types = new FileExtensionContentTypeProvider();
                types.Mappings[".dat"] = "application/octet-stream";
                app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
                app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
                await app.StartAsync();
                source = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/csharp-pdf/";
            }
            using var pw = await Playwright.CreateAsync();
            await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
            for (var repeat = 0; repeat < 3; repeat++)
            {
                await using var context = await browser.NewContextAsync(new() { AcceptDownloads = true, ViewportSize = new() { Width = 1440, Height = 1000 }, DeviceScaleFactor = 1 });
                await context.AddInitScriptAsync(Observe);
                var page = await context.NewPageAsync();
                page.SetDefaultTimeout(180000);
                page.PageError += (_, error) => Console.Error.WriteLine(error);
                var cdp = await context.NewCDPSessionAsync(page);
                await cdp.SendAsync("Performance.enable");
                await cdp.SendAsync("Network.enable");
                await cdp.SendAsync("Network.setCacheDisabled", new Dictionary<string, object> { ["cacheDisabled"] = true });
                await page.GotoAsync(source);
                await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
                await Frames(page);
                records.Add(new { repeat, document = "", phase = "cold-start", result = await page.EvaluateAsync<JsonElement>("({ms:performance.now(), longTasks:profileLongTasks, resources:performance.getEntriesByType('resource').map(e=>({name:e.name, bytes:e.transferSize, ms:e.duration}))})") });
                await page.EvaluateAsync(Instrument);
                foreach (var document in documents)
                {
                    var name = Path.GetFileName(document);
                    double? inputAckMs = null;
                    await Measure("open", async () =>
                    {
                        await page.Locator("#open").SetInputFilesAsync(Path.GetFullPath(document));
                        await page.WaitForFunctionAsync("document.getElementById('name').textContent.includes(" + JsonSerializer.Serialize(name) + ") && document.getElementById('status').textContent.startsWith('Ready.') && document.getElementById('base')?.naturalWidth > 0");
                    });
                    var pageCount = await page.Locator("#pt").TextContentAsync();
                    if (name.StartsWith("adobye-editor-perf-", StringComparison.Ordinal) && pageCount != "/ 12")
                        throw new Exception($"Large performance fixture has unexpected page count: {pageCount}.");
                    await SidebarProbe(page, repeat, name, "desktop", 1440, output, records);
                    await SidebarProbe(page, repeat, name, "mobile", 390, output, records);
                    await page.SetViewportSizeAsync(1440, 1000);
                    await Frames(page);
                    var baseImage = await page.Locator("#base").GetAttributeAsync("src");
                    records.Add(new { repeat, document = name, phase = "first-page-image", hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(baseImage!.Split(',')[1]))) });
                    var stressDpi = name.StartsWith("adobye-editor-perf-", StringComparison.Ordinal) ? 240 : 137;
                    await Measure("fresh-render-input", async () =>
                    {
                        var render = page.EvaluateAsync($"async () => {{ await new Promise(resolve=>setTimeout(resolve,20)); await pdfHost.image({JsonSerializer.Serialize($"/api/page/0.png?dpi={stressDpi}")}); }}");
                        await Task.Delay(50);
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        await cdp.SendAsync("Input.dispatchMouseEvent", new Dictionary<string, object>
                        {
                            ["type"] = "mouseMoved", ["x"] = 20, ["y"] = 20,
                            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                        });
                        inputAckMs = watch.Elapsed.TotalMilliseconds;
                        await render;
                    });
                    await Measure("zoom", async () =>
                    {
                        var started = await page.EvaluateAsync<double>("performance.now()");
                        await page.Locator("#zi").ClickAsync();
                        await Frames(page);
                        var visualMs = await page.EvaluateAsync<double>("start => performance.now() - start", started);
                        await page.EvaluateAsync("(x)=>profileInteractions.push(x)", new { phase = "zoom", ms = visualMs });
                        await page.WaitForFunctionAsync("() => { const img = document.getElementById('base'); return img.dataset.loadedUrl === img.dataset.targetUrl; }");
                    });
                    await page.Locator("#fp").ClickAsync();
                    await Frames(page);
                    if (await page.Locator(".thumb").CountAsync() > 1)
                    {
                        await Measure("next-page", async () =>
                        {
                            await page.Locator("#np").ClickAsync();
                            await page.WaitForFunctionAsync("document.getElementById('pn').value === '2' && document.getElementById('base').naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl.includes('/api/page/1.png')");
                        });
                        await page.Locator("#pp").ClickAsync();
                        await page.WaitForFunctionAsync("document.getElementById('pn').value === '1' && document.getElementById('base').naturalWidth > 0");
                    }
                    await Measure("add-overlay", async () =>
                    {
                        await page.Locator("[data-tool=rect]").ClickAsync();
                        var box = (await page.Locator("#layer").BoundingBoxAsync())!;
                        await page.Mouse.MoveAsync(box.X + 80, box.Y + 80);
                        var started = await page.EvaluateAsync<double>("performance.now()");
                        await page.Mouse.DownAsync();
                        await page.Mouse.MoveAsync(box.X + 150, box.Y + 130, new() { Steps = 5 });
                        await page.Mouse.UpAsync();
                        await Frames(page);
                        var visualMs = await page.EvaluateAsync<double>("start => performance.now() - start", started);
                        await page.EvaluateAsync("(x)=>profileInteractions.push(x)", new { phase = "add-overlay", ms = visualMs });
                        await Saved(page);
                    });
                    await Measure("selection-drag", async () =>
                    {
                        var box = (await page.Locator("#layer .it").Last.BoundingBoxAsync())!;
                        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                        var started = await page.EvaluateAsync<double>("performance.now()");
                        await page.Mouse.DownAsync();
                        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 20, box.Y + box.Height / 2 + 20, new() { Steps = 12 });
                        await page.Mouse.UpAsync();
                        await Frames(page);
                        var visualMs = await page.EvaluateAsync<double>("start => performance.now() - start", started);
                        await page.EvaluateAsync("(x)=>profileInteractions.push(x)", new { phase = "selection-drag", ms = visualMs });
                        await Saved(page);
                    });
                    await Measure("undo", async () =>
                    {
                        var started = await page.EvaluateAsync<double>("performance.now()");
                        await page.Keyboard.PressAsync("Control+z");
                        await Frames(page);
                        var visualMs = await page.EvaluateAsync<double>("start => performance.now() - start", started);
                        await page.EvaluateAsync("(x)=>profileInteractions.push(x)", new { phase = "undo", ms = visualMs });
                        await Saved(page);
                    });
                    var span = page.Locator("#layer .span").First;
                    if (await span.CountAsync() > 0)
                    {
                        await Measure("edit-text", async () =>
                        {
                            await span.ClickAsync(new() { Force = true });
                            var text = page.Locator("#props textarea[data-k=text], #props textarea[data-original-text]");
                            var started = await page.EvaluateAsync<double>("performance.now()");
                            await text.FillAsync("Measured editor change");
                            await text.PressAsync("Tab");
                            await page.WaitForFunctionAsync("profileCommands.some(c=>/\\/items$/.test(c.url))");
                            var commitMs = await page.EvaluateAsync<double>(
                              "start => profileCommands.filter(c => /\\/items$/.test(c.url)).reduce((latest, c) => Math.max(latest, c.start + c.ms - start), 0)", started);
                            await page.EvaluateAsync("(x)=>profileInteractions.push(x)", new { phase = "text-edit-commit", ms = commitMs });
                            await Saved(page);
                        });
                    }
                    await Measure("save-download", async () =>
                    {
                        await page.Locator("#dl").ClickAsync();
                        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
                        await page.Locator(".mdl").WaitForAsync(new() { State = WaitForSelectorState.Detached });
                        if (await download.FailureAsync() is { } failure) throw new Exception(failure);
                    });
                    if (await page.Locator("#recovery").CountAsync() > 0)
                        await Measure("local-recovery", () => page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'"));
                    async Task Measure(string phase, Func<Task> action)
                    {
                        await page.EvaluateAsync("() => { profileCommands=[]; profileLongTasks=[]; profileHeartbeatDelays=[]; profileInputDelays=[]; profileWorkerStages=[]; profileRenderStages=[]; profileInteractions=[]; window.profileStart=performance.now(); }");
                        var before = await cdp.SendAsync("Performance.getMetrics");
                        await action();
                        await Frames(page);
                        var result = await page.EvaluateAsync<JsonElement>("({ms:performance.now()-profileStart, commands:profileCommands, longTasks:profileLongTasks, heartbeatDelays:profileHeartbeatDelays, inputDelays:profileInputDelays, workerStages:profileWorkerStages, renderStages:profileRenderStages, interactions:profileInteractions})");
                        var after = await cdp.SendAsync("Performance.getMetrics");
                        records.Add(new { repeat, document = name, phase, result, inputAckMs = phase == "fresh-render-input" ? inputAckMs : null, before, after });
                        Console.WriteLine($"{repeat} {name} {phase}: {result.GetProperty("ms").GetDouble():F0} ms, {result.GetProperty("commands").GetArrayLength()} commands");
                        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
                        if (phase is "fresh-render-input" or "local-recovery" && await page.EvaluateAsync<bool>("pdfHost.execution === 'worker'") &&
                            ((phase == "fresh-render-input" && inputAckMs >= 200) || result.GetProperty("heartbeatDelays").EnumerateArray().Any(delay => delay.GetDouble() >= 200)))
                            throw new Exception($"{name}: worker render exceeded the 200 ms input/heartbeat latency goal.");
                    }
                }
            }
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            files?.Dispose();
            if (largeFixture is not null) File.Delete(largeFixture);
        }
    }

    static string CreateLargeFixture(string[] documents)
    {
        var report = documents.Single(path => Path.GetFileName(path) == "20-report.pdf");
        var images = documents.Single(path => Path.GetFileName(path) == "09-images.pdf");
        var reportDoc = PdfDocument.Load(File.ReadAllBytes(report));
        var imageDoc = PdfDocument.Load(File.ReadAllBytes(images));
        var editor = PdfEditor.Open(reportDoc);
        for (var i = 0; i < 6; i++) editor.ImportPages(imageDoc, [0]);
        var path = Path.Combine(Path.GetTempPath(), $"adobye-editor-perf-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, editor.Save());
        return path;
    }

    static Task Frames(IPage page) => page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

    static async Task SidebarProbe(IPage page, int repeat, string document, string viewport, int width, string output, List<object> records)
    {
        await page.SetViewportSizeAsync(width, 1000);
        await page.WaitForTimeoutAsync(250);
        await page.WaitForFunctionAsync("document.getElementById('base')?.naturalWidth > 0");
        await page.WaitForFunctionAsync("document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
        await page.EvaluateAsync("""
            () => {
              profileCommands=[]; profileWorkerStages=[]; window.sidebarSamples=[];
              window.sidebarSampling=true;
              const sample=()=>{
                const img=document.getElementById('base');
                if(img) {
                  const r=img.getBoundingClientRect(), s=getComputedStyle(img);
                  sidebarSamples.push({loaded:img.complete&&img.naturalWidth>0, visible:s.visibility!=='hidden'&&s.display!=='none'&&+s.opacity>0,
                    width:r.width,height:r.height,loadedUrl:img.dataset.loadedUrl});
                } else sidebarSamples.push({loaded:false,visible:false,width:0,height:0});
                if(window.sidebarSampling) requestAnimationFrame(sample);
              };
              requestAnimationFrame(sample);
            }
            """);
        var before = await page.Locator("#base").GetAttributeAsync("data-loaded-url");
        var artifacts = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
        if (artifacts is not null)
        {
            Directory.CreateDirectory(artifacts);
            await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"sidebar-{viewport}-before.png") });
        }
        for (var i = 0; i < 3; i++)
        {
            await page.Locator("#tl").ClickAsync(); await page.WaitForTimeoutAsync(220);
            await page.Locator("#tr").ClickAsync(); await page.WaitForTimeoutAsync(220);
            await page.Locator("#tl").ClickAsync(); await page.WaitForTimeoutAsync(220);
            await page.Locator("#tr").ClickAsync(); await page.WaitForTimeoutAsync(220);
        }
        await page.WaitForFunctionAsync("""
            () => {
              const root=document.querySelector('#thumbs').getBoundingClientRect();
              return [...document.querySelectorAll('.thumb img')].filter(img=>{
                const r=img.getBoundingClientRect(); return r.bottom>root.top&&r.top<root.bottom;
              }).every(img=>img.complete&&img.naturalWidth>0);
            }
            """);
        await page.WaitForFunctionAsync("document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
        await Frames(page);
        await page.EvaluateAsync("() => { window.sidebarSampling=false; }");
        var samples = await page.EvaluateAsync<JsonElement>("sidebarSamples");
        var rasterWork = await page.EvaluateAsync<JsonElement>("() => {" +
            "const active=`/api/page/${Number(document.getElementById('pn').value)-1}.png`;" +
            "const renders=profileWorkerStages.filter(s=>/\\/api\\/page\\/\\d+\\.png/.test(s.url)&&s.engine&&!s.engine.cacheHit);" +
            "return {active:renders.filter(s=>new URL(s.url,location.href).pathname===active)," +
            "thumbnails:renders.filter(s=>new URL(s.url,location.href).pathname!==active)}" +
            "}");
        var activeRenders = rasterWork.GetProperty("active");
        var renderCount = activeRenders.GetArrayLength();
        var thumbnailRenderCount = rasterWork.GetProperty("thumbnails").GetArrayLength();
        if (samples.GetArrayLength() == 0 || samples.EnumerateArray().Any(sample =>
              !sample.GetProperty("loaded").GetBoolean() || !sample.GetProperty("visible").GetBoolean() ||
              sample.GetProperty("width").GetDouble() <= 0 || sample.GetProperty("height").GetDouble() <= 0))
            throw new Exception($"{document}: {viewport} sidebar toggle produced a blank or hidden active page frame.");
        if (renderCount > 3) throw new Exception($"{document}: {viewport} sidebar toggles triggered {renderCount} active-page rerenders (budget 3): {activeRenders}");
        records.Add(new { repeat, document, phase = "sidebar-toggle-" + viewport, result = new { samples = samples.GetArrayLength(), activeRenders, thumbnailRenderCount, before, after = await page.Locator("#base").GetAttributeAsync("data-loaded-url") } });
        if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"sidebar-{viewport}-after.png") });
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }

    static Task Saved(IPage page) => page.WaitForFunctionAsync("() => profileCommands.some(c=>/\\/items$/.test(c.url)) && profileCommands.some(c=>/\\.png\\?dpi=40/.test(c.url))");
}
