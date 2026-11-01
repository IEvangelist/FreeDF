using System.Diagnostics;
using System.Text;
using Microsoft.Playwright;
using PortableDocument.Core;
using PortableDocument.Document;

/// <summary>Reports open and protected-save time for a ~20 MB AES-256 PDF in the published WASM editor (managed crypto).</summary>
static class EditorCryptoPerf
{
    static byte[] Build(int streamBytes)
    {
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        Write("%PDF-1.7\n");
        string[] objs = ["<</Type/Catalog/Pages 2 0 R>>", "<</Type/Pages/Kids[3 0 R]/Count 1>>", "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>"];
        for (var i = 0; i < objs.Length; i++) { offsets.Add(ms.Position); Write($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        offsets.Add(ms.Position);
        Write($"4 0 obj\n<</Length {streamBytes}>>\nstream\n");
        var content = new byte[streamBytes];
        new Random(7).NextBytes(content);
        var head = Encoding.ASCII.GetBytes("0 0 m ");
        head.CopyTo(content, 0);
        for (var i = head.Length; i < streamBytes; i++) content[i] = (byte)' ';
        ms.Write(content);
        Write("\nendstream\nendobj\n");
        var xref = ms.Position;
        Write($"xref\n0 5\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:0000000000} 00000 n \n");
        Write($"trailer\n<</Size 5/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    public static async Task Run(IPage page)
    {
        var plain = Build(20 * 1024 * 1024);
        var encrypted = PdfEditor.Open(plain).Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { UserPassword = "perf-user", OwnerPassword = "perf-owner" } });
        var path = Path.Combine(Path.GetTempPath(), "crypto-perf-20mb.pdf");
        await File.WriteAllBytesAsync(path, encrypted);
        try
        {
            Console.WriteLine($"Encrypted fixture: {encrypted.Length / 1048576.0:0.0} MB");
            await page.Locator("#open").SetInputFilesAsync(path);
            var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Password required" });
            await dialog.Locator("#pw").WaitForAsync();
            await dialog.Locator("#pw").FillAsync("perf-user");
            var open = Stopwatch.StartNew();
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('Protected PDF')", null, new() { Timeout = 600000 });
            Console.WriteLine($"Open (decrypt + parse, 20 MB AES-256): {open.ElapsedMilliseconds} ms");
            await page.Locator("#fileBtn").ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Save as…" }).ClickAsync();
            var save = page.GetByRole(AriaRole.Dialog, new() { Name = "Save protected PDF" });
            await save.Locator("#pu").FillAsync("perf-new");
            await save.Locator("#pc").FillAsync("perf-new");
            await save.Locator("#po").FillAsync("perf-new-owner");
            var sw = Stopwatch.StartNew();
            var download = await page.RunAndWaitForDownloadAsync(() => save.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync(), new() { Timeout = 600000 });
            await download.PathAsync();
            Console.WriteLine($"Save as protected (re-encrypt 20 MB): {sw.ElapsedMilliseconds} ms");
        }
        finally { File.Delete(path); }
    }
}
