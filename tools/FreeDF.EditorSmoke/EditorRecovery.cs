using Microsoft.Playwright;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

static class EditorRecovery
{
    const string Read = """
        async () => {
          const db = await new Promise((resolve,reject) => { const r=indexedDB.open('freedf-editor',1); r.onsuccess=()=>resolve(r.result); r.onerror=()=>reject(r.error); });
          try { return await new Promise((resolve,reject) => { const tx=db.transaction('recovery'), r=tx.objectStore('recovery').get('current'); tx.oncomplete=()=>resolve(r.result ? {updatedAt:r.result.updatedAt, size:r.result.payload.size} : null); tx.onabort=()=>reject(tx.error); }); }
          finally { db.close(); }
        }
        """;
    static void Check(bool success, string message) { if (!success) throw new Exception("Recovery: " + message); }
    static Task Durable(IPage page) => page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
    static Task Ready(IPage page) => page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
    static Task<int> Images(IPage page) => page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/page/0/items')).json()).filter(i=>i.type==='image').length");

    public static async Task Run(IPage page)
    {
        EventHandler<IDialog> accept = async (_, dialog) => await dialog.AcceptAsync();
        page.Dialog += accept;
        try
        {
            var source = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "03-typography-embedded.pdf"));
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "local-recovery.pdf", MimeType = "application/pdf", Buffer = source });
            await page.WaitForFunctionAsync("document.getElementById('name').textContent === 'local-recovery.pdf' && document.getElementById('base')?.naturalWidth > 0");
            if (await page.Locator("#pn").InputValueAsync() != "1") await page.Locator(".thumb").First.ClickAsync();
            await page.Locator("#layer .span").First.ClickAsync();
            var original = page.Locator("[data-original-text]");
            var changed = await original.InputValueAsync() + " recovery";
            await original.FillAsync(changed);
            await page.WaitForFunctionAsync("async t => (await (await pdfHost.request('/api/page/0/items')).json()).some(i=>i.text===t)", changed);
            await page.Locator("#img").SetInputFilesAsync(new FilePayload {
                Name = "recovered.png", MimeType = "image/png", Buffer = PngWriter.Encode(2, 2, Enumerable.Repeat(new byte[] { 250, 20, 40, 255 }, 4).SelectMany(x => x).ToArray()) });
            await page.Locator("#layer .it:has(img)").WaitForAsync();
            await page.Locator("#layer .it:has(img)").ClickAsync();
            await page.Keyboard.PressAsync("Control+c");
            await page.Keyboard.PressAsync("Delete");
            await page.WaitForFunctionAsync("!document.querySelector('#layer .it img')");
            await page.Keyboard.PressAsync("Control+z");
            await page.WaitForFunctionAsync("document.querySelectorAll('#layer .it img').length === 1");
            await page.Keyboard.PressAsync("Control+z");
            await page.WaitForFunctionAsync("!document.querySelector('#layer .it img')");
            await Durable(page);
            Check(await Images(page) == 0, "Undone image is retained only by clipboard/redo.");
            await page.WaitForFunctionAsync("document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
            var raster = await EditorTextSelection.BasePixelHash(page);
            var state = await page.EvaluateAsync<string>("async () => JSON.stringify(await (await pdfHost.request('/api/page/0/items')).json())");
            var retained = await page.EvaluateAsync<string>("async () => JSON.stringify(await (" + Read + ")())");
            Check(retained != "null", "Saved means the IndexedDB transaction completed.");
            await page.ReloadAsync();
            var prompt = page.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true });
            await prompt.WaitForAsync();
            Check(await page.Locator("#base").CountAsync() == 0, "Reload does not reveal the PDF before choosing Resume.");
            Check(await page.EvaluateAsync<int>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount") == 0, "Startup has not opened a PDF.");
            await page.WaitForTimeoutAsync(1800);
            Check(retained == await page.EvaluateAsync<string>("async () => JSON.stringify(await (" + Read + ")())"), "Blank startup never overwrites the retained edit.");
            await prompt.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }).ClickAsync();
            await prompt.WaitForAsync(new() { State = WaitForSelectorState.Detached });
            await Ready(page);
            await page.WaitForFunctionAsync("document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl");
            Check(raster == await EditorTextSelection.BasePixelHash(page), "Resume retains exact original-font/geometry raster fidelity.");
            Check(state == await page.EvaluateAsync<string>("async () => JSON.stringify(await (await pdfHost.request('/api/page/0/items')).json())"), "Resume retains editable original-text replacement data, not a flattened PDF.");
            Check(await page.EvaluateAsync<bool>("async () => (await (await pdfHost.request('/api/state')).json()).dirty"), "Unsaved state remains unsaved.");
            await EditorHeader.State(page, true);
            await page.Keyboard.PressAsync("Control+Shift+z");
            await page.WaitForFunctionAsync("document.querySelectorAll('#layer .it img').length === 1");
            await page.Keyboard.PressAsync("Control+Shift+z");
            await page.WaitForFunctionAsync("!document.querySelector('#layer .it img')");
            await page.Keyboard.PressAsync("Control+z");
            await page.WaitForFunctionAsync("document.querySelectorAll('#layer .it img').length === 1");
            await page.Keyboard.PressAsync("Control+v");
            await page.WaitForFunctionAsync("document.querySelectorAll('#layer .it img').length === 2");
            await Durable(page);
            Check(await Images(page) == 2, "Persisted redo, undo and clipboard paste after resume retain the actual image bytes.");
            await page.WaitForFunctionAsync("[...document.querySelectorAll('#layer .it img')].every(i=>i.naturalWidth===2)");
            await page.EvaluateAsync("""
                () => {
                  window.recoveryCalls=0;
                  const request=pdfHost.request.bind(pdfHost);
                  pdfHost.request=async (url,options)=>{ if(url==='/api/recovery') recoveryCalls++; return request(url,options); };
                }
                """);
            for (var i = 0; i < 20; i++) await page.Mouse.MoveAsync(900 + i, 700);
            await page.WaitForTimeoutAsync(1800);
            Check(await page.EvaluateAsync<int>("recoveryCalls") == 0, "Pointer movement alone never serializes or saves the PDF.");
            await page.ReloadAsync();
            await page.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true }).WaitForAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
            await Ready(page);
            Check(await page.EvaluateAsync<object?>(Read) is null && await page.Locator("#base").CountAsync() == 0, "Discard clears the durable record and leaves a blank editor.");
            await page.EvaluateAsync("""
                async () => {
                  const db=await new Promise(r=>{const q=indexedDB.open('freedf-editor',1);q.onsuccess=()=>r(q.result);});
                  await new Promise((resolve,reject)=>{const tx=db.transaction('recovery','readwrite');tx.objectStore('recovery').put({format:99,payload:new Blob(['corrupt'])},'current');tx.oncomplete=resolve;tx.onabort=()=>reject(tx.error);});
                  db.close();
                }
                """);
            await page.ReloadAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("document.querySelector('[data-recovery-error]').textContent.includes('corrupt')");
            Check(await page.EvaluateAsync<object?>(Read) is not null && await page.Locator("#base").CountAsync() == 0, "Corruption is visible without deleting the retained record or opening a document.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Discard and clear", Exact = true }).ClickAsync();
            await Ready(page);
            var protectedSource = PdfEditor.Open(source).Save(options: new PdfWriteOptions {
                Encryption = new PdfEncryption { OwnerPassword = "never-stored", Method = PdfEncryptionMethod.Rc4_40 } });
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "protected.pdf", MimeType = "application/pdf", Buffer = protectedSource });
            await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery off (encrypted)' || document.getElementById('status').textContent.includes('Encrypted PDFs require the server editor')");
            Check(await page.EvaluateAsync<object?>(Read) is null, "Encrypted input exposes a safe limitation and never persists decrypted bytes.");
            await page.EvaluateAsync("""
                () => {
                  window.originalRecoveryPut=IDBObjectStore.prototype.put;
                  IDBObjectStore.prototype.put=function(...args) {
                    if(this.name==='recovery') throw new DOMException('simulated recovery quota failure','QuotaExceededError');
                    return originalRecoveryPut.apply(this,args);
                  };
                }
                """);
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "quota.pdf", MimeType = "application/pdf", Buffer = source });
            await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Retry recovery'");
            Check((await page.Locator("#status").TextContentAsync())!.Contains("quota failure"), "Quota failure is visible and never reported saved.");
            await page.EvaluateAsync("() => { IDBObjectStore.prototype.put=window.originalRecoveryPut; }");
            await page.Locator("#recovery").ClickAsync();
            await Durable(page);
            await page.Locator("#recovery").ClickAsync();
            await page.GetByRole(AriaRole.Dialog, new() { Name = "Clear local recovery", Exact = true })
                .GetByRole(AriaRole.Button, new() { Name = "Clear recovery", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery off'");
            Check(await page.EvaluateAsync<object?>(Read) is null, "Explicit Clear completes a durable delete.");
            Console.WriteLine("PASS: real unsaved text/image state and exact raster, reload gating, durable Resume, image undo/clipboard retention, no pointer-event PDF snapshots, Discard/Clear, encrypted opt-out, corrupt-record retention and quota failure/retry.");
        }
        finally { page.Dialog -= accept; }
    }
}
