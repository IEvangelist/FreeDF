using Microsoft.Playwright;
using PortableDocument.Document;
using System.Text.Json;

static class EditorJsonSafety
{
    public static async Task Run(IBrowserContext context, string source)
    {
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(90000);
        try
        {
            await page.GotoAsync(source);
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            var sample = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "11-acroform.pdf");
            var saved = await page.EvaluateAsync<JsonElement>("""
                async base64 => {
                  const check = (ok, message) => { if (!ok) throw new Error('JSON safety: ' + message); };
                  const json = async (url, body) => (await pdfHost.request(url,
                    body === undefined ? {} : {method:'POST', body:JSON.stringify(body)})).json();
                  const download = async () => {
                    const result = new Uint8Array(await (await pdfHost.request('/api/download')).arrayBuffer());
                    let binary = '';
                    for (let i=0; i<result.length; i+=32768) binary += String.fromCharCode(...result.subarray(i,i+32768));
                    return btoa(binary);
                  };
                  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
                  const form = new FormData(); form.append('file', new File([bytes], 'forms.pdf'));
                  await pdfHost.request('/api/open', {method:'POST', body:form});
                  const state = await json('/api/state');
                  check(state.pageCount > 0 && state.pages[0].width > 0 && typeof state.version === 'number', 'state shape');
                  const fonts = await json('/api/fonts');
                  check(fonts.length > 0 && typeof fonts[0].family === 'string', 'font payload');
                  const fields = await json('/api/fields'), field = fields.find(f => f.type === 'Tx');
                  check(field && typeof field.name === 'string', 'form field payload');
                  await pdfHost.request('/api/field?n=' + encodeURIComponent(field.name) + '&v=Trim-safe', {method:'POST'});
                  check((await json('/api/fields')).find(f => f.name === field.name)?.value === 'Trim-safe', 'form edit round trip');
                  await json('/api/info', {title:'Trim-safe metadata', author:'Smoke'});
                  check((await json('/api/info')).title === 'Trim-safe metadata', 'metadata round trip');
                  const spans = await json('/api/page/0/text');
                  check(spans.length > 0 && typeof spans[0].text === 'string' && typeof spans[0].x === 'number', 'text payload');
                  const objects = await json('/api/page/0/objects');
                  check(Array.isArray(objects) && objects.some(o => typeof o.index === 'number' && typeof o.kind === 'string'), 'object payload');
                  const term = spans[0].text;
                  check((await json('/api/find?q=' + encodeURIComponent(term))).length > 0, 'find payload');
                  const recovery = await json('/api/recovery');
                  check(recovery.format > 0 && recovery.pages.length > 0 && typeof recovery.document === 'string', 'recovery payload');
                  await json('/api/recovery', recovery);
                  check((await json('/api/info')).title === 'Trim-safe metadata', 'recovery metadata round trip');
                  check((await json('/api/fields')).find(f => f.name === field.name)?.value === 'Trim-safe', 'recovery form round trip');
                  const edited = await download();
                  const pg = state.pages[0];
                  await pdfHost.request('/api/page/0/items', {method:'PUT', body:JSON.stringify([
                    {id:'trim-redaction', type:'redact', x:pg.x0, y:0, w:pg.width, h:pg.height}
                  ])});
                  check((await json('/api/page/0/items'))[0]?.type === 'redact', 'item deserialize/serialize');
                  check((await json('/api/find?q=' + encodeURIComponent(term))).length === 0, 'redaction reaches saved content');
                  await json('/api/recovery', await json('/api/recovery'));
                  check((await json('/api/find?q=' + encodeURIComponent(term))).length === 0, 'redaction recovery round trip');
                  for (const url of ['/api/export/text', '/api/export/png?page=0&dpi=40', '/api/export/zip?dpi=40', '/api/export/secure']) {
                    check((await (await pdfHost.request(url)).arrayBuffer()).byteLength > 0, 'export ' + url);
                  }
                  return {edited, redacted:await download()};
                }
                """, Convert.ToBase64String(await File.ReadAllBytesAsync(sample)));
            var doc = PdfDocument.Load(Convert.FromBase64String(saved.GetProperty("edited").GetString()!));
            if (doc.Info("Title") != "Trim-safe metadata" || !doc.GetFormFields().Any(f => f.Value == "Trim-safe"))
                throw new Exception("JSON safety: exported PDF lost metadata or form value.");
            var redacted = PdfDocument.Load(Convert.FromBase64String(saved.GetProperty("redacted").GetString()!));
            if (!string.IsNullOrEmpty(redacted.Info("Title")) || redacted.GetFormFields().Any())
                throw new Exception("JSON safety: whole-page redaction retained metadata or form fields.");
            Console.WriteLine("PASS: trimmed JSON state/fonts/text/objects/items, form and metadata edits, real redaction/find, recovery round trips and all export formats.");
        }
        finally { await page.CloseAsync(); }
    }
}
