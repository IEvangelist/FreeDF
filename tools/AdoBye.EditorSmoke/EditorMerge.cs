using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

static class EditorMerge
{
    static bool ExpectedError(string error) =>
        error.Contains("File 2: no pages in range. The PDF has 1 page(s).", StringComparison.Ordinal)
        || error.Contains("20-report.pdf: no pages in range. The PDF has 6 page(s).", StringComparison.Ordinal);
    public static bool ExpectedErrors(IEnumerable<string> errors, Func<string, bool> otherExpected)
    {
        var entries = errors.ToArray();
        string[] stack =
        [
            "   at EditorSession.ImportRange(String range, Int32 count, String label)",
            "   at EditorSession.Handle(String method, String path, IReadOnlyDictionary`2 query, Byte[] body, String uploadName)",
            "   at BrowserEditor.Request(String command)",
        ];
        for (var i = 0; i < entries.Length; i++)
        {
            if (!ExpectedError(entries[i])) { if (!otherExpected(entries[i])) return false; continue; }
            if (!entries[i].StartsWith("System.ArgumentException:", StringComparison.Ordinal)) continue;
            foreach (var frame in stack)
                if (++i >= entries.Length || entries[i] != frame) return false;
        }
        return true;
    }
    static void Check(bool success, string message) { if (!success) throw new Exception("Merge: " + message); }
    static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", name);
    static string FirstPageText(PdfDocument doc, int page) => string.Concat(Pdf.ExtractText(doc.Pages[page]).Select(s => s.Text));

    public static async Task Run(IPage page)
    {
        var baseDoc = PdfDocument.Open(Sample("01-spec-showcase.pdf"));
        var report = PdfDocument.Open(Sample("20-report.pdf"));
        var invoice = PdfDocument.Open(Sample("22-invoice.pdf"));
        var form = PdfDocument.Open(Sample("11-acroform.pdf"));
        var navigation = PdfDocument.Open(Sample("12-navigation.pdf"));
        await page.Locator("#open").SetInputFilesAsync(Sample("01-spec-showcase.pdf"));
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '01-spec-showcase.pdf' && document.getElementById('base')?.naturalWidth > 0");
        await AssertRejectedBatches(page);

        await page.Locator("#fileBtn").ClickAsync();
        var chooser = await page.RunAndWaitForFileChooserAsync(() => page.GetByRole(AriaRole.Menuitem, new() { Name = "Merge PDFs…" }).ClickAsync());
        Check(chooser.IsMultiple, "The picker accepts several files.");
        await chooser.SetFilesAsync([Sample("20-report.pdf"), Sample("22-invoice.pdf"), Sample("17-encrypted-user-password.pdf"), Sample("11-acroform.pdf"), Sample("12-navigation.pdf")]);
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Merge PDFs", Exact = true });
        await dialog.Locator(".mrow").Nth(4).WaitForAsync();
        await dialog.Locator(".mrow").Nth(1).Locator("[data-m=up]").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Check(await dialog.Locator(".mrow").Nth(0).Locator(".mn").TextContentAsync() == "22-invoice.pdf", "Enter activates Move up without submitting.");
        Check(await page.EvaluateAsync<bool>("document.activeElement?.closest('.mrow')?.dataset.i === '0'"), "Focus follows the moved row.");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Space");
        Check(await dialog.Locator(".mrow").Nth(1).Locator(".mn").TextContentAsync() == "22-invoice.pdf", "Space activates Move down.");
        await dialog.Locator(".mrow").Nth(1).Locator("[data-m=rm]").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Check(await dialog.Locator(".mrow").CountAsync() == 4, "Enter removes without submitting.");
        Check(await page.EvaluateAsync<bool>("!!document.activeElement?.closest('.mrow')"), "Focus survives removal.");
        await page.Keyboard.PressAsync("Escape");
        Check(await dialog.CountAsync() == 0, "Escape closes after reordering/removal.");
        await page.Locator("#fileBtn").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Merge PDFs…" }).FocusAsync();
        var reopen = await page.RunAndWaitForFileChooserAsync(() => page.Keyboard.PressAsync("Enter"));
        await reopen.SetFilesAsync(Sample("20-report.pdf"));
        await dialog.Locator(".mrow").Nth(0).WaitForAsync();
        var keyboardChooser = await page.RunAndWaitForFileChooserAsync(() => dialog.Locator("#mga").PressAsync("Enter"));
        await keyboardChooser.SetFilesAsync([Sample("22-invoice.pdf"), Sample("17-encrypted-user-password.pdf"), Sample("11-acroform.pdf"), Sample("12-navigation.pdf")]);
        await dialog.Locator(".mrow").Nth(4).WaitForAsync();
        Check(await dialog.Locator(".mrow .merr").CountAsync() == 1, "The password-protected file is flagged, not opened.");

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Merge", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('Remove the files')");
        Check(await dialog.CountAsync() == 1, "The dialog stays open while a file is invalid.");
        Check(await page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount") == baseDoc.Pages.Count, "Invalid input leaves the document untouched.");

        await dialog.Locator(".mrow").Nth(2).Locator("[data-m=rm]").ClickAsync();
        await dialog.Locator(".mrow").Nth(1).Locator("[data-m=up]").ClickAsync();
        await dialog.Locator(".mrow").Nth(0).Locator(".mr").FillAsync("1");
        await dialog.Locator(".mrow").Nth(1).Locator(".mr").FillAsync("99");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Merge", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('no pages in range')");
        Check(await dialog.CountAsync() == 1, "The dialog stays open after a bad range.");
        Check(await page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount") == baseDoc.Pages.Count, "A bad range in a later file leaves the document untouched (all-or-nothing).");
        await dialog.Locator(".mrow").Nth(1).Locator(".mr").FillAsync("");
        await dialog.Locator("#mat").SelectOptionAsync("1");
        await page.EvaluateAsync("""
            () => {
              window.mergeRequest = pdfHost.request;
              pdfHost.request = (url, options) => window.mergeRequest(url.startsWith('/api/import-batch?') ? url.replace(/ranges=[^&]*/, 'ranges=1%7C999') : url, options);
            }
            """);
        try
        {
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Merge", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("document.querySelector('[data-error]')?.textContent.includes('no pages in range')");
            Check(await dialog.Locator(".mrow").CountAsync() == 4, "Server rejection preserves ordered files.");
            Check(await dialog.Locator(".mrow").Nth(0).Locator(".mr").InputValueAsync() == "1", "Server rejection preserves page ranges.");
            Check(await dialog.Locator("#mat").InputValueAsync() == "1", "Server rejection preserves insertion position.");
        }
        finally { await page.EvaluateAsync("pdfHost.request = window.mergeRequest; delete window.mergeRequest"); }
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Merge", Exact = true }).ClickAsync();
        var expected = baseDoc.Pages.Count + 1 + report.Pages.Count + form.Pages.Count + navigation.Pages.Count;
        try { await page.WaitForFunctionAsync("n => document.getElementById('pages').textContent.startsWith(n + ' ')", expected); }
        catch (TimeoutException) { throw new Exception("Merge did not complete: " + await page.Locator("#status").TextContentAsync()); }
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Merged')");
        await page.WaitForFunctionAsync("document.getElementById('pn').value === '2' && document.getElementById('base').naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.url");
        Check(await page.EvaluateAsync<bool>("""
            () => {
              const img = document.getElementById('base'), canvas = document.createElement('canvas');
              canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
              const ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0);
              const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
              let ink = 0;
              for (let i = 0; i < pixels.length; i += 4) if (pixels[i] < 200 && pixels[i+1] < 200 && pixels[i+2] < 200) ink++;
              return ink > 100;
            }
            """), "The first merged page renders with content.");
        await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");

        await page.Locator("#dl").ClickAsync();
        await page.Locator("#sn").FillAsync("merged.pdf");
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        var saved = PdfDocument.Open(await download.PathAsync() ?? throw new Exception("No downloaded PDF."));
        Check(saved.Pages.Count == expected, $"Saved page count {saved.Pages.Count} != {expected}.");
        Check(FirstPageText(saved, 1) == FirstPageText(invoice, 0), "Page 2 is the selected invoice page.");
        Check(FirstPageText(saved, 2) == FirstPageText(report, 0), "Report pages follow the invoice page in list order.");
        Check(FirstPageText(saved, 0) == FirstPageText(baseDoc, 0), "The original first page is unchanged.");
        var offset = 2 + report.Pages.Count;
        var fieldNames = saved.GetFormFields().Select(f => f.FullName).ToList();
        Check(form.GetFormFields().All(f => fieldNames.Contains(f.FullName)), "Merged form fields are registered in the saved AcroForm.");
        Check(saved.GetOutlines().Any(o => o.Title == "12-navigation" && o.PageIndex == offset + form.Pages.Count), "Merged outlines nest under the file bookmark at its first page.");
        Check(Enumerable.Range(0, navigation.Pages.Count).All(i => saved.GetPageLabel(offset + form.Pages.Count + i) == navigation.GetPageLabel(i)), "Merged pages keep their source page labels.");
        Check(await page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount") == expected, "Single merge step added every page.");

        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "merged-reopened.pdf", MimeType = "application/pdf", Buffer = await File.ReadAllBytesAsync(await download.PathAsync()!) });
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'merged-reopened.pdf' && document.getElementById('base')?.naturalWidth > 0");
        Check(await page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount") == expected, "Reopened document has every merged page.");
    }

    static async Task AssertRejectedBatches(IPage page)
    {
        await page.Locator("[data-tool=rect]").ClickAsync();
        var box = await page.Locator("#base").BoundingBoxAsync() ?? throw new Exception("No page bitmap.");
        await page.Mouse.MoveAsync(box.X + 30, box.Y + 30);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 90, box.Y + 80);
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync("async () => (await (await pdfHost.request('/api/page/0/items')).json()).length === 1");
        var files = new[] { "20-report.pdf", "22-invoice.pdf", "17-encrypted-user-password.pdf" };
        var encoded = await Task.WhenAll(files.Select(async name => Convert.ToBase64String(await File.ReadAllBytesAsync(Sample(name)))));
        Check(await page.EvaluateAsync<bool>("""
            async encoded => {
              const formOf = file => { const body = new FormData(); body.append('f', file); return body; };
              const packFiles = async files => {
                const buffers = await Promise.all(files.map(file => file.arrayBuffer()));
                const bytes = new Uint8Array(4 + buffers.reduce((n, buffer) => n + 4 + buffer.byteLength, 0));
                const view = new DataView(bytes.buffer); view.setUint32(0, files.length, true);
                let offset = 4;
                for (const buffer of buffers) { view.setUint32(offset, buffer.byteLength, true); bytes.set(new Uint8Array(buffer), offset + 4); offset += 4 + buffer.byteLength; }
                return new File([bytes], 'batch.bin');
              };
              const files = encoded.map((data, i) => new File([Uint8Array.from(atob(data), c => c.charCodeAt(0))], `source-${i}.pdf`));
              const canvas = document.createElement('canvas'); canvas.width = canvas.height = 2;
              const asset = await (await pdfHost.request('/api/asset', {method:'POST', body:formOf(new File([await new Promise(r => canvas.toBlob(r))], 'pixel.png'))})).json();
              const items = await (await pdfHost.request('/api/page/0/items')).json();
              await pdfHost.request('/api/page/0/items', {method:'PUT', body:JSON.stringify({items, historyAssets:[asset.id], clipboardAssets:[asset.id]})});
              const snapshot = async () => {
                const recovery = await (await pdfHost.request('/api/recovery')).json();
                delete recovery.document;
                return JSON.stringify({state:await (await pdfHost.request('/api/state')).json(), recovery});
              };
              const before = await snapshot();
              for (const [second, ranges] of [[1, '|999'], [2, '|']]) {
                let rejected = false;
                try { await pdfHost.request('/api/import-batch?ranges=' + encodeURIComponent(ranges), {method:'POST', body:formOf(await packFiles([files[0],files[second]]))}); }
                catch { rejected = true; }
                if (!rejected || await snapshot() !== before || !(await (await pdfHost.request('/api/asset/' + asset.id)).blob()).size) return false;
              }
              await pdfHost.request('/api/page/0/items', {method:'PUT', body:JSON.stringify({items, historyAssets:[], clipboardAssets:[]})});
              return true;
            }
            """, encoded), "Direct batch requests reject later bad-range/encrypted frames without changing state, history, assets or recovery metadata.");
        await page.Keyboard.PressAsync("Control+z");
        await page.WaitForFunctionAsync("async () => (await (await pdfHost.request('/api/page/0/items')).json()).length === 0");
        await page.Locator("[data-tool=select]").ClickAsync();
    }
}
