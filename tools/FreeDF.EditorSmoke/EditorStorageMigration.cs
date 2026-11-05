using System.Text.Json;
using Microsoft.Playwright;

static class EditorStorageMigration
{
    const string Setup = """
        (() => {
          const put = IDBObjectStore.prototype.put;
          IDBObjectStore.prototype.put = function (...args) {
            if (sessionStorage.getItem('storage-fault') === this.transaction.db.name)
              throw new DOMException('simulated migration quota failure', 'QuotaExceededError');
            return put.apply(this, args);
          };
          const open = (name, store) => new Promise((resolve, reject) => {
            const request = indexedDB.open(name, 1);
            request.onupgradeneeded = () => request.result.createObjectStore(store);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
          });
          const transact = async (name, store, mode, action) => {
            const db = await open(name, store);
            try {
              return await new Promise((resolve, reject) => {
                const tx = db.transaction(store, mode), request = action(tx.objectStore(store));
                tx.oncomplete = () => resolve(request.result);
                tx.onabort = tx.onerror = () => reject(tx.error ?? request.error);
              });
            } finally { db.close(); }
          };
          window.__migration = {
            get: (name, store, key) => transact(name, store, 'readonly', s => s.get(key)),
            put: (name, store, key, value) => transact(name, store, 'readwrite', s => s.put(value, key)),
            delete: (name, store, key) => transact(name, store, 'readwrite', s => s.delete(key)),
          };
        })();
        """;

    static void Check(bool success, string message) { if (!success) throw new Exception("Storage migration: " + message); }
    public static Task Install(IPage page) => page.AddInitScriptAsync(Setup);

    public static async Task Run(IPage page, string site)
    {
        await page.RouteAsync("**/migration-seed", route => route.FulfillAsync(new()
        {
            Status = 200, ContentType = "text/html", Body = "<!doctype html><title>Storage test</title>",
        }));
        var seed = site + "migration-seed";
        var source = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "03-typography-embedded.pdf"));
        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "migration.pdf", MimeType = "application/pdf", Buffer = source });
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'migration.pdf' && document.getElementById('status').textContent.startsWith('Ready.')");
        await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
        var savedAt = await page.EvaluateAsync<double>("async () => (await window.__migration.get('freedf-editor', 'recovery', 'current')).updatedAt");
        await page.Locator("#rr").ClickAsync();
        await page.WaitForFunctionAsync("""
            document.getElementById('status').textContent.startsWith('Ready.') &&
              document.getElementById('dstate')?.dataset.dirty === 'true'
            """);
        await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
        Check(await page.EvaluateAsync<double>("async () => (await window.__migration.get('freedf-editor', 'recovery', 'current')).updatedAt") > savedAt,
            "The completed rotation has a newer durable recovery record.");
        var savedSnapshot = await page.EvaluateAsync<string>("async () => (await window.__migration.get('freedf-editor', 'recovery', 'current')).payload.text()");
        using (var json = JsonDocument.Parse(savedSnapshot))
            Check(PortableDocument.Document.PdfDocument.Load(Convert.FromBase64String(json.RootElement.GetProperty("document").GetString()!)).Pages[0].Rotate == 90,
                "The durable recovery snapshot contains the completed rotation before migration.");
        await page.EvaluateAsync("""
            async () => {
              const m = window.__migration;
              const record = await m.get('freedf-editor', 'recovery', 'current');
              if (!record) throw new Error('No recovery record to migrate.');
              await m.put('adobye-editor', 'recovery', 'current', record);
              const handle = await (await navigator.storage.getDirectory()).getFileHandle('migration.pdf', { create: true });
              await m.put('adobye-file-handles', 'handles', 'current', { handle, name: handle.name });
              await m.put('adobye-local-fonts', 'metadata', 'families', { format: 1, families: [{ family: 'Legacy Font', styles: [] }] });
            }
            """);
        await page.GotoAsync(seed);
        await page.EvaluateAsync("""
            async () => {
              const m = window.__migration;
              for (const [db, store, key] of [
                ['freedf-editor', 'recovery', 'current'],
                ['freedf-file-handles', 'handles', 'current'],
                ['freedf-local-fonts', 'metadata', 'families'],
              ]) {
                await m.delete(db, store, key);
                localStorage.removeItem('freedf-storage-migrated:' + db);
              }
              sessionStorage.setItem('storage-fault', 'freedf-editor');
            }
            """);

        await page.GotoAsync(site);
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('simulated migration quota failure')");
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const m = window.__migration;
              return !!(await m.get('adobye-editor', 'recovery', 'current')) &&
                !!(await m.get('adobye-file-handles', 'handles', 'current')) &&
                !(await m.get('freedf-editor', 'recovery', 'current')) &&
                localStorage.getItem('freedf-storage-migrated:freedf-editor') === null;
            }
            """), "A failed recovery migration reports the error and retains both old data and its file handle.");

        await page.GotoAsync(seed);
        await page.EvaluateAsync("sessionStorage.removeItem('storage-fault')");
        var other = await page.Context.NewPageAsync();
        try
        {
            await EditorFileAccess.Install(other);
            await Install(other);
            await Task.WhenAll(page.GotoAsync(site), other.GotoAsync(site));
            await other.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true }).WaitForAsync();
        }
        finally { await other.CloseAsync(); }
        var prompt = page.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true });
        await prompt.WaitForAsync();
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const record = await window.__migration.get('freedf-editor', 'recovery', 'current');
              return record?.name === 'migration.pdf' && record.payload instanceof Blob && record.payload.size > 0;
            }
            """), "Concurrent tabs copied the previous recovery payload into the new database.");
        await page.EvaluateAsync("sessionStorage.setItem('storage-fault', 'freedf-file-handles')");
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('simulated migration quota failure')");
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const m = window.__migration;
              return !!(await m.get('adobye-file-handles', 'handles', 'current')) &&
                !(await m.get('freedf-file-handles', 'handles', 'current'));
            }
            """), "A failed file-handle migration does not erase the previous handle.");
        await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
        await page.GotoAsync(seed);
        await page.EvaluateAsync("sessionStorage.removeItem('storage-fault')");
        await page.GotoAsync(site);
        await prompt.WaitForAsync();
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }).ClickAsync();
        await prompt.WaitForAsync(new() { State = WaitForSelectorState.Detached });
        var resumed = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const record = await window.__migration.get('freedf-file-handles', 'handles', 'current');
              const handle = await (await navigator.storage.getDirectory()).getFileHandle('migration.pdf');
              const state = await (await pdfHost.request('/api/state')).json();
              return { sameHandle: record?.name === 'migration.pdf' && await record.handle.isSameEntry(handle), rotation: state.pages[0].rotation };
            }
            """);
        Check(resumed.GetProperty("sameHandle").GetBoolean() && resumed.GetProperty("rotation").GetInt32() == 90,
            "Resume preserves the real file handle and the unsaved page rotation: " + resumed);
        await page.Locator("#recovery").ClickAsync();
        var warning = page.GetByRole(AriaRole.Dialog, new() { Name = "Clear local recovery", Exact = true });
        Check((await warning.InnerTextAsync()).Contains("this and previous versions", StringComparison.Ordinal),
            "The recovery-clear confirmation explains that it affects both generations.");
        await warning.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await page.Keyboard.PressAsync("Control+s");
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.includes('Saved to migration.pdf')");
        Check(await page.EvaluateAsync<bool>("""
            async () => (await (await (await navigator.storage.getDirectory()).getFileHandle('migration.pdf')).getFile()).size > 0
            """), "The migrated handle can save to the original file.");

        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const m = window.__migration, root = await navigator.storage.getDirectory();
              const previous = await m.get('adobye-file-handles', 'handles', 'current');
              const unrelated = await root.getFileHandle('unrelated.pdf', { create: true });
              await m.put('freedf-file-handles', 'handles', 'current', { handle: unrelated, name: unrelated.name });
              const files = await window.__editorImport('fileaccess.js');
              let status = '';
              const access = files.createFileAccess({
                prepare: async () => ({ blob: await (await pdfHost.request('/api/export/pdf?name=migration.pdf')).blob(), version: 0 }),
                saved: async () => {}, status: text => { status = text; }, suggestedName: () => 'migration.pdf',
                error: error => { throw error; }, download: () => { throw new Error('Unexpected download fallback.'); },
              });
              if (await access.restore('missing.pdf')) return false;
              if ((await m.get('freedf-file-handles', 'handles', 'current'))?.name !== 'unrelated.pdf' ||
                  (await m.get('adobye-file-handles', 'handles', 'current'))?.name !== 'migration.pdf') return false;
              if (await access.restore('migration.pdf') || access.handle || !status.includes('Save as')) return false;
              const oldFile = await previous.handle.createWritable();
              await oldFile.write('legacy contents');
              await oldFile.close();
              const calls = window.__fa.saveCalls;
              window.__fa.nextName = 'chosen/migration.pdf';
              await access.save();
              const chosen = await (await root.getDirectoryHandle('chosen')).getFileHandle('migration.pdf');
              if (window.__fa.saveCalls !== calls + 1 || await (await previous.handle.getFile()).text() !== 'legacy contents' ||
                  (await unrelated.getFile()).size !== 0 ||
                  (await chosen.getFile()).size === 0 ||
                  !await (await m.get('freedf-file-handles', 'handles', 'current')).handle.isSameEntry(chosen) ||
                  (await m.get('adobye-file-handles', 'handles', 'current')).name !== 'migration.pdf') return false;

              const first = await (await root.getDirectoryHandle('first', { create: true })).getFileHandle('same.pdf', { create: true });
              const second = await (await root.getDirectoryHandle('second', { create: true })).getFileHandle('same.pdf', { create: true });
              await m.put('freedf-file-handles', 'handles', 'current', { handle: first, name: first.name });
              await m.put('adobye-file-handles', 'handles', 'current', { handle: second, name: second.name });
              return !await access.restore('same.pdf') && !access.handle &&
                await (await m.get('freedf-file-handles', 'handles', 'current')).handle.isSameEntry(first) &&
                await (await m.get('adobye-file-handles', 'handles', 'current')).handle.isSameEntry(second);
            }
            """), "Conflicting handles, including same-name files, are preserved and require an explicit Save as.");

        await page.EvaluateAsync("sessionStorage.setItem('storage-fault', 'freedf-local-fonts')");
        var fontError = await page.EvaluateAsync<string>("""
            async () => {
              try {
                const fonts = await window.__editorImport('localfonts.js');
                await fonts.createLocalFonts({ upload: async () => {} }).cached();
                return '';
              } catch (error) { return error.message; }
            }
            """);
        Check(fontError.Contains("simulated migration quota failure", StringComparison.Ordinal) &&
            await page.EvaluateAsync<bool>("async () => !!(await window.__migration.get('adobye-local-fonts', 'metadata', 'families'))"),
            "A font metadata migration failure is exposed and preserves the previous cache.");
        await page.EvaluateAsync("sessionStorage.removeItem('storage-fault')");
        Check(await page.EvaluateAsync<string>("""
            async () => {
              const fonts = await window.__editorImport('localfonts.js');
              return (await fonts.createLocalFonts({ upload: async () => {} }).cached())[0].family;
            }
            """) == "Legacy Font", "Installed-font metadata is migrated without storing font bytes.");

        await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
        await page.GotoAsync(seed);
        await page.EvaluateAsync("""
            async () => {
              const m = window.__migration, current = await m.get('freedf-editor', 'recovery', 'current');
              await m.put('freedf-editor', 'recovery', 'current', { ...current, name: 'preferred.pdf' });
              await m.put('adobye-editor', 'recovery', 'current', { ...current, name: 'older.pdf' });
              const root = await navigator.storage.getDirectory();
              const newer = await root.getFileHandle('preferred.pdf', { create: true });
              const older = await root.getFileHandle('older.pdf', { create: true });
              await m.put('freedf-file-handles', 'handles', 'current', { handle: newer, name: newer.name });
              await m.put('adobye-file-handles', 'handles', 'current', { handle: older, name: older.name });
              await m.put('freedf-local-fonts', 'metadata', 'families', { format: 1, families: [{ family: 'Preferred Font', styles: [] }] });
              for (const db of ['freedf-editor', 'freedf-file-handles', 'freedf-local-fonts'])
                localStorage.removeItem('freedf-storage-migrated:' + db);
            }
            """);
        await page.GotoAsync(site);
        await prompt.WaitForAsync();
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const m = window.__migration, storage = await window.__editorImport('storage.js');
              await storage.migrateLegacyStorage(window, { legacy: 'adobye-file-handles', current: 'freedf-file-handles', store: 'handles', key: 'current' });
              const fonts = await window.__editorImport('localfonts.js');
              return (await m.get('freedf-editor', 'recovery', 'current')).name === 'preferred.pdf' &&
                (await m.get('adobye-editor', 'recovery', 'current')).name === 'older.pdf' &&
                (await m.get('freedf-file-handles', 'handles', 'current')).name === 'preferred.pdf' &&
                (await m.get('adobye-file-handles', 'handles', 'current')).name === 'older.pdf' &&
                (await fonts.createLocalFonts({ upload: async () => {} }).cached())[0].family === 'Preferred Font';
            }
            """), "Existing FreeDF records are never overwritten by older records.");
        await page.EvaluateAsync("""
            async () => {
              const m = window.__migration, previous = await m.get('adobye-editor', 'recovery', 'current');
              await m.put('adobye-editor', 'recovery', 'current', { ...previous, name: 'later-old-version.pdf', updatedAt: Date.now() });
            }
            """);
        await page.GotoAsync(seed);
        await page.GotoAsync(site);
        await prompt.WaitForAsync();
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const m = window.__migration;
              return (await m.get('freedf-editor', 'recovery', 'current')).name === 'preferred.pdf' &&
                (await m.get('adobye-editor', 'recovery', 'current')).name === 'later-old-version.pdf';
            }
            """), "A later previous-version edit remains separate and does not overwrite the FreeDF record.");
        Check((await prompt.InnerTextAsync()).Contains("this and previous versions", StringComparison.Ordinal),
            "The discard prompt explains that it clears recovery data from both generations.");
        await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const m = window.__migration;
              return !(await m.get('freedf-editor', 'recovery', 'current')) &&
                !(await m.get('adobye-editor', 'recovery', 'current')) &&
                !(await m.get('freedf-file-handles', 'handles', 'current')) &&
                !(await m.get('adobye-file-handles', 'handles', 'current'));
            }
            """), "Discard clears both generations of recovery data and file handles.");
        await page.GotoAsync(seed);
        await page.EvaluateAsync("""
            () => {
              localStorage.removeItem('freedf-storage-migrated:freedf-editor');
              localStorage.removeItem('freedf-storage-migrated:freedf-file-handles');
            }
            """);
        await page.GotoAsync(site);
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
        Check(await prompt.CountAsync() == 0, "Cleared data cannot be resurrected when migration markers are removed.");
        Console.WriteLine("PASS: legacy recovery, real file handles and font metadata migrate without overwriting newer records; quota failures retain data; discard clears both generations without resurrection.");
    }
}
