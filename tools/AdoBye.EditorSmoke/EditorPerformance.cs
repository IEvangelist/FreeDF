using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

static class EditorPerformance
{
    const string Observe = """
        window.profileCommands = [];
        window.profileLongTasks = [];
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
                  try { return original(url); }
                  finally { profileCommands.push({url, start, ms:performance.now()-start}); }
                };
          }
        }
        """;

    public static async Task Run(string source, string output, string[] documents)
    {
        WebApplication? app = null;
        PhysicalFileProvider? files = null;
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
        var records = new List<object>();
        try
        {
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
                    await Measure("open", async () =>
                    {
                        await page.Locator("#open").SetInputFilesAsync(Path.GetFullPath(document));
                        await page.WaitForFunctionAsync("document.getElementById('name').textContent.includes(" + JsonSerializer.Serialize(name) + ") && document.getElementById('status').textContent.startsWith('Ready.') && document.getElementById('base')?.naturalWidth > 0");
                    });
                    var baseImage = await page.Locator("#base").GetAttributeAsync("src");
                    records.Add(new { repeat, document = name, phase = "first-page-image", hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(baseImage!.Split(',')[1]))) });
                    await Measure("zoom", async () =>
                    {
                        await page.Locator("#zi").ClickAsync();
                        await page.WaitForFunctionAsync("document.getElementById('base').complete && document.getElementById('base').naturalWidth > 0");
                    });
                    await page.Locator("#fp").ClickAsync();
                    await Frames(page);
                    if (await page.Locator(".thumb").CountAsync() > 1)
                    {
                        await Measure("next-page", async () =>
                        {
                            await page.Locator("#np").ClickAsync();
                            await page.WaitForFunctionAsync("document.getElementById('pn').value === '2' && document.getElementById('base').naturalWidth > 0");
                        });
                        await page.Locator("#pp").ClickAsync();
                        await page.WaitForFunctionAsync("document.getElementById('pn').value === '1' && document.getElementById('base').naturalWidth > 0");
                    }
                    await Measure("add-overlay", async () =>
                    {
                        await page.Locator("[data-tool=rect]").ClickAsync();
                        var box = (await page.Locator("#layer").BoundingBoxAsync())!;
                        await page.Mouse.MoveAsync(box.X + 80, box.Y + 80);
                        await page.Mouse.DownAsync();
                        await page.Mouse.MoveAsync(box.X + 150, box.Y + 130, new() { Steps = 5 });
                        await page.Mouse.UpAsync();
                        await Saved(page);
                    });
                    await Measure("selection-drag", async () =>
                    {
                        var box = (await page.Locator("#layer .it").Last.BoundingBoxAsync())!;
                        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                        await page.Mouse.DownAsync();
                        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 20, box.Y + box.Height / 2 + 20, new() { Steps = 12 });
                        await page.Mouse.UpAsync();
                        await Saved(page);
                    });
                    await Measure("undo", async () => { await page.Keyboard.PressAsync("Control+z"); await Saved(page); });
                    var span = page.Locator("#layer .span").First;
                    if (await span.CountAsync() > 0)
                    {
                        await Measure("edit-text", async () =>
                        {
                            await span.ClickAsync(new() { Force = true });
                            var text = page.Locator("#props textarea[data-k=text]");
                            await text.FillAsync("Measured editor change");
                            await text.PressAsync("Tab");
                            await Saved(page);
                        });
                    }
                    await Measure("save-download", async () =>
                    {
                        await page.Locator("#dl").ClickAsync();
                        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
                        if (await download.FailureAsync() is { } failure) throw new Exception(failure);
                    });
                    async Task Measure(string phase, Func<Task> action)
                    {
                        await page.EvaluateAsync("() => { profileCommands=[]; profileLongTasks=[]; window.profileStart=performance.now(); }");
                        var before = await cdp.SendAsync("Performance.getMetrics");
                        await action();
                        await Frames(page);
                        var result = await page.EvaluateAsync<JsonElement>("({ms:performance.now()-profileStart, commands:profileCommands, longTasks:profileLongTasks})");
                        var after = await cdp.SendAsync("Performance.getMetrics");
                        records.Add(new { repeat, document = name, phase, result, before, after });
                        Console.WriteLine($"{repeat} {name} {phase}: {result.GetProperty("ms").GetDouble():F0} ms, {result.GetProperty("commands").GetArrayLength()} commands");
                        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
            }
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            files?.Dispose();
        }
    }

    static Task Frames(IPage page) => page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

    static Task Saved(IPage page) => page.WaitForFunctionAsync("() => profileCommands.some(c=>/\\/items$/.test(c.url)) && profileCommands.some(c=>/\\.png\\?dpi=40/.test(c.url))");
}
