using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

static class EditorRasterProfile
{
    public static async Task Run(string root, string output, string document)
    {
        Directory.CreateDirectory(output);
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
            await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block, ViewportSize = new() { Width = 1440, Height = 1000 } });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(180000);
            var main = await context.NewCDPSessionAsync(page);
            var traceReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Event("Tracing.tracingComplete").OnEvent += (_, data) =>
                traceReady.SetResult(data!.Value.GetProperty("stream").GetString()!);
            await main.SendAsync("Tracing.start", new Dictionary<string, object>
                { ["categories"] = "devtools.timeline,v8,disabled-by-default-v8.compile", ["transferMode"] = "ReturnAsStream" });
            await main.SendAsync("Profiler.enable");
            await main.SendAsync("Profiler.start");
            await page.GotoAsync(address + "/csharp-pdf/");
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            var startup = (await main.SendAsync("Profiler.stop"))!.Value;
            await File.WriteAllTextAsync(Path.Combine(output, "startup.cpuprofile"), startup.GetProperty("profile").GetRawText());
            await main.SendAsync("Tracing.end");
            var traceStream = await traceReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var trace = new StringBuilder();
            try
            {
                while (true)
                {
                    var chunk = (await main.SendAsync("IO.read", new Dictionary<string, object> { ["handle"] = traceStream }))!.Value;
                    var data = chunk.GetProperty("data").GetString()!;
                    trace.Append(chunk.TryGetProperty("base64Encoded", out var encoded) && encoded.GetBoolean()
                        ? Encoding.UTF8.GetString(Convert.FromBase64String(data)) : data);
                    if (chunk.GetProperty("eof").GetBoolean()) break;
                }
            }
            finally { await main.SendAsync("IO.close", new Dictionary<string, object> { ["handle"] = traceStream }); }
            await File.WriteAllTextAsync(Path.Combine(output, "startup.trace.json"), trace.ToString());
            await main.DetachAsync();
            await page.Locator("#open").SetInputFilesAsync(Path.GetFullPath(document));
            await page.WaitForFunctionAsync("document.getElementById('base')?.naturalWidth > 0 && document.getElementById('status').textContent.startsWith('Ready.')");
            await page.WaitForFunctionAsync("document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
            await page.WaitForTimeoutAsync(1800);
            await page.EvaluateAsync("async () => { await pdfHost.request('/api/state'); }");
            var cdp = await browser.NewBrowserCDPSessionAsync();
            var targets = (await cdp.SendAsync("Target.getTargets"))!.Value;
            var target = targets.GetProperty("targetInfos").EnumerateArray().Single(t =>
                t.GetProperty("type").GetString() == "worker" && t.GetProperty("url").GetString()!.EndsWith("/worker.js", StringComparison.Ordinal));
            var attached = (await cdp.SendAsync("Target.attachToTarget", new Dictionary<string, object>
                { ["targetId"] = target.GetProperty("targetId").GetString()!, ["flatten"] = false }))!.Value;
            var session = attached.GetProperty("sessionId").GetString()!;
            var pending = new ConcurrentDictionary<int, TaskCompletionSource<JsonElement>>();
            var nextId = 0;
            cdp.Event("Target.receivedMessageFromTarget").OnEvent += (_, e) =>
            {
                if (e is not { } envelope || envelope.GetProperty("sessionId").GetString() != session) return;
                using var message = JsonDocument.Parse(envelope.GetProperty("message").GetString()!);
                var response = message.RootElement;
                if (!response.TryGetProperty("id", out var id) || !pending.TryRemove(id.GetInt32(), out var completion)) return;
                if (response.TryGetProperty("error", out var error)) completion.SetException(new Exception(error.ToString()));
                else completion.SetResult(response.GetProperty("result").Clone());
            };
            async Task<JsonElement> Worker(string method)
            {
                var id = ++nextId;
                var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending[id] = completion;
                await cdp.SendAsync("Target.sendMessageToTarget", new Dictionary<string, object>
                    { ["sessionId"] = session, ["message"] = JsonSerializer.Serialize(new { id, method }) });
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            await Worker("Profiler.enable");
            var measurements = new List<JsonElement>();
            foreach (var dpi in new[] { 41, 137, 240 })
            {
                await Worker("Profiler.start");
                var measurement = await page.EvaluateAsync<JsonElement>("""
                    async dpi => {
                      window.profileWorkerStages = [];
                      const start = performance.now();
                      const image = await pdfHost.image(`/api/page/0.png?dpi=${dpi}`);
                      const bitmap = await createImageBitmap(await (await fetch(image)).blob());
                      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
                      const context = canvas.getContext('2d'); context.drawImage(bitmap, 0, 0); bitmap.close();
                      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
                      const hash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', pixels))].map(x => x.toString(16).padStart(2,'0')).join('');
                      return {dpi, ms:performance.now()-start, width:canvas.width, height:canvas.height, hash, stages:profileWorkerStages};
                    }
                    """, dpi);
                var profile = await Worker("Profiler.stop");
                await File.WriteAllTextAsync(Path.Combine(output, $"raster-{dpi}.cpuprofile"), profile.GetProperty("profile").GetRawText());
                measurements.Add(measurement);
                Console.WriteLine(measurement);
            }
            await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
            await cdp.DetachAsync();
        }
        finally { await app.StopAsync(); }
    }
}
