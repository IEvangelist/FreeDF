using System.Collections.Concurrent;
using Microsoft.Playwright;
using PortableDocument.Document;

// One-click sample collection: lazy same-origin fetch, integrity-checked open through the normal worker path.
static class EditorSamples
{
    const string Full = "27-full-collection.pdf";
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Samples: " + message); }
    static Task Opened(IPage page, string name) => page.WaitForFunctionAsync(
        "name => document.getElementById('name').textContent === name && document.getElementById('base')?.naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl", name);
    static Task<int> Pages(IPage page) => page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount");
    static Task Durable(IPage page) => page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");

    public static bool ExpectedError(string error) => error.Contains("samples/27-full-collection.pdf") || error.StartsWith("Failed to load resource:");

    public static async Task Run(IPage page, string address, string root, ConcurrentQueue<string> network)
    {
        var requests = new ConcurrentQueue<(string Method, string Url)>();
        page.Request += (_, r) => requests.Enqueue((r.Method, r.Url));
        int Samples(string file) => requests.Count(r => r.Url == address + "/csharp-pdf/samples/" + file);
        var source = PdfDocument.Open(Path.Combine(root, "samples", Full));

        // Lazy: nothing about the samples is fetched by startup.
        Check(!requests.Any(r => r.Url.Contains("/samples/")), "Startup does not fetch the manifest or any PDF.");
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

        // File > Open sample: full collection first, then 01-26; encrypted ones are visibly unavailable; replace confirmation when dirty.
        await page.Locator("#rr").ClickAsync();
        await EditorHeader.State(page, true);
        await page.RunAndWaitForResponseAsync(() => page.Locator("#fileBtn").HoverAsync(), r => r.Url.EndsWith("/samples/manifest.json"));
        await page.Locator("#fileBtn").ClickAsync();
        await page.Locator(".cm-i.has-sub", new() { HasTextString = "Open sample" }).First.HoverAsync();
        await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextString = "Full sample collection" }).First.WaitForAsync();
        var items = await page.Locator(".cm-i.has-sub", new() { HasTextString = "Open sample" }).First.Locator(".cm.sub > .cm-i").AllInnerTextsAsync();
        Check(items.Count == 27 && items[0].StartsWith("Full sample collection (50 pages)") && items[1].StartsWith("01 "), "Menu lists the full collection first, then 01-26 (" + items.Count + ").");
        var disabled = await page.Locator(".cm-i.has-sub", new() { HasTextString = "Open sample" }).First.Locator(".cm.sub > .cm-i:disabled").AllInnerTextsAsync();
        Check(disabled.Count == 2 && disabled.All(d => d.Contains("requires password support")), "Encrypted samples are marked, not broken.");
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

        await page.UnrouteAsync("**/csharp-pdf/samples/" + Full);
        Check(network.IsEmpty, "No uploads or external requests: " + string.Join(", ", network));
        Check(requests.All(r => r.Url.StartsWith(address + "/")), "Every request stays on the editor's origin.");
        Check(requests.Where(r => r.Url.Contains("/samples/")).All(r => r.Method == "GET"), "Samples are only read, never sent.");
    }
}
