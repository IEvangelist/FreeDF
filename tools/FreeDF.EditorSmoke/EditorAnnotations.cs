using Microsoft.Playwright;
using PortableDocument.Document;

static class EditorAnnotations
{
    public static async Task Run(IPage page)
    {
        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "01-spec-showcase.pdf"));
        await page.Locator("#open").SetInputFilesAsync(sample);
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '01-spec-showcase.pdf' && !document.getElementById('view').inert");
        await page.Locator("#annot").ClickAsync();
        await page.Locator("#annot-dock").GetByLabel("Author", new() { Exact = true }).FillAsync("Smoke reviewer");
        await page.Locator("[data-annot-tool=note]").ClickAsync();
        EventHandler<IDialog>? acceptNote = null;
        acceptNote = async (_, dialog) => { page.Dialog -= acceptNote; await dialog.AcceptAsync("Public annotation round trip"); };
        page.Dialog += acceptNote;
        await page.GetByRole(AriaRole.Button, new() { Name = "Add selected tool at page centre", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.querySelector('#annot-props textarea')");
        await page.Locator("#annot-props textarea").First.FillAsync("Public annotation round trip");
        await page.Locator("#annot-props").GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent === 'Annotation updated.'");
        await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Review status", Exact = true }), "Accepted");
        await page.GetByLabel("Reply text", new() { Exact = true }).FillAsync("Reviewed");
        await page.Locator("#annot-props").GetByRole(AriaRole.Button, new() { Name = "Reply", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent === 'Reply added.'");
        await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by author", Exact = true }), "Smoke reviewer");
        await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by status", Exact = true }), "Accepted");
        if (!(await page.Locator("#annot-list").TextContentAsync())!.Contains("Public annotation round trip"))
            throw new Exception("Custom annotation filters must retain matching comments.");
        await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by author", Exact = true }), "");
        await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by status", Exact = true }), "");
        await page.Locator("#dl").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        var bytes = await File.ReadAllBytesAsync((await download.PathAsync())!);
        if (!PdfEditor.Open(PdfDocument.Load(bytes)).ListAnnotations(0).Any(a => a.Spec.Contents == "Public annotation round trip"))
            throw new Exception("Authored annotation missing from saved PDF");
        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "annotated.pdf", MimeType = "application/pdf", Buffer = bytes });
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'annotated.pdf' && !document.getElementById('view').inert");
        await page.Locator("#annot").ClickAsync();
        if (!await page.Locator("#annot-dock").IsVisibleAsync()) await page.Locator("#annot").ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('annot-list').textContent.includes('Public annotation round trip')");
        Console.WriteLine("PASS: real annotation module, keyboard-centre sticky-note creation, property update, save and reopened comments.");
    }
}
