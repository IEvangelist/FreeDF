using System.Text;
using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

static class EditorTextSelection
{
    public static async Task Run(IPage page)
    {
        var source = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "03-typography-embedded.pdf"));
        var transformed = PdfEditor.Open(source);
        transformed.SetContentOverride(0, Encoding.Latin1.GetBytes("q 1.15 0 0.18 0.85 12 20 cm\n")
            .Concat(PdfDocument.Load(source).Pages[0].GetContents()).Concat(Encoding.Latin1.GetBytes("\nQ")).ToArray());
        await AddedText(page);
        foreach (var (name, bytes) in new[] { ("embedded.pdf", source), ("transformed.pdf", transformed.Save()) })
        {
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "application/pdf", Buffer = bytes });
            await page.WaitForFunctionAsync("name => document.getElementById('name').textContent === name && document.getElementById('status').textContent.startsWith('Ready.') && document.getElementById('base')?.naturalWidth > 0", name);
            if (await page.Locator("#pn").InputValueAsync() != "1") await page.Locator(".thumb").First.ClickAsync();
            await page.WaitForFunctionAsync("document.getElementById('pn').value === '1' && document.getElementById('base')?.naturalWidth > 0");
            await page.WaitForFunctionAsync("document.querySelector('#base')?.dataset.loadedUrl === document.querySelector('#base')?.dataset.targetUrl");
            var before = await BasePixelHash(page);
            var state = await page.EvaluateAsync<string>("async () => JSON.stringify(await (await pdfHost.request('/api/state')).json())");
            await page.Locator("#layer .span").First.DblClickAsync();
            Check(await page.EvaluateAsync<bool>("document.activeElement?.matches('#props [data-original-text]')"),
                name + ": double-clicking original text focuses its editor");
            if (name == "embedded.pdf")
                await RetainEditingOnResize(page, "#props [data-original-text]", name);
            await page.Keyboard.PressAsync("Escape");
            await page.Locator("#lock").ClickAsync();
            await page.Locator("#layer .span").First.DblClickAsync();
            Check(await page.EvaluateAsync<bool>("document.activeElement?.matches('#props [data-original-text]') && document.querySelector('#props [data-original=x]').disabled"),
                name + ": locked text remains editable while position controls are disabled");
            await page.Keyboard.PressAsync("Escape");
            await page.Locator("#lock").ClickAsync();
            await page.SetViewportSizeAsync(390, 800);
            if (!await page.EvaluateAsync<bool>("document.querySelector('main').classList.contains('hide-r')"))
                await page.Locator("#tr").ClickAsync();
            await page.WaitForFunctionAsync("document.querySelector('main').classList.contains('hide-r')");
            await page.Locator("#layer .span").First.DblClickAsync();
            Check(await page.EvaluateAsync<bool>("!document.querySelector('main').classList.contains('hide-r') && document.activeElement?.matches('#props [data-original-text]') && document.getElementById('props').getBoundingClientRect().width >= 200"),
                name + ": double-click opens a usable Properties drawer on narrow screens");
            await page.Keyboard.PressAsync("Escape");
            await page.SetViewportSizeAsync(1440, 1000);
            await page.Keyboard.PressAsync("Escape");
            await page.Locator("#layer .span").First.ClickAsync();
            var input = page.Locator("[data-original-text]");
            await input.WaitForAsync();
            var original = await input.InputValueAsync();
            await input.FocusAsync();
            await input.FillAsync(original);
            await input.PressAsync("Escape");
            await page.Locator("#layer .span").First.ClickAsync();
            await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 3, Y = 3 } });
            var afterState = await page.EvaluateAsync<string>("async () => JSON.stringify(await (await pdfHost.request('/api/state')).json())");
            Check(afterState == state,
                name + ": selection/focus/unchanged input/Escape/deselect must not dirty or mutate the session: " + state + " -> " + afterState);
            Check(await page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/page/0/items')).json()).length") == 0,
                name + ": selection must not create a replacement item");
            Check(await BasePixelHash(page) == before,
                name + ": untouched selection must not repaint or displace text");
            await page.Locator("#dl").ClickAsync();
            var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
            await page.Locator(".mdl").WaitForAsync(new() { State = WaitForSelectorState.Detached });
            var saved = await File.ReadAllBytesAsync((await download.PathAsync())!);
            var originalSpans = Pdf.ExtractText(PdfDocument.Load(bytes).Pages[0]);
            var savedSpans = Pdf.ExtractText(PdfDocument.Load(saved).Pages[0]);
            Check(originalSpans.Select(s => (s.Text, s.FontName, s.X, s.Y, s.Width, s.Size, s.RenderingMatrix))
                .SequenceEqual(savedSpans.Select(s => (s.Text, s.FontName, s.X, s.Y, s.Width, s.Size, s.RenderingMatrix))),
                name + ": untouched save retains every text run's font and geometry");
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "round-trip-" + name, MimeType = "application/pdf", Buffer = saved });
            await page.WaitForFunctionAsync("name => document.getElementById('name').textContent === 'round-trip-' + name && document.getElementById('status').textContent.startsWith('Ready.') && document.getElementById('base')?.naturalWidth > 0", name);
            if (await page.Locator("#pn").InputValueAsync() != "1") await page.Locator(".thumb").First.ClickAsync();
            await page.WaitForFunctionAsync("document.getElementById('pn').value === '1' && document.getElementById('base')?.naturalWidth > 0");
            await page.WaitForFunctionAsync("document.querySelector('#base')?.dataset.loadedUrl === document.querySelector('#base')?.dataset.targetUrl");
            Check(await BasePixelHash(page) == before,
                name + ": untouched save/reopen retains exact rendered pixels");
            var editableSpans = originalSpans.Where(s => !s.Vertical && Math.Abs(s.Angle) < 1 && s.Size > 1 && !string.IsNullOrWhiteSpace(s.Text)).ToList();
            var supported = editableSpans.FindIndex(s => s.Font?.FontFile is not null &&
                s.Text.EnumerateRunes().All(r => s.Font.TryEncode(r.Value, out _, out _)));
            Check(supported >= 0, name + ": fixture must include editable embedded glyphs");
            var prior = editableSpans[supported];
            await page.Locator("#layer .span").Nth(supported).DblClickAsync();
            var edited = prior.Text + prior.Text.EnumerateRunes().First();
            await page.Locator("[data-original-text]").FillAsync(edited);
            await page.WaitForFunctionAsync("async text => (await (await pdfHost.request('/api/page/0/items')).json()).some(item => item.text === text)", edited);
            Check(await page.Locator("#layer .it.txt").First.EvaluateAsync<string>("el => getComputedStyle(el).color") == "rgba(0, 0, 0, 0)",
                name + ": edited originals use the PDF renderer, never an approximate browser font");
            await page.Locator("#layer .it.txt").First.DblClickAsync();
            Check(await page.EvaluateAsync<bool>("document.activeElement?.matches('#props [data-k=text]')"),
                name + ": double-clicking an edited original reopens its Properties editor");
            await page.Locator("#dl").ClickAsync();
            var editDownload = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
            await page.Locator(".mdl").WaitForAsync(new() { State = WaitForSelectorState.Detached });
            var resultSpans = Pdf.ExtractText(PdfDocument.Open((await editDownload.PathAsync())!).Pages[0]);
            var result = resultSpans.SingleOrDefault(s => s.Text == edited)
                ?? throw new Exception($"Edited run not found: [{edited}]; extracted: " + string.Join(" | ", resultSpans.Select(s => $"[{s.Text}] {s.FontName}")));
            Check(result.FontName == prior.FontName && Math.Abs(result.X - prior.X) < .02 && Math.Abs(result.Y - prior.Y) < .02 &&
                Math.Abs(result.Size - prior.Size) < .02 && Math.Abs(result.RenderingMatrix.C - prior.RenderingMatrix.C) < .02,
                name + ": actual edit retains the original embedded font, baseline, size and shear");
            await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 3, Y = 3 } });
        }
        Console.WriteLine("PASS: original and added text double-click, mobile and locked editing, dragging, non-mutating selection, exact save/reopen pixels and original font/matrix retention.");
    }

    static async Task AddedText(IPage page)
    {
        await page.Keyboard.PressAsync("Alt+n");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('base')?.naturalWidth > 0 && !document.getElementById('view').inert");
        await page.Locator("[data-tool=text]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        var editor = page.Locator("#layer [contenteditable='plaintext-only']");
        await editor.FillAsync("Added text");
        await RetainEditingOnResize(page, "#layer [contenteditable='plaintext-only']", "added text");
        await editor.PressAsync("Escape");
        await page.Locator("[data-tool=rect]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 240, Y = 250 } });
        Check(await page.Locator("#layer > .it").CountAsync() == 2, "added text: fixture includes a second object after the text");
        await page.Locator("#layer .it.txt").DblClickAsync();
        Check(await page.EvaluateAsync<bool>("document.activeElement?.matches('#layer .it.txt[contenteditable=\"plaintext-only\"]')"),
            "added text: double-click reopens inline editing");
        await RetainEditingOnResize(page, "#layer [contenteditable='plaintext-only']", "added text with another object");
        await page.Keyboard.PressAsync("Escape");
        var text = page.Locator("#layer .it.txt");
        var box = await text.BoundingBoxAsync() ?? throw new Exception("Added text has no bounding box.");
        var before = await AddedTextX(page);
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 24, box.Y + box.Height / 2 + 12, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await AddedTextX(page, before);
        Check(await page.Locator("#props [data-k=x]").CountAsync() == 1, "added text: exact position remains available after dragging");
        await page.Locator("#lock").ClickAsync();
        await page.Locator("#layer .it.txt").DblClickAsync();
        Check(await page.EvaluateAsync<bool>("document.activeElement?.matches('#layer .it.txt[contenteditable=\"plaintext-only\"]') && document.querySelector('#props [data-k=x]').disabled"),
            "added text: locking geometry does not prevent inline editing");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#lock").ClickAsync();
    }

    static async Task RetainEditingOnResize(IPage page, string selector, string name)
    {
        var viewport = page.ViewportSize ?? throw new Exception("No editor viewport.");
        await page.EvaluateAsync("""
            selector => {
              const el = document.querySelector(selector);
              if (!el || document.activeElement !== el) throw new Error('Text editor is not focused');
              const offset = Math.min(3, el.isContentEditable ? el.textContent.length : el.value.length);
              if (el.isContentEditable) {
                const range = document.createRange(), selection = getSelection();
                range.setStart(el.firstChild, offset); range.collapse(true);
                selection.removeAllRanges(); selection.addRange(range);
              } else el.setSelectionRange(offset, offset);
              const state = window.__editingResize = { el, offset, layouts: 0, order: [...document.querySelectorAll('#layer > .it')].map(item => item._it.id) };
              state.observer = new MutationObserver(() => state.layouts++);
              state.observer.observe(document.getElementById('layer'), { attributes: true, attributeFilter: ['style'] });
            }
            """, selector);
        foreach (var height in new[] { viewport.Height - 200, viewport.Height })
        {
            await page.EvaluateAsync("window.__editingResize.layouts = 0");
            await page.SetViewportSizeAsync(viewport.Width, height);
            await page.WaitForFunctionAsync("window.__editingResize.layouts > 0");
            Check(await page.EvaluateAsync<bool>("""
                () => {
                  const { el, offset, order } = window.__editingResize, selection = getSelection();
                  const currentOrder = [...document.querySelectorAll('#layer > .it')].map(item => item._it.id);
                  return JSON.stringify(order) === JSON.stringify(currentOrder) && el.isConnected && document.activeElement === el && (el.isContentEditable
                    ? selection.anchorNode === el.firstChild && selection.anchorOffset === offset && selection.isCollapsed
                    : el.selectionStart === offset && el.selectionEnd === offset);
                }
                """), name + ": layout repaint retains the focused editor and caret");
        }
        await page.EvaluateAsync("window.__editingResize.observer.disconnect(); delete window.__editingResize");
    }

    static async Task<double> AddedTextX(IPage page, double? after = null)
    {
        var x = await page.EvaluateAsync<double?>("""
            async after => {
              const deadline = performance.now() + 30000;
              do {
                const item = (await (await pdfHost.request('/api/page/0/items')).json()).find(item => item.text === 'Added text');
                if (item && (after === null || item.x > after)) return item.x;
                await new Promise(resolve => setTimeout(resolve, 100));
              } while (performance.now() < deadline);
              return null;
            }
            """, after);
        return x ?? throw new Exception("FAIL: added text was not saved at the expected position");
    }

    public static Task<string> BasePixelHash(IPage page) => page.EvaluateAsync<string>("async () => {" +
        "const image=document.getElementById('base'), canvas=document.createElement('canvas');" +
        "canvas.width=image.naturalWidth; canvas.height=image.naturalHeight;" +
        "const context=canvas.getContext('2d'); context.drawImage(image,0,0);" +
        "const pixels=context.getImageData(0,0,canvas.width,canvas.height).data;" +
        "const digest=await crypto.subtle.digest('SHA-256',pixels);" +
        "return `${canvas.width}x${canvas.height}:`+Array.from(new Uint8Array(digest),b=>b.toString(16).padStart(2,'0')).join('');" +
        "}");

    static void Check(bool success, string message)
    {
        if (!success) throw new Exception("FAIL: " + message);
    }
}
