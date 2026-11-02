using System.Text.Json;
using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

/// <summary>Fills every AcroForm field type through the real Select-mode UI, in a lone form and in a merged 50-page-style collection.</summary>
static class EditorForms
{
    sealed record Widget(string Name, string Kind, double X0, double Y0, double X1, double Y1, string Value, string? On, bool Checked, bool ReadOnly, int MaxLen, bool Comb, bool Multiline, List<Opt>? Options, bool Multi, bool Edit, List<string>? Values);
    sealed record Opt(string Value, string Label);

    public static async Task Run(IPage page, string siteRoot)
    {
        var samples = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples");
        var lone = await File.ReadAllBytesAsync(Path.Combine(samples, "11-acroform.pdf"));
        var merged = PdfEditor.Create();
        foreach (var file in Directory.GetFiles(samples, "*.pdf").OrderBy(f => f, StringComparer.Ordinal))
        {
            PdfDocument source;
            try { source = PdfDocument.Load(await File.ReadAllBytesAsync(file)); } catch (PortableDocument.Core.PdfPasswordException) { continue; }
            merged.ImportPages(source, Enumerable.Range(0, source.Pages.Count).ToList(), merged.PageCount, PdfImportOptions.Merge(Path.GetFileName(file)));
        }
        await Fill(page, "11-acroform.pdf", lone, lone);
        var collection = Path.Combine(siteRoot, "samples", "27-full-collection.pdf");
        var collectionBytes = await File.ReadAllBytesAsync(collection);
        var real = PdfDocument.Load(collectionBytes);
        Check(real.Pages.Count == 50 && real.Pages[13].GetAnnotations().Any(a => a.Subtype == "Widget"), "Published collection has 50 pages and forms on page 14");
        await Fill(page, "merged-collection.pdf", merged.Save(), lone);
        await Fill(page, "27-full-collection.pdf", collectionBytes, lone);
        await Synthetic(page);
        Console.WriteLine("PASS: default Edit and keyboard Fill out/Edit toggle; all field types in lone/merged forms and real published 50-page collection page 14; mode persists across navigation/save without changing dirty/history; widgets win over page groups, push buttons inert, Tab/Space, save/reopen values/appearance; rotated CropBox/no-AP export radios, multi-select, editable combo and undo.");
    }

    static byte[] SyntheticForm()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 500] /CropBox [20 30 320 430] /Rotate 90 /Resources << >> /Contents 4 0 R /Annots [7 0 R 8 0 R 9 0 R 10 0 R 11 0 R 12 0 R 13 0 R] >>",
            "<< /Length 25 >>\nstream\n0.95 g 0 0 400 500 re f\nendstream",
            "<< >>",
            "<< /Fields [7 0 R 8 0 R 15 0 R 12 0 R 13 0 R] /NeedAppearances true /DA (/Helv 12 Tf 0 g) >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Name) /F 4 /P 3 0 R /Rect [40 400 200 420] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Btn /T (Agree) /F 4 /P 3 0 R /Rect [40 360 60 380] >>",
            "<< /Type /Annot /Subtype /Widget /Parent 15 0 R /F 4 /P 3 0 R /Rect [40 320 60 340] >>",
            "<< /Type /Annot /Subtype /Widget /Parent 15 0 R /F 4 /P 3 0 R /Rect [70 320 90 340] >>",
            "<< /Type /Annot /Subtype /Widget /Parent 15 0 R /F 4 /P 3 0 R /Rect [100 320 120 340] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 2097152 /T (Tags) /Opt [(a) (b) (c)] /V [(a) (c)] /F 4 /P 3 0 R /Rect [40 220 160 300] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 393216 /T (City) /Opt [[(sf) (San Francisco)] [(ny) (New York)]] /F 4 /P 3 0 R /Rect [40 180 200 200] >>",
            "<< >>",
            "<< /FT /Btn /Ff 49152 /T (Size) /Opt [(S) (M) (L)] /Kids [9 0 R 10 0 R 11 0 R] >>"
        };
        var sb = new System.Text.StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.Latin1.GetBytes(sb.ToString());
    }

    static async Task Synthetic(IPage page)
    {
        const string name = "synthetic-form.pdf";
        var bytes = SyntheticForm();
        await Open(page, name, bytes, 0);
        var widgets = await Widgets(page, 0);
        Check(widgets.Where(w => w.Kind == "radio").Select(w => w.On).SequenceEqual(["S", "M", "L"]), name + ": radios without /AP use /Opt exports");
        Check(widgets.Single(w => w.Kind == "check").On == "Yes", name + ": check box without /AP falls back to Yes");
        var tags = widgets.Single(w => w.Name == "Tags");
        Check(tags.Multi && tags.Values!.SequenceEqual(["a", "c"]), name + ": multi-select list exposes every selected value");
        Check(widgets.Single(w => w.Name == "City").Edit, name + ": editable combo exposes the Edit flag");
        Check(await page.Locator("#layer select.wgt.list[multiple]").CountAsync() == 1 &&
            await page.Locator("#layer input.wgt.combo[role=combobox][aria-autocomplete=list]:not([list])").CountAsync() == 1,
            name + ": the multi-select list is preserved and the editable combo uses custom suggestions");
        var city = page.Locator("#layer input.wgt.combo");
        await city.FillAsync("San");
        Check(await page.Locator(".select-popup [role=option]").CountAsync() == 1,
            name + ": typing opens and filters editable combo suggestions");
        await city.PressAsync("Escape");
        Check(await city.EvaluateAsync<bool>("el => document.activeElement === el && el.getAttribute('aria-expanded') === 'false'"),
            name + ": Escape closes suggestions without blurring the editable combo");
        await city.FillAsync("San F");
        await page.GetByRole(AriaRole.Option, new() { Name = "San Francisco", Exact = true }).ClickAsync();
        await Poll(async () => FieldValue(JsonDocument.Parse(await Values(page)).RootElement, "City") == "sf", name + ": choosing an editable combo suggestion saves its export value");
        Check(await city.EvaluateAsync<bool>("el => document.activeElement === el"), name + ": choosing an editable combo suggestion keeps focus");
        await city.FillAsync("");
        await city.PressAsync("Tab");
        await Poll(async () => FieldValue(JsonDocument.Parse(await Values(page)).RootElement, "City") == "", name + ": editable combos still accept free text");

        // Page-sized background: plain click passes through with a hint, Alt+click and Tab+Enter select it.
        var big = page.Locator("#layer .obj[tabindex]").First;
        Check((await big.GetAttributeAsync("title"))!.Contains("Alt+click"), name + ": page-sized object tooltip explains how to select it");
        var spot = await page.EvaluateAsync<double[]>("() => { const b = document.getElementById('layer').getBoundingClientRect(); for (let fy = .05; fy < 1; fy += .1) for (let fx = .05; fx < 1; fx += .1) { const x = b.left + b.width * fx, y = b.top + b.height * fy; if (document.elementFromPoint(x, y)?.matches('.obj[tabindex]')) return [x, y]; } return null; }");
        Check(spot is not null, name + ": some point of the page reaches the page-sized object");
        await page.Mouse.ClickAsync((float)spot![0], (float)spot[1]);
        Check(await WholePage(page) == 0 && (await page.Locator("#status").TextContentAsync())!.Contains("Alt+click"), name + ": plain click shows the hint without selecting: " + await WholePage(page) + " / " + await page.Locator("#status").TextContentAsync());
        await big.FocusAsync(); await page.Keyboard.PressAsync("Enter");
        Check(await WholePage(page) == 1, name + ": keyboard Enter selects the page-sized object");
        await page.Keyboard.PressAsync("Escape");

        await page.Locator("#layer .wgt.text").FillAsync("Ada"); await page.Keyboard.PressAsync("Tab");
        await page.Locator("#layer .wgt.check").CheckAsync();
        await page.Locator("#layer .wgt.radio").Nth(1).CheckAsync();
        // Hold one reply until the next field's input event so undo snapshots cannot pass only on a fast worker.
        await page.EvaluateAsync("""
            () => {
              const original = pdfHost.request.bind(pdfHost);
              let release;
              const completion = new Promise(resolve => { release = resolve; });
              window.releaseFieldReply = () => { release(); delete window.releaseFieldReply; };
              pdfHost.request = async (url, options) => {
                const delay = url.startsWith('/api/field?') && new URL(url, location.href).searchParams.get('n') === 'Tags';
                if (delay) pdfHost.request = original;
                const response = await original(url, options);
                if (delay) await completion;
                return response;
              };
            }
            """);
        await page.Locator("#layer .wgt.list").SelectOptionAsync(["b", "c"]);
        await page.Locator("#layer .wgt.combo").FillAsync("Zurich"); await page.Keyboard.PressAsync("Tab");
        await page.EvaluateAsync("window.releaseFieldReply()");
        var expected = new Dictionary<string, string> { ["Name"] = "Ada", ["Agree"] = "Yes", ["Size"] = "M", ["City"] = "Zurich" };
        await Poll(async () => { var values = await Values(page); return expected.All(kv => FieldValue(JsonDocument.Parse(values).RootElement, kv.Key) == kv.Value); }, name + ": UI edits reach the document");

        await page.Locator("#form-mode button[aria-pressed=true]").FocusAsync();
        await page.Keyboard.PressAsync("Control+z");
        await Poll(async () => FieldValue(JsonDocument.Parse(await Values(page)).RootElement, "City") == "", name + ": undo restores the previous combo value");

        await page.Locator("#dl").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        await page.Locator(".mdl").WaitForAsync(new() { State = WaitForSelectorState.Detached });
        var saved = PdfDocument.Load(await File.ReadAllBytesAsync((await download.PathAsync())!));
        var byName = saved.GetFormFields().ToDictionary(f => f.FullName);
        Check(byName["Name"].Value == "Ada" && byName["Agree"].Value == "Yes" && byName["Size"].Value == "M", name + ": saved text/check/radio values");
        var savedTags = ((PortableDocument.Core.PdfArray)saved.File.Resolve(byName["Tags"].Dict.Get("V"))!).Items.Select(v => ((PortableDocument.Core.PdfString)saved.File.Resolve(v)!).Text).ToList();
        Check(savedTags.SequenceEqual(["b", "c"]) && saved.File.Resolve(byName["Tags"].Dict.Get("I")) is PortableDocument.Core.PdfArray { Count: 2 }, name + ": saved multi-select /V array and /I indices: " + string.Join(",", savedTags));
        var radioStates = saved.Pages[0].GetAnnotations().Where(a => a.Subtype == "Widget" && a.Dict.Get("Parent") is not null).Select(a => saved.File.Resolve(a.Dict.Get("AS")) is PortableDocument.Core.PdfName asName ? asName.Value : null).ToList();
        Check(radioStates.SequenceEqual(["Off", "M", "Off"]), name + ": only the chosen radio is on: " + string.Join(",", radioStates));
    }

    static string? FieldValue(JsonElement fields, string name) =>
        fields.EnumerateArray().Where(f => f.GetProperty("name").GetString() == name).Select(f => f.GetProperty("value").GetString()).FirstOrDefault();

    static async Task Fill(IPage page, string name, byte[] bytes, byte[] _)
    {
        var original = PdfDocument.Load(bytes);
        var formPage = Enumerable.Range(0, original.Pages.Count).First(i => original.Pages[i].GetAnnotations().Any(a => a.Subtype == "Widget"));
        await Open(page, name, bytes, formPage);
        if (name == "27-full-collection.pdf") await OutOfOrderFormLoads(page, formPage);
        var widgets = await Widgets(page, formPage);
        Check(widgets.Any(w => w.Kind == "text") && widgets.Any(w => w.Kind == "combo") && widgets.Any(w => w.Kind == "list") && widgets.Any(w => w.Kind == "check") && widgets.Any(w => w.Kind == "radio") && widgets.Any(w => w.Kind == "push"),
            name + ": widget listing must expose every field type: " + string.Join(",", widgets.Select(w => w.Kind)));
        var live = widgets.Where(w => w.Kind != "push").ToList();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .wgt').length === n", live.Count);
        Check(await page.Locator("#layer .wgt").CountAsync() == live.Count, name + ": one overlay per fillable widget");
        Check(await page.EvaluateAsync<int>("document.querySelectorAll('.wgt.push, .wgt button').length") == 0, name + ": push buttons get no interactive overlay");

        Check(await page.EvaluateAsync<bool>("""
            () => new Promise(resolve => {
              const layer = document.getElementById('layer'), input = layer.querySelector('.wgt.text');
              input.focus();
              const start = input.selectionStart, end = input.selectionEnd;
              const observer = new MutationObserver(() => {
                observer.disconnect();
                resolve(input.isConnected && document.activeElement === input &&
                  input.selectionStart === start && input.selectionEnd === end);
              });
              observer.observe(layer, { childList: true });
              window.dispatchEvent(new Event('resize'));
            })
            """), name + ": repainting unchanged form geometry preserves the focused input and caret");

        // A plain click on a widget edits it; it must not select a page-level group.
        var firstText = page.Locator("#layer .wgt.text").First;
        await firstText.ClickAsync();
        Check(await WholePage(page) == 0, name + ": plain click on a widget must not select a page group");
        var scale = await page.EvaluateAsync<double>("async i => document.getElementById('layer').offsetWidth / (await (await pdfHost.request('/api/state')).json()).pages[i].width", formPage);
        var layer = page.Locator("#layer");
        var before = await Values(page);
        foreach (var push in widgets.Where(w => w.Kind == "push"))
        {
            var pagesBefore = page.Context.Pages.Count; var url = page.Url;
            await layer.ClickAsync(new() { Position = new() {             X = (float)((push.X0 + push.X1) / 2 * scale), Y = (float)((push.Y0 + push.Y1) / 2 * scale) } });
            Check(await WholePage(page) == 0, name + ": clicking " + push.Name + " must not select the whole page: " + await page.EvaluateAsync<string>("() => [...document.querySelectorAll('#layer .obj.on')].map(e => e.title + ' ' + e.offsetWidth + 'x' + e.offsetHeight + ' layer ' + document.getElementById('layer').offsetWidth).join(';')"));
            Check(page.Url == url && page.Context.Pages.Count == pagesBefore && await Values(page) == before, name + ": " + push.Name + " must stay inert");
        }

        var expected = new Dictionary<string, string>();
        var radioDone = new HashSet<string>();
        var toggledKeyboard = false;
        for (var i = 0; i < live.Count; i++)
        {
            var w = live[i]; var el = page.Locator("#layer .wgt").Nth(i);
            switch (w.Kind)
            {
                case "text":
                    var text = w.Comb ? "12345678901234567890"[..Math.Min(w.MaxLen, 20)] : w.Multiline ? "First line\nSecond line" : "Smoke " + i;
                    if (w.MaxLen > 0 && !w.Comb && text.Length > w.MaxLen) text = text[..w.MaxLen];
                    await el.FillAsync(text); await el.PressAsync("Tab");
                    expected[w.Name] = text; break;
                case "combo" when w.Edit:
                    await el.FillAsync("Custom"); await el.PressAsync("Tab"); expected[w.Name] = "Custom"; break;
                case "combo":
                    var pick = w.Options!.Select(o => o.Value).First(v => v != w.Value);
                    await EditorChoices.Choose(page, el, pick); expected[w.Name] = pick; break;
                case "list":
                    var listPick = w.Options!.Select(o => o.Value).First(v => v != w.Value);
                    await el.SelectOptionAsync(listPick); expected[w.Name] = listPick; break;
                case "check":
                    var on = w.Checked ? "Off" : w.On!;
                    if (!toggledKeyboard)
                    {
                        toggledKeyboard = true;
                        await el.FocusAsync(); await el.PressAsync("Space");
                    }
                    else if (w.Checked) await el.UncheckAsync(); else await el.CheckAsync();
                    expected[w.Name] = on; break;
                case "radio":
                    if (w.Checked || !radioDone.Add(w.Name)) break;
                    await el.CheckAsync(); expected[w.Name] = w.On!; break;
            }
        }
        foreach (var w in live.Where(w => w.Kind == "radio" && w.Checked && !expected.ContainsKey(w.Name))) expected[w.Name] = w.On!;

        // Keyboard: Tab moves between overlays in document order.
        await page.Locator("#layer .wgt").First.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        var focused = await page.EvaluateAsync<string?>("document.activeElement?.getAttribute('aria-label')");
        Check(focused == live[1].Name, name + ": Tab moves to the next field (expected " + live[1].Name + ", focused " + (focused ?? "<none>") + ")");

        await Poll(async () =>
        {
            var fields = await page.EvaluateAsync<JsonElement>("async () => await (await pdfHost.request('/api/fields')).json()");
            var current = fields.EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!, f => f.GetProperty("value").GetString()!);
            return expected.All(kv => current.GetValueOrDefault(kv.Key) == kv.Value);
        }, name + ": every UI edit reaches the document");
        Check(await WholePage(page) == 0, name + ": filling never selected a page group");

        if (formPage > 0)
        {
            // Undo history is per page: leaving the page discards it, so Ctrl+Z elsewhere cannot touch field edits or pop a stale entry.
            await page.Locator("#pn").FillAsync("1"); await page.Locator("#pn").PressAsync("Enter");
            await page.WaitForFunctionAsync("""
                () => { const base = document.getElementById('base');
                  return document.getElementById('pn').value === '1' && !document.getElementById('view').inert
                    && base?.naturalWidth > 0 && base.dataset.loadedUrl === base.dataset.targetUrl
                    && base.dataset.loadedUrl.startsWith('/api/page/0.png?'); }
                """);
            await page.Locator("body").PressAsync("Control+z");
            await page.Locator("#pn").FillAsync((formPage + 1).ToString()); await page.Locator("#pn").PressAsync("Enter");
            try
            {
                await page.WaitForFunctionAsync("p => document.getElementById('pn').value === String(p) && !document.getElementById('view').inert && document.querySelectorAll('#layer .wgt').length > 0", formPage + 1);
            }
            catch (TimeoutException e)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => { const base = document.getElementById('base');
                      return JSON.stringify({ page: document.getElementById('pn').value,
                        viewInert: document.getElementById('view').inert,
                        widgets: document.querySelectorAll('#layer .wgt').length,
                        loadedUrl: base?.dataset.loadedUrl, targetUrl: base?.dataset.targetUrl,
                        imageWidth: base?.naturalWidth, status: document.getElementById('status').textContent,
                        focused: document.activeElement?.id }); }
                    """);
                throw new Exception($"{name}: returning to form page {formPage + 1} after cross-page Ctrl+Z did not show widget overlays. Page/render state: {state}", e);
            }
            var current = (await Widgets(page, formPage)).Where(w => w.Kind == "text").ToDictionary(w => w.Name, w => w.Value);
            Check(expected.Where(kv => current.ContainsKey(kv.Key)).All(kv => current[kv.Key] == kv.Value), name + ": cross-page Ctrl+Z must leave field edits intact");
        }

        await page.Locator("#dl").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        await page.Locator(".mdl").WaitForAsync(new() { State = WaitForSelectorState.Detached });
        var saved = await File.ReadAllBytesAsync((await download.PathAsync())!);
        var savedFields = PdfDocument.Load(saved).GetFormFields().ToDictionary(f => f.FullName, f => f.Value ?? "");
        foreach (var (field, value) in expected)
            Check(savedFields.GetValueOrDefault(field) == value, $"{name}: saved {field} = [{savedFields.GetValueOrDefault(field)}], expected [{value}]");
        var changed = !Pdf.RenderPage(original.Pages[formPage], 72).Rgba.SequenceEqual(Pdf.RenderPage(PdfDocument.Load(saved).Pages[formPage], 72).Rgba);
        Check(changed, name + ": saved page appearance reflects the filled values");

        await Open(page, "filled-" + name, saved, formPage);
        var reopened = await Widgets(page, formPage);
        foreach (var w in reopened)
            if (expected.TryGetValue(w.Name, out var value))
                Check(w.Kind is "check" or "radio" ? w.Checked == (w.On == value) : w.Value == value, $"{name}: reopened {w.Name} must show {value}");
        var stateBefore = await page.EvaluateAsync<string>("async () => { const s = await (await pdfHost.request('/api/state')).json(); return JSON.stringify([s.version,s.dirty,s.pageUndo,s.pageRedo]); }");
        await page.Locator("#form-mode").GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
        Check(await page.Locator("#layer .ffbox").CountAsync() > 0 && await page.Locator("#layer .wgt").CountAsync() == 0, name + ": Edit restores layout overlays");
        var stateAfter = await page.EvaluateAsync<string>("async () => { const s = await (await pdfHost.request('/api/state')).json(); return JSON.stringify([s.version,s.dirty,s.pageUndo,s.pageRedo]); }");
        Check(stateBefore == stateAfter, name + ": mode toggle leaves version, dirty and undo/redo unchanged");
    }

    static async Task OutOfOrderFormLoads(IPage page, int formPage)
    {
        await page.Locator("#form-mode").GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
        await page.EvaluateAsync("""
            () => {
              const original = pdfHost.request;
              let release, finished;
              const pending = new Promise(resolve => { release = resolve; });
              window.__formLoadFinished = new Promise(resolve => { finished = resolve; });
              window.__releaseFormLoad = release;
              window.__originalFormRequest = original;
              pdfHost.request = async (...args) => {
                const response = await original(...args);
                if (args[0] === '/api/forms?page=0') {
                  pdfHost.request = original;
                  window.__formLoadHeld = true;
                  const read = response.json.bind(response);
                  response.json = async () => { const data = await read(); setTimeout(finished, 0); return data; };
                  await pending;
                }
                return response;
              };
            }
            """);
        try
        {
            await page.Locator("#pn").FillAsync("1");
            await page.Locator("#pn").PressAsync("Enter");
            await page.WaitForFunctionAsync("window.__formLoadHeld === true");
            await page.Locator("#pn").FillAsync((formPage + 1).ToString());
            await page.Locator("#pn").PressAsync("Enter");
            await page.WaitForFunctionAsync("p => document.getElementById('pn').value === String(p) && !document.getElementById('view').inert && document.querySelectorAll('#layer .ffbox').length > 0", formPage + 1);
            await page.EvaluateAsync("async () => { window.__releaseFormLoad(); await window.__formLoadFinished; }");
            await page.Locator("#lock").ClickAsync();
            Check(await page.Locator("#layer .ffbox").CountAsync() > 0, "A delayed previous-page form load must not replace the current page's fields.");
            await page.Locator("#lock").ClickAsync();
            await page.Locator("#form-mode").GetByRole(AriaRole.Button, new() { Name = "Fill out", Exact = true }).ClickAsync();
        }
        finally
        {
            await page.EvaluateAsync("""
                () => {
                  window.__releaseFormLoad?.();
                  pdfHost.request = window.__originalFormRequest;
                  delete window.__formLoadHeld; delete window.__formLoadFinished;
                  delete window.__releaseFormLoad; delete window.__originalFormRequest;
                }
                """);
        }
    }

    static async Task Open(IPage page, string name, byte[] bytes, int formPage)
    {
        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "application/pdf", Buffer = bytes });
        try { await page.WaitForFunctionAsync("name => document.getElementById('name').textContent === name && document.getElementById('status').textContent.startsWith('Ready.') && document.getElementById('base')?.naturalWidth > 0", name); }
        catch (TimeoutException e)
        {
            var state = await page.EvaluateAsync<string>("() => JSON.stringify({ name: document.getElementById('name').textContent, status: document.getElementById('status').textContent, baseWidth: document.getElementById('base')?.naturalWidth })");
            throw new Exception($"{name}: opening did not reach Ready. State: {state}", e);
        }
        await page.Locator("[data-tool=select]").ClickAsync();
        if (formPage > 0)
        {
            await page.Locator("#pn").FillAsync((formPage + 1).ToString());
            Check(await page.EvaluateAsync<bool>("""
                () => new Promise(resolve => {
                  const input = document.getElementById('pn'), value = input.value;
                  const observer = new MutationObserver(() => {
                    observer.disconnect();
                    resolve(document.activeElement === input && input.value === value);
                  });
                  observer.observe(document.getElementById('layer'), { childList: true });
                  window.dispatchEvent(new Event('resize'));
                })
                """), name + ": repainting preserves a page number being entered");
            await page.Locator("#pn").PressAsync("Enter");
        }
        try
        {
            await page.WaitForFunctionAsync("p => document.getElementById('pn').value === String(p) && !document.getElementById('view').inert && document.querySelectorAll('#layer .ffbox').length > 0", formPage + 1);
        }
        catch (TimeoutException e)
        {
            var state = await page.EvaluateAsync<string>("""
                async p => {
                  const base = document.getElementById('base');
                  const forms = await (await pdfHost.request(`/api/forms?page=${p}`)).json();
                  return JSON.stringify({ name: document.getElementById('name').textContent,
                    page: document.getElementById('pn').value, status: document.getElementById('status').textContent,
                    viewInert: document.getElementById('view').inert,
                    formBoxes: document.querySelectorAll('#layer .ffbox').length,
                    widgets: document.querySelectorAll('#layer .wgt').length, forms: forms.length,
                    mode: [...document.querySelectorAll('#form-mode button')].map(b => [b.textContent, b.getAttribute('aria-pressed')]),
                    loadedUrl: base?.dataset.loadedUrl, targetUrl: base?.dataset.targetUrl });
                }
                """, formPage);
            throw new Exception($"{name}: form page {formPage + 1} did not finish loading. Page/render state: {state}", e);
        }
        var fill = page.Locator("#form-mode").GetByRole(AriaRole.Button, new() { Name = "Fill out", Exact = true });
        Check(await page.Locator("#form-mode").GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).GetAttributeAsync("aria-pressed") == "true", name + ": document defaults to Edit");
        var artifacts = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
        if (name == "27-full-collection.pdf" && artifacts is not null)
        {
            Directory.CreateDirectory(artifacts);
            await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "collection-edit.png") });
        }
        await fill.FocusAsync(); await fill.PressAsync("Space");
        await page.WaitForFunctionAsync("document.querySelectorAll('#layer .wgt').length > 0 && document.querySelectorAll('#layer .ffbox').length === 0");
        Check(await fill.GetAttributeAsync("aria-pressed") == "true" && (await page.Locator("#status").TextContentAsync())!.StartsWith("Fill out form:"), name + ": keyboard toggle updates pressed state and announces Fill out");
        if (name == "27-full-collection.pdf" && artifacts is not null)
            await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "collection-fill.png") });
    }

    static async Task<List<Widget>> Widgets(IPage page, int index) =>
        JsonSerializer.Deserialize<List<Widget>>(await page.EvaluateAsync<string>("async i => JSON.stringify(await (await pdfHost.request(`/api/page/${i}/widgets`)).json())", index),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    static Task<int> WholePage(IPage page) => page.EvaluateAsync<int>("() => { const w = document.getElementById('layer').offsetWidth; return [...document.querySelectorAll('#layer .obj.on')].filter(e => e.offsetWidth >= 0.9 * w).length; }");

    static Task<string> Values(IPage page) => page.EvaluateAsync<string>("async () => JSON.stringify(await (await pdfHost.request('/api/fields')).json())");

    static async Task Poll(Func<Task<bool>> condition, string message)
    {
        for (var i = 0; i < 80; i++)
        {
            if (await condition()) return;
            await Task.Delay(250);
        }
        throw new Exception("FAIL: " + message);
    }

    static void Check(bool success, string message)
    {
        if (!success) throw new Exception("FAIL: " + message);
    }
}
