using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

static class EditorBoot
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Boot: " + message); }

    public static async Task Run(string root, string output, bool baseline)
    {
        Directory.CreateDirectory(output);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        var remote = Uri.TryCreate(root, UriKind.Absolute, out var remoteUri) && remoteUri.Scheme is "http" or "https";
        using var files = new PhysicalFileProvider(remote ? AppContext.BaseDirectory : Path.GetFullPath(root));
        app.UsePathBase("/csharp-pdf");
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".dat"] = "application/octet-stream";
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
        await app.StartAsync();
        var url = remote ? root : app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/csharp-pdf/";
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
        var results = new List<object>();
        var coldStarts = new List<double>();
        foreach (var scenario in baseline ? new[] { "cold", "cold", "cold" } : ["cold", "cold", "cold", "transient", "permanent", "missing", "integrity", "module", "stalled-worker"])
        {
            await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block, ViewportSize = new() { Width = 1440, Height = 1000 }, DeviceScaleFactor = 1 });
            var unexpected = new ConcurrentQueue<string>();
            context.Request += (_, request) =>
            {
                if (!request.Url.StartsWith("http:", StringComparison.Ordinal) && !request.Url.StartsWith("https:", StringComparison.Ordinal)) return;
                if (!request.Url.StartsWith(url, StringComparison.Ordinal) || request.Method != "GET" || new Uri(request.Url).AbsolutePath.Contains("/api/"))
                    unexpected.Enqueue(request.Method + " " + request.Url);
            };
            var attempts = new ConcurrentDictionary<string, int>();
            var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (scenario == "stalled-worker")
                await context.RouteAsync("**/worker.js", async route =>
                {
                    await route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = "// Deliberately never sends ready." });
                    workerStarted.SetResult();
                });
            else if (scenario != "cold")
                await context.RouteAsync("**/_framework/**", async route =>
                {
                    var name = new Uri(route.Request.Url).AbsolutePath.Split('/')[^1];
                    var selected = name.EndsWith(".wasm", StringComparison.Ordinal) && !name.StartsWith("dotnet.native.", StringComparison.Ordinal);
                    if (scenario == "module") selected = name == "dotnet.js";
                    if (!selected || (scenario != "transient" && !attempts.ContainsKey(name) && attempts.Count >= 1)
                        || (scenario == "transient" && !attempts.ContainsKey(name) && attempts.Count >= 3))
                    { await route.ContinueAsync(); return; }
                    var count = attempts.AddOrUpdate(name, 1, (_, value) => value + 1);
                    if (scenario == "transient" && count > 2 || scenario == "module" && count > 2)
                    { await route.ContinueAsync(); return; }
                    if (scenario == "transient" && attempts.Keys.Order().First() == name && count == 2)
                    { await route.AbortAsync("connectionreset"); return; }
                    await route.FulfillAsync(new()
                    {
                        Status = scenario == "missing" ? 404 : scenario == "integrity" ? 200 : 503,
                        ContentType = scenario == "integrity" ? "application/wasm" : "text/plain",
                        Body = scenario == "integrity" ? "corrupted WASM" : "Injected boot failure",
                    });
                });
            var page = await context.NewPageAsync();
            if (scenario == "stalled-worker") await page.Clock.InstallAsync();
            page.SetDefaultTimeout(90000);
            var errors = new ConcurrentQueue<string>();
            page.PageError += (_, error) => errors.Enqueue(error);
            var cdp = await context.NewCDPSessionAsync(page);
            await cdp.SendAsync("Performance.enable");
            await cdp.SendAsync("Network.enable");
            await cdp.SendAsync("Network.setCacheDisabled", new Dictionary<string, object> { ["cacheDisabled"] = true });
            await page.GotoAsync(url);
            if (scenario == "stalled-worker")
            {
                await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await page.Clock.FastForwardAsync(91000);
            }
            if (scenario is "permanent" or "missing" or "integrity" or "stalled-worker")
            {
                var alert = page.GetByRole(AriaRole.Alert);
                await alert.WaitForAsync(new() { Timeout = 15000 });
                Check((await alert.TextContentAsync())!.Contains("could not start", StringComparison.OrdinalIgnoreCase), "Visible failure explains startup stopped.");
                var reload = page.GetByRole(AriaRole.Button, new() { Name = "Reload editor", Exact = true });
                Check(await reload.IsEnabledAsync(), "Reload remains usable outside the inert editor.");
                Check(await page.EvaluateAsync<bool>("!window.pdfHost && !document.getElementById('status').textContent.startsWith('Loading')"), "No false ready state or infinite loading status.");
                if (scenario == "stalled-worker")
                    Check((await alert.TextContentAsync())!.Contains("90 seconds", StringComparison.Ordinal), "A silent worker reaches the startup deadline.");
                else
                    Check(attempts.Values.Single() == 4, $"Retry count is bounded ({scenario}: {JsonSerializer.Serialize(attempts)}; {await alert.TextContentAsync()}).");
                await page.WaitForTimeoutAsync(2200);
                Check(scenario == "stalled-worker" || attempts.Values.Single() == 4, "No requests continue after terminal failure.");
                if (scenario == "permanent")
                {
                    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "boot-error-desktop.png") });
                    await page.SetViewportSizeAsync(390, 844);
                    Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"), "Mobile failure fits without horizontal scrolling.");
                    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "boot-error-mobile.png") });
                }
                await reload.FocusAsync();
                await context.UnrouteAllAsync();
                if (scenario == "stalled-worker") await page.Clock.ResumeAsync();
                await reload.PressAsync("Enter");
                await page.WaitForFunctionAsync("document.getElementById('status')?.textContent.startsWith('Ready.')");
            }
            else
            {
                await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
                await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
                if (scenario == "cold")
                {
                    coldStarts.Add(await page.EvaluateAsync<double>("performance.now()"));
                    Check(errors.IsEmpty, "Uninterrupted boot has no browser errors.");
                }
                else Check(attempts.Count == (scenario == "transient" ? 3 : 1) && attempts.Values.All(n => n == 3), "Every injected transient failure recovers on attempt three.");
                Check(await page.GetByRole(AriaRole.Alert).CountAsync() == 0, "Recovered boot leaves no terminal error.");
            }
            var retryCount = await page.EvaluateAsync<int>("pdfHost.bootRetries");
            if (scenario is "transient" or "module")
                Check(retryCount == (scenario == "transient" ? 6 : 2), "Recovered loader retries remain observable.");
            Check(unexpected.IsEmpty, "Static GETs only, including worker resources: " + string.Join(", ", unexpected));
            results.Add(new { scenario, attempts, retryCount, errors, unexpected });
        }
        var medianMs = coldStarts.Order().ElementAt(1);
        await File.WriteAllTextAsync(Path.Combine(output, baseline ? "boot-baseline.json" : "boot-resilience.json"),
            JsonSerializer.Serialize(new { coldStarts, medianMs, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {(baseline ? "baseline cold boot" : "cold boot, transient 503/network/module recovery, permanent/404/integrity errors, silent-worker deadline and keyboard reload")}; cold-start median {medianMs:F1} ms; no outbound requests.");
    }
}
