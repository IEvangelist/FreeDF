using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

if (args is ["--print-layout", var printHtml])
{
    await EditorPrint.Run(printHtml);
    return;
}

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

if (args.Length == 3 && args[0] is "--boot" or "--boot-baseline" or "--shell")
{
    await EditorBoot.Run(args[1], args[2], args[0] == "--boot-baseline", shellOnly: args[0] == "--shell");
    return;
}

if (args.Length >= 4 && args[0] == "--pool")
{
    await EditorPool.Run(args[1], args[2], args[3], args.Length > 4 ? int.Parse(args[4]) : 4);
    return;
}

if (args.Length == 2 && args[0] == "--culture")
{
    await EditorCulture.Run(args[1]);
    return;
}

if (args.Length == 3 && args[0] is "--service-worker" or "--service-worker-migration" or "--update-feedback")
{
    await EditorServiceWorker.Run(args[1], args[2], migrationOnly: args[0] == "--service-worker-migration", updateFeedbackOnly: args[0] == "--update-feedback");
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
var interactionsOnly = args.Length == 2 && args[0] == "--interactions";
var interactionProfileOnly = args.Length == 2 && args[0] == "--interaction-profile";
var choicesOnly = args.Length == 2 && args[0] == "--choices";
var recoveryOnly = args.Length == 2 && args[0] == "--recovery";
var mergeOnly = args.Length == 2 && args[0] == "--merge";
var jsonSafetyOnly = args.Length == 2 && args[0] == "--json-safety";
var saveOverlapOnly = args.Length == 2 && args[0] == "--save-overlap";
var outlineOnly = args.Length == 2 && args[0] == "--outline";
var fileAccessOnly = args.Length == 2 && args[0] == "--file-access";
var storageMigrationOnly = args.Length == 2 && args[0] == "--storage-migration";
var formsOnly = args.Length == 2 && args[0] == "--forms";
var annotationsOnly = args.Length == 2 && args[0] == "--annotations";
var pageHistoryOnly = args.Length == 2 && args[0] == "--page-history";
var objectBreadthOnly = args.Length == 2 && args[0] == "--object-breadth";
var samplesOnly = args.Length == 2 && args[0] == "--samples";
var protectionOnly = args.Length == 2 && args[0] == "--protection";
var viewportOnly = args.Length == 3 && args[0] == "--viewport";
var a11yOnly = args.Length == 3 && args[0] == "--a11y";
var responsiveOnly = args.Length == 3 && args[0] == "--responsive";
var browserOnly = viewportOnly || a11yOnly || responsiveOnly;
var cryptoPerfOnly = args.Length == 2 && args[0] == "--crypto-perf";
if (args.Length != 1 && !browserOnly && !textSelectionOnly && !interactionsOnly && !interactionProfileOnly && !choicesOnly && !recoveryOnly && !mergeOnly && !jsonSafetyOnly && !saveOverlapOnly && !formsOnly && !annotationsOnly && !pageHistoryOnly && !samplesOnly && !protectionOnly && !cryptoPerfOnly && !objectBreadthOnly && !fileAccessOnly && !storageMigrationOnly && !outlineOnly) throw new ArgumentException("Usage: FreeDF.EditorSmoke [--text-selection|--interactions|--interaction-profile|--choices|--recovery|--merge|--json-safety|--save-overlap|--forms|--annotations|--page-history|--samples|--protection|--crypto-perf|--object-breadth|--file-access|--storage-migration|--outline] <published-wwwroot-or-url> | --viewport|--a11y|--responsive <published-wwwroot-or-url> <sample.pdf>");
var source = browserOnly ? args[1] : args[^1];
var publicSite = Uri.TryCreate(source, UriKind.Absolute, out var publicUri) && publicUri.Scheme is "http" or "https";
if (publicSite && publicUri!.AbsolutePath != "/FreeDF/")
    throw new ArgumentException("The public editor URL must end in /FreeDF/.");
var root = publicSite ? Path.GetFullPath(Environment.GetEnvironmentVariable("EDITOR_SMOKE_SITE_ROOT") ?? AppContext.BaseDirectory) : Path.GetFullPath(source);
if (!publicSite && !File.Exists(Path.Combine(root, "index.html"))) throw new FileNotFoundException("Publish the Pages site first.");
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
await using var app = builder.Build();
app.UsePathBase("/FreeDF");
app.Use(async (context, next) =>
{
    if (context.Request.PathBase != "/FreeDF") { context.Response.StatusCode = 404; return; }
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
    var address = publicSite ? publicUri!.GetLeftPart(UriPartial.Authority) : app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var pw = await Playwright.CreateAsync();
    await using var browser = await pw.Chromium.LaunchAsync(    new() { Headless = true, Args = ["--disable-features=OverlayScrollbar"], IgnoreDefaultArgs = viewportOnly || responsiveOnly ? ["--hide-scrollbars"] : null });
    await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block, AcceptDownloads = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
    var page = await context.NewPageAsync();
    if (int.TryParse(Environment.GetEnvironmentVariable("EDITOR_SMOKE_CPU_THROTTLE"), out var throttle) && throttle > 1)
        await (await context.NewCDPSessionAsync(page)).SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = throttle });
    page.SetDefaultTimeout(60000);
    var errors = new ConcurrentQueue<string>();
    var network = new ConcurrentQueue<string>();
    var bootConsole = new ConcurrentQueue<string>();
    var bootFailures = new ConcurrentDictionary<string, string>();
    var bootSuccesses = new ConcurrentDictionary<string, bool>();
    var booting = true;
    string ResourceKey(string url) => new Uri(url).GetLeftPart(UriPartial.Path);
    bool Framework(string url) => url.StartsWith(address + "/FreeDF/_framework/", StringComparison.Ordinal);
    page.PageError += (_, error) => errors.Enqueue(error);
    page.Console += (_, message) =>
    {
        if (message.Type != "error") return;
        if (booting && message.Text.StartsWith("Failed to load resource:", StringComparison.Ordinal)) bootConsole.Enqueue(message.Text);
        else errors.Enqueue(message.Text);
        Console.Error.WriteLine(message.Text);
    };
    context.Request += (_, request) =>
    {
        if (request.Method == "GET" && request.Url.StartsWith("blob:" + address + "/", StringComparison.Ordinal)) return;
        if (!request.Url.StartsWith("http:", StringComparison.Ordinal) && !request.Url.StartsWith("https:", StringComparison.Ordinal)) return;
        if (!request.Url.StartsWith(address + "/FreeDF/", StringComparison.Ordinal) || request.Method != "GET" || new Uri(request.Url).AbsolutePath.Contains("/api/"))
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
    if (fileAccessOnly || storageMigrationOnly) await EditorFileAccess.Install(page);
    if (storageMigrationOnly) await EditorStorageMigration.Install(page);
    await page.GotoAsync(address + "/FreeDF/");
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
    Check(await page.EvaluateAsync<string>("document.baseURI") == address + "/FreeDF/", "Repository base path");
    Check(await page.EvaluateAsync<bool>("!!window.pdfHost"), "WASM transport");
    Check(await page.EvaluateAsync<bool>("pdfHost.execution === 'worker' && !crossOriginIsolated && !window.Blazor"), "Single worker PDF runtime without shared-memory isolation or a main-thread Blazor runtime");
    if (objectBreadthOnly)
    {
        await EditorObjectBreadth.Run(page);
        Check(network.IsEmpty && errors.IsEmpty, "No outbound traffic or browser errors during object editing.");
        return;
    }
    if (pageHistoryOnly)
    {
        await EditorPageHistory.Run(page, address + "/FreeDF/");
        Check(network.IsEmpty && errors.IsEmpty, "No outbound traffic or browser errors during page history.");
        return;
    }
    if (viewportOnly)
    {
        await EditorViewport.Run(browser, address + "/FreeDF/", await File.ReadAllBytesAsync(args[2]), Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS"));
        Console.WriteLine("PASS: app shell fits the viewport and the window never scrolls across viewport/DPR/zoom/panel/menu configurations.");
        return;
    }
    if (a11yOnly)
    {
        await EditorA11y.Run(browser, address + "/FreeDF/", await File.ReadAllBytesAsync(args[2]), Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS"));
        Check(network.IsEmpty, "No outbound traffic during the accessibility sweep: " + string.Join("\n", network));
        Console.WriteLine("PASS: zero serious/critical axe violations and all keyboard/target/focus/spacing assertions across screens, themes, forced colors, zoom and mobile widths.");
        return;
    }
    if (responsiveOnly)
    {
        await EditorResponsive.Run(browser, address + "/FreeDF/", await File.ReadAllBytesAsync(args[2]), Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS"));
        Console.WriteLine("PASS: no overflow, clipping or unreachable controls across the responsive matrix.");
        return;
    }
    if (saveOverlapOnly)
    {
        await EditorSaveOverlap.Run(browser, address + "/FreeDF/");
        return;
    }
    if (jsonSafetyOnly)
    {
        await EditorJsonSafety.Run(context, address + "/FreeDF/");
        Check(network.IsEmpty && errors.IsEmpty, "No unexpected JSON-safety browser errors or traffic.");
        return;
    }
    if (formsOnly)
    {
        await EditorForms.Run(page, root);
        await EditorFormAuthoring.Run(page);
        Check(network.IsEmpty && errors.IsEmpty, "No outbound PDF traffic or browser errors: " + string.Join("\n", errors) + "\nNetwork: " + string.Join("\n", network));
        return;
    }
    if (annotationsOnly)
    {
        await EditorAnnotations.Run(page);
        Check(network.IsEmpty && errors.IsEmpty, "No unexpected annotation errors or outbound traffic.");
        return;
    }
    if (outlineOnly)
    {
        var shots = Environment.GetEnvironmentVariable("EDITOR_SMOKE_SHOTS") ?? Path.Combine(Path.GetTempPath(), "editor-outline-shots");
        await EditorOutline.Run(page, shots, root);
        Check(network.IsEmpty && errors.IsEmpty, "No outbound PDF traffic or browser errors: " + string.Join("\n", errors) + "\nNetwork: " + string.Join("\n", network));
        Console.WriteLine("PASS: Contents tab highlights/expands/reveals the active page's outline entry (keyboard, Go to page, thumbnails, outline click), 10k outline windowed, empty state, no document scroll or focus theft. Screenshots: " + shots);
        return;
    }
    if (interactionsOnly)
    {
        await EditorInteractions.Run(page);
        if (!errors.IsEmpty) throw new Exception(string.Join("\n", errors));
        return;
    }
    if (interactionProfileOnly)
    {
        await EditorInteractions.Profile(page, enforce: false);
        Check(network.IsEmpty && errors.IsEmpty, "No unexpected network requests or browser errors during profiling.");
        return;
    }
    if (textSelectionOnly)
    {
        await EditorTextSelection.Run(page);
        Check(network.IsEmpty && errors.IsEmpty, "No outbound PDF traffic or browser errors.");
        return;
    }
    if (choicesOnly)
    {
        await EditorChoices.Run(page);
        Check(network.IsEmpty && errors.IsEmpty, "No outbound PDF traffic or browser errors: " + string.Join("\n", errors));
        return;
    }
    if (mergeOnly)
    {
        await EditorMerge.Run(page);
        Check(network.IsEmpty && MergeErrorClassifier.ExpectedErrors(errors, ExpectedRecoveryError), "No outbound PDF traffic or unexpected browser errors: " + System.Text.Json.JsonSerializer.Serialize(errors) + "\nNetwork: " + string.Join("\n", network));
        Console.WriteLine("PASS: keyboard merge controls/focus/Escape, inline server rejection with retained choices, direct later-frame rejection preserving state/history/assets and undo, multi-file order/range/insertion, render/save/reopen.");
        return;
    }
    if (samplesOnly)
    {
        await EditorSamples.Run(page, address, root, network);
        Check(errors.All(EditorSamples.ExpectedError), "No unexpected browser errors: " + string.Join("\n", errors));
        Console.WriteLine("PASS: primary showcase (18 tagged pages, distinct nonblank 96dpi renders, save, desktop/mobile welcome), secondary full collection (failure/Retry, 50 pages, outline/labels), showcase-first menu/manifest ordering, replace confirmation, Resume/Discard, #sample=28 and collection deep links, no uploads or external requests.");
        return;
    }
    if (cryptoPerfOnly)
    {
        await EditorCryptoPerf.Run(page);
        return;
    }
    if (protectionOnly)
    {
        await EditorProtection.Run(page);
        Check(network.IsEmpty && errors.All(e => EditorProtection.ExpectedError(e) || ExpectedRecoveryError(e)), "No outbound PDF traffic or unexpected browser errors: " + string.Join("\n", errors) + "\nNetwork: " + string.Join("\n", network));
        Console.WriteLine("PASS: encrypted samples open through the accessible password dialog (cancel, wrong password retry), nothing persisted, permissions enforced, protected save (AES-256) and owner protection removal in WASM.");
        return;
    }
    if (fileAccessOnly)
    {
        if (Environment.GetEnvironmentVariable("EDITOR_SMOKE_FONTS_ONLY") != "1") await EditorFileAccess.Run(page);
        await EditorFileAccess.RunFonts(page);
        Check(network.IsEmpty && errors.All(EditorFileAccess.ExpectedError), "No external requests or unexpected browser errors: " + string.Join("\n", errors) + "\nNetwork: " + string.Join("\n", network));
        return;
    }
    if (storageMigrationOnly)
    {
        await EditorStorageMigration.Run(page, address + "/FreeDF/");
        Check(network.IsEmpty && errors.All(error => error.Contains("simulated migration quota failure", StringComparison.Ordinal)),
            "No external requests or unexpected browser errors during storage migration: " + string.Join("\n", errors) + "\nNetwork: " + string.Join("\n", network));
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
    Check(await page.EvaluateAsync<bool>("['data:image/png;base64,', 'blob:'].some(prefix => document.getElementById('base').src.startsWith(prefix))"), "Local rendered preview");
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
    await page.Locator("#fx").ClickAsync();
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
          for (const method of ['image', 'imageBlob']) {
            if (!window.pdfHost[method]) continue;
            const image = window.pdfHost[method].bind(window.pdfHost);
            window.pdfHost[method] = (url, options) => {
              if (/\/api\/page\/.*\.png/.test(url)) window.saveRenders.push({url, stack:new Error().stack});
              return image(url, options);
            };
          }
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
    if (await page.EvaluateAsync<bool>("document.querySelector('main').classList.contains('hide-l')"))
        await page.Locator("#tl").ClickAsync();
    await page.Locator(".thumb").Nth(1).ClickAsync();
    await page.Locator(".thumb").First.ClickAsync();
    await page.EvaluateAsync("async () => { await window.renderProbe; }");
    try { await page.WaitForFunctionAsync("!document.getElementById('view').inert && document.getElementById('pn').value === '1' && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.url"); }
    catch (TimeoutException)
    {
        throw new Exception("Final page never settled after queued zoom/navigation: " + await page.EvaluateAsync<string>("() => { const b = document.getElementById('base'); return JSON.stringify({ inert: document.getElementById('view').inert, pn: document.getElementById('pn').value, url: b?.dataset.url, loadedUrl: b?.dataset.loadedUrl, targetUrl: b?.dataset.targetUrl, status: document.getElementById('status').textContent, modal: !!document.querySelector('.mdl') }); }"));
    }
    Check(await page.EvaluateAsync<bool>("""
        async () => {
          const img = document.getElementById('base'), expected = new Image();
          expected.src = await pdfHost.image(img.dataset.url); await expected.decode();
          const pixels = image => {
            const canvas = document.createElement('canvas');
            canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
            canvas.getContext('2d').drawImage(image, 0, 0); return canvas.toDataURL();
          };
          return pixels(img) === pixels(expected);
        }
        """),
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
    await EditorJsonSafety.Run(context, address + "/FreeDF/");
    await EditorSaveOverlap.Run(browser, address + "/FreeDF/");
    Check(network.IsEmpty, "No uploads, API requests, external requests or root-relative assets: " + string.Join(", ", network));
    Check(errors.IsEmpty, "No browser errors: " + string.Join(", ", errors));
    Console.WriteLine("PASS: WASM startup, /FreeDF/ assets, local PDF open, real UI text edit/preview/save/reopen, rotation/find, PNG/ZIP/text/compressed exports, image decode/delete/undo and clipboard paste after navigation/history release, error recovery, mobile layout, and no outbound PDF traffic.");
}
finally { await app.StopAsync(); files.Dispose(); }

static void Check(bool success, string message)
{
    if (!success) throw new Exception("FAIL: " + message);
}
static bool ExpectedRecoveryError(string error) =>
    error.Contains("simulated recovery quota failure") || error.Contains("Encrypted PDFs require the server editor") || error.Contains("password-protected or uses encryption");
