using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Playwright;
using PortableDocument.Document;

static class EditorPageHistory
{
    const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    const string Settled = "!document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl";

    static Task<JsonElement> Api(IPage page, string url, string method = "GET") => page.EvaluateAsync<JsonElement>(
        "async ([url, method]) => (await (await pdfHost.request(url, {method})).json())", new object[] { url, method });

    static async Task Until(IPage page, Func<Task<bool>> condition, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condition()) return;
            await page.WaitForTimeoutAsync(50);
        }
        throw new Exception("Timed out waiting for " + what);
    }

    public static async Task Run(IPage page, string source)
    {
        var report = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "20-report.pdf"));
        await page.Locator("#open").SetInputFilesAsync(report);
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '20-report.pdf' && " + Settled);
        var pageCount = (await Api(page, "/api/state")).GetProperty("pageCount").GetInt32();
        if (pageCount < 2) throw new Exception("The sample needs at least two pages.");

        await page.Locator("#rr").ClickAsync();
        await Until(page, async () => (await Api(page, "/api/state")).GetProperty("pageUndo").GetInt32() == 1, "rotate to be recorded");
        await page.Locator("#img").SetInputFilesAsync(new FilePayload { Name = "pixel.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Png) });
        string asset = "";
        await Until(page, async () =>
        {
            var items = await Api(page, "/api/page/0/items");
            if (items.GetArrayLength() != 1) return false;
            asset = items[0].GetProperty("asset").GetString()!;
            return true;
        }, "the image to be saved");

        // Selecting another page resets the per-page item stack so the next Ctrl+Z reaches page history.
        await page.Locator(".thumb").Nth(1).ClickAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('.thumb')[1].classList.contains('sel') && " + Settled);
        await page.Locator(".thumb").Nth(0).ClickAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('.thumb')[0].classList.contains('sel') && " + Settled);
        var pixels = await Hash(page);

        await page.Keyboard.PressAsync("Control+z");
        await Until(page, async () => (await Api(page, "/api/state")).GetProperty("pageRedo").GetInt32() == 1, "page undo");
        var undone = await Api(page, "/api/state");
        if (undone.GetProperty("pages")[0].GetProperty("rotation").GetInt32() != 0) throw new Exception("Page undo did not restore rotation.");
        if (!await page.EvaluateAsync<bool>("url => pdfHost.request(url).then(r => r.ok)", "/api/asset/" + asset))
            throw new Exception("Page undo dropped an image used only by the state being left.");
        var message = await page.EvaluateAsync<string>("document.getElementById('status').textContent");
        if (!message.StartsWith("Undid", StringComparison.Ordinal)) throw new Exception("Page undo was not announced: " + message);

        await page.Keyboard.PressAsync("Control+y");
        await Until(page, async () => (await Api(page, "/api/state")).GetProperty("pageRedo").GetInt32() == 0, "page redo");
        await page.WaitForFunctionAsync(Settled);
        var redone = await Api(page, "/api/state");
        if (redone.GetProperty("pageCount").GetInt32() != pageCount || redone.GetProperty("pages")[0].GetProperty("rotation").GetInt32() != 90)
            throw new Exception("Redo did not restore rotation and page count.");
        if ((await Api(page, "/api/page/0/items")).GetArrayLength() != 1) throw new Exception("Redo lost the image item.");
        if (await Hash(page) != pixels) throw new Exception("Redo did not reproduce the rendered page exactly.");

        await page.Locator("#dl").ClickAsync();
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await button.WaitForAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => button.ClickAsync());
        if (await download.FailureAsync() is { } failure) throw new Exception(failure);
        var path = await download.PathAsync() ?? throw new Exception("No download path.");
        var saved = PdfDocument.Load(await File.ReadAllBytesAsync(path));
        if (saved.Pages.Count != pageCount || saved.Pages[0].Rotate != 90) throw new Exception("Saved PDF lost the redone rotation or page count.");

        await page.Locator("#open").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync(Settled);
        if ((await Api(page, "/api/state")).GetProperty("pages")[0].GetProperty("rotation").GetInt32() != 90) throw new Exception("Reopened PDF lost rotation.");
        Console.WriteLine($"PASS: rotate + image survives Ctrl+Z page undo and Ctrl+Y redo ({pageCount} pages, exact pixels, asset retained, announced, save/reopen rotation 90).");
    }

    static async Task<string> Hash(IPage page) => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(
        await page.EvaluateAsync<string>("async () => { const b = new Uint8Array(await (await pdfHost.request('/api/page/0.png?dpi=40')).arrayBuffer()); let s = ''; for (const x of b) s += String.fromCharCode(x); return btoa(s); }"))));
}
