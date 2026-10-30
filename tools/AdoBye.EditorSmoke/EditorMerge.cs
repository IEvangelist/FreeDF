using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

static class EditorMerge
{
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

        await page.Locator("#fileBtn").ClickAsync();
        var chooser = await page.RunAndWaitForFileChooserAsync(() => page.GetByRole(AriaRole.Menuitem, new() { Name = "Merge PDFs…" }).ClickAsync());
        Check(chooser.IsMultiple, "The picker accepts several files.");
        await chooser.SetFilesAsync([Sample("20-report.pdf"), Sample("22-invoice.pdf"), Sample("17-encrypted-user-password.pdf"), Sample("11-acroform.pdf"), Sample("12-navigation.pdf")]);
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Merge PDFs", Exact = true });
        await dialog.Locator(".mrow").Nth(4).WaitForAsync();
        Check(await dialog.Locator(".merr").CountAsync() == 1, "The password-protected file is flagged, not opened.");

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
}
