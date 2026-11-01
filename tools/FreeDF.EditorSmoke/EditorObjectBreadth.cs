using System.Text.Json;
using Microsoft.Playwright;
using PortableDocument.Document;

static class EditorObjectBreadth
{
    const string Settled = "!document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl";

    static Task<JsonElement> Api(IPage page, string url) => page.EvaluateAsync<JsonElement>(
        "async url => (await (await pdfHost.request(url)).json())", url);

    public static async Task Run(IPage page)
    {
        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "05-vector-graphics.pdf"));
        await page.Locator("#open").SetInputFilesAsync(sample);
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '05-vector-graphics.pdf' && " + Settled);

        await page.Locator("#view").FocusAsync();
        await page.Keyboard.PressAsync("n");
        await page.WaitForFunctionAsync("/ of \\d+ selected/.test(document.getElementById('status').textContent)");
        await page.Keyboard.PressAsync("Shift+N");
        await page.WaitForFunctionAsync("/ of \\d+ selected/.test(document.getElementById('status').textContent)");
        for (var i = 0; i < 60 && await page.EvaluateAsync<bool>("!(+document.querySelector('[data-o=w]')?.value < 120)"); i++) await page.Keyboard.PressAsync("n");
        if (await page.EvaluateAsync<bool>("!(+document.querySelector('[data-o=w]')?.value < 120)")) throw new Exception("The sample has no small object to zoom to.");
        var before = await page.EvaluateAsync<double>("document.getElementById('base').getBoundingClientRect().width");
        await page.Keyboard.PressAsync("z");
        await page.WaitForFunctionAsync("document.getElementById('status').textContent === 'Zoomed to the selection.'");
        var after = await page.EvaluateAsync<double>("document.getElementById('base').getBoundingClientRect().width");
        if (after <= before) throw new Exception($"Zoom to selection did not enlarge the page ({before} -> {after}).");

        var rotate = page.Locator("[data-ox=rot]");
        await rotate.FillAsync("15");
        await rotate.PressAsync("Tab");
        await page.Locator("[data-ox=skx]").FillAsync("10");
        await page.Locator("[data-ox=skx]").PressAsync("Tab");
        await page.Locator("[data-oz=front]").ClickAsync();
        await page.WaitForFunctionAsync(Settled);

        var objects = await Api(page, "/api/page/0/objects");
        var edited = objects.EnumerateArray().Where(o => o.TryGetProperty("edit", out var e) && e.ValueKind == JsonValueKind.Object).ToList();
        if (edited.Count != 1) throw new Exception("Expected one edited object, found " + edited.Count);
        var edit = edited[0].GetProperty("edit");
        if (edit.GetProperty("rot").GetDouble() != 15 || edit.GetProperty("skx").GetDouble() != 10 || edit.GetProperty("z").GetString() != "front")
            throw new Exception("Object edit lost rotation, skew or z-order: " + edit);

        var before64 = await Hash(page, bare: false);
        await page.Locator("#dl").ClickAsync();
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await button.WaitForAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        if (await download.FailureAsync() is { } failure) throw new Exception(failure);
        var path = await download.PathAsync() ?? throw new Exception("No download path.");
        var saved = PdfDocument.Load(await File.ReadAllBytesAsync(path));
        var content = System.Text.Encoding.Latin1.GetString(saved.Pages[0].GetContents());
        if (!content.Contains(" cm ", StringComparison.Ordinal)) throw new Exception("Saved content lost the object transform.");
        if (System.Text.RegularExpressions.Regex.Matches(content, @"(?<![A-Za-z0-9*])q(?![A-Za-z0-9*])").Count !=
            System.Text.RegularExpressions.Regex.Matches(content, @"(?<![A-Za-z0-9*])Q(?![A-Za-z0-9*])").Count)
            throw new Exception("Saved content has unbalanced q/Q.");

        await page.Locator("#open").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync(Settled);
        if (await Hash(page, bare: false) != before64) throw new Exception("Reopened PDF does not render like the edited page.");
        Console.WriteLine("PASS: keyboard object selection/zoom-to-selection, rotate/skew/front edits saved with balanced q/Q and identical reopen render.");
    }

    static async Task<string> Hash(IPage page, bool bare) => await page.EvaluateAsync<string>(
        "async bare => { const b = new Uint8Array(await crypto.subtle.digest('SHA-256', await (await pdfHost.request('/api/page/0.png?dpi=40' + (bare ? '&bare=true' : ''))).arrayBuffer())); return [...b].map(x => x.toString(16).padStart(2, '0')).join(''); }", bare);
}
