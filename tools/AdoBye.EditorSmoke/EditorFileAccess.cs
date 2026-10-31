using System.Buffers.Binary;
using Microsoft.Playwright;

// Chromium smoke for File System Access saving and Local Font Access embedding. Native pickers are replaced with real
// origin-private-file-system handles (so they structured-clone into IndexedDB) wrapped to simulate permission and disk failures.
static class EditorFileAccess
{
    const string Stubs = """
        (() => {
          const s = window.__fa = { saveCalls: 0, openCalls: 0, requests: 0, permission: 'granted', request: 'granted', fail: null, cancel: false,
            nextName: null, openName: 'opened.pdf', lastOptions: null, lastSuggested: null, fontQueries: [], fontMode: 'granted', fonts: [] };
          if (sessionStorage.getItem('fa-unsupported') === '1') {
            for (const name of ['showSaveFilePicker', 'showOpenFilePicker', 'queryLocalFonts']) Object.defineProperty(window, name, { value: undefined, configurable: true });
            return;
          }
          const root = () => navigator.storage.getDirectory();
          const dirOf = async (path, create) => { let dir = await root(); const parts = path.split('/'); for (const p of parts.slice(0, -1)) dir = await dir.getDirectoryHandle(p, { create }); return [dir, parts.at(-1)]; };
          const decorate = handle => {
            handle.queryPermission = async () => s.permission;
            handle.requestPermission = async () => { s.requests++; s.permission = s.request; return s.request; };
            const createWritable = handle.createWritable.bind(handle);
            handle.createWritable = async options => {
              s.lastOptions = options;
              const stream = await createWritable(options);
              if (s.fail) { const fail = s.fail; stream.write = async () => { throw new DOMException(fail.message, fail.name); }; }
              return stream;
            };
            return handle;
          };
          const b64 = buffer => { const b = new Uint8Array(buffer); let t = ''; for (let i = 0; i < b.length; i += 8192) t += String.fromCharCode(...b.subarray(i, i + 8192)); return btoa(t); };
          s.b64 = b64;
          window.showSaveFilePicker = async options => {
            s.saveCalls++; s.lastSuggested = options.suggestedName;
            if (s.cancel) throw new DOMException('cancelled', 'AbortError');
            const [dir, name] = await dirOf(s.nextName ?? options.suggestedName, true);
            return decorate(await dir.getFileHandle(name, { create: true }));
          };
          window.showOpenFilePicker = async () => { s.openCalls++; const [dir, name] = await dirOf(s.openName, false); return [decorate(await dir.getFileHandle(name))]; };
          s.seed = async (path, data) => {
            const [dir, name] = await dirOf(path, true), w = await (await dir.getFileHandle(name, { create: true })).createWritable();
            await w.write(Uint8Array.from(atob(data), c => c.charCodeAt(0))); await w.close();
          };
          s.read = async path => { const [dir, name] = await dirOf(path, false); return b64(await (await (await dir.getFileHandle(name)).getFile()).arrayBuffer()); };
          s.exportPdf = async () => b64(await (await pdfHost.request('/api/export/pdf?name=x.pdf')).arrayBuffer());
          s.stored = async () => {
            if (!(await indexedDB.databases()).some(d => d.name === 'adobye-file-handles')) return null;
            const db = await new Promise((resolve, reject) => { const r = indexedDB.open('adobye-file-handles', 1); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });
            try {
              const v = await new Promise((resolve, reject) => { const tx = db.transaction('handles'), r = tx.objectStore('handles').get('current'); tx.oncomplete = () => resolve(r.result); tx.onabort = () => reject(tx.error); });
              return v ? { name: v.name, isHandle: v.handle instanceof FileSystemFileHandle, keys: Object.keys(v).sort().join(',') } : null;
            } finally { db.close(); }
          };
          s.drop = async (kind, path) => {
            let handle;
            if (kind === 'file') { const [dir, name] = await dirOf(path, false); handle = decorate(await dir.getFileHandle(name)); }
            else handle = await (await root()).getDirectoryHandle(path);
            const event = new Event('drop', { bubbles: true, cancelable: true });
            Object.defineProperty(event, 'dataTransfer', { value: { files: [new File([], 'dropped')], items: [{ kind: 'file', getAsFileSystemHandle: async () => handle }], types: ['Files'] } });
            document.body.dispatchEvent(event);
          };
          window.queryLocalFonts = async options => {
            s.fontQueries.push(options ?? null);
            if (s.fontMode !== 'granted') throw new DOMException('denied', 'NotAllowedError');
            const wanted = options?.postscriptNames;
            return s.fonts.filter(f => !wanted || wanted.includes(f.postscriptName)).map(f => ({ ...f, blob: async () => new Blob([Uint8Array.from(atob(f.data), c => c.charCodeAt(0))]) }));
          };
        })();
        """;

    static void Check(bool success, string message) { if (!success) throw new Exception("FileAccess: " + message); }
    static string Sample => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "03-typography-embedded.pdf")));
    static byte[] FontBytes => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "fixtures", "fonts", "SourceCodePro-Regular.otf"));

    public static bool ExpectedError(string error) =>
        error.Contains("Could not write") || error.Contains("was revoked") || error.Contains("Permission to read installed fonts");

    public static Task Install(IPage page) => page.AddInitScriptAsync(Stubs);

    static Task<string> Status(IPage page) => page.EvaluateAsync<string>("document.getElementById('status').textContent");
    static Task WaitStatus(IPage page, string text) => page.WaitForFunctionAsync("t => document.getElementById('status').textContent.includes(t)", text);
    static Task<T> Fa<T>(IPage page, string expression) => page.EvaluateAsync<T>("() => " + expression.Replace("$fa", "window.__fa"));
    static Task Ready(IPage page) => page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");

    static async Task Mutate(IPage page)
    {
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Insert" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Blank page after current" }).ClickAsync();
        await EditorHeader.State(page, true);
    }

    static async Task Menu(IPage page, string item)
    {
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = item }).ClickAsync();
    }

    static async Task<bool> Same(IPage page, string file) =>
        await Fa<string>(page, "$fa.read('" + file + "')") == await Fa<string>(page, "$fa.exportPdf()");

    public static async Task Run(IPage page)
    {
        EventHandler<IDialog> accept = async (_, dialog) => await dialog.AcceptAsync();
        page.Dialog += accept;
        try
        {
            await Fa<object?>(page, "$fa.seed('opened.pdf', '" + Sample + "')");
            await Menu(page, "Open");
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'opened.pdf' && document.getElementById('base')?.naturalWidth > 0");
            Check(await Fa<int>(page, "$fa.openCalls") == 1, "File > Open uses showOpenFilePicker.");
            var stored = await Fa<System.Text.Json.JsonElement>(page, "$fa.stored()");
            Check(stored.GetProperty("isHandle").GetBoolean() && stored.GetProperty("keys").GetString() == "handle,name" && stored.GetProperty("name").GetString() == "opened.pdf", "Only a FileSystemFileHandle and its name are stored.");
            await EditorHeader.State(page, false);

            await Mutate(page);
            await page.Keyboard.PressAsync("Control+s");
            await WaitStatus(page, "Saved to opened.pdf");
            await EditorHeader.State(page, false);
            Check(await Fa<int>(page, "$fa.saveCalls") == 0, "Save overwrites the opened file without a picker.");
            Check(!await Fa<bool>(page, "$fa.lastOptions.keepExistingData"), "The swap-file write replaces content (keepExistingData: false).");
            Check(await Same(page, "opened.pdf"), "Saved file bytes equal the editor output.");
            var good = await Fa<string>(page, "$fa.read('opened.pdf')");

            await Mutate(page);
            await Fa<object?>(page, "($fa.permission = 'prompt', $fa.request = 'denied', 0)");
            var download = await page.RunAndWaitForDownloadAsync(() => page.Keyboard.PressAsync("Control+s"));
            Check(download.SuggestedFilename == "opened.pdf", "Denied permission falls back to a download.");
            Check(await Fa<string>(page, "$fa.read('opened.pdf')") == good, "Denied permission leaves the original file untouched.");
            await WaitStatus(page, "downloaded instead");
            Check(await Fa<int>(page, "$fa.requests") == 1, "Permission is re-requested on the Save gesture.");

            await Fa<object?>(page, "($fa.permission = 'granted', $fa.request = 'granted', 0)");
            await Mutate(page);
            await Fa<object?>(page, "($fa.fail = { name: 'QuotaExceededError', message: 'disk full' }, 0)");
            await page.Keyboard.PressAsync("Control+s");
            await WaitStatus(page, "Could not write opened.pdf: disk full");
            await EditorHeader.State(page, true);
            Check(await Fa<string>(page, "$fa.read('opened.pdf')") == good, "A failed write is atomic: the original is untouched.");
            await Fa<object?>(page, "($fa.fail = { name: 'NotAllowedError', message: 'revoked' }, 0)");
            await page.Keyboard.PressAsync("Control+s");
            await WaitStatus(page, "was revoked");
            await EditorHeader.State(page, true);
            await Fa<object?>(page, "($fa.fail = null, 0)");
            await page.Keyboard.PressAsync("Control+s");
            await WaitStatus(page, "Saved to opened.pdf");
            await EditorHeader.State(page, false);
            Check(await Same(page, "opened.pdf"), "Retry after a failure saves the unsaved edits intact.");

            await Fa<object?>(page, "($fa.cancel = true, 0)");
            await Menu(page, "Save as");
            await WaitStatus(page, "Save cancelled.");
            Check(await Fa<int>(page, "$fa.saveCalls") == 1, "Save as asks the picker.");
            await Fa<object?>(page, "($fa.cancel = false, $fa.nextName = 'copy.pdf', 0)");
            await Menu(page, "Save as");
            await WaitStatus(page, "Saved to copy.pdf");
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'copy.pdf'");
            Check(await Same(page, "copy.pdf"), "Save as writes the picked file.");
            Check((await Fa<System.Text.Json.JsonElement>(page, "$fa.stored()")).GetProperty("name").GetString() == "copy.pdf", "The new handle replaces the stored one.");
            await Mutate(page);
            await page.Keyboard.PressAsync("Control+s");
            await WaitStatus(page, "Saved to copy.pdf");
            Check(await Fa<int>(page, "$fa.saveCalls") == 2 && await Same(page, "copy.pdf"), "Later saves overwrite the picked file.");

            await Mutate(page);
            await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
            await page.ReloadAsync();
            var prompt = page.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true });
            await prompt.WaitForAsync();
            Check((await Fa<System.Text.Json.JsonElement>(page, "$fa.stored()")).GetProperty("name").GetString() == "copy.pdf", "The handle survives a reload while a recovery record exists.");
            await prompt.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }).ClickAsync();
            await prompt.WaitForAsync(new() { State = WaitForSelectorState.Detached });
            await Ready(page);
            await EditorHeader.State(page, true);
            await page.Keyboard.PressAsync("Control+s");
            await WaitStatus(page, "Saved to copy.pdf");
            await EditorHeader.State(page, false);
            Check(await Fa<int>(page, "$fa.saveCalls") == 0 && await Same(page, "copy.pdf"), "Resume re-attaches the stored handle: Save overwrites copy.pdf without a picker.");

            await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
            await page.ReloadAsync();
            await prompt.WaitForAsync();
            await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
            await Ready(page);
            Check(await Fa<object?>(page, "$fa.stored()") is null, "Discard clears the stored handle.");

            await Fa<object?>(page, "($fa.nextName = 'fresh.pdf', 0)");
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "plain.pdf", MimeType = "application/pdf", Buffer = Convert.FromBase64String(Sample) });
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'plain.pdf' && document.getElementById('base')?.naturalWidth > 0");
            Check(await Fa<object?>(page, "$fa.stored()") is null, "A file opened without a handle has none stored.");
            await Mutate(page);
            await page.Keyboard.PressAsync("Control+s");
            await WaitStatus(page, "Saved to fresh.pdf");
            Check(await Fa<int>(page, "$fa.saveCalls") == 1 && await Same(page, "fresh.pdf"), "Saving a handle-less document picks a file.");
            await Menu(page, "Close document");
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'No document'");
            Check(await Fa<object?>(page, "$fa.stored()") is null, "Closing the document clears the handle.");

            await Fa<object?>(page, "$fa.seed('dropped.pdf', '" + Sample + "')");
            await Fa<object?>(page, "$fa.drop('file', 'dropped.pdf')");
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'dropped.pdf' && document.getElementById('base')?.naturalWidth > 0");
            Check((await Fa<System.Text.Json.JsonElement>(page, "$fa.stored()")).GetProperty("name").GetString() == "dropped.pdf", "A dropped file handle is adopted for Save.");
            await Menu(page, "Close document");
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'No document'");
            await Fa<object?>(page, "$fa.seed('folder/b.pdf', '" + Sample + "')");
            await Fa<object?>(page, "$fa.seed('folder/a.pdf', '" + Sample + "')");
            await Fa<object?>(page, "$fa.seed('folder/readme.txt', 'aGk=')");
            await Fa<object?>(page, "$fa.drop('directory', 'folder')");
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'a.pdf' && document.getElementById('base')?.naturalWidth > 0");
            Check(await Fa<object?>(page, "$fa.stored()") is null, "Dropping a folder opens its PDFs in name order and stores no handle.");

            await page.EvaluateAsync("sessionStorage.setItem('fa-unsupported', '1')");
            await page.ReloadAsync();
            try { await prompt.WaitForAsync(new() { Timeout = 5000 }); await prompt.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync(); } catch (TimeoutException) { }
            await Ready(page);
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "fallback.pdf", MimeType = "application/pdf", Buffer = Convert.FromBase64String(Sample) });
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'fallback.pdf' && document.getElementById('base')?.naturalWidth > 0");
            await Mutate(page);
            await page.Keyboard.PressAsync("Control+s");
            await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).WaitForAsync();
            var fallback = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
            Check(fallback.SuggestedFilename == "fallback.pdf", "Browsers without the API keep the download fallback.");
            await EditorHeader.State(page, false);
            await page.EvaluateAsync("sessionStorage.removeItem('fa-unsupported')");
            Console.WriteLine("PASS: in-place Save without a picker, byte-identical writes, keepExistingData false, handle-only persistence, denied-permission download fallback, atomic failed/revoked writes with visible errors, Save as and cancel, recovery Resume re-attach and Discard clear, drop of file/folder handles, Close clears, unsupported-browser fallback.");
        }
        finally { page.Dialog -= accept; }
    }

    static byte[] WithFsType(byte[] font, int fsType)
    {
        var copy = (byte[])font.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(FontSubsetterOs2(copy) + 8), (ushort)fsType);
        return copy;
    }

    static int FontSubsetterOs2(byte[] font)
    {
        var count = font[4] << 8 | font[5];
        for (var i = 0; i < count; i++)
            if (System.Text.Encoding.ASCII.GetString(font, 12 + i * 16, 4) == "OS/2") return (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(12 + i * 16 + 8));
        throw new InvalidDataException("No OS/2 table.");
    }

    public static async Task RunFonts(IPage page)
    {
        EventHandler<IDialog> accept = async (_, dialog) => await dialog.AcceptAsync();
        page.Dialog += accept;
        try
        {
            var good = FontBytes;
            await page.ReloadAsync();
            var discard = page.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true });
            try { await discard.WaitForAsync(new() { Timeout = 5000 }); await discard.ClickAsync(); } catch (TimeoutException) { }
            await Ready(page);
            var restricted = Convert.ToBase64String(WithFsType(good, FontLicense2.Restricted));
            await Fa<object?>(page, "($fa.fonts = [{ family: 'Restricted Demo', fullName: 'Restricted Demo Regular', postscriptName: 'Restricted-Regular', style: 'Regular', data: '" + restricted + "' }, { family: 'Source Code Pro', fullName: 'Source Code Pro Regular', postscriptName: 'SourceCodePro-Regular', style: 'Regular', data: '" + Convert.ToBase64String(good) + "' }], $fa.fontMode = 'denied', 0)");
            await Menu(page, "New");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
            await page.Locator("#base").WaitForAsync();
            await page.Locator("[data-tool=text]").ClickAsync();
            var box = (await page.Locator("#layer").BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + 60, box.Y + 60);
            await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(box.X + 240, box.Y + 100); await page.Mouse.UpAsync();
            var text = page.Locator("#layer [contenteditable='plaintext-only']");
            await text.WaitForAsync();
            await text.FillAsync("Local font text");
            await text.PressAsync("Escape");
            await page.Locator("#layer .it").First.ClickAsync();
            await page.Locator("#fpb").ClickAsync();
            var picker = page.Locator(".fpop");
            await picker.GetByRole(AriaRole.Button, new() { Name = "Installed fonts on this device…" }).ClickAsync();
            await picker.Locator(".fmsg").Filter(new() { HasTextString = "Permission to read installed fonts was not granted" }).WaitForAsync();
            Check(await picker.Locator(".fo[data-f=Helvetica]").CountAsync() == 1, "Denied permission leaves the standard families available.");

            await Fa<object?>(page, "($fa.fontMode = 'granted', 0)");
            await picker.GetByRole(AriaRole.Button, new() { Name = "Installed fonts on this device…" }).ClickAsync();
            await picker.Locator(".flo").First.WaitForAsync();
            Check(await picker.Locator(".flo").CountAsync() == 2, "Both installed families are listed.");
            await picker.Locator("input[type=search]").FillAsync("source");
            Check(await picker.Locator(".flo").CountAsync() == 1, "Search filters installed fonts.");
            await picker.Locator("input[type=search]").FillAsync("");
            await picker.Locator(".flo[data-local='Restricted Demo']").HoverAsync();
            Check((await picker.Locator(".fprev").EvaluateAsync<string>("e => e.style.fontFamily")).Contains("Restricted Demo"), "Hover previews the font.");

            await picker.Locator(".flo[data-local='Restricted Demo']").ClickAsync();
            await picker.Locator(".fmsg").Filter(new() { HasTextString = "cannot be embedded" }).WaitForAsync();
            Check((await picker.Locator(".fmsg").TextContentAsync())!.Contains("restricts embedding"), "The refusal explains the license restriction.");
            Check(!await page.EvaluateAsync<bool>("async () => (await (await pdfHost.request('/api/fonts')).json()).some(f => f.family === 'Source Code Pro')"), "A refused font never enters the engine.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Use Courier instead" }).ClickAsync();
            await page.WaitForFunctionAsync("document.querySelector('#fpb b')?.textContent === 'Courier'");

            await page.Locator("#fpb").ClickAsync();
            picker = page.Locator(".fpop");
            await picker.Locator(".flo").First.WaitForAsync();
            var queries = await Fa<int>(page, "$fa.fontQueries.length");
            Check(await picker.Locator(".flo").CountAsync() == 2, "Cached metadata lists installed fonts without a new query.");
            Check(await Fa<int>(page, "$fa.fontQueries.length") == queries, "No Local Font Access query for cached metadata.");
            await picker.Locator(".flo[data-local='Source Code Pro']").ClickAsync();
            await page.WaitForFunctionAsync("document.querySelector('#fpb b')?.textContent === 'Source Code Pro'");
            Check(await Fa<string>(page, "JSON.stringify($fa.fontQueries.at(-1))") == "{\"postscriptNames\":[\"SourceCodePro-Regular\"]}", "Only the chosen face's bytes are requested.");
            var cache = await page.EvaluateAsync<string>("""
                async () => {
                  const db = await new Promise(r => { const q = indexedDB.open('adobye-local-fonts', 1); q.onsuccess = () => r(q.result); });
                  const v = await new Promise(r => { const tx = db.transaction('metadata'), g = tx.objectStore('metadata').get('families'); tx.oncomplete = () => r(g.result); });
                  db.close(); return JSON.stringify(v);
                }
                """);
            Check(cache.Contains("SourceCodePro-Regular") && cache.Length < 1000 && !cache.Contains("data"), "Only font metadata is cached, never bytes.");

            for (var attempt = 0; attempt < 50 && !await page.EvaluateAsync<bool>("async () => (await (await pdfHost.request('/api/page/0/items')).json()).some(i => i.font === 'Source Code Pro')"); attempt++)
                await page.WaitForTimeoutAsync(200);
            var exported = await page.EvaluateAsync<string>("async () => { const b = new Uint8Array(await (await pdfHost.request('/api/export/pdf?name=x.pdf')).arrayBuffer()); let t = ''; for (const x of b) t += String.fromCharCode(x); return t; }");
            Check(exported.Contains("SourceCodePro") && exported.Contains("/FontFile3"), $"The chosen local font is embedded ({await page.EvaluateAsync<string>("async () => JSON.stringify(await (await pdfHost.request('/api/page/0/items')).json())")} :: {exported.Replace("\0", "")}).");
            Check(exported.Length < good.Length, "It is embedded as a subset, not the full font file.");

            await page.EvaluateAsync("sessionStorage.setItem('fa-unsupported', '1')");
            await page.ReloadAsync();
            var resume = page.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true });
            try { await resume.WaitForAsync(new() { Timeout = 5000 }); await resume.ClickAsync(); } catch (TimeoutException) { }
            await Ready(page);
            await Menu(page, "New");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
            await page.Locator("#base").WaitForAsync();
            await page.Locator("[data-tool=text]").ClickAsync();
            box = (await page.Locator("#layer").BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + 60, box.Y + 60);
            await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(box.X + 240, box.Y + 100); await page.Mouse.UpAsync();
            await text.WaitForAsync(); await text.FillAsync("Fallback"); await text.PressAsync("Escape");
            await page.Locator("#layer .it").First.ClickAsync();
            await page.Locator("#fpb").ClickAsync();
            await page.Locator(".fpop .fmsg").Filter(new() { HasTextString = "cannot list installed fonts" }).WaitForAsync();
            Check(await page.Locator(".fpop .fo[data-f=Times]").CountAsync() == 1, "Unsupported browsers keep the standard families.");
            await page.EvaluateAsync("sessionStorage.removeItem('fa-unsupported')");
            Console.WriteLine("PASS: Local Font Access permission denial, search, preview, cached metadata (no bytes), fsType refusal with substitute, subset embedding of the chosen local font, unsupported-browser fallback.");
        }
        finally { page.Dialog -= accept; }
    }

    static class FontLicense2 { public const int Restricted = 0x0002; }
}
