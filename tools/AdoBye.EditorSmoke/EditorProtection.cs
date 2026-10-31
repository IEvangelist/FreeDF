using Microsoft.Playwright;
using PortableDocument.Core;
using PortableDocument.Document;

/// <summary>Opens, restricts, re-protects and unprotects the encrypted samples through the real editor UI in Chromium (managed crypto in WASM).</summary>
static class EditorProtection
{
    static void Check(bool success, string message) { if (!success) throw new Exception("Protection: " + message); }
    static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", name);
    public static bool ExpectedError(string error) =>
        error.Contains("password-required") || error.Contains("password-incorrect") || error.Contains("permissions do not allow");

    static async Task<byte[]> Download(IPage page, Func<Task> action)
    {
        var download = await page.RunAndWaitForDownloadAsync(action);
        var path = await download.PathAsync() ?? throw new Exception("Protection: download has no path.");
        return await File.ReadAllBytesAsync(path);
    }

    static async Task Open(IPage page, string file)
    {
        await page.WaitForFunctionAsync("!document.getElementById('status').textContent.startsWith('Processing')");
        await page.EvaluateAsync("document.getElementById('open').value = ''");
        await page.Locator("#open").SetInputFilesAsync(Sample(file));
    }

    public static async Task Run(IPage page)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Password required" });
        var password = dialog.Locator("#pw");

        // Prompt, cancel, wrong password (retry with a clear error), then the right one.
        await Open(page, "17-encrypted-user-password.pdf");
        await password.WaitForAsync();
        Check(await password.GetAttributeAsync("type") == "password", "The field masks input.");
        Check(await password.EvaluateAsync<bool>("el => el === document.activeElement"), "Focus starts in the password field.");
        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached });
        Check(await page.Locator("#base").CountAsync() == 0, "Cancel opens nothing.");

        await Open(page, "17-encrypted-user-password.pdf");
        await password.FillAsync("not-the-password");
        await page.Keyboard.PressAsync("Enter");
        await dialog.Locator("[data-error]:has-text('not correct')").WaitForAsync();
        Check(await page.Locator("#base").CountAsync() == 0, "A wrong password opens nothing.");
        await password.FillAsync("user123");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("document.getElementById('base')?.naturalWidth > 0");
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('Protected PDF')");
        await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery off (encrypted)'");

        var leaked = await page.EvaluateAsync<bool>("""
            async () => {
              const secrets = ['user123', 'not-the-password'];
              const has = text => secrets.some(s => text.includes(s));
              if (Object.entries(localStorage).some(([k, v]) => has(k + v)) || Object.entries(sessionStorage).some(([k, v]) => has(k + v))) return true;
              for (const { name } of await indexedDB.databases()) {
                const db = await new Promise((ok, fail) => { const r = indexedDB.open(name); r.onsuccess = () => ok(r.result); r.onerror = () => fail(r.error); });
                for (const store of db.objectStoreNames) {
                  const rows = await new Promise((ok, fail) => { const r = db.transaction(store).objectStore(store).getAll(); r.onsuccess = () => ok(r.result); r.onerror = () => fail(r.error); });
                  if (rows.length > 0) return true;
                }
                db.close();
              }
              return false;
            }
            """);
        Check(!leaked, "Neither the password nor any decrypted recovery data reaches web storage.");

        // Save as a protected copy: user password, distinct owner password, copying disallowed.
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Save as…" }).ClickAsync();
        var save = page.GetByRole(AriaRole.Dialog, new() { Name = "Save protected PDF" });
        await save.Locator("#pu").FillAsync("copy-user");
        await save.Locator("#pc").FillAsync("copy-user");
        await save.Locator("#po").FillAsync("copy-owner");
        var bytes = await Download(page, () => save.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        Check(!PdfDocument.Load(bytes, "copy-user").File.Decryptor!.IsOwner, "The protected copy opens with its user password.");
        Check(PdfDocument.Load(bytes, "copy-owner").File.Decryptor!.IsOwner, "The protected copy accepts its owner password.");
        Check(PdfDocument.Load(bytes, "copy-user").File.Decryptor!.EncryptionMethod == PdfEncryptionMethod.Aes256, "Browser-written protection is AES-256.");
        Check(PdfDocument.Load(bytes, "copy-user").Pages.Count == PdfDocument.Load(File.ReadAllBytes(Sample("17-encrypted-user-password.pdf")), "user123").Pages.Count, "No pages are lost.");

        // Restricted sample: opens freely, actions are refused with a clear message.
        await Open(page, "18-encrypted-owner-restricted.pdf");
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('Not allowed')");
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Export" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Text (.txt)" }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('permissions do not allow')");

        // Owner password: protection can be removed.
        await Open(page, "17-encrypted-user-password.pdf");
        await password.WaitForAsync();
        await password.FillAsync("owner-user123");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('owner password')");
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Save as…" }).ClickAsync();
        await save.Locator("#prm").CheckAsync();
        var plain = await Download(page, () => save.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        Check(!PdfDocument.Load(plain).File.IsEncrypted, "Remove protection yields an unencrypted PDF.");
    }
}
