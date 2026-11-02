using System.Globalization;
using Microsoft.Playwright;

static class EditorChoices
{
    static void Check(bool success, string message) { if (!success) throw new Exception("Choices: " + message); }

    public static async Task Choose(IPage page, ILocator control, string value)
    {
        await control.ClickAsync();
        var options = page.Locator(".select-popup [role=option]");
        for (var i = 0; i < await options.CountAsync(); i++)
        {
            var option = options.Nth(i);
            if (await option.GetAttributeAsync("data-value") != value) continue;
            await option.ClickAsync();
            return;
        }
        throw new Exception($"Choices: option '{value}' is missing.");
    }

    public static async Task Run(IPage page)
    {
        var artifacts = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
        if (artifacts is not null) Directory.CreateDirectory(artifacts);
        var sizes = new (string Name, double Width, double Height)[]
        {
            ("A4", 595.276, 841.89), ("A3", 841.89, 1190.55), ("A5", 419.53, 595.276),
            ("Letter", 612, 792), ("Legal", 612, 1008), ("Tabloid", 792, 1224)
        };
        await page.Keyboard.PressAsync("Alt+n");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "New document", Exact = true });
        var size = dialog.GetByRole(AriaRole.Combobox, new() { Name = "Page size", Exact = true });
        var orientation = dialog.GetByRole(AriaRole.Combobox, new() { Name = "Orientation", Exact = true });
        Check(await dialog.EvaluateAsync<bool>("el => el.querySelector('.new-doc-preview').getBoundingClientRect().left >= el.querySelector('.new-doc-controls').getBoundingClientRect().right"),
            "The desktop page preview sits beside the controls.");
        await size.PressAsync("ArrowDown");
        Check(await size.GetAttributeAsync("aria-expanded") == "true", "ArrowDown opens the size list.");
        Check(await page.Locator(".select-popup").EvaluateAsync<bool>("el => { const r = el.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight; }"),
            "The custom list stays within the viewport.");
        if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "choices-desktop.png") });
        await size.PressAsync("End");
        Check(await page.Locator(".select-popup [aria-selected=true]").TextContentAsync() == "Tabloid", "End moves to the last option.");
        await size.PressAsync("Escape");
        Check(await dialog.CountAsync() == 1 && await size.GetAttributeAsync("aria-expanded") == "false" &&
            await dialog.Locator("#nps").InputValueAsync() == "A4", "Escape closes only the list without changing the value.");
        await size.PressAsync("Space");
        await size.PressAsync("l");
        await size.PressAsync("Enter");
        Check(await dialog.Locator("#nps").InputValueAsync() == "Letter", "Typeahead and Enter select the matching page size.");
        await orientation.PressAsync("ArrowDown");
        await orientation.PressAsync("ArrowDown");
        await orientation.PressAsync("Enter");
        Check(await dialog.Locator("#nor").InputValueAsync() == "Landscape", "Keyboard selection updates orientation.");
        await page.Keyboard.PressAsync("Escape");

        foreach (var (name, width, height) in sizes)
        {
            foreach (var landscape in new[] { false, true })
            {
                await page.Keyboard.PressAsync("Alt+n");
                await Choose(page, size, name);
                await Choose(page, orientation, landscape ? "Landscape" : "Portrait");
                var (w, h) = landscape ? (height, width) : (width, height);
                var paper = await dialog.Locator("#npage").EvaluateAsync<double[]>(
                    "el => [parseFloat(el.style.width), parseFloat(el.style.height), el.getBoundingClientRect().width, el.getBoundingClientRect().height]");
                Check(Math.Abs(paper[0] / paper[1] - w / h) < .001 && Math.Abs(paper[2] / paper[3] - w / h) < .01,
                    $"{name} preview has the correct aspect ratio.");
                Check(Math.Abs(paper[0] - w * 176 / 1224) < .01 && Math.Abs(paper[1] - h * 176 / 1224) < .01,
                    $"{name} preview is scaled consistently with other sizes.");
                static string Format(double value, int places) => Math.Round(value, places).ToString("0." + new string('#', places), CultureInfo.InvariantCulture);
                var expected = $"{name} · {(landscape ? "Landscape" : "Portrait")}\n{Format(w * 25.4 / 72, 1)} × {Format(h * 25.4 / 72, 1)} mm\n{Format(w / 72, 2)} × {Format(h / 72, 2)} in";
                Check(await dialog.Locator("#npreview-facts").TextContentAsync() == expected, $"{name} preview shows the correct dimensions.");
                await dialog.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached });
                await page.WaitForFunctionAsync("""
                    async ([w, h]) => {
                      const s = await (await pdfHost.request('/api/state')).json();
                      return !document.getElementById('view').inert && s.pageCount === 1 &&
                        Math.abs(s.pages[0].width - w) < .02 && Math.abs(s.pages[0].height - h) < .02;
                    }
                    """, new[] { w, h });
            }
        }

        await page.SetViewportSizeAsync(390, 800);
        await page.Keyboard.PressAsync("Alt+n");
        await size.ClickAsync();
        Check(await page.Locator(".select-popup").EvaluateAsync<bool>("el => { const r = el.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight; }"),
            "The mobile custom list stays within the viewport.");
        await page.Keyboard.PressAsync("Escape");
        await Choose(page, size, "Tabloid");
        await Choose(page, orientation, "Landscape");
        Check(await dialog.EvaluateAsync<bool>("""
            el => { const r = el.getBoundingClientRect(), controls = el.querySelector('.new-doc-controls').getBoundingClientRect(),
              preview = el.querySelector('.new-doc-preview').getBoundingClientRect(), paper = el.querySelector('#npage').getBoundingClientRect(),
              stage = el.querySelector('.new-page-stage').getBoundingClientRect();
              return r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight &&
                preview.top >= controls.bottom && paper.left >= stage.left - 1 && paper.right <= stage.right + 1 &&
                paper.top >= stage.top - 1 && paper.bottom <= stage.bottom + 1; }
            """), "The mobile dialog and complete page thumbnail fit without clipping.");
        if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "choices-mobile.png") });
        await page.Keyboard.PressAsync("Escape");
        await page.SetViewportSizeAsync(1440, 1000);
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Export", Exact = true }).HoverAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Pages as images…", Exact = true }).ClickAsync();
        var export = page.GetByRole(AriaRole.Dialog, new() { Name = "Export as images", Exact = true });
        await Choose(page, export.GetByRole(AriaRole.Combobox, new() { Name = "Resolution", Exact = true }), "300");
        await Choose(page, export.GetByRole(AriaRole.Combobox, new() { Name = "Scope", Exact = true }), "a");
        Check(await export.Locator("#ed").InputValueAsync() == "300" && await export.Locator("#es").InputValueAsync() == "a",
            "Custom export choices update their underlying values.");
        await page.Keyboard.PressAsync("Escape");
        var sample = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "01-spec-showcase.pdf");
        await page.Locator("#merge").SetInputFilesAsync(sample);
        var merge = page.GetByRole(AriaRole.Dialog, new() { Name = "Merge PDFs", Exact = true });
        var insertion = merge.GetByRole(AriaRole.Combobox, new() { Name = "Insert at", Exact = true });
        await insertion.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "After current page (1)", Exact = true }).ClickAsync();
        await insertion.ClickAsync();
        Check(await page.Locator(".select-popup [aria-selected=true]").TextContentAsync() == "After current page (1)",
            "Options with the same value retain the selected label.");
        await page.Keyboard.PressAsync("Escape");
        await page.Keyboard.PressAsync("Escape");
        Console.WriteLine("PASS: custom choice keyboard controls and all New page-size/orientation previews match created PDFs, including mobile layout.");
    }
}
