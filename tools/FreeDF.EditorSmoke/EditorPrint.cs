using Microsoft.Playwright;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

static class EditorPrint
{
    public static async Task Run(string htmlPath)
    {
        var html = await File.ReadAllTextAsync(htmlPath);
        var start = html.IndexOf("async function printDoc()", StringComparison.Ordinal);
        var end = html.IndexOf("$('pr').onclick", start, StringComparison.Ordinal);
        var source = html[start..end];
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        // Exercise the actual print handler without opening a native print dialog.
        await page.EvaluateAsync("""
            () => {
              window.state = { version: 1, pageCount: 6, pages: [
                { width: 612, height: 792, rotation: 0 },
                { width: 595.28, height: 841.89, rotation: 0 },
                { width: 960, height: 540, rotation: 0 },
                { width: 612, height: 792, rotation: 90 },
                { width: 612, height: 792, rotation: 270 },
                { width: 306, height: 396, rotation: 0, userUnit: 2 }
              ] };
              window.flush = async () => {};
              window.status = text => { throw new Error(text); };
              window.imageUrl = async url => {
                const i = Number(url.match(/page\/(\d+)/)[1]), p = state.pages[i], sw = p.rotation % 180 !== 0;
                const canvas = document.createElement('canvas');
                canvas.width = Math.ceil((sw ? p.height : p.width) * (p.userUnit ?? 1) * 150 / 72);
                canvas.height = Math.ceil((sw ? p.width : p.height) * (p.userUnit ?? 1) * 150 / 72);
                const ctx = canvas.getContext('2d');
                ctx.fillStyle = 'white'; ctx.fillRect(0, 0, canvas.width, canvas.height);
                ctx.fillStyle = 'black';
                for (const x of [0, canvas.width - 40])
                  for (const y of [0, canvas.height - 40]) ctx.fillRect(x, y, 40, 40);
                return canvas.toDataURL();
              };
              const append = document.body.appendChild.bind(document.body);
              document.body.appendChild = frame => {
                const loaded = frame.onload;
                frame.onload = async () => {
                  frame.contentWindow.print = () => { window.printReady = true; };
                  await loaded();
                };
                return append(frame);
              };
            }
            """);
        await page.EvaluateAsync("async () => {\n" + source + "\nawait printDoc(); }");
        await page.WaitForFunctionAsync("window.printReady === true");
        var printHtml = await page.Locator("iframe").EvaluateAsync<string>("f => f.srcdoc");
        await page.SetContentAsync(printHtml);
        await page.EvaluateAsync("async () => { await Promise.all([...document.images].map(i => i.decode())); }");
        var bytes = await page.PdfAsync(new() { PreferCSSPageSize = true, PrintBackground = true });
        var document = PdfDocument.Load(bytes);
        var sizes = new (double W, double H)[] { (612, 792), (595.28, 841.89), (960, 540), (792, 612), (792, 612), (612, 792) };
        if (document.Pages.Count != sizes.Length)
            throw new Exception($"Print produced {document.Pages.Count} pages instead of {sizes.Length}.");
        for (var i = 0; i < sizes.Length; i++)
        {
            var box = document.Pages[i].MediaBox;
            var (w, h) = sizes[i];
            if (Math.Abs(box.Width - w) > 1 || Math.Abs(box.Height - h) > 1)
                throw new Exception($"Print page {i + 1} is {box.Width}x{box.Height}pt, expected {w}x{h}pt.");
            var raster = Pdf.RenderPage(document.Pages[i], dpi: 72);
            foreach (var x in new[] { 5, raster.Width - 6 })
                foreach (var y in new[] { 5, raster.Height - 6 })
                {
                    var at = (y * raster.Width + x) * 4;
                    if (raster.Rgba[at] > 40 || raster.Rgba[at + 1] > 40 || raster.Rgba[at + 2] > 40)
                        throw new Exception($"Print page {i + 1} clipped or scaled its corner marker at {x},{y}.");
                }
        }
        Console.WriteLine("Print layout passed: Letter, A4, landscape, rotated and UserUnit pages retain their size, all four corners, and one sheet per page.");
    }
}
