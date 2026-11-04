using System.Text.Json;
using Microsoft.Playwright;

static class EditorInteractions
{
    const string Settled = "!document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0 && document.getElementById('base').dataset.loadedUrl === document.getElementById('base').dataset.targetUrl";

    static void Check(bool ok, string message) { if (!ok) throw new Exception("Interactions: " + message); }

    public static async Task Run(IPage page)
    {
        await page.Keyboard.PressAsync("Alt+n");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "New document", Exact = true });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync(Settled);
        await page.Locator("[data-tool=rect]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 100, Y = 120 } });
        await page.Locator("[data-tool=rect]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 280, Y = 300 } });
        await SavedItems(page, 2);

        var item = page.Locator("#layer > .it").First;
        await item.ClickAsync();
        await Track(page, "#layer > .it.on");
        var box = (await item.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 20, box.Y + 20);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 80, box.Y + 50, new() { Steps = 24 });
        await page.Mouse.UpAsync();
        await SavedItems(page, 2);
        await Stable(page, "moving an overlay");
        Check(await page.EvaluateAsync<bool>("+document.querySelector('[data-k=x]').value > interactionProbe.x"), "move updates position controls");
        var savedMove = await page.EvaluateAsync<string>("async () => JSON.stringify(await (await pdfHost.request('/api/page/0/items')).json())");
        Check(await page.EvaluateAsync<bool>("saved => Math.abs(JSON.parse(saved)[0]?.x - +document.querySelector('[data-k=x]').value) < .02", savedMove), "final move position is persisted: " + savedMove);

        await Track(page, "#layer > .it.on");
        var corner = (await item.Locator(".hd").Last.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(corner.X + corner.Width / 2, corner.Y + corner.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(corner.X + 40, corner.Y + 30, new() { Steps = 20 });
        await page.Mouse.UpAsync();
        await SavedItems(page, 2);
        await Stable(page, "resizing an overlay");
        Check(await page.EvaluateAsync<bool>("+document.querySelector('[data-k=w]').value > interactionProbe.w"), "resize updates width controls");

        await page.Locator("[data-tool=line]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 80, Y = 400 } });
        await SavedItems(page, 3);
        var endpoint = (await page.Locator("#layer > .hd").Last.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(endpoint.X + 4, endpoint.Y + 4);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(endpoint.X + 44, endpoint.Y + 24, new() { Steps = 12 });
        await page.Mouse.UpAsync();
        await SavedItems(page, 3);
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              const saved = (await (await pdfHost.request('/api/page/0/items')).json()).find(x => x.type === 'line');
              const state = await (await pdfHost.request('/api/state')).json();
              const scale = parseFloat(document.getElementById('layer').style.width) / state.pages[0].width;
              const line = document.querySelector('#layer .lines line');
              return Math.abs(+document.querySelector('[data-k=x2]').value - saved.x2) < .02
                && Math.abs(+line.getAttribute('x2') - saved.x2 * scale) < .02
                && Math.abs(+line.getAttribute('y2') - saved.y2 * scale) < .02;
            }
            """), "line endpoint, controls and saved geometry agree");

        var raster = await Raster(page);
        await page.Locator("#dl").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        Check(await download.FailureAsync() is null, "edited PDF downloads successfully");
        var path = await download.PathAsync() ?? throw new Exception("No edited PDF download path.");
        await page.Locator("#open").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync("name => document.getElementById('name').textContent === name && " + Settled, Path.GetFileName(path));
        Check(await Raster(page) == raster, "moved/resized rectangles and line endpoints retain exact pixels after PDF save/reopen");

        var typography = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "03-typography-embedded.pdf"));
        await page.Locator("#open").SetInputFilesAsync(typography);
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '03-typography-embedded.pdf' && " + Settled);
        await page.Locator("#layer > .span").First.ClickAsync();
        await Track(page, "#layer > .span.on");
        await MoveFocusedSelection(page);
        await SavedItems(page, 1);
        await Stable(page, "moving original text", sameProperties: false);
        Check(await page.EvaluateAsync<bool>("document.activeElement === interactionProbe.selected && interactionProbe.selected.matches('.it.txt.on')"), "original text promotes its selection target without losing focus");

        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "05-vector-graphics.pdf"));
        await page.Locator("#open").SetInputFilesAsync(sample);
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '05-vector-graphics.pdf' && " + Settled);
        await page.Locator("#layer > .obj").EvaluateAllAsync<object>("""
            nodes => {
              const layer = document.getElementById('layer');
              const el = nodes.find(e => e.offsetWidth * e.offsetHeight < .9 * layer.offsetWidth * layer.offsetHeight);
              const b = el.getBoundingClientRect();
              const options = { bubbles: true, cancelable: true, pointerId: 17, clientX: b.x + b.width / 2, clientY: b.y + b.height / 2 };
              el.dispatchEvent(new PointerEvent('pointerdown', options));
              window.dispatchEvent(new PointerEvent('pointerup', options));
            }
            """);
        await Track(page, "#layer > .obj.on");
        await MoveFocusedSelection(page);
        await Until(page, "async () => (await (await pdfHost.request('/api/page/0/objects')).json()).some(o => o.edit?.tx > 0)");
        await page.WaitForFunctionAsync(Settled);
        await Stable(page, "moving original page content");
        Check(await page.EvaluateAsync<bool>("document.activeElement === interactionProbe.selected"), "original object retains keyboard focus after render/save");
        Check(await page.EvaluateAsync<bool>("async () => (await (await pdfHost.request('/api/page/0/objects')).json()).some(o => o.edit?.tx > 0)"), "original object transform persists");
        await page.EvaluateAsync("interactionProbe.observer.disconnect(); delete window.interactionProbe");
        Console.WriteLine("PASS: stable sidebar/page nodes, retained focus, overlay move/resize, line endpoint and original-object persistence.");
        await Profile(page);
    }

    public static async Task Profile(IPage page, bool enforce = true)
    {
        EventHandler<IDialog> discard = async (_, dialog) => await dialog.AcceptAsync();
        page.Dialog += discard;
        try { await page.Keyboard.PressAsync("Alt+n"); }
        finally { page.Dialog -= discard; }
        await page.GetByRole(AriaRole.Dialog, new() { Name = "New document", Exact = true })
            .GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync(Settled);
        await page.Locator("[data-tool=rect]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 100, Y = 120 } });
        for (var i = 1; i < 40; i++) await page.Keyboard.PressAsync("Control+d");
        await SavedItems(page, 40);
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 4 });
        var results = new List<JsonElement>();
        try
        {
            for (var repeat = 0; repeat < 3; repeat++)
            {
                var result = await page.EvaluateAsync<JsonElement>("""
                    async () => {
                      const layer = document.getElementById('layer'), panel = document.getElementById('props');
                      const selected = layer.querySelector('.it'), box = selected.getBoundingClientRect();
                      const options = {bubbles:true,cancelable:true,pointerId:31,clientX:box.x+20,clientY:box.y+20};
                      selected.dispatchEvent(new PointerEvent('pointerdown', options));
                      selected.tabIndex = 0; selected.focus();
                      const controls = [...panel.querySelectorAll('input,button,textarea')];
                      const siblings = [...layer.children].filter(e => e !== selected);
                      panel.scrollTop = panel.scrollHeight;
                      const scroll = panel.scrollTop, startX = selected._it.x;
                      let removed = 0, panelRemoved = 0, renders = 0, saves = 0, longTasks = 0;
                      const mutations = new MutationObserver(records => {
                        for (const r of records) {
                          if (r.target === layer) removed += r.removedNodes.length;
                          if (panel.contains(r.target)) panelRemoved += r.removedNodes.length;
                        }
                      });
                      mutations.observe(layer, {childList:true});
                      mutations.observe(panel, {childList:true,subtree:true});
                      const tasks = new PerformanceObserver(list => longTasks += list.getEntries().length);
                      tasks.observe({type:'longtask'});
                      const originals = {};
                      for (const method of ['request','image','imageBlob']) {
                        const original = originals[method] = pdfHost[method];
                        pdfHost[method] = function(url, options) {
                          if (/\/api\/page\/\d+\.png/.test(url)) renders++;
                          if (/\/items$/.test(url) && options?.method === 'PUT') saves++;
                          return original.apply(this, arguments);
                        };
                      }
                      const frames = [], handlers = [];
                      let final;
                      try {
                        for (let frame = 0; frame < 24; frame++) {
                          const began = performance.now();
                          for (let burst = 1; burst <= 8; burst++) {
                            final = {...options,clientX:options.clientX + frame*2 + burst/4,clientY:options.clientY + frame};
                            const start = performance.now();
                            window.dispatchEvent(new PointerEvent('pointermove', final));
                            handlers.push(performance.now()-start);
                          }
                          await new Promise(resolve => requestAnimationFrame(() => { frames.push(performance.now()-began); resolve(); }));
                        }
                        window.dispatchEvent(new PointerEvent('pointerup', final));
                        await new Promise(resolve => requestAnimationFrame(resolve));
                        const current = layer.querySelector('.it.on');
                        const state = await (await pdfHost.request('/api/state')).json();
                        const scale = parseFloat(layer.style.width)/state.pages[0].width;
                        const exact = Math.abs(current._it.x-startX-48/scale) < .001;
                        const percentile = (values, p) => [...values].sort((a,b) => a-b)[Math.ceil(values.length*p)-1];
                        return {objects:40,events:192,cpu:4,removed,panelRemoved,renders,saves,longTasks,exact,
                          retained:selected.isConnected && siblings.every(e => e.isConnected),
                          controlsRetained:controls.every(e => e.isConnected),
                          focused:document.activeElement === selected,scrollStable:panel.scrollTop === scroll,
                          handlerP95Ms:percentile(handlers,.95),handlerTotalMs:handlers.reduce((a,b) => a+b,0),
                          frameP95Ms:percentile(frames,.95),frameMaxMs:Math.max(...frames)};
                      } finally {
                        mutations.disconnect(); tasks.disconnect();
                        for (const [method, original] of Object.entries(originals)) pdfHost[method] = original;
                      }
                    }
                    """);
                results.Add(result);
                Console.WriteLine($"Interaction profile {repeat} (4x CPU): {result}");
                await SavedItems(page, 40);
                if (!result.GetProperty("exact").GetBoolean()) throw new Exception("Profile drag lost the final pointer geometry.");
                if (enforce)
                {
                    Check(result.GetProperty("removed").GetInt32() == 0 && result.GetProperty("panelRemoved").GetInt32() == 0,
                        "40-object drag must not remove page objects or property controls");
                    Check(result.GetProperty("retained").GetBoolean() && result.GetProperty("controlsRetained").GetBoolean()
                        && result.GetProperty("focused").GetBoolean() && result.GetProperty("scrollStable").GetBoolean(),
                        "40-object drag must preserve nodes, focus and sidebar scroll");
                    Check(result.GetProperty("renders").GetInt32() == 0 && result.GetProperty("saves").GetInt32() == 0,
                        "overlay drag must not render PDF pages or save items before release");
                }
            }
            var output = Environment.GetEnvironmentVariable("EDITOR_SMOKE_ARTIFACTS");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "interaction-profile.json"),
                    JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            if (enforce && results.Any(r => r.GetProperty("frameP95Ms").GetDouble() > 50 || r.GetProperty("handlerP95Ms").GetDouble() > 8))
            {
                const string message = "Interaction profile exceeds p95 frame 50 ms or pointer handler 8 ms at 4x CPU.";
                if (Environment.GetEnvironmentVariable("EDITOR_PERF_REPORT_ONLY") != "1") throw new Exception(message);
                Console.WriteLine("::warning::" + message + " (timing report-only)");
            }
        }
        finally
        {
            await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 1 });
            await cdp.DetachAsync();
        }
    }

    static Task MoveFocusedSelection(IPage page) => page.EvaluateAsync("""
        () => {
          const el = interactionProbe.selected, b = el.getBoundingClientRect();
          el.tabIndex = 0; el.focus();
          const options = { bubbles: true, cancelable: true, pointerId: 17, clientX: b.x + b.width / 2, clientY: b.y + b.height / 2 };
          el.dispatchEvent(new PointerEvent('pointerdown', options));
          for (let i = 1; i <= 40; i++) window.dispatchEvent(new PointerEvent('pointermove', { ...options, clientX: options.clientX + i }));
          window.dispatchEvent(new PointerEvent('pointerup', { ...options, clientX: options.clientX + 40 }));
        }
        """);

    static Task<string> Raster(IPage page) => page.EvaluateAsync<string>("""
        async () => {
          const bytes = await (await pdfHost.request('/api/page/0.png?dpi=72')).arrayBuffer();
          return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))).join(',');
        }
        """);

    static Task SavedItems(IPage page, int count) => Until(page, """
        async count => {
          const saved = await (await pdfHost.request('/api/page/0/items')).json();
          const live = [...document.querySelectorAll('#layer > .it, #layer > .lines')].map(e => e._it);
          return saved.length === count && live.length === count && live.every(it => {
            const item = saved.find(s => s.id === it.id);
            return item && ['x', 'y', 'w', 'h', 'x2', 'y2', 'size'].every(k => item[k] === it[k]);
          });
        }
        """, count);

    static async Task Until(IPage page, string predicate, object? arg = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!await page.EvaluateAsync<bool>(predicate, arg))
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Interaction geometry was not persisted.");
            await Task.Delay(50);
        }
    }

    static Task Track(IPage page, string selector) => page.EvaluateAsync("""
        selector => {
          window.interactionProbe?.observer.disconnect();
          const selected = document.querySelector(selector), panel = document.getElementById('props');
          panel.scrollTop = panel.scrollHeight;
          const probe = window.interactionProbe = {
            selected, base: document.getElementById('base'), page: document.getElementById('page'),
            layer: document.getElementById('layer'), siblings: [...document.getElementById('layer').children].filter(e => e !== selected && !e.classList.contains('hd')),
            controls: [...panel.querySelectorAll('input, textarea, button')], removed: 0, scrollTop: panel.scrollTop,
            x: +panel.querySelector('[data-k=x], [data-o=x]')?.value,
            w: +panel.querySelector('[data-k=w], [data-o=w]')?.value,
          };
          probe.observer = new MutationObserver(records => { for (const r of records) probe.removed += r.removedNodes.length; });
          probe.observer.observe(panel, { childList: true, subtree: true });
        }
        """, selector);

    static async Task Stable(IPage page, string action, bool sameProperties = true)
    {
        var result = await page.EvaluateAsync<string>("""
            sameProperties => {
              const p = interactionProbe;
              if (!p.selected.isConnected || p.siblings.some(e => !e.isConnected)) return 'page object nodes were replaced';
              if (p.page !== document.getElementById('page') || p.layer !== document.getElementById('layer')) return 'page containers were replaced';
              if (sameProperties && (p.removed || p.controls.some(e => !e.isConnected))) return 'property controls were replaced';
              if (sameProperties && document.getElementById('props').scrollTop !== p.scrollTop) return 'sidebar scroll position changed';
              if ([...document.querySelectorAll('#props section')].some(e => getComputedStyle(e).animationName !== 'none')) return 'sidebar entrance animations restart';
              return '';
            }
            """, sameProperties);
        Check(result.Length == 0, action + ": " + result);
    }
}
