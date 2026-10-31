using System.Text.Json;
using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

/// <summary>Fills every AcroForm field type through the real Select-mode UI, in a lone form and in a merged 50-page-style collection.</summary>
static class EditorForms
{
    sealed record Widget(string Name, string Kind, double X0, double Y0, double X1, double Y1, string Value, string? On, bool Checked, bool ReadOnly, int MaxLen, bool Comb, bool Multiline, List<Opt>? Options);
    sealed record Opt(string Value, string Label);

    public static async Task Run(IPage page)
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
        await Fill(page, "27-full-collection.pdf", merged.Save(), lone);
        Console.WriteLine("PASS: Select-mode form filling (text/email/comb/multiline/combo/list/check/radio) in a lone form and a merged collection; widgets win over page groups, push buttons inert, Tab/Space keyboard, save/reopen values and appearance.");
    }

    static async Task Fill(IPage page, string name, byte[] bytes, byte[] _)
    {
        var original = PdfDocument.Load(bytes);
        var formPage = Enumerable.Range(0, original.Pages.Count).First(i => original.Pages[i].GetAnnotations().Any(a => a.Subtype == "Widget"));
        await Open(page, name, bytes, formPage);
        var widgets = await Widgets(page, formPage);
        Check(widgets.Any(w => w.Kind == "text") && widgets.Any(w => w.Kind == "combo") && widgets.Any(w => w.Kind == "list") && widgets.Any(w => w.Kind == "check") && widgets.Any(w => w.Kind == "radio") && widgets.Any(w => w.Kind == "push"),
            name + ": widget listing must expose every field type: " + string.Join(",", widgets.Select(w => w.Kind)));
        var live = widgets.Where(w => w.Kind != "push").ToList();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .wgt').length === n", live.Count);
        Check(await page.Locator("#layer .wgt").CountAsync() == live.Count, name + ": one overlay per fillable widget");
        Check(await page.EvaluateAsync<int>("document.querySelectorAll('.wgt.push, .wgt button').length") == 0, name + ": push buttons get no interactive overlay");

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
                case "combo": case "list":
                    var pick = w.Options!.Select(o => o.Value).First(v => v != w.Value);
                    await el.SelectOptionAsync(pick); expected[w.Name] = pick; break;
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
        Check(await page.EvaluateAsync<string>("document.activeElement.getAttribute('aria-label')") == live[1].Name, name + ": Tab moves to the next field");

        await Poll(async () =>
        {
            var fields = await page.EvaluateAsync<JsonElement>("async () => await (await pdfHost.request('/api/fields')).json()");
            var current = fields.EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!, f => f.GetProperty("value").GetString()!);
            return expected.All(kv => current.GetValueOrDefault(kv.Key) == kv.Value);
        }, name + ": every UI edit reaches the document");
        Check(await WholePage(page) == 0, name + ": filling never selected a page group");

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
    }

    static async Task Open(IPage page, string name, byte[] bytes, int formPage)
    {
        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "application/pdf", Buffer = bytes });
        await page.WaitForFunctionAsync("name => document.getElementById('name').textContent === name && document.getElementById('status').textContent.startsWith('Ready.') && document.getElementById('base')?.naturalWidth > 0", name);
        await page.Locator("[data-tool=select]").ClickAsync();
        if (formPage > 0)
        {
            await page.Locator("#pn").FillAsync((formPage + 1).ToString());
            await page.Locator("#pn").PressAsync("Enter");
        }
        await page.WaitForFunctionAsync("p => document.getElementById('pn').value === String(p) && document.getElementById('base')?.naturalWidth > 0 && document.querySelectorAll('#layer .wgt').length > 0", formPage + 1);
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
