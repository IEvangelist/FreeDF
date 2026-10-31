using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

if (args is ["--raster-profile", var rasterSite, var rasterOutput, var rasterPdf])
{
    await EditorRasterProfile.Run(rasterSite, rasterOutput, rasterPdf);
    return;
}

if (args is ["--profile-ci", var ciProfile, var ciPolicy])
{
    await EditorPerformanceCi.Run(ciProfile, ciPolicy);
    return;
}

if (args is ["--profile-summary", var profile])
{
    var misses = await EditorPerformanceReport.Write(profile);
    if (misses.Count > 0 && Environment.GetEnvironmentVariable("EDITOR_PROFILE_REPORT_ONLY") != "1")
        throw new Exception(string.Join(Environment.NewLine, misses));
    return;
}

if (args.Length == 3 && args[0] is "--boot" or "--boot-baseline")
{
    await EditorBoot.Run(args[1], args[2], args[0] == "--boot-baseline");
    return;
}

if (args.Length == 2 && args[0] == "--culture")
{
    await EditorCulture.Run(args[1]);
    return;
}

if (args.Length == 3 && args[0] == "--service-worker")
{
    await EditorServiceWorker.Run(args[1], args[2]);
    return;
}

if (args.Length >= 4 && args[0] == "--profile")
{
    await EditorPerformance.Run(args[1], args[2], args[3..]);
    return;
}

if (args.Length == 4 && args[0] == "--page-image")
{
    await EditorPageImage.Run(args[1], args[2], args[3]);
    return;
}

var textSelectionOnly = args.Length == 2 && args[0] == "--text-selection";
var recoveryOnly = args.Length == 2 && args[0] == "--recovery";
var mergeOnly = args.Length == 2 && args[0] == "--merge";
var jsonSafetyOnly = args.Length == 2 && args[0] == "--json-safety";
var saveOverlapOnly = args.Length == 2 && args[0] == "--save-overlap";
if (args.Length != 1 && !textSelectionOnly && !recoveryOnly && !mergeOnly && !jsonSafetyOnly && !saveOverlapOnly) throw new ArgumentException("Usage: AdoBye.EditorSmoke [--text-selection|--recovery|--merge|--json-safety|--save-overlap] <published-wwwroot>");
var root = Path.GetFullPath(args[^1]);
if (!File.Exists(Path.Combine(root, "index.html"))) throw new FileNotFoundException("Publish the Pages site first.");
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
await using var app = builder.Build();
app.UsePathBase("/csharp-pdf");
app.Use(async (context, next) =>
{
    if (context.Request.PathBase != "/csharp-pdf") { context.Response.StatusCode = 404; return; }
    await next();
});
var files = new PhysicalFileProvider(root);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".dat"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = contentTypes });
await app.StartAsync();
try
{
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var pw = await Playwright.CreateAsync();
    await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
    await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block, AcceptDownloads = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
    var page = await context.NewPageAsync();
    page.SetDefaultTimeout(60000);
    var errors = new ConcurrentQueue<string>();
    var network = new ConcurrentQueue<string>();
    var bootConsole = new ConcurrentQueue<string>();
    var bootFailures = new ConcurrentDictionary<string, string>();
    var bootSuccesses = new ConcurrentDictionary<string, bool>();
    var booting = true;
    string ResourceKey(string url) => new Uri(url).GetLeftPart(UriPartial.Path);
    bool Framework(string url) => url.StartsWith(address + "/csharp-pdf/_framework/", StringComparison.Ordinal);
    page.PageError += (_, error) => errors.Enqueue(error);
    page.Console += (_, message) =>
    {
        if (message.Type != "error") return;
        if (booting && message.Text.StartsWith("Failed to load resource:", StringComparison.Ordinal)) bootConsole.Enqueue(message.Text);
        else errors.Enqueue(message.Text);
        Console.Error.WriteLine(message.Text);
    };
    page.Request += (_, request) =>
    {
        if (!request.Url.StartsWith(address + "/csharp-pdf/", StringComparison.Ordinal) || request.Method != "GET" || new Uri(request.Url).AbsolutePath.Contains("/api/"))
            network.Enqueue(request.Method + " " + request.Url);
    };
    context.Response += (_, response) =>
    {
        if (booting && Framework(response.Url))
        {
            if (response.Status >= 500)
            {
                var key = ResourceKey(response.Url);
                bootSuccesses[key] = false;
                bootFailures[key] = $"HTTP {response.Status}";
                return;
            }
            if (response.Ok) bootSuccesses[ResourceKey(response.Url)] = true;
        }
        if (response.Status >= 400) errors.Enqueue($"{response.Status} {response.Url}");
    };
    context.RequestFailed += (_, request) =>
    {
        if (booting && Framework(request.Url))
        {
            var key = ResourceKey(request.Url);
            bootSuccesses[key] = false;
            bootFailures[key] = request.Failure ?? "Network failure";
        }
        else errors.Enqueue($"Request failed: {request.Url}: {request.Failure}");
    };
    var injectBootRetry = Environment.GetEnvironmentVariable("EDITOR_SMOKE_TRANSIENT_BOOT") == "1";
    var injectedBootFailure = false;
    if (injectBootRetry)
        await context.RouteAsync("**/_framework/*.wasm", async route =>
        {
            if (injectedBootFailure) { await route.ContinueAsync(); return; }
            injectedBootFailure = true;
            await route.FulfillAsync(new() { Status = 503, ContentType = "text/plain", Body = "Injected transient delivery failure" });
        });
    await page.GotoAsync(address + "/csharp-pdf/");
    try { await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')"); }
    catch (TimeoutException)
    {
        throw new Exception("Startup: " + await page.Locator("#status").TextContentAsync() + "\n" + string.Join("\n", errors.Concat(bootConsole)) + "\n" + string.Join("\n", network));
    }
    booting = false;
    var bootRetries = await page.EvaluateAsync<int>("pdfHost.bootRetries");
    var recovered = bootRetries > 0 && !bootFailures.IsEmpty && bootFailures.Keys.All(key => bootSuccesses.GetValueOrDefault(key));
    Check(bootFailures.IsEmpty || recovered, "Every transient framework delivery failure recovered.");
    Check(!injectBootRetry || injectedBootFailure && recovered, "Injected transient delivery failure is recorded and recovered.");
    foreach (var error in bootConsole) if (!recovered) errors.Enqueue(error);
    Console.WriteLine($"PDF boot retries: {bootRetries}; recovered resource failures: {bootFailures.Count}.");
    Check(await page.EvaluateAsync<string>("document.baseURI") == address + "/csharp-pdf/", "Repository base path");
    Check(await page.EvaluateAsync<bool>("!!window.pdfHost"), "WASM transport");
    Check(await page.EvaluateAsync<bool>("pdfHost.execution === 'worker' && !crossOriginIsolated && !window.Blazor"), "Single worker PDF runtime without shared-memory isolation or a main-thread Blazor runtime");
    if (saveOverlapOnly)
    {
        await EditorSaveOverlap.Run(browser, address + "/csharp-pdf/");
        return;
    }
    if (jsonSafetyOnly)
    {
        await EditorJsonSafety.Run(context, address + "/csharp-pdf/");
        Check(network.IsEmpty && errors.IsEmpty, "No unexpected JSON-safety browser errors or traffic.");
        return;
    }
    if (textSelectionOnly)
    {
        await EditorTextSelection.Run(page);
        Check(network.IsEmpty && errors.IsEmpty, "No outbound PDF traffic or browser errors.");
        return;
    }
    if (mergeOnly)
    {
        await EditorMerge.Run(page);
        Check(network.IsEmpty && EditorMerge.ExpectedErrors(errors, ExpectedRecoveryError), "No outbound PDF traffic or unexpected browser errors: " + System.Text.Json.JsonSerializer.Serialize(errors) + "\nNetwork: " + string.Join("\n", network));
        Console.WriteLine("PASS: keyboard merge controls/focus/Escape, inline server rejection with retained choices, direct later-frame rejection preserving state/history/assets and undo, multi-file order/range/insertion, render/save/reopen.");
        return;
    }
    if (recoveryOnly)
    {
        await EditorRecovery.Run(page);
        Check(network.IsEmpty && errors.All(ExpectedRecoveryError), "No outbound PDF traffic or unexpected browser errors: " + string.Join("\n", errors) + "\nNetwork: " + string.Join("\n", network));
        return;
    }
    await EditorHeader.Empty(page);
    await page.Locator("#fileBtn").ClickAsync();
    await page.GetByRole(AriaRole.Menuitem, new() { Name = "New…", Exact = false }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
    await page.Locator("#base").WaitForAsync();
    await page.WaitForFunctionAsync("document.getElementById('base').naturalWidth > 0");
    await page.Locator("[data-tool=text]").ClickAsync();
    var box = (await page.Locator("#layer").BoundingBoxAsync())!;
    await page.Mouse.MoveAsync(box.X + 60, box.Y + 60);
    await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(box.X + 240, box.Y + 100); await page.Mouse.UpAsync();
    var text = page.Locator("#layer [contenteditable='plaintext-only']");
    await text.WaitForAsync();
    await text.FillAsync("Browser local round trip");
    await text.PressAsync("Escape");
    await EditorHeader.State(page, true);
    await page.Locator("#prev").ClickAsync();
    await page.WaitForFunctionAsync("document.getElementById('prev').classList.contains('on') && document.getElementById('base').dataset.url.includes('bare=false') && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.url && document.getElementById('base').naturalWidth > 0");
    Check(await page.EvaluateAsync<bool>("document.getElementById('base').src.startsWith('data:image/png;base64,')"), "Local rendered preview");
    Check(await page.EvaluateAsync<bool>("""
        () => {
          const img = document.getElementById('base'), canvas = document.createElement('canvas');
          canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
          const ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0);
          const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
          let ink = 0;
          for (let i = 0; i < pixels.length; i += 4) if (pixels[i] < 200 && pixels[i+1] < 200 && pixels[i+2] < 200) ink++;
          return ink > 100;
        }
        """), "Text preview actually paints glyphs, not just a blank PNG");
    await page.Locator("#dl").ClickAsync();
    await page.Locator("#sn").FillAsync("local-round-trip.pdf");
    var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
    var downloaded = await download.PathAsync() ?? throw new Exception("No downloaded PDF.");
    await EditorHeader.State(page, false);
    Check(download.SuggestedFilename == "local-round-trip.pdf", "Download filename");
    var pdf = PdfDocument.Open(downloaded);
    var extracted = string.Join("", Pdf.ExtractText(pdf.Pages[0]).Select(s => s.Text));
    Check(extracted.Contains("Browser local round trip"), "Edit/save round trip: " + extracted);
    // Exercise the file picker with the real downloaded bytes, then change an existing page.
    await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "reopened.pdf", MimeType = "application/pdf", Buffer = await File.ReadAllBytesAsync(downloaded) });
    await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'reopened.pdf'");
    await page.Locator("#rr").ClickAsync();
    await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
    await page.Locator("#fb").ClickAsync(); await page.Locator("#fq").FillAsync("round trip");
    await page.WaitForFunctionAsync("document.getElementById('fc').textContent.includes('1')");
    // PNG, ZIP, text, compression and image insertion use the same local dispatcher.
    await page.EvaluateAsync("""
        async () => {
          for (const url of ['/api/export/png?page=0&dpi=96', '/api/export/zip?dpi=40', '/api/export/text', '/api/export/secure']) {
            const r = await window.pdfHost.request(url);
            if (!(await r.blob()).size) throw new Error('Empty export: ' + url);
          }
          const fd = new FormData();
          const png = await (await window.pdfHost.request('/api/export/png?page=0&dpi=40')).blob();
          fd.append('f', new File([png], 'page.png', {type: 'image/png'}));
          const image = await (await window.pdfHost.request('/api/asset', {method:'POST', body:fd})).json();
          if (!image.w || !image.h) throw new Error('Image did not decode');
        }
        """);
    // Deletion saves an empty live list, but undo must still be able to fetch the image bytes.
    if (await page.Locator("#prev").EvaluateAsync<bool>("el => el.classList.contains('on')"))
        await page.Locator("#prev").ClickAsync();
    await page.Locator("#img").SetInputFilesAsync(new FilePayload
    {
        Name = "undo.png", MimeType = "image/png",
        Buffer = PortableDocument.Render.PngWriter.Encode(1, 1, [255, 0, 0, 255]),
    });
    await page.WaitForFunctionAsync("document.querySelector('#layer .it img')?.naturalWidth > 0");
    await page.WaitForFunctionAsync("async () => (await (await window.pdfHost.request('/api/page/0/items')).json()).some(item => item.type === 'image')");
    await page.Locator("#layer .it").Filter(new() { Has = page.Locator("img") }).ClickAsync();
    await page.Keyboard.PressAsync("Delete");
    await page.WaitForFunctionAsync("async () => !(await (await window.pdfHost.request('/api/page/0/items')).json()).some(item => item.type === 'image')");
    await page.Keyboard.PressAsync("Control+z");
    await page.WaitForFunctionAsync("document.querySelector('#layer .it img')?.naturalWidth > 0");
    await page.WaitForFunctionAsync("async () => (await (await window.pdfHost.request('/api/page/0/items')).json()).some(item => item.type === 'image')");
    await page.Locator("#layer .it").Filter(new() { Has = page.Locator("img") }).ClickAsync();
    await page.Keyboard.PressAsync("Control+c");
    await page.Keyboard.PressAsync("Delete");
    await page.WaitForFunctionAsync("async () => !(await (await window.pdfHost.request('/api/page/0/items')).json()).some(item => item.type === 'image')");
    await page.Locator("#fileBtn").ClickAsync();
    await page.GetByRole(AriaRole.Menuitem, new() { Name = "Insert", Exact = false }).HoverAsync();
    await page.GetByRole(AriaRole.Menuitem, new() { Name = "Blank page after current", Exact = true }).ClickAsync();
    await page.WaitForFunctionAsync("document.getElementById('pn').value === '2'");
    await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 10, Y = 10 } });
    await page.Keyboard.PressAsync("Control+v");
    await page.WaitForFunctionAsync("document.querySelector('#layer .it img')?.naturalWidth > 0");
    await page.WaitForFunctionAsync("async () => (await (await window.pdfHost.request('/api/page/1/items')).json()).some(item => item.type === 'image')");
    await page.Locator("#dl").ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).WaitForAsync();
    var pendingSave = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
    if (await pendingSave.FailureAsync() is { } pendingFailure) throw new Exception(pendingFailure);
    await page.WaitForFunctionAsync("!document.querySelector('.mdl')");
    await page.WaitForFunctionAsync("[...document.querySelectorAll('.thumb img')].every(img => img.naturalWidth > 0 && img.dataset.loadedUrl === img.dataset.url)");
    await page.WaitForFunctionAsync("document.querySelector('#base')?.dataset.loadedUrl === document.querySelector('#base')?.dataset.targetUrl");
    // Separately check a subsequent unchanged Save: it must not start new renders.
    await page.Locator("#dl").ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).WaitForAsync();
    await page.EvaluateAsync("""
        () => {
          window.saveRenders = [];
          const image = window.pdfHost.image.bind(window.pdfHost);
          window.pdfHost.image = url => {
            if (/\/api\/page\/.*\.png/.test(url)) window.saveRenders.push({url, stack:new Error().stack});
            return image(url);
          };
        }
        """);
    var savedAgain = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
    if (await savedAgain.FailureAsync() is { } downloadFailure) throw new Exception(downloadFailure);
    await page.WaitForFunctionAsync("!document.querySelector('.mdl')");
    await EditorHeader.State(page, false);
    Check(await page.EvaluateAsync<int>("window.saveRenders.length") == 0, "Saving does not rerender unchanged pages or thumbnails: " + await page.EvaluateAsync<string>("JSON.stringify(window.saveRenders)"));
    Check(errors.IsEmpty, "No browser errors during editing: " + string.Join("\n", errors));
    await EditorHeader.Open(page, false, []);
    var artifacts = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
    if (artifacts is not null)
    {
        Directory.CreateDirectory(artifacts);
        await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "editor-desktop.png") });
    }
    // Invalid local input reports an error without replacing the open document.
    var beforeInvalid = await page.Locator("#name").TextContentAsync();
    await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "invalid.pdf", MimeType = "application/pdf", Buffer = [1, 2, 3] });
    await page.WaitForFunctionAsync("!document.getElementById('status').textContent.startsWith('Processing')");
    Check(await page.Locator("#name").TextContentAsync() == beforeInvalid, "Failed open preserves edits");
    Check((await page.Locator("#status").TextContentAsync())!.Contains("PDF"), "Visible invalid PDF error");
    // The intentionally invalid PDF is logged by both the C# boundary and UI error reporter.
    errors.Clear();
    await EditorTextSelection.Run(page);
    await page.EvaluateAsync("() => { window.renderProbe = pdfHost.image('/api/page/0.png?dpi=149'); }");
    await page.Locator("#zi").ClickAsync();
    await page.Locator("#zi").ClickAsync();
    await page.Locator("#zo").ClickAsync();
    await page.Locator(".thumb").Nth(1).ClickAsync();
    await page.Locator(".thumb").First.ClickAsync();
    await page.EvaluateAsync("async () => { await window.renderProbe; }");
    await page.WaitForFunctionAsync("!document.getElementById('view').inert && document.getElementById('pn').value === '1' && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.url");
    Check(await page.EvaluateAsync<bool>("async () => { const img = document.getElementById('base'); return img.src === await pdfHost.image(img.dataset.url); }"),
        "Queued obsolete zoom/navigation renders cannot replace the final selected page");
    await EditorRecovery.Run(page);
    Check(errors.All(ExpectedRecoveryError), "No unexpected browser errors during recovery: " + string.Join("\n", errors));
    errors.Clear();
    await page.SetViewportSizeAsync(390, 844);
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
    await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "mobile.pdf", MimeType = "application/pdf", Buffer = await File.ReadAllBytesAsync(downloaded) });
    await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'mobile.pdf'");
    Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"), "Mobile layout");
    await EditorHeader.Open(page, true, await File.ReadAllBytesAsync(downloaded));
    Check(await page.EvaluateAsync<bool>("[...document.querySelectorAll('main.hide-l .thumb img')].every(img => getComputedStyle(img).visibility === 'hidden')"), "Collapsed mobile thumbnails stay hidden");
    await page.Locator("#fileBtn").ClickAsync();
    Check(await page.GetByRole(AriaRole.Menuitem, new() { Name = "New…", Exact = false }).IsVisibleAsync(), "Mobile file controls");
    await page.Keyboard.PressAsync("Escape");
    if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "editor-mobile.png") });
    await EditorJsonSafety.Run(context, address + "/csharp-pdf/");
    await EditorSaveOverlap.Run(browser, address + "/csharp-pdf/");
    Check(network.IsEmpty, "No uploads, API requests, external requests or root-relative assets: " + string.Join(", ", network));
    Check(errors.IsEmpty, "No browser errors: " + string.Join(", ", errors));
    Console.WriteLine("PASS: WASM startup, /csharp-pdf/ assets, local PDF open, real UI text edit/preview/save/reopen, rotation/find, PNG/ZIP/text/compressed exports, image decode/delete/undo and clipboard paste after navigation/history release, error recovery, mobile layout, and no outbound PDF traffic.");
}
finally { await app.StopAsync(); files.Dispose(); }

static void Check(bool success, string message)
{
    if (!success) throw new Exception("FAIL: " + message);
}
static bool ExpectedRecoveryError(string error) =>
    error.Contains("simulated recovery quota failure") || error.Contains("Encrypted PDFs require the server editor") || error.Contains("password-protected or uses encryption");
