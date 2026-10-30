using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

static class EditorPageImage
{
    public static async Task Run(string source, string pdfPath, string output)
    {
        WebApplication? app = null;
        PhysicalFileProvider? files = null;
        IPage? page = null;
        var records = new List<object>();
        Directory.CreateDirectory(output);
        try
        {
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

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            await using var context = await browser.NewContextAsync(new()
            {
                AcceptDownloads = true,
                ViewportSize = new() { Width = 1440, Height = 1000 },
                DeviceScaleFactor = 1,
            });
            var unexpectedRequests = new List<string>();
            var site = new Uri(source);
            context.Request += (_, request) =>
            {
                var url = new Uri(request.Url);
                if (url.Scheme is not ("http" or "https")) return;
                if (request.Method != "GET" || url.GetLeftPart(UriPartial.Authority) != site.GetLeftPart(UriPartial.Authority) ||
                    (!url.AbsolutePath.StartsWith("/csharp-pdf/", StringComparison.Ordinal) && url.AbsolutePath != "/favicon.ico") ||
                    url.AbsolutePath.Contains("/api/", StringComparison.Ordinal))
                    unexpectedRequests.Add(request.Method + " " + request.Url);
            };
            page = await context.NewPageAsync();
            page.SetDefaultTimeout(30000);
            page.PageError += (_, error) => records.Add(new { type = "page-error", message = error });
            page.Console += (_, message) =>
            {
                if (message.Type == "error") records.Add(new { type = "console-error", message = message.Text });
            };
            page.RequestFailed += (_, request) => records.Add(new { type = "request-failed", url = request.Url, error = request.Failure });
            page.Response += (_, response) =>
            {
                if (response.Status >= 400) records.Add(new { type = "http-error", status = response.Status, url = response.Url });
            };
            await page.GotoAsync(source);
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            var bytes = await File.ReadAllBytesAsync(pdfPath);
            await page.Locator("#open").SetInputFilesAsync(new FilePayload
            {
                Name = Path.GetFileName(pdfPath),
                MimeType = "application/pdf",
                Buffer = bytes,
            });
            await page.WaitForFunctionAsync("name => document.getElementById('name').textContent === name && document.getElementById('base')?.naturalWidth > 0", Path.GetFileName(pdfPath));
            await AssertPage("open", waitForTarget: true);
            var pageCountText = await page.Locator("#pt").TextContentAsync() ?? string.Empty;
            var pageCountSeparator = pageCountText.LastIndexOf('/');
            if (pageCountSeparator < 0 ||
                !int.TryParse(pageCountText[(pageCountSeparator + 1)..].Trim(), out var pageCount))
                throw new Exception($"Could not determine sample page count from '{pageCountText}'.");
            if (Path.GetFileName(pdfPath) == "26-repacked-object-streams.pdf" && pageCount != 6)
                throw new Exception("Image regression fixture has an unexpected page count.");
            await page.Locator("#fw").ClickAsync();
            await AssertPage("fit-width-start", waitForTarget: true);
            var canInjectRetry = await page.EvaluateAsync<bool>("() => typeof window.pdfHost?.evictImage === 'function'");
            if (canInjectRetry)
            {
                await page.EvaluateAsync("""
                    () => {
                      const original=window.pdfHost.image.bind(window.pdfHost);
                      let failed=false,calls=0;
                      window.__originalPageImage=original;
                      window.__imageRetryState=()=>({failed,calls});
                      window.pdfHost.image=async url=>{
                        if(url.includes('/api/page/')) {
                          calls++;
                          if(!failed) { failed=true; return 'data:image/png;base64,AA=='; }
                        }
                        return original(url);
                      };
                    }
                    """);
                await page.Locator("#zi").ClickAsync();
                await AssertPage("transient-image-decode-retry", waitForTarget: true);
                var retryState = await page.EvaluateAsync<JsonElement>("window.__imageRetryState()");
                records.Add(new { type = "transient-image-retry", state = retryState });
                if (!retryState.GetProperty("failed").GetBoolean() || retryState.GetProperty("calls").GetInt32() < 2)
                    throw new Exception($"The forced image decode failure did not trigger a retry: {retryState}");

                await page.Locator("#zo").ClickAsync();
                await AssertPage("page-image-before-onerror", waitForTarget: true);
                await page.EvaluateAsync("() => document.getElementById('base').dispatchEvent(new Event('error'))");
                await AssertPage("image-onerror-reload", waitForTarget: true);
                retryState = await page.EvaluateAsync<JsonElement>("window.__imageRetryState()");
                records.Add(new { type = "image-onerror-retry", state = retryState });
                if (retryState.GetProperty("calls").GetInt32() < 4)
                    throw new Exception($"The image error handler did not re-request the page image: {retryState}");

                await page.EvaluateAsync("""
                    () => {
                      const original=window.__originalPageImage;
                      window.pdfHost.image=async url=>url.includes('/api/page/')
                        ? 'data:image/png;base64,AA=='
                        : original(url);
                    }
                    """);
                await page.Locator("#zi").ClickAsync();
                await page.WaitForTimeoutAsync(300);
                await AssertPage("failed-retry-keeps-last-good-image", waitForTarget: false);
                var failureStatus = await page.Locator("#status").TextContentAsync() ?? string.Empty;
                if (!failureStatus.Contains("Unable to render page", StringComparison.Ordinal))
                    throw new Exception($"Exhausted image retries were not shown to the user: {failureStatus}");
                records.Add(new { type = "exhausted-image-retry", message = failureStatus });

                await page.EvaluateAsync("() => { window.pdfHost.image=window.__originalPageImage; }");
                await page.Locator("#zo").ClickAsync();
                await AssertPage("image-recovers-after-render-error", waitForTarget: true);
            }
            else
            {
                records.Add(new { type = "transient-image-retry", skipped = "The loaded site does not expose cache eviction." });
            }
            await page.EvaluateAsync("""
                () => {
                  window.pageImageSampling=true;
                  window.pageImageFrameSamples=[];
                  const sample=()=>{
                    const img=document.getElementById('base');
                    const rect=img?.getBoundingClientRect(), style=img&&getComputedStyle(img);
                    pageImageFrameSamples.push({loaded:!!img?.complete&&img.naturalWidth>0,
                      visible:!!img&&style.display!=='none'&&style.visibility!=='hidden'&&Number(style.opacity)>0,
                      width:rect?.width??0,height:rect?.height??0});
                    if(window.pageImageSampling)requestAnimationFrame(sample);
                  };
                  requestAnimationFrame(sample);
                }
                """);

            var actions = new[]
            {
                ("zoom-in", "#zi"),
                ("zoom-out", "#zo"),
                ("fit-width", "#fw"),
                ("collapse-left", "#tl"),
                ("collapse-right", "#tr"),
                ("expand-left", "#tl"),
                ("expand-right", "#tr"),
                ("fit-page", "#fp"),
                ("zoom-in-again", "#zi"),
                ("zoom-out-again", "#zo"),
            };
            for (var round = 0; round < 4; round++)
            {
                foreach (var (name, selector) in actions)
                {
                    await page.Locator(selector).ClickAsync();
                    await Frames(page);
                    await AssertPage($"{round}-{name}", waitForTarget: false);
                }
            }

            for (var i = 1; i < Math.Min(pageCount, 6); i++)
            {
                await page.Locator("#np").ClickAsync();
                await page.WaitForFunctionAsync($"document.getElementById('pn').value === '{i + 1}'");
                await AssertPage($"navigate-{i + 1}", waitForTarget: true);
                await page.Locator("#zi").ClickAsync();
                await Frames(page);
                await AssertPage($"page-{i + 1}-zoom", waitForTarget: false);
                await page.Locator("#zo").ClickAsync();
                await Frames(page);
                await AssertPage($"page-{i + 1}-zoom-back", waitForTarget: true);
            }

            await page.EvaluateAsync("() => { window.pageImageSampling=false; }");
            var frames = await page.EvaluateAsync<JsonElement>("pageImageFrameSamples");
            records.Add(new { type = "continuous-frame-samples", count = frames.GetArrayLength(), frames });
            if (frames.GetArrayLength() == 0 || frames.EnumerateArray().Any(frame =>
                    !frame.GetProperty("loaded").GetBoolean() || !frame.GetProperty("visible").GetBoolean() ||
                    frame.GetProperty("width").GetDouble() <= 0 || frame.GetProperty("height").GetDouble() <= 0))
            {
                var failure = Path.Combine(output, "page-image-failure-frames.png");
                await page.ScreenshotAsync(new() { Path = failure, FullPage = true });
                await File.WriteAllTextAsync(Path.Combine(output, "page-image-failure.json"),
                    JsonSerializer.Serialize(new { source, pdfPath, frameSamples = frames, records }, new JsonSerializerOptions { WriteIndented = true }));
                throw new Exception($"Active page image disappeared in one of {frames.GetArrayLength()} sampled frames.");
            }
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "page-image-final.png") });
            records.Add(new { type = "network-check", unexpectedRequests });
            if (unexpectedRequests.Count > 0)
                throw new Exception("Unexpected external/upload/API traffic: " + string.Join(", ", unexpectedRequests));
            await File.WriteAllTextAsync(Path.Combine(output, "page-image-trace.json"),
                JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: active page image remained decoded and nonblank through zoom, sidebar and page-navigation stress on {source}.");

            async Task AssertPage(string phase, bool waitForTarget)
            {
                var targetTracked = await page.EvaluateAsync<bool>("() => !!document.getElementById('base')?.dataset.targetUrl");
                if (waitForTarget && targetTracked)
                {
                    try
                    {
                        await page.WaitForFunctionAsync("() => { const el=document.getElementById('base'); return el?.naturalWidth>0 && el.dataset.loadedUrl===el.dataset.targetUrl; }");
                    }
                    catch (TimeoutException error)
                    {
                        var failedState = await page.EvaluateAsync<JsonElement>("() => { const el=document.getElementById('base'); if(!el)return null; const redact=s=>s?.startsWith('data:')?`${s.slice(0,s.indexOf(','))},<redacted ${s.length} chars>`:s; return { src:redact(el.src),currentSrc:redact(el.currentSrc),complete:el.complete,naturalWidth:el.naturalWidth,naturalHeight:el.naturalHeight,loadedUrl:el.dataset.loadedUrl,targetUrl:el.dataset.targetUrl }; }");
                        records.Add(new { type = "target-image-timeout", phase, state = failedState, message = error.Message });
                        await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"page-image-failure-{phase}.png"), FullPage = true });
                        await File.WriteAllTextAsync(Path.Combine(output, "page-image-failure.json"),
                            JsonSerializer.Serialize(new { source, pdfPath, phase, state = failedState, records }, new JsonSerializerOptions { WriteIndented = true }));
                        throw new Exception($"Replacement page image did not load at {phase}: {failedState}", error);
                    }
                }
                else if (waitForTarget)
                {
                    records.Add(new { type = "target-tracking-unavailable", phase });
                    await Frames(page);
                }
                var state = await page.EvaluateAsync<JsonElement>("() => {" +
                    "const img=document.getElementById('base');" +
                    "if(!img)return {exists:false,naturalWidth:0,naturalHeight:0,loadedUrl:null,targetUrl:null,nonWhite:0};" +
                    "const c=document.createElement('canvas'),s=32;c.width=s;c.height=s;" +
                    "const x=c.getContext('2d');if(img.naturalWidth)x.drawImage(img,0,0,s,s);" +
                    "const p=img.naturalWidth?x.getImageData(0,0,s,s).data:[];let nonWhite=0;" +
                    "for(let i=0;i<p.length;i+=4)if(p[i]<245||p[i+1]<245||p[i+2]<245)nonWhite++;" +
                    "const r=img.getBoundingClientRect(),style=getComputedStyle(img);" +
                    "return {exists:true,complete:img.complete,naturalWidth:img.naturalWidth,naturalHeight:img.naturalHeight," +
                    "loadedUrl:img.dataset.loadedUrl,targetUrl:img.dataset.targetUrl,nonWhite,rect:{width:r.width,height:r.height}," +
                    "visible:style.display!=='none'&&style.visibility!=='hidden'&&Number(style.opacity)>0};}");
                records.Add(new { type = "frame-check", phase, state });
                var ok = state.GetProperty("exists").GetBoolean() &&
                    state.GetProperty("complete").GetBoolean() &&
                    state.GetProperty("naturalWidth").GetInt32() > 0 &&
                    state.GetProperty("naturalHeight").GetInt32() > 0 &&
                    state.GetProperty("nonWhite").GetInt32() > 0 &&
                    state.GetProperty("visible").GetBoolean() &&
                    state.GetProperty("rect").GetProperty("width").GetDouble() > 0 &&
                    state.GetProperty("rect").GetProperty("height").GetDouble() > 0;
                if (ok) return;
                var path = Path.Combine(output, $"page-image-failure-{records.Count:D3}-{phase}.png");
                await page.ScreenshotAsync(new() { Path = path, FullPage = true });
                await File.WriteAllTextAsync(Path.Combine(output, "page-image-failure.json"),
                    JsonSerializer.Serialize(new { source, pdfPath, phase, state, records }, new JsonSerializerOptions { WriteIndented = true }));
                throw new Exception($"Active page image failed at {phase}: {state}");
            }
        }
        catch (Exception error)
        {
            records.Add(new { type = "failure", message = error.ToString() });
            if (page is not null && !File.Exists(Path.Combine(output, "page-image-failure.json")))
            {
                try { await page.ScreenshotAsync(new() { Path = Path.Combine(output, "page-image-failure-final.png"), FullPage = true }); }
                catch (Exception screenshotError) { records.Add(new { type = "screenshot-failure", message = screenshotError.Message }); }
            }
            await File.WriteAllTextAsync(Path.Combine(output, "page-image-trace.json"),
                JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            files?.Dispose();
        }
    }

    static Task Frames(IPage page) => page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
}
