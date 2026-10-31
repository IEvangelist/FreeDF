using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

/// <summary>Service worker behaviour: precache, repeat visit, offline, update prompt, rollback, no stale mix, no PDF bytes.</summary>
static partial class EditorServiceWorker
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Service worker: " + message); }

    [GeneratedRegex(@"const MANIFEST = (\{.*?\});", RegexOptions.Singleline)]
    private static partial Regex ManifestPattern();

    sealed record Manifest(string Version, string[] Urls);

    static byte[] MinimalPdf()
    {
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        void Obj(string body) { offsets.Add(sb.Length); sb.Append(body); }
        Obj("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Obj("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Obj("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n");
        var xref = sb.Length;
        sb.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var offset in offsets) sb.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    static async Task MakeDirty(IPage tab)
    {
        await tab.SetInputFilesAsync("#open", new FilePayload { Name = "dirty.pdf", MimeType = "application/pdf", Buffer = MinimalPdf() });
        await tab.WaitForFunctionAsync("document.getElementById('dstate') && !document.getElementById('dstate').hidden");
        await tab.Locator("#rr").ClickAsync();
        await tab.WaitForFunctionAsync("document.getElementById('dstate').dataset.dirty === 'true'");
    }

    static Manifest ReadManifest(string site)
    {
        var match = ManifestPattern().Match(File.ReadAllText(Path.Combine(site, "sw.js")));
        Check(match.Success, "sw.js carries a build manifest.");
        using var doc = JsonDocument.Parse(match.Groups[1].Value);
        return new(doc.RootElement.GetProperty("version").GetString()!, doc.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("url").GetString()!).ToArray());
    }

    // Re-issues sw.js for a modified site: new digests for changed files, a new version and optional deliberate corruption.
    static void Reseal(string site, string version, Func<string, bool>? corrupt = null)
    {
        var path = Path.Combine(site, "sw.js");
        var text = File.ReadAllText(path);
        var match = ManifestPattern().Match(text);
        using var doc = JsonDocument.Parse(match.Groups[1].Value);
        var files = doc.RootElement.GetProperty("files").EnumerateArray().Select(f =>
        {
            var url = f.GetProperty("url").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(site, url.Replace('/', Path.DirectorySeparatorChar)));
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(bytes));
            return new { url, hash = corrupt?.Invoke(url) == true ? Convert.ToBase64String(new byte[32]) : hash };
        }).ToArray();
        File.WriteAllText(path, text.Replace(match.Value, "const MANIFEST = " + JsonSerializer.Serialize(new { version, files }) + ";") + "\n// build " + version + "\n");
    }

    public static async Task Run(string source, string output)
    {
        Directory.CreateDirectory(output);
        var site = Path.Combine(output, "site");
        if (Directory.Exists(site)) Directory.Delete(site, true);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(directory.Replace(source, site));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, file.Replace(source, site));
        var manifestA = ReadManifest(site);
        var originalCss = File.ReadAllText(Path.Combine(site, "editor.css"));
        var originalWorker = File.ReadAllText(Path.Combine(site, "worker.js"));
        var originalSw = File.ReadAllText(Path.Combine(site, "sw.js"));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        var served = new ConcurrentQueue<string>();
        app.UsePathBase("/csharp-pdf");
        app.Use(async (context, next) =>
        {
            served.Enqueue(context.Request.Path.Value!);
            // A returning visitor after the 10 minute Pages freshness window revalidates every asset.
            context.Response.Headers.CacheControl = "no-cache";
            await next();
        });
        var files = new PhysicalFileProvider(site);
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".dat"] = "application/octet-stream";
        var gzipped = new ConcurrentDictionary<string, Lazy<byte[]>>();
        byte[] Gzip(string relative, IFileInfo info, string etag) => gzipped.GetOrAdd(relative + etag, _ => new Lazy<byte[]>(() =>
        {
            using var output = new MemoryStream();
            using (var zip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                using var input = info.CreateReadStream();
                input.CopyTo(zip);
            }
            return output.ToArray();
        })).Value;
        // Pages-like delivery: gzip on the wire, ETag revalidation.
        app.Use(async (context, next) =>
        {
            var relative = context.Request.Path.Value!.TrimStart('/');
            var info = files.GetFileInfo(relative.Length == 0 ? "index.html" : relative);
            if (context.Request.Method != "GET" || !info.Exists || info.IsDirectory || !context.Request.Headers.AcceptEncoding.ToString().Contains("gzip", StringComparison.Ordinal) || !types.TryGetContentType(info.Name, out var type)) { await next(); return; }
            var etag = $"\"{info.Length:x}-{info.LastModified.UtcTicks:x}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "no-cache";
            if (context.Request.Headers.IfNoneMatch == etag) { context.Response.StatusCode = 304; return; }
            var body = Gzip(relative, info, etag);

            context.Response.ContentType = type;
            context.Response.Headers.ContentEncoding = "gzip";
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(body);
        });
        // Compress once up front so first-request latency is not part of the measured transfer.
        foreach (var path in Directory.EnumerateFiles(site, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(site, path).Replace('\\', '/');
            var info = files.GetFileInfo(relative);
            if (!info.Exists) continue;
            Gzip(relative, info, $"\"{info.Length:x}-{info.LastModified.UtcTicks:x}\"");
        }
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/csharp-pdf/";
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 }, DeviceScaleFactor = 1 });
        var external = new ConcurrentQueue<string>();
        context.Request += (_, request) =>
        {
            if ((request.Url.StartsWith("http:", StringComparison.Ordinal) || request.Url.StartsWith("https:", StringComparison.Ordinal)) && !request.Url.StartsWith(url, StringComparison.Ordinal))
                external.Enqueue(request.Url);
        };
        await context.AddInitScriptAsync("window.profileWorkerStages = [];");
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(90000);
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);

        // Recovery from a closed dirty tab legitimately offers Resume on the next start; discard it to reach Ready.
        async Task WaitReady(IPage tab)
        {
            await tab.WaitForFunctionAsync("document.getElementById('status')?.textContent.startsWith('Ready.') || !!document.querySelector('.dc')");
            if (await tab.Locator(".dc").CountAsync() > 0) await tab.Locator(".dc").ClickAsync();
            await tab.WaitForFunctionAsync("document.getElementById('status')?.textContent.startsWith('Ready.')");
        }
        async Task<double> Ready()
        {
            try { await WaitReady(page); }
            catch (TimeoutException) { throw new Exception("Service worker: editor never became ready: " + await page.Locator("#status").TextContentAsync() + " | " + string.Join(" | ", errors) + " | " + page.Url); }
            return await page.EvaluateAsync<double>("performance.now()");
        }
        Task<string?> Version() => page.EvaluateAsync<string?>(@"async () => {
            const reg = await navigator.serviceWorker.ready;
            return await new Promise(resolve => {
                navigator.serviceWorker.addEventListener('message', e => resolve(e.data.version), { once: true });
                reg.active.postMessage({ type: 'GET_VERSION' });
            });
        }");
        Task<string[]> CacheNames() => page.EvaluateAsync<string[]>("caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\")))");
        async Task AcceptUpdate()
        {
            await page.EvaluateAsync("window.__stale = true");
            await page.Locator("#update-banner button.primary").ClickAsync();
            await WaitFor("window.__stale === undefined", "reload after accepting the update");
            await Ready();
        }
        async Task WaitFor(string script, string what)
        {
            try { await page.WaitForFunctionAsync(script); }
            catch (TimeoutException) { throw new Exception("Service worker: timed out waiting for " + what); }
        }

        // 1. First visit: boots from the network; the worker installs afterwards and precaches exactly this build.
        await page.GotoAsync(url);
        var coldMs = await Ready();
        var coldBoot = await page.EvaluateAsync<string>("JSON.stringify({ ...window.pdfHost.bootTimings, workerStartAt: window.pdfHost.bootTimings.origin - performance.timeOrigin, dcl: performance.getEntriesByType(\"navigation\")[0].domContentLoadedEventEnd, fcp: performance.getEntriesByName(\"first-contentful-paint\")[0]?.startTime })");
        await page.EvaluateAsync("navigator.serviceWorker.ready.then(() => true)");
        Check(await Version() == manifestA.Version, "first visit activates the build's version.");
        await WaitFor($"caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\"))).then(async k => k.length === 1 && (await (await caches.open(k[0])).keys()).length === {manifestA.Urls.Length})", "precache completion");
        Check((await CacheNames()).Single() == "adobye-editor-" + manifestA.Version, "one versioned cache.");
        var cachedUrls = await page.EvaluateAsync<string[]>("caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\"))).then(async k => (await (await caches.open(k[0])).keys()).map(r => new URL(r.url).pathname))");
        Check(cachedUrls.All(u => u.StartsWith("/csharp-pdf/", StringComparison.Ordinal) && !u.Contains("/api/")) && cachedUrls.Length == manifestA.Urls.Length, "only manifest assets are cached.");

        var native = manifestA.Urls.First(u => u.Contains("dotnet.native.", StringComparison.Ordinal) && u.EndsWith(".wasm", StringComparison.Ordinal));
        var gzipRatio = await page.EvaluateAsync<double>("async name => { const href = new URL(name, document.baseURI).href; await (await fetch(href, { cache: \"reload\" })).arrayBuffer(); const e = performance.getEntriesByName(href).at(-1); return e.encodedBodySize / e.decodedBodySize; }", native);
        Check(gzipRatio < 0.8, $"the test server gzips assets like Pages (ratio {gzipRatio:F2}).");
        // 2. Repeat visit: controlled page boots from the cache with (almost) no asset traffic.
        var warm = new List<double>();
        var warmRequests = 0;
        var warmBoot = "";
        var warmStages = "";
        for (var i = 0; i < 3; i++)
        {
            while (served.TryDequeue(out _)) { }
            await page.ReloadAsync();
            Check(await page.EvaluateAsync<bool>("!!navigator.serviceWorker.controller"), "repeat visit is controlled by the worker.");
            warm.Add(await Ready());
            warmStages = await page.EvaluateAsync<string>("JSON.stringify(window.profileWorkerStages.map(s => [s.url, Math.round(s.dispatchMs), Math.round(s.dotnetMs)]))");
            warmBoot = await page.EvaluateAsync<string>("JSON.stringify({ ...window.pdfHost.bootTimings, workerStartAt: window.pdfHost.bootTimings.origin - performance.timeOrigin, dcl: performance.getEntriesByType(\"navigation\")[0].domContentLoadedEventEnd, fcp: performance.getEntriesByName(\"first-contentful-paint\")[0]?.startTime })");
            await Task.Delay(500);
            warmRequests = served.Count(path => !path.EndsWith("/sw.js", StringComparison.Ordinal));
            Check(warmRequests == 0, $"repeat visit served from cache, but the server saw: {string.Join(", ", served)}");
        }

        // 3. Offline: the full editor starts without the network.
        await context.SetOfflineAsync(true);
        await page.ReloadAsync();
        var offlineMs = await Ready();
        await context.SetOfflineAsync(false);

        // 4. Evicted entry: the worker refetches only bytes that match the build digest and repairs the cache.
        var victim = manifestA.Urls.First(u => u.EndsWith(".js", StringComparison.Ordinal) && u.Contains("dotnet.runtime", StringComparison.Ordinal));
        await page.EvaluateAsync("async name => (await (await caches.open((await caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\"))))[0])).delete(new URL(name, document.baseURI)))", victim);
        await page.ReloadAsync();
        await Ready();
        await WaitFor($"caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\"))).then(async k => !!(await (await caches.open(k[0])).match(new URL('{victim}', document.baseURI))))", "cache repair");

        // 5. Deploy B: old build keeps serving until the user accepts, never a mix of old and new.
        File.WriteAllText(Path.Combine(site, "editor.css"), originalCss + "\n/* build B */\n");
        File.WriteAllText(Path.Combine(site, "worker.js"), originalWorker + "\n// build B worker\n");
        Reseal(site, "bbbbbbbbbbbbbbbb");
        await page.EvaluateAsync("navigator.serviceWorker.getRegistration().then(r => r.update())");
        await WaitFor("!!document.getElementById('update-banner')", "update banner");
        Check((await CacheNames()).Length == 2, "new version precached beside the old one.");
        var cssWhileWaiting = await page.EvaluateAsync<string>("fetch('editor.css').then(r => r.text())");
        Check(!cssWhileWaiting.Contains("build B", StringComparison.Ordinal), "waiting version does not leak into the running page.");
        Check(await Version() == manifestA.Version, "old worker still active before accepting.");
        // A second tab on the old build must follow the first tab to the new one, never run old code under the new worker.
        var second = await context.NewPageAsync();
        second.SetDefaultTimeout(90000);
        await second.GotoAsync(url);
        await WaitReady(second);
        await second.WaitForFunctionAsync("!!document.getElementById('update-banner')");
        // A tab with unsaved edits blocks activation: nothing reloads and the old build keeps serving everyone.
        await MakeDirty(second);
        await second.EvaluateAsync("window.__stale = true");
        await page.Locator("#update-banner button.primary").ClickAsync();
        await WaitFor("document.getElementById('update-banner')?.textContent.includes('another editor tab')", "blocked-by-dirty-tab notice");
        Check(await Version() == manifestA.Version, "a dirty tab blocks activation of the new worker.");
        Check(await second.EvaluateAsync<bool>("window.__stale === true && document.getElementById('dstate').dataset.dirty === 'true'"), "the dirty tab was not reloaded.");
        Check((await CacheNames()).Length == 2, "both versions stay cached while activation is blocked.");
        await second.CloseAsync();
        second = await context.NewPageAsync();
        second.SetDefaultTimeout(90000);
        await second.GotoAsync(url);
        await WaitReady(second);
        await second.WaitForFunctionAsync("!!document.getElementById('update-banner')");
        await second.EvaluateAsync("window.__stale = true");
        await page.ReloadAsync();
        await Ready();
        Check(await Version() == manifestA.Version, "a plain reload does not swap versions.");
        Check(!(await page.EvaluateAsync<string>("fetch('editor.css').then(r => r.text())")).Contains("build B", StringComparison.Ordinal), "plain reload still serves one consistent old build.");
        await WaitFor("!!document.getElementById('update-banner')", "update banner after reload");

        // An inline edit in progress (contentEditable) blocks activation even though nothing is unsaved yet.
        const string checking = "document.querySelector('#update-banner span').textContent = 'checking'";
        await second.EvaluateAsync("const d = document.createElement('div'); d.id = 'typing'; d.contentEditable = 'plaintext-only'; d.textContent = 'x'; document.body.append(d); d.focus();");
        await page.EvaluateAsync(checking);
        await page.Locator("#update-banner button.primary").ClickAsync();
        await WaitFor("document.getElementById('update-banner')?.textContent.includes('another editor tab')", "block by an inline edit");
        Check(await second.EvaluateAsync<bool>("window.__stale === true"), "a tab mid-inline-edit was not reloaded.");
        await second.EvaluateAsync("document.getElementById('typing').remove()");
        var typing = await context.NewPageAsync();
        typing.SetDefaultTimeout(90000);
        await typing.GotoAsync(url);
        await WaitReady(typing);
        await typing.SetInputFilesAsync("#open", new FilePayload { Name = "typing.pdf", MimeType = "application/pdf", Buffer = MinimalPdf() });
        await typing.WaitForFunctionAsync("document.getElementById('dstate') && !document.getElementById('dstate').hidden");
        await typing.Locator("[data-tool=text]").ClickAsync();
        await typing.Locator("#page").ClickAsync();
        await typing.WaitForFunctionAsync("!!document.querySelector('[contenteditable=\"plaintext-only\"]')");
        await typing.EvaluateAsync("window.__stale = true");
        await page.EvaluateAsync(checking);
        await page.Locator("#update-banner button.primary").ClickAsync();
        await WaitFor("document.getElementById('update-banner')?.textContent.includes('another editor tab')", "block by the real text tool");
        Check(await typing.EvaluateAsync<bool>("window.__stale === true && !!document.querySelector('[contenteditable=\"plaintext-only\"]')"), "typing in the real text tool was not interrupted.");
        await typing.CloseAsync();

        // A tab that is still BOOTING when build B activates (its engine worker is held back), and that never answers
        // update queries, must not block the update and must finish booting from build A, never mixing in build B.
        var silent = await context.NewPageAsync();
        silent.SetDefaultTimeout(90000);
        await silent.AddInitScriptAsync("const add = ServiceWorkerContainer.prototype.addEventListener; ServiceWorkerContainer.prototype.addEventListener = function (type, ...rest) { if (type === 'message' || type === 'controllerchange') return; return add.call(this, type, ...rest); };");
        await silent.AddInitScriptAsync(@"(() => {
            const Real = window.Worker; const handlers = ['onmessage', 'onerror', 'onmessageerror'];
            window.Worker = class { constructor(u, o) { this.u = u; this.o = o; this.q = []; window.__gate = this; }
              postMessage(...a) { this.real ? this.real.postMessage(...a) : this.q.push(a); }
              terminate() { this.real?.terminate(); }
              release() { this.real = new Real(this.u, this.o); for (const h of handlers) this.real[h] = this[h]; for (const a of this.q) this.real.postMessage(...a); } };
        })();");
        var bootFailures = new ConcurrentQueue<string>();
        silent.Response += (_, response) => { if (response.Url.Contains("/_framework/", StringComparison.Ordinal) && response.Status >= 400) bootFailures.Enqueue(response.Status + " " + response.Url); };
        await silent.GotoAsync(url);
        await silent.WaitForFunctionAsync("!!window.__gate");
        await silent.EvaluateAsync("window.__stale = true");
        await AcceptUpdate();
        Check(await silent.EvaluateAsync<bool>("window.__stale === true"), "a booting, unresponsive tab does not block the update and is not reloaded.");
        Check(!(await silent.EvaluateAsync<string>("fetch('worker.js').then(r => r.text())")).Contains("build B worker", StringComparison.Ordinal), "a client pinned to build A is served build A's files after B activated.");
        Check((await CacheNames()).Length == 2, "the old build's cache is kept while a client is pinned to it.");
        await silent.EvaluateAsync("window.__gate.release()");
        await WaitReady(silent);
        Check(bootFailures.IsEmpty, "the booting tab finished from its own build with no failed asset: " + string.Join(", ", bootFailures));
        Check(await silent.EvaluateAsync<bool>("window.__stale === true && window.pdfHost.request('/api/state').then(r => r.ok)"), "the tab that booted across the activation works and was never reloaded.");
        await silent.CloseAsync();
        var fresh = await context.NewPageAsync();
        fresh.SetDefaultTimeout(90000);
        await fresh.GotoAsync(url);
        await WaitReady(fresh);
        await WaitFor("caches.keys().then(k => k.filter(n => n.startsWith('adobye-editor-')).length === 1)", "old cache cleanup after the pinned client closed");
        await fresh.CloseAsync();        await WaitFor("!!navigator.serviceWorker.controller", "controller");
        await second.WaitForFunctionAsync("window.__stale === undefined");
        await WaitReady(second);
        Check((await second.EvaluateAsync<string>("fetch('editor.css').then(r => r.text())")).Contains("build B", StringComparison.Ordinal), "the other tab reloaded onto the new build.");
        Check(await second.EvaluateAsync<bool>("navigator.serviceWorker.controller !== null"), "the other tab is controlled by the new worker.");
        await second.CloseAsync();
        Check(await Version() == "bbbbbbbbbbbbbbbb", "accepting activates the new version.");
        await page.EvaluateAsync("navigator.serviceWorker.ready.then(r => r.active.postMessage({ type: 'CLEANUP' }))");
        await WaitFor("caches.keys().then(k => k.filter(n => n.startsWith('adobye-editor-')).length === 1)", "old cache removal after activation");
        Check((await CacheNames()).Single() == "adobye-editor-bbbbbbbbbbbbbbbb", "old cache removed after activation.");
        Check((await page.EvaluateAsync<string>("fetch('editor.css').then(r => r.text())")).Contains("build B", StringComparison.Ordinal), "new version serves new assets.");

        // 6. A deployment whose bytes do not match its manifest must never activate.
        File.WriteAllText(Path.Combine(site, "editor.css"), originalCss + "\n/* build C */\n");
        Reseal(site, "cccccccccccccccc", url => url == "editor.css");
        await page.EvaluateAsync("navigator.serviceWorker.getRegistration().then(r => r.update().catch(() => {}))");
        await Task.Delay(4000);
        Check(!await page.EvaluateAsync<bool>("!!document.getElementById('update-banner')"), "corrupt build offers no update.");
        Check((await CacheNames()).Single() == "adobye-editor-bbbbbbbbbbbbbbbb", "corrupt build leaves no cache behind.");
        Check(await Version() == "bbbbbbbbbbbbbbbb", "corrupt build leaves the working version in place.");

        // 7. Rollback to build A uses the same update flow and returns to the original bytes.
        File.WriteAllText(Path.Combine(site, "editor.css"), originalCss);
        File.WriteAllText(Path.Combine(site, "worker.js"), originalWorker);
        File.WriteAllText(Path.Combine(site, "sw.js"), originalSw);
        await page.EvaluateAsync("navigator.serviceWorker.getRegistration().then(r => r.update())");
        await WaitFor("!!document.getElementById('update-banner')", "rollback banner");
        await AcceptUpdate();
        await WaitFor("!!navigator.serviceWorker.controller", "controller after rollback");
        Check(await Version() == manifestA.Version, "rollback re-activates the original build.");
        await page.EvaluateAsync("navigator.serviceWorker.ready.then(r => r.active.postMessage({ type: 'CLEANUP' }))");
        await WaitFor("caches.keys().then(k => k.filter(n => n.startsWith('adobye-editor-')).length === 1)", "old cache removal after rollback");
        Check((await CacheNames()).Single() == "adobye-editor-" + manifestA.Version, "rollback leaves exactly the original cache.");
        Check(!(await page.EvaluateAsync<string>("fetch('editor.css').then(r => r.text())")).Contains("/* build ", StringComparison.Ordinal), "rollback serves the original assets.");

        // 8. Range requests are never answered from the cache.
        var rangeStatus = await page.EvaluateAsync<int>("async name => (await fetch(name, { headers: { Range: 'bytes=0-9' } })).status", native);
        Check(rangeStatus == 206, $"range requests pass through to the network (status {rangeStatus}).");

        // 9. A failing cache write (quota/eviction) must not fail bytes that already matched the digest.
        // Playwright .NET has no service worker handle, so patch Cache.put through the browser's DevTools target API.
        await using var browserCdp = await browser.NewBrowserCDPSessionAsync();
        var targets = (await browserCdp.SendAsync("Target.getTargets"))!.Value.GetProperty("targetInfos").EnumerateArray().ToArray();
        foreach (var target in targets.Where(t => t.GetProperty("type").GetString() == "service_worker" && t.GetProperty("url").GetString()!.EndsWith("/sw.js", StringComparison.Ordinal)).ToArray())
        {
            var attached = (await browserCdp.SendAsync("Target.attachToTarget", new Dictionary<string, object> { ["targetId"] = target.GetProperty("targetId").GetString()!, ["flatten"] = false }))!.Value;
            await browserCdp.SendAsync("Target.sendMessageToTarget", new Dictionary<string, object>
            {
                ["sessionId"] = attached.GetProperty("sessionId").GetString()!,
                ["message"] = JsonSerializer.Serialize(new { id = 1, method = "Runtime.evaluate", @params = new { expression = "Cache.prototype.put = () => Promise.reject(new DOMException('full', 'QuotaExceededError'))" } }),
            });
        }        await Task.Delay(500);
        await page.EvaluateAsync("async name => (await (await caches.open((await caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\"))))[0])).delete(new URL(name, document.baseURI)))", victim);
        await page.ReloadAsync();
        await Ready();
        Check(!await page.EvaluateAsync<bool>("async name => !!(await (await caches.open((await caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\"))))[0])).match(new URL(name, document.baseURI)))", victim), "the failed write left the entry absent while the editor still started.");

        // 10. Kill switch: caches removed, worker unregistered, pages reloaded, and the client does not register again.
        // The kill switch must not reload a tab with unsaved edits (here also with recovery reported off, as for encrypted input).
        var dirtyTab = await context.NewPageAsync();
        dirtyTab.SetDefaultTimeout(90000);
        await dirtyTab.GotoAsync(url);
        await WaitReady(dirtyTab);
        await MakeDirty(dirtyTab);
        await dirtyTab.EvaluateAsync("window.__stale = true; const r = document.getElementById('recovery'); r.hidden = false; r.textContent = 'Recovery off (encrypted)'");
        await page.EvaluateAsync("window.__stale = true");
        await Task.Delay(3500);
        File.WriteAllText(Path.Combine(site, "sw.js"), ManifestPattern().Replace(originalSw, "const MANIFEST = {\"kill\":true};"));
        await page.EvaluateAsync("navigator.serviceWorker.getRegistration().then(r => r.update())");
        await WaitFor("navigator.serviceWorker.getRegistration().then(r => !r)", "kill switch unregistration");
        await dirtyTab.WaitForFunctionAsync("document.getElementById('update-banner')?.textContent.includes('unsaved changes')");
        Check(await dirtyTab.EvaluateAsync<bool>("window.__stale === true && document.getElementById('dstate').dataset.dirty === 'true'"), "the kill switch did not reload a dirty tab.");
        await WaitFor("window.__stale === undefined", "clean tab reload after the kill switch");
        await dirtyTab.CloseAsync();
        await Ready();
        await Task.Delay(4000);
        Check(await page.EvaluateAsync<bool>("navigator.serviceWorker.getRegistration().then(r => !r)"), "client does not re-register while the kill switch is published.");
        Check((await CacheNames()).Length == 0, "kill switch removed every editor cache.");
        File.WriteAllText(Path.Combine(site, "sw.js"), originalSw);
        // Control: the same repeat visit with no worker, i.e. whatever the browser HTTP cache gives for free.
        await using var plain = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block, ViewportSize = new() { Width = 1440, Height = 1000 } });
        var plainPage = await plain.NewPageAsync();
        plainPage.SetDefaultTimeout(90000);
        await plainPage.GotoAsync(url);
        await plainPage.WaitForFunctionAsync("document.getElementById('status')?.textContent.startsWith('Ready.')");
        var httpRepeat = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            await plainPage.ReloadAsync();
            await plainPage.WaitForFunctionAsync("document.getElementById('status')?.textContent.startsWith('Ready.')");
            httpRepeat.Add(await plainPage.EvaluateAsync<double>("performance.now()"));
        }
        var httpBoot = await plainPage.EvaluateAsync<string>("JSON.stringify(window.pdfHost.bootTimings)");

        // Slow 4G (1.6 Mbps, 150 ms RTT), gzip transfer: first visit vs worker repeat vs HTTP-revalidation repeat.
        async Task<(double Cold, double Repeat)> Throttled(bool worker)
        {
            await using var slowContext = await browser.NewContextAsync(new() { ServiceWorkers = worker ? ServiceWorkerPolicy.Allow : ServiceWorkerPolicy.Block, ViewportSize = new() { Width = 1440, Height = 1000 } });
            var slow = await slowContext.NewPageAsync();
            slow.SetDefaultTimeout(180000);
            var cdp = await slowContext.NewCDPSessionAsync(slow);
            await cdp.SendAsync("Network.enable");
            await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object> { ["offline"] = false, ["latency"] = 150, ["downloadThroughput"] = 1_600_000 / 8, ["uploadThroughput"] = 750_000 / 8 });
            var timeline = new ConcurrentQueue<string>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string Short(string u) => new Uri(u).AbsolutePath.Split('/')[^1];
            slowContext.Request += (_, r) => timeline.Enqueue($"{clock.ElapsedMilliseconds,6} req  {Short(r.Url)}");
            slowContext.Response += (_, r) => timeline.Enqueue($"{clock.ElapsedMilliseconds,6} head {Short(r.Url)} {r.Status}");
            slowContext.RequestFinished += (_, r) => timeline.Enqueue($"{clock.ElapsedMilliseconds,6} done {Short(r.Url)}");
            slowContext.RequestFailed += (_, r) => timeline.Enqueue($"{clock.ElapsedMilliseconds,6} FAIL {Short(r.Url)} {r.Failure}");
            int responses = 0, gzipped = 0;
            slowContext.Response += (_, response) => { responses++; if (response.Headers.TryGetValue("content-encoding", out var enc) && enc == "gzip") gzipped++; };
            await slow.GotoAsync(url);
            try { await slow.WaitForFunctionAsync("document.getElementById(\"status\")?.textContent.startsWith(\"Ready.\")", null, new() { Timeout = 150000 }); }
            catch (TimeoutException) { { File.WriteAllLines(Path.Combine(output, "slow-timeline.txt"), timeline); throw new Exception($"Slow 4G first visit never became ready ({responses} responses, {gzipped} gzip): " + await slow.Locator("#status").TextContentAsync()); } }
            var first = await slow.EvaluateAsync<double>("performance.now()");
            if (worker) await slow.EvaluateAsync("navigator.serviceWorker.ready.then(() => true)");
            if (worker) await slow.WaitForFunctionAsync("caches.keys().then(k => k.filter(n => n.startsWith(\"adobye-editor-\"))).then(async k => k.length === 1 && (await (await caches.open(k[0])).keys()).length > 0)");
            if (worker) await Task.Delay(5000);
            await slow.ReloadAsync();
            await slow.WaitForFunctionAsync("document.getElementById(\"status\")?.textContent.startsWith(\"Ready.\")");
            return (first, await slow.EvaluateAsync<double>("performance.now()"));
        }
        var slowWorker = await Throttled(true);
        var slowHttp = await Throttled(false);

        Check(external.IsEmpty, "no external requests: " + string.Join(", ", external));
        Check(errors.IsEmpty, "no page errors: " + string.Join(" | ", errors));
        var report = new { coldMs, coldBoot, warmBoot, warmMs = warm, offlineMs, slow4g = new { firstVisitMs = slowWorker.Cold, workerRepeatMs = slowWorker.Repeat, httpRevalidateRepeatMs = slowHttp.Repeat }, httpCacheRepeatMs = httpRepeat, httpBoot, precachedFiles = manifestA.Urls.Length, version = manifestA.Version };
        File.WriteAllText(Path.Combine(output, "service-worker.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: service worker precache ({manifestA.Urls.Length} files), controlled repeat visits with zero asset requests, offline start, repair, update prompt, plain-reload consistency, corrupt build rejection, rollback, two-tab update, dirty-tab protection, range pass-through, failed cache write and kill switch. cold {coldMs:F0} ms, repeat {string.Join("/", warm.Select(w => w.ToString("F0")))} ms, offline {offlineMs:F0} ms, slow4G first {slowWorker.Cold:F0} ms, repeat with worker {slowWorker.Repeat:F0} ms, repeat without worker (revalidating) {slowHttp.Repeat:F0} ms; no-worker repeat {string.Join("/", httpRepeat.Select(w => w.ToString("F0")))} ms. boot cold {coldBoot} warm {warmBoot} stages {warmStages} no-worker boot {httpBoot}");
    }
}
