using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

if (args.Length != 1) throw new ArgumentException("Usage: AdoBye.EditorSmoke <published-wwwroot>");
var root = Path.GetFullPath(args[0]);
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
    await using var context = await browser.NewContextAsync(new() { AcceptDownloads = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
    var page = await context.NewPageAsync();
    page.SetDefaultTimeout(60000);
    var errors = new ConcurrentQueue<string>();
    var network = new ConcurrentQueue<string>();
    page.PageError += (_, error) => errors.Enqueue(error);
    page.Console += (_, message) => { if (message.Type == "error") { errors.Enqueue(message.Text); Console.Error.WriteLine(message.Text); } };
    page.Request += (_, request) =>
    {
        if (!request.Url.StartsWith(address + "/csharp-pdf/", StringComparison.Ordinal) || request.Method != "GET" || new Uri(request.Url).AbsolutePath.Contains("/api/"))
            network.Enqueue(request.Method + " " + request.Url);
    };
    page.Response += (_, response) => { if (response.Status >= 400) errors.Enqueue($"{response.Status} {response.Url}"); };
    await page.GotoAsync(address + "/csharp-pdf/");
    try { await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')"); }
    catch (TimeoutException)
    {
        throw new Exception("Startup: " + await page.Locator("#status").TextContentAsync() + "\n" + string.Join("\n", errors) + "\n" + string.Join("\n", network));
    }
    Check(await page.EvaluateAsync<string>("document.baseURI") == address + "/csharp-pdf/", "Repository base path");
    Check(await page.EvaluateAsync<bool>("!!window.pdfHost"), "WASM transport");
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
    await page.Locator("#prev").ClickAsync();
    await page.WaitForFunctionAsync("document.getElementById('base').naturalWidth > 0");
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
    Check(errors.IsEmpty, "No browser errors during editing: " + string.Join("\n", errors));
    var artifacts = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
    if (artifacts is not null)
    {
        Directory.CreateDirectory(artifacts);
        await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "editor-desktop.png") });
    }
    // Invalid local input reports an error without replacing the open document.
    await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "invalid.pdf", MimeType = "application/pdf", Buffer = [1, 2, 3] });
    await page.WaitForFunctionAsync("!document.getElementById('status').textContent.startsWith('Processing')");
    Check(await page.Locator("#name").TextContentAsync() == "reopened.pdf •", "Failed open preserves edits");
    Check((await page.Locator("#status").TextContentAsync())!.Contains("PDF"), "Visible invalid PDF error");
    // The intentionally invalid PDF is logged by both the C# boundary and UI error reporter.
    errors.Clear();
    await page.SetViewportSizeAsync(390, 844);
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
    await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "mobile.pdf", MimeType = "application/pdf", Buffer = await File.ReadAllBytesAsync(downloaded) });
    await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'mobile.pdf'");
    Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"), "Mobile layout");
    await page.Locator("#fileBtn").ClickAsync();
    Check(await page.GetByRole(AriaRole.Menuitem, new() { Name = "New…", Exact = false }).IsVisibleAsync(), "Mobile file controls");
    await page.Keyboard.PressAsync("Escape");
    if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "editor-mobile.png") });
    Check(network.IsEmpty, "No uploads, API requests, external requests or root-relative assets: " + string.Join(", ", network));
    Check(errors.IsEmpty, "No browser errors: " + string.Join(", ", errors));
    Console.WriteLine("PASS: WASM startup, /csharp-pdf/ assets, local PDF open, real UI text edit/preview/save/reopen, rotation/find, PNG/ZIP/text/compressed exports, image decode, error recovery, mobile layout, and no outbound PDF traffic.");
}
finally { await app.StopAsync(); files.Dispose(); }

static void Check(bool success, string message)
{
    if (!success) throw new Exception("FAIL: " + message);
}
