using System.Text.Json;
using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

static class EditorSaveOverlap
{
    public static async Task Run(IBrowser browser, string source)
    {
        await using var context = await browser.NewContextAsync(new()
        {
            AcceptDownloads = true, ViewportSize = new() { Width = 1440, Height = 1000 },
        });
        var network = new System.Collections.Concurrent.ConcurrentQueue<string>();
        context.Request += (_, request) =>
        {
            if (request.Url.StartsWith("http:", StringComparison.Ordinal) || request.Url.StartsWith("https:", StringComparison.Ordinal))
                if (!request.Url.StartsWith(source, StringComparison.Ordinal) || request.Method != "GET" || new Uri(request.Url).AbsolutePath.Contains("/api/"))
                    network.Enqueue(request.Method + " " + request.Url);
        };
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(90000);
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 4 });
        try
        {
            await page.GotoAsync(source);
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            await page.EvaluateAsync("""
                () => {
                  const timeout = window.setTimeout, cancel = window.clearTimeout;
                  window.prefetchInput = {scheduled:[], cancelled:[],
                    restore:() => {window.setTimeout=timeout; window.clearTimeout=cancel;}};
                  window.setTimeout = (callback, delay, ...args) => {
                    if (delay !== 250) return timeout(callback, delay, ...args);
                    const id = timeout(callback, 60000, ...args);
                    prefetchInput.scheduled.push(id);
                    return id;
                  };
                  window.clearTimeout = id => {
                    if (prefetchInput.scheduled.includes(id)) prefetchInput.cancelled.push(id);
                    cancel(id);
                  };
                }
                """);
            var report = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "20-report.pdf"));
            await page.Locator("#open").SetInputFilesAsync(report);
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === '20-report.pdf' && !document.getElementById('view').inert && prefetchInput.scheduled.length > 0");
            await page.Locator("[data-tool=rect]").ClickAsync();
            if (!await page.EvaluateAsync<bool>("prefetchInput.scheduled.every(id => prefetchInput.cancelled.includes(id))"))
                throw new Exception("Pointer input did not cancel pending speculative neighbor work.");
            await page.Locator("#np").ClickAsync();
            await page.WaitForFunctionAsync("!document.getElementById('view').inert && prefetchInput.scheduled.length > 1");
            await page.Keyboard.PressAsync("Escape");
            if (!await page.EvaluateAsync<bool>("prefetchInput.scheduled.every(id => prefetchInput.cancelled.includes(id))"))
                throw new Exception("Keyboard input did not cancel pending speculative neighbor work.");
            await page.EvaluateAsync("prefetchInput.restore()");
            await page.Locator("[data-tool=select]").ClickAsync();
            Console.WriteLine("PASS: pointer and keyboard input cancel queued neighbor prefetch before editing.");
            var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "09-images.pdf"));
            await page.Locator("#open").SetInputFilesAsync(sample);
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === '09-images.pdf' && document.getElementById('base')?.naturalWidth > 0 && !document.getElementById('view').inert && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
            var results = new List<JsonElement>();
            for (var repeat = 0; repeat < 3; repeat++)
            {
                await page.EvaluateAsync("""
                    () => {
                      window.saveOverlap = {events:[], swaps:0, blank:0, watching:true, released:false};
                      const state = saveOverlap;
                      state.original = document.getElementById('base');
                      state.thumbnails = [...document.querySelectorAll('.thumb img')].map(img => img.src);
                      state.gate = new Promise(resolve => state.release = () => { state.released=true; resolve(); });
                      const frame = () => {
                        const base = document.getElementById('base');
                        if (!base?.naturalWidth || !base.isConnected) state.blank++;
                        if (state.watching) requestAnimationFrame(frame);
                      };
                      requestAnimationFrame(frame);
                      state.observe = new MutationObserver(() => {
                        if (document.getElementById('base') !== state.original) state.swaps++;
                      });
                      state.observe.observe(document.getElementById('page'), {childList:true});
                      state.onInput = event => {
                        if (!event.target.closest('.dok')) return;
                        const start = event.timeStamp;
                        requestAnimationFrame(() => state.events.push({type:event.type, ms:performance.now()-start}));
                      };
                      for (const type of ['pointermove','pointerdown','pointerup','click'])
                        addEventListener(type, state.onInput, true);
                      const originalImage = pdfHost.image.bind(pdfHost);
                      state.image = pdfHost.image;
                      pdfHost.image = async url => {
                        const result = await originalImage(url);
                        if (url.includes('/api/page/') && !state.released) {
                          if (url.includes('bare=')) state.ready = true;
                          await state.gate;
                        }
                        return result;
                      };
                    }
                    """);
                await page.Locator("#layer .span").First.ClickAsync(new() { Force = true });
                var text = page.Locator("#props textarea[data-k=text], #props textarea[data-original-text]");
                await text.FillAsync($"Save overlap edit {repeat}");
                await text.PressAsync("Tab");
                await page.WaitForFunctionAsync("saveOverlap.ready");
                await page.Locator("#dl").ClickAsync();
                var button = page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
                await button.WaitForAsync();
                await page.EvaluateAsync("saveOverlap.release()");
                // Force the decoded replacement to arrive while the real dialog owns input.
                await page.WaitForTimeoutAsync(150);
                var swappedInDialog = await page.EvaluateAsync<bool>("document.getElementById('base') !== saveOverlap.original");
                var thumbnailsChanged = await page.EvaluateAsync<bool>("[...document.querySelectorAll('.thumb img')].some((img,i) => img.src !== saveOverlap.thumbnails[i])");
                var download = await page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
                await page.WaitForFunctionAsync("!document.querySelector('.mdl') && document.getElementById('base') !== saveOverlap.original && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl && saveOverlap.events.length >= 4");
                if (await download.FailureAsync() is { } failure) throw new Exception(failure);
                var saved = PdfDocument.Load(await File.ReadAllBytesAsync((await download.PathAsync())!));
                var savedText = string.Concat(saved.Pages.SelectMany(Pdf.ExtractText).Select(span => span.Text));
                if (!savedText.Contains($"Save overlap edit {repeat}", StringComparison.Ordinal))
                    throw new Exception("Save overlap download lost the edit while presentation was deferred: " + savedText);
                var result = await page.EvaluateAsync<JsonElement>("""
                    overlap => {
                      const s = saveOverlap;
                      s.watching = false; s.observe.disconnect(); pdfHost.image = s.image;
                      for (const type of ['pointermove','pointerdown','pointerup','click'])
                        removeEventListener(type, s.onInput, true);
                      return {...overlap, blank:s.blank, events:s.events,
                        maxMs:Math.max(...s.events.map(e => e.ms)),
                        exact:document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl};
                    }
                    """, new { swappedInDialog, thumbnailsChanged });
                results.Add(result);
                Console.WriteLine($"Save overlap {repeat} (4x CPU): {result}");
            }
            var output = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "save-overlap.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            if (results.Any(r => r.GetProperty("blank").GetInt32() != 0 || !r.GetProperty("exact").GetBoolean()))
                throw new Exception("Save overlap lost the active page or exact replacement.");
            if (results.Any(r => r.GetProperty("swappedInDialog").GetBoolean() || r.GetProperty("thumbnailsChanged").GetBoolean()))
                throw new Exception("Save overlap committed a pending replacement while the Save dialog owned input.");
            if (results.Any(r => r.GetProperty("maxMs").GetDouble() > 50))
                throw new Exception("Save confirmation input-to-frame exceeded the unchanged 50 ms ceiling at 4x CPU.");
            if (!network.IsEmpty) throw new Exception("Save overlap made unexpected network requests: " + string.Join(", ", network));
            Console.WriteLine("PASS: pending replacement deferred across Save confirmation at 4x CPU, input-to-frame <= 50 ms, no blank frames and exact replacement resumed.");
        }
        finally { await page.CloseAsync(); }
    }
}
