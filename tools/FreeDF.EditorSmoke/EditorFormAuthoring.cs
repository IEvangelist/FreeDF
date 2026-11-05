using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PortableDocument.Core;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

/// <summary>Chromium scenario: create every field type through the real UI, fill, align, set tab order, save, reopen, flatten and render.</summary>
static class EditorFormAuthoring
{
    static void Check(bool success, string message) { if (!success) throw new Exception("Form authoring: " + message); }

    static readonly (string Label, string Name, string? Value)[] Fields =
    [
        ("Text field", "f_text", "Ada Lovelace"), ("Multi-line text", "f_multi", "alpha beta gamma delta epsilon zeta eta theta iota kappa"),
        ("Comb text (fixed cells)", "f_comb", "12345"), ("Password field", "f_pass", "secret"), ("Check box", "f_check", null), ("Radio button", "f_radio", null),
        ("Combo box", "f_combo", "Option 2"), ("List box", "f_list", "Option 3"), ("Push button", "f_button", null), ("Signature placeholder", "f_sig", null),
    ];

    static async Task Updated(IPage page, string name) =>
        await page.WaitForFunctionAsync("n => document.getElementById('status').textContent.includes(n)", $"Updated {name}");

    static async Task<int> Boxes(IPage page) => await page.Locator("#layer .ffbox").CountAsync();

    static async Task SetField(IPage page, string selector, string value, string name)
    {
        var input = page.Locator("#props " + selector);
        await input.FillAsync(value);
        await input.PressAsync("Tab");
        await Updated(page, name);
    }

    static async Task<string> Save(IPage page)
    {
        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.Locator("#dl").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        });
        await page.Locator(".mdl").WaitForAsync(new() { State = WaitForSelectorState.Detached });
        return await download.PathAsync() ?? throw new Exception("No downloaded PDF.");
    }

    static async Task<double[]> Positions(IPage page) =>
        await page.EvaluateAsync<double[]>("async () => (await (await pdfHost.request('/api/forms?page=0')).json()).map(f => f.x)");

    public static async Task Run(IPage page)
    {
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "New…", Exact = false }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('base')?.naturalWidth > 0");
        await page.WaitForFunctionAsync("!!document.getElementById('formBtn')");
        var blank = Pdf.RenderPage(PdfDocument.Load(await File.ReadAllBytesAsync(await Save(page))).Pages[0], 72).Rgba;

        var box = (await page.Locator("#layer").BoundingBoxAsync())!;
        var placed = 0;
        for (var i = 0; i < Fields.Length; i++)
        {
            var (label, name, value) = Fields[i];
            await page.Locator("#formBtn").ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Add field" }).HoverAsync();
            await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextRegex = new Regex("^" + Regex.Escape(label) + "$") }).ClickAsync();
            var y = box.Y + 30 + placed * 62;
            await page.Mouse.MoveAsync(box.X + 40, y);
            await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(box.X + 190, y + 40); await page.Mouse.UpAsync();
            await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .ffbox').length === n", ++placed);
            await page.Locator("#props [data-ff=name]").WaitForAsync();
            await SetField(page, "[data-ff=name]", name, name);
            if (value is not null) await SetField(page, "[data-ff=value]", value, name);
            if (label == "Text field")
            {
                await EditorChoices.Choose(page, page.Locator("#props").GetByRole(AriaRole.Combobox, new() { Name = "Alignment", Exact = true }), "2");
                await Updated(page, name);
                Check(await page.Locator("#props [data-ff=alignment]").InputValueAsync() == "2", "custom alignment choice updates field properties");
            }
            if (label == "Check box")
            {
                await page.Locator("#props [data-ffcheck]").CheckAsync();
                await Updated(page, name);
            }
            if (label == "Radio button")
            {
                await page.Locator("#props [data-ffact=radio]").ClickAsync();
                await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .ffbox').length === n", ++placed);
                            }
        }
        var total = await Boxes(page);
        Check(total == 11, $"expected 11 widgets (10 fields + radio option), found {total}");
        Check(await page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/forms')).json()).length") == 11, "the server lists every widget");

        // Keyboard: arrow keys nudge the focused field, Delete removes it, Ctrl+Z restores it.
        var first = page.Locator("#layer .ffbox").First;
        await first.FocusAsync();
        var before = (await Positions(page))[0];
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Updated 1 field')");
        Check((await Positions(page))[0] > before + 9, "Shift+Arrow nudges by 10 points");
        await page.Locator("#layer .ffbox").First.FocusAsync();
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .ffbox').length === n", 10);
        await page.Keyboard.PressAsync("Control+z");
        await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .ffbox').length === n", 11);

        // Contextual alignment through Edit, then row tab order from the Form menu.
        await page.Locator("#formBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Select all fields on page" }).ClickAsync();
        await page.Locator("#editBtn").ClickAsync();
        var edit = page.GetByRole(AriaRole.Menu, new() { Name = "Edit", Exact = true });
        Check(await edit.GetByRole(AriaRole.Menuitem, new() { Name = "Copy" }).IsEnabledAsync(), "Edit exposes field clipboard actions");
        await edit.GetByRole(AriaRole.Menuitem, new() { Name = "Align fields", Exact = true }).HoverAsync();
        await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextRegex = new Regex("^Left$") }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Updated 11 fields')");
        var xs = await Positions(page);
        Check(xs.Max() - xs.Min() < 0.01, "Align left puts every field on the same X");
        await page.Locator("#formBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Tab order" }).HoverAsync();
        await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextString = "Row order" }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('row')");

        var saved = await Save(page);
        var doc = PdfDocument.Open(saved);
        var names = doc.GetFormFields().Select(f => f.FullName).ToHashSet();
        foreach (var (_, name, _) in Fields) Check(names.Contains(name), $"saved PDF has field {name}");
        var values = doc.GetFormFields().ToDictionary(f => f.FullName, f => f.Value);
        Check(values["f_text"] == "Ada Lovelace" && values["f_combo"] == "Option 2" && values["f_check"] == "Yes", "saved values");
        Check(Pdf.RenderPage(doc.Pages[0], 72).Rgba.AsSpan().SequenceEqual(blank) is false, "widgets render");
        Check(doc.Pages[0].Dict.Get("Tabs") is PdfName { Value: "R" }, "saved /Tabs /R");

        // Reopen the saved file: the authored widgets are editable again.
        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "forms.pdf", MimeType = "application/pdf", Buffer = await File.ReadAllBytesAsync(saved) });
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'forms.pdf'");
        await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .ffbox').length === n", 11);

        // Flatten everything: widgets and AcroForm disappear but the rendered page stays populated.
        await page.Locator("#formBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Flatten form" }).HoverAsync();
        await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextString = "All fields" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Flatten form fields", Exact = true })
            .GetByRole(AriaRole.Button, new() { Name = "Flatten fields", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .ffbox').length === n", 0);
        // A completed file download can precede the Save modal closing; undo must wait for the UI, not just the file.
        await page.EvaluateAsync("""
            () => {
              const original = pdfHost.download.bind(pdfHost);
              pdfHost.download = async (...args) => {
                pdfHost.download = original;
                await original(...args);
                await new Promise(resolve => setTimeout(resolve, 750));
              };
            }
            """);
        var flat = PdfDocument.Open(await Save(page));
        Check(flat.GetFormFields().Count == 0 && flat.File.Catalog.Get("AcroForm") is null, "flattened PDF has no AcroForm");
        Check(!flat.Pages[0].GetAnnotations().Any(a => a.Subtype == "Widget"), "flattened PDF has no widgets");
        var flatPixels = Pdf.RenderPage(flat.Pages[0], 72).Rgba;
        Check(!flatPixels.AsSpan().SequenceEqual(blank), "flattened page still renders the field appearances");
        await page.Keyboard.PressAsync("Control+z");
        await page.WaitForFunctionAsync("n => document.querySelectorAll('#layer .ffbox').length === n", 11);
        await page.Locator("#form-mode").GetByRole(AriaRole.Button, new() { Name = "Fill out", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('#layer .ffbox').length === 0 && document.querySelectorAll('#layer .wgt').length > 0");
        await page.Locator("#form-mode").GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('#layer .ffbox').length === 11 && document.querySelectorAll('#layer .wgt').length === 0");
        Console.WriteLine("PASS: created every field type from the UI, filled, nudged/deleted/undone by keyboard, aligned, set tab order, saved, reopened, flattened and rendered.");
    }
}
