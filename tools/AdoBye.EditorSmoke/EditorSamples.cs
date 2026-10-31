using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Playwright;
using PortableDocument.Document;

// One-click sample collection: lazy same-origin fetch, integrity-checked open through the normal worker path.
static class EditorSamples
{
    const string Full = "27-full-collection.pdf";
    const string Showcase = "28-benchmarks-brand-spec.pdf";
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Samples: " + message); }
    static Task Opened(IPage page, string name) => page.WaitForFunctionAsync(
        "name => document.getElementById('name').textContent === name && document.getElementById('base')?.naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl", name);
    static Task<int> Pages(IPage page) => page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount");
    static Task Durable(IPage page) => page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");

    static async Task<bool> WaitsFor(ILocator l) { try { await l.WaitForAsync(new() { Timeout = 3000 }); return true; } catch (TimeoutException) { return false; } }
    public static bool ExpectedError(string error) => error.Contains("samples/27-full-collection.pdf") || error.StartsWith("Failed to load resource:") || error.Contains("encryption") || error.Contains("Encrypted");

    public static async Task Run(IPage page, string address, string root, ConcurrentQueue<string> network)
    {
        var requests = new ConcurrentQueue<(string Method, string Url)>();
        page.Request += (_, r) => requests.Enqueue((r.Method, r.Url));
        int Samples(string file) => requests.Count(r => r.Url == address + "/csharp-pdf/samples/" + file);
        var source = PdfDocument.Open(Path.Combine(root, "samples", Full));

        // Lazy: nothing about the samples is fetched by startup.
        Check(!requests.Any(r => r.Url.Contains("/samples/")), "Startup does not fetch the manifest or any PDF.");
        await Welcome(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Open the showcase", Exact = true }).ClickAsync();
        await Opened(page, Showcase);
        Check(await Pages(page) == 18 && Samples(Showcase) == 1 && Samples(Full) == 0, "The primary action lazily opens only the 18-page showcase.");
        await ShowcasePixels(page);
        await page.Locator("#dl").ClickAsync();
        await page.Locator("#sn").FillAsync("showcase-copy.pdf");
        var showcaseDownload = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        var showcaseCopy = PdfDocument.Open(await showcaseDownload.PathAsync() ?? throw new Exception("No showcase download."));
        Check(showcaseCopy.Pages.Count == 18 && showcaseCopy.Structure is not null && showcaseCopy.GetOutlines().Count == 6, "The downloaded showcase retains tagging, pages and outline.");
        await page.Locator(".mdl").WaitForAsync(new() { State = WaitForSelectorState.Detached });
        await Durable(page);
        await page.ReloadAsync();
        var initialRecovery = page.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true });
        await initialRecovery.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Open the full sample collection" });
        await button.WaitForAsync();
        Check(await page.EvaluateAsync<bool>("document.getElementById('empty').contains(document.getElementById('sampleOpen'))"), "The primary button lives on the empty state.");

        // Failure then Retry (keyboard).
        var failing = true;
        await page.RouteAsync("**/csharp-pdf/samples/" + Full, async route =>
        {
            if (failing) await route.FulfillAsync(new() { Status = 503, ContentType = "text/plain", Body = "offline" });
            else await route.ContinueAsync();
        });
        await button.ClickAsync();
        var alert = page.Locator(".sample-load [role=alert]");
        await alert.Filter(new() { HasTextString = "HTTP 503" }).WaitForAsync();
        Check(await Pages(page) == 0, "A failed download opens nothing.");
        var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        Check(await page.EvaluateAsync<bool>("document.activeElement?.textContent === 'Retry'"), "Retry receives focus after a failure.");
        failing = false;
        await page.Keyboard.PressAsync("Enter");

        // Opens through the worker path with name, pages, first page raster.
        await Opened(page, Full);
        Check(await Pages(page) == 50, "The full collection has 50 pages.");
        Check(await page.EvaluateAsync<bool>("""
            () => {
              const img = document.getElementById('base'), canvas = document.createElement('canvas');
              canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
              const ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0);
              const px = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
              let ink = 0;
              for (let i = 0; i < px.length; i += 4) if (px[i] < 235 || px[i+1] < 235 || px[i+2] < 235) ink++;
              return ink > 2000;
            }
            """), "The first page is rendered, not blank.");
        Check(await page.Locator(".sample-load").CountAsync() == 0, "The progress panel closes after opening.");
        Check(Samples("manifest.json") >= 1 && Samples(Full) == 2, "Manifest once and the PDF fetched on demand (failed try + retry).");

        // Outline and page labels survive the open: save a copy and read it back.
        await page.Locator("#dl").ClickAsync();
        await page.Locator("#sn").FillAsync("sample-copy.pdf");
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        var copy = PdfDocument.Open(await download.PathAsync() ?? throw new Exception("No download."));
        var outline = copy.GetOutlines();
        Check(copy.Pages.Count == 50 && outline.Count == source.GetOutlines().Count && outline.Count == 26, "Outline entries are preserved.");
        Check(outline.Select(o => o.Title).SequenceEqual(source.GetOutlines().Select(o => o.Title)) && outline.Select(o => o.PageIndex).SequenceEqual(source.GetOutlines().Select(o => o.PageIndex)), "Outline titles and targets are preserved.");
        Check(Enumerable.Range(0, 50).All(i => copy.GetPageLabel(i) == source.GetPageLabel(i)) && copy.GetPageLabel(0)!.StartsWith("01"), "Page labels are preserved.");

        // Recovery: the opened sample is retained, and a reload asks before anything opens.
        await Durable(page);
        var before = Samples(Full);
        await page.ReloadAsync();
        var prompt = page.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true });
        await prompt.WaitForAsync();
        Check(Samples(Full) == before, "Recovery does not refetch the sample.");
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }).ClickAsync();
        await Opened(page, "sample-copy.pdf");
        Check(await Pages(page) == 50, "Resume restores the sample.");

        // File > Open sample: showcase, collection, then 01-26; replace confirmation when dirty.
        await page.Locator("#rr").ClickAsync();
        await EditorHeader.State(page, true);
        await page.RunAndWaitForResponseAsync(() => page.Locator("#fileBtn").HoverAsync(), r => r.Url.EndsWith("/samples/manifest.json"));
        await page.Locator("#fileBtn").ClickAsync();
        await page.Locator(".cm-i.has-sub", new() { HasTextString = "Open sample" }).First.HoverAsync();
        await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextString = "Full sample collection" }).First.WaitForAsync();
        var items = await page.Locator(".cm-i.has-sub", new() { HasTextString = "Open sample" }).First.Locator(".cm.sub > .cm-i").AllInnerTextsAsync();
        Check(items.Count == 28 && items[0].StartsWith("28 Benchmarks, brand and spec parity") && items[1].StartsWith("Full sample collection (50 pages)") && items[2].StartsWith("01 ") && items[^1].StartsWith("26 "), "Menu lists the showcase first, collection second, then 01-26 (" + items.Count + ").");
        var disabled = await page.Locator(".cm-i.has-sub", new() { HasTextString = "Open sample" }).First.Locator(".cm.sub > .cm-i:disabled").AllInnerTextsAsync();
        Check(disabled.Count == 0, "openEncrypted is advertised before the menu renders, so 17 and 18 are offered: " + string.Join(", ", disabled));
        var asked = 0;
        EventHandler<IDialog> decline = async (_, d) => { asked++; await d.DismissAsync(); };
        page.Dialog += decline;
        await page.Locator(".cm.sub > .cm-i", new() { HasTextString = "01 Spec showcase" }).ClickAsync();
        await page.WaitForTimeoutAsync(400);
        page.Dialog -= decline;
        Check(asked == 1 && Samples("01-spec-showcase.pdf") == 0 && await page.Locator("#name").TextContentAsync() == "sample-copy.pdf", "Declining the unsaved-changes prompt keeps the document and fetches nothing.");
        EventHandler<IDialog> accept = async (_, d) => await d.AcceptAsync();
        page.Dialog += accept;
        await page.Locator("#fileBtn").ClickAsync();
        await page.Locator(".cm-i.has-sub", new() { HasTextString = "Open sample" }).First.HoverAsync();
        await page.Locator(".cm.sub > .cm-i", new() { HasTextString = "01 Spec showcase" }).ClickAsync();
        await Opened(page, "01-spec-showcase.pdf");
        page.Dialog -= accept;

        // Deep links wait for the recovery decision, load without further clicks and are consumed.
        before = Samples(Full);
        await Durable(page);
        await page.GotoAsync("about:blank");
        await page.GotoAsync(address + "/csharp-pdf/#sample=27-full-collection");
        await prompt.WaitForAsync();
        await page.WaitForTimeoutAsync(500);
        Check(Samples(Full) == before && await page.Locator("#base").CountAsync() == 0, "A deep link does not load behind the Resume/Discard prompt.");
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
        await Opened(page, Full);
        Check(await Pages(page) == 50 && !page.Url.Contains("sample="), "#sample= loads the collection and is removed from the URL.");
        await Durable(page);
        await page.GotoAsync(address + "/csharp-pdf/?sample=27");
        await prompt.WaitForAsync();
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
        await Opened(page, Full);
        Check(await Pages(page) == 50, "?sample= loads the collection.");

        await Durable(page);
        await page.GotoAsync("about:blank");
        await page.GotoAsync(address + "/csharp-pdf/#sample=28");
        await prompt.WaitForAsync();
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
        await Opened(page, Showcase);
        Check(await Pages(page) == 18 && !page.Url.Contains("sample="), "The documented #sample=28 opens the showcase and is consumed.");

        // Encrypted samples: 18 (empty user password) is attempted, either opening or reporting the precise limitation; 17 needs a password.
        await Durable(page);
        await page.GotoAsync("about:blank");
        await page.GotoAsync(address + "/csharp-pdf/#sample=18");
        await prompt.WaitForAsync();
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '18-encrypted-owner-restricted.pdf' || document.querySelector('.sample-load [role=alert]')?.textContent");
        var limitation = await page.Locator(".sample-load [role=alert]").CountAsync() > 0 ? await page.Locator(".sample-load [role=alert]").TextContentAsync() : null;
        if (string.IsNullOrWhiteSpace(limitation)) limitation = null;
        Check(limitation is null ? await Pages(page) > 0 : limitation.Contains("18-encrypted-owner-restricted.pdf is encrypted and this build could not open it"),
            "#sample=18 opens, or states the precise limitation: " + limitation);
        Console.WriteLine("Sample 18 on the WASM host: " + (limitation ?? "opened"));
        await page.GotoAsync("about:blank");
        await page.GotoAsync(address + "/csharp-pdf/#sample=17");
        if (await prompt.IsVisibleAsync() || await WaitsFor(prompt)) await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Password required" }).WaitForAsync();

        await page.UnrouteAsync("**/csharp-pdf/samples/" + Full);
        Check(network.IsEmpty, "No uploads or external requests: " + string.Join(", ", network));
        Check(requests.All(r => r.Url.StartsWith(address + "/")), "Every request stays on the editor's origin.");
        Check(requests.Where(r => r.Url.Contains("/samples/")).All(r => r.Method == "GET"), "Samples are only read, never sent.");
    }

    static async Task Welcome(IPage page)
    {
        var original = page.ViewportSize!;
        foreach (var (width, height) in new[] { (1440, 1000), (390, 844) })
        {
            await page.SetViewportSizeAsync(width, height);
            Check(await page.EvaluateAsync<bool>("""
                () => {
                  const buttons = [...document.querySelectorAll('#empty button')];
                  return buttons.length === 2 && buttons[0].id === 'sampleOpen'
                    && buttons[0].classList.contains('primary') && buttons[1].id === 'sampleCollection'
                    && buttons.every(b => { const r = b.getBoundingClientRect();
                      return r.width > 0 && r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight; })
                    && document.documentElement.scrollWidth <= innerWidth;
                }
                """), $"Primary showcase and secondary collection fit the {width}px welcome screen.");
            var output = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"welcome-{width}.png") });
            }
        }
        await page.SetViewportSizeAsync(original.Width, original.Height);
    }

    static async Task ShowcasePixels(IPage page)
    {
        var hashes = new HashSet<string>();
        var output = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
        for (var i = 0; i < 18; i++)
        {
            var data = await page.EvaluateAsync<string>("i => pdfHost.image(`/api/page/${i}.png?dpi=96`)", i);
            var png = Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]);
            var image = global::PortableDocument.Render.PngWriter.Decode(png);
            Check(image.W is > 700 and < 850 && image.H is > 1000 and < 1200, $"Showcase page {i + 1} has A4 dimensions at 96 dpi.");
            var ink = 0;
            for (var p = 0; p < image.Rgba.Length; p += 4)
                if (image.Rgba[p] < 235 || image.Rgba[p + 1] < 235 || image.Rgba[p + 2] < 235) ink++;
            Check(ink > 2000, $"Showcase page {i + 1} is not blank.");
            var hash = Convert.ToHexString(SHA256.HashData(image.Rgba));
            Check(hashes.Add(hash), $"Showcase page {i + 1} has distinct rendered content.");
            Console.WriteLine($"Showcase page {i + 1}: {image.W}x{image.H}, ink={ink}, decoded SHA256={hash}");
            if (output is not null) await File.WriteAllBytesAsync(Path.Combine(output, $"showcase-{i + 1:D2}.png"), png);
        }
    }
}
