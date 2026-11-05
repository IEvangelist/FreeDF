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
        await Messaging(page);
        await EditMenu(page);
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
        await LiveGeometry(page, "[data-k]");
        Check(await page.EvaluateAsync<bool>("+document.querySelector('[data-k=x]').value > interactionProbe.x"), "position inputs advance while pointer is down");
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
        await LiveGeometry(page, "[data-k]");
        Check(await page.EvaluateAsync<bool>("+document.querySelector('[data-k=w]').value > interactionProbe.w"), "width input advances while pointer is down");
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
        await LiveGeometry(page, "[data-k]");
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
        await page.Locator("#layer > .span").First.ClickAsync();
        await AlignSelected(page, "Center horizontally");
        await SavedItems(page, 2);
        await CheckCentered(page, horizontal: true);

        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "05-vector-graphics.pdf"));
        await page.Locator("#open").SetInputFilesAsync(sample);
        await page.WaitForFunctionAsync("document.getElementById('name').textContent === '05-vector-graphics.pdf' && " + Settled);
        await SelectOriginal(page);
        await Track(page, "#layer > .obj.on");
        await page.EvaluateAsync("""
            () => {
              const probe = interactionProbe;
              probe.baseUrls = [];
              probe.baseObserver = new MutationObserver(records => {
                for (const record of records) for (const node of record.addedNodes)
                  if (node.id === 'base') probe.baseUrls.push(node.dataset.loadedUrl);
              });
              probe.baseObserver.observe(document.getElementById('page'), {childList:true});
            }
            """);
        await MoveFocusedSelection(page);
        await LiveGeometry(page, "[data-o]");
        await Until(page, "async () => (await (await pdfHost.request('/api/page/0/objects')).json()).some(o => o.edit?.tx > 0)");
        await page.WaitForFunctionAsync("interactionProbe.baseUrls.length > 0 && " + Settled);
        await Stable(page, "moving original page content");
        Check(await page.EvaluateAsync<bool>("document.activeElement === interactionProbe.selected"), "original object retains keyboard focus after render/save");
        Check(await page.EvaluateAsync<bool>("async () => (await (await pdfHost.request('/api/page/0/objects')).json()).some(o => o.edit?.tx > 0)"), "original object transform persists");
        var baseUrls = await page.EvaluateAsync<string[]>("interactionProbe.baseUrls");
        Check(await page.EvaluateAsync<bool>("interactionProbe.baseUrls.every(url => new URL(url, location.href).searchParams.get('dpi') === new URL(document.getElementById('base').dataset.targetUrl, location.href).searchParams.get('dpi'))"), "post-edit page replacements never downgrade to a low-resolution preview: " + string.Join(", ", baseUrls));
        await page.EvaluateAsync("interactionProbe.baseObserver.disconnect()");
        var objectIndex = await page.EvaluateAsync<int>("document.querySelector('#layer > .obj.on')._obj.index");
        await page.Keyboard.PressAsync("Control+z");
        await Until(page, "async () => !(await (await pdfHost.request('/api/page/0/objects')).json()).some(o => o.edit?.tx > 0)");
        await page.Keyboard.PressAsync("Control+Shift+z");
        await Until(page, "async () => (await (await pdfHost.request('/api/page/0/objects')).json()).some(o => o.edit?.tx > 0)");
        await page.EvaluateAsync("interactionProbe.observer.disconnect(); delete window.interactionProbe");
        await SelectOriginal(page, objectIndex);
        await AlignSelected(page, "Center horizontally");
        await CheckCentered(page, horizontal: true);
        var horizontal = await page.EvaluateAsync<double[]>("() => { const o = document.querySelector('#layer > .obj.on')._obj; return [o.index, o.edit.tx, o.edit.ty]; }");
        await AlignSelected(page, "Center vertically");
        await CheckCentered(page, horizontal: false);
        var aligned = await page.EvaluateAsync<double[]>("() => { const o = document.querySelector('#layer > .obj.on')._obj; return [o.index, o.edit.tx, o.edit.ty]; }");
        const string savedAlignment = "async ([index, tx, ty]) => { const o = (await (await pdfHost.request('/api/page/0/objects')).json()).find(o => o.index === index); return Math.abs((o.edit?.tx ?? 0) - tx) < .001 && Math.abs((o.edit?.ty ?? 0) - ty) < .001; }";
        await Until(page, savedAlignment, aligned);
        await page.Keyboard.PressAsync("Control+z");
        await Until(page, savedAlignment, horizontal);
        await page.Keyboard.PressAsync("Control+Shift+z");
        await Until(page, savedAlignment, aligned);
        Console.WriteLine("PASS: stable sidebar/page nodes, retained focus, overlay move/resize, line endpoint and original-object persistence.");
        await MultiSelection(page, objectIndex);
        await Profile(page);
    }

    static async Task Messaging(IPage page)
    {
        await page.Locator("#fileBtn").FocusAsync();
        await page.EvaluateAsync("() => document.getElementById('del').click()");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Delete page", Exact = true });
        await dialog.WaitForAsync();
        Check(await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).EvaluateAsync<bool>("el => document.activeElement === el"), "confirmation initially focuses the safe choice");
        Check(await page.EvaluateAsync<bool>("document.querySelector('header').inert && document.querySelector('main').inert"), "confirmation makes the editor inert");
        await page.Keyboard.PressAsync("Shift+Tab");
        Check(await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete page", Exact = true }).EvaluateAsync<bool>("el => document.activeElement === el"), "Shift+Tab stays inside the confirmation");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Enter");
        Check(await page.EvaluateAsync<bool>("async () => (await (await pdfHost.request('/api/state')).json()).pageCount === 1"), "canceling page deletion leaves its page untouched");
        Check(await page.Locator("#fileBtn").EvaluateAsync<bool>("el => document.activeElement === el && !document.querySelector('main').inert"), "cancel returns focus and releases only dialog-owned inert state");
        await page.EvaluateAsync("() => document.getElementById('title').click()");
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Document title", Exact = true });
        await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Title", Exact = true }).FillAsync("Do not apply");
        await page.Keyboard.PressAsync("Escape");
        Check(await dialog.CountAsync() == 0, "Escape cancels a real text prompt");
        await page.EvaluateAsync("""
            () => {
              window.messageResult = undefined;
              window.editorUi.prompt({ title: 'Input check', label: 'Value', value: '<b>literal text</b>', confirmLabel: 'Apply' }).then(value => window.messageResult = value);
            }
            """);
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Input check", Exact = true });
        await dialog.WaitForAsync();
        Check(await dialog.Locator("b").CountAsync() == 0 && await dialog.GetByRole(AriaRole.Textbox).InputValueAsync() == "<b>literal text</b>", "prompt values are literal text, never HTML");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("window.messageResult === '<b>literal text</b>'");
        await page.EvaluateAsync("""
            () => {
              window.editorUi.confirm({ title: 'First decision', message: 'First', confirmLabel: 'Continue' }).then(value => window.firstDecision = value);
              window.editorUi.confirm({ title: 'Second decision', message: 'Second', confirmLabel: 'Continue' }).then(value => window.secondDecision = value);
            }
            """);
        await page.GetByRole(AriaRole.Dialog, new() { Name = "First decision", Exact = true }).WaitForAsync();
        Check(await page.Locator(".message-dialog").CountAsync() == 1, "concurrent messages are queued rather than replacing unresolved prompts");
        await page.Keyboard.PressAsync("Escape");
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Second decision", Exact = true });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("window.firstDecision === false && window.secondDecision === true");
        await page.Locator("#dl").ClickAsync();
        var save = page.GetByRole(AriaRole.Dialog, new() { Name = "Save as", Exact = true });
        await save.WaitForAsync();
        await page.EvaluateAsync("() => { window.editorUi.confirm({ title: 'Nested decision', message: 'Keep the underlying dialog.' }); }");
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Nested decision", Exact = true });
        await dialog.WaitForAsync();
        Check(await save.EvaluateAsync<bool>("el => el.closest('.mdl').inert"), "a nested message blocks the earlier modal without replacing it");
        await page.Keyboard.PressAsync("Escape");
        Check(await save.EvaluateAsync<bool>("el => !el.closest('.mdl').inert && document.querySelector('main').inert"), "cancel restores the earlier modal but not its inert background");
        await save.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("!document.querySelector('main').inert");
        Console.WriteLine("PASS: themed confirmation/input UX, safe initial focus, focus trap/return, inert background, Escape/Enter, literal text and queued decisions.");
    }

    static async Task MultiSelection(IPage page, int objectIndex)
    {
        await page.WaitForFunctionAsync(Settled);
        await page.Locator("[data-tool=rect]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 100, Y = 120 } });
        await SavedItems(page, 1);
        await page.Locator("#layer > .span").First.ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        await page.Locator("#layer > .obj").EvaluateAllAsync<object>("(nodes, index) => { const el = nodes.find(n => n._obj.index === index); el.tabIndex = 0; el.focus(); }", objectIndex);
        await page.Keyboard.PressAsync("Shift+Enter");
        Check(await page.EvaluateAsync<bool>("document.getElementById('layer').dataset.multi === '3' && document.querySelectorAll('#layer > .on').length === 3 && !document.querySelector('#layer .hd')"), "mixed keyboard/pointer selection has one shared outline and no individual resize handles");
        await SavedItems(page, 1);
        await page.WaitForFunctionAsync(Settled);
        await page.EvaluateAsync("""
            async () => {
              const layer = document.getElementById('layer'), panel = document.getElementById('props');
              const selected = [...layer.querySelectorAll(':scope > .on')];
              window.multiProof = {
                items: await (await pdfHost.request('/api/page/0/items')).json(),
                edits: (await (await pdfHost.request('/api/page/0/objects')).json()).map(o => o.edit),
                selected, controls: [...panel.querySelectorAll('input,button')], outline: layer.querySelector('.selection-box'),
                x: +panel.querySelector('[data-group=x]').value, y: +panel.querySelector('[data-group=y]').value,
                positions: selected.map(e => e._it ? [e._it.x,e._it.y] : e._span ? [e._span.x,e._span.y-.95*e._span.size] : [e._obj.edit?.tx ?? 0,e._obj.edit?.ty ?? 0]),
                removed: 0, renders: 0, saves: 0, methods: {},
              };
              multiProof.observer = new MutationObserver(records => { for (const r of records) multiProof.removed += r.removedNodes.length; });
              multiProof.observer.observe(layer, {childList:true});
              multiProof.observer.observe(panel, {childList:true,subtree:true});
              for (const method of ['request','image','imageBlob']) {
                const original = multiProof.methods[method] = pdfHost[method];
                pdfHost[method] = function(url, options) {
                  if (/\/api\/page\/\d+\.png/.test(url)) multiProof.renders++;
                  if (options?.method === 'PUT') multiProof.saves++;
                  return original.apply(this, arguments);
                };
              }
            }
            """);
        var box = (await page.Locator("#layer > .it.on").First.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 20, box.Y + 20);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 70, box.Y + 50, new() { Steps = 24 });
        Check(await page.EvaluateAsync<bool>("""
            async () => {
              await new Promise(resolve => requestAnimationFrame(resolve));
              const p = multiProof, outline = p.outline, layer = document.getElementById('layer');
              const scale = parseFloat(layer.style.width) / (await (await pdfHost.request('/api/state')).json()).pages[0].width;
              const selected = [...layer.querySelectorAll(':scope > .on')];
              const moved = selected.every((e, i) => {
                const position = e._it ? [e._it.x,e._it.y] : [e._obj.edit.tx,e._obj.edit.ty];
                return Math.abs(position[0]-p.positions[i][0]-50/scale) < .02 && Math.abs(position[1]-p.positions[i][1]-30/scale) < .02;
              });
              return moved && !p.removed && !p.renders && !p.saves
                && p.selected.every(e => e.isConnected) && p.controls.every(e => e.isConnected)
                && +document.querySelector('[data-group=x]').value > p.x + 10
                && +document.querySelector('[data-group=y]').value > p.y + 10
                && Math.abs(+document.querySelector('[data-group=x]').value - parseFloat(outline.style.left)/scale) < .02
                && Math.abs(+document.querySelector('[data-group=y]').value - parseFloat(outline.style.top)/scale) < .02;
            }
            """), "group drag preserves spacing, text/other nodes and controls, updates live union geometry, and does not render/save before release");
        await page.EvaluateAsync("() => { multiProof.observer.disconnect(); for (const [method, original] of Object.entries(multiProof.methods)) pdfHost[method] = original; }");
        await page.Mouse.UpAsync();
        await SavedItems(page, 2);
        const string savedObjects = """
            async () => {
              const objects = await (await pdfHost.request('/api/page/0/objects')).json();
              return [...document.querySelectorAll('#layer > .obj.on')].every(e => {
                const saved = objects.find(o => o.index === e._obj.index).edit;
                return Object.entries(e._obj.edit).every(([key, value]) => JSON.stringify(saved?.[key]) === JSON.stringify(value));
              });
            }
            """;
        await Until(page, savedObjects);
        await page.Keyboard.PressAsync("Control+z");
        await Until(page, """
            async () => JSON.stringify(await (await pdfHost.request('/api/page/0/items')).json()) === JSON.stringify(multiProof.items)
              && JSON.stringify((await (await pdfHost.request('/api/page/0/objects')).json()).map(o => o.edit)) === JSON.stringify(multiProof.edits)
            """);
        Check(await page.Locator(".selection-box").CountAsync() == 0, "one Undo restores every mixed member and clears temporary selection");
        await page.Keyboard.PressAsync("Control+Shift+z");
        await SavedItems(page, 2);
        await page.Locator("#layer > .it").First.ClickAsync();
        await page.Locator("#layer > .it").Last.ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        await SelectOriginal(page, objectIndex, additive: true);
        await AlignSelected(page, "Center horizontally");
        await CheckCentered(page, horizontal: true);
        var position = await page.EvaluateAsync<double[]>("[+document.querySelector('[data-group=x]').value,+document.querySelector('[data-group=y]').value]");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        Check(await page.EvaluateAsync<bool>("([x,y]) => Math.abs(+document.querySelector('[data-group=x]').value-x-1)<.02 && Math.abs(+document.querySelector('[data-group=y]').value-y-10)<.02", position), "arrow keys nudge the whole group by 1/10 points");
        await page.Locator("#lock").ClickAsync();
        position = await page.EvaluateAsync<double[]>("[+document.querySelector('[data-group=x]').value,+document.querySelector('[data-group=y]').value]");
        await page.Keyboard.PressAsync("ArrowRight");
        Check(await page.EvaluateAsync<bool>("([x,y]) => +document.querySelector('[data-group=x]').value===x && +document.querySelector('[data-group=y]').value===y", position), "geometry locking prevents group movement");
        await page.Locator("#lock").ClickAsync();
        await page.Locator("[data-group=x]").FillAsync((position[0] + 5).ToString(System.Globalization.CultureInfo.InvariantCulture));
        await page.Keyboard.PressAsync("Tab");
        Check(await page.EvaluateAsync<bool>("x => Math.abs(+document.querySelector('[data-group=x]').value-x-5)<.02", position[0]), "group X input moves its union");
        await page.Locator("#editBtn").ClickAsync();
        var menu = page.GetByRole(AriaRole.Menu, new() { Name = "Edit", Exact = true });
        Check(await menu.GetByRole(AriaRole.Menuitem, new() { NameRegex = new("^Delete") }).IsDisabledAsync(), "unsupported multi-object commands are not misleadingly enabled");
        await page.Keyboard.PressAsync("Escape");
        await page.Keyboard.PressAsync("Escape");
        Check(await page.Locator(".selection-box").CountAsync() == 0, "Escape clears the shared selection");
        await SavedItems(page, 2);
        await page.WaitForFunctionAsync(Settled);
        // Reselect so persistence is observed while a temporary selection is active.
        await page.Locator("#layer > .it").First.ClickAsync();
        await page.Locator("#layer > .it").Last.ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        await SelectOriginal(page, objectIndex, additive: true);
        Check(await page.EvaluateAsync<bool>("document.getElementById('layer').dataset.multi === '3'"), "every member is reselected before checking temporary selection persistence");
        await Until(page, savedObjects);
        var raster = await Raster(page);
        await page.Locator("#dl").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());
        var path = await download.PathAsync() ?? throw new Exception("No multi-selection PDF download path.");
        await page.Locator("#open").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync("name => document.getElementById('name').textContent === name && " + Settled, Path.GetFileName(path));
        Check(await Raster(page) == raster, "mixed group movement/alignment retains exact PDF pixels after save/reopen");
        Check(await page.Locator(".selection-box").CountAsync() == 0, "temporary selection does not persist in the PDF");
        await page.EvaluateAsync("delete window.multiProof");
        Console.WriteLine("PASS: mixed multi-selection, keyboard addition, stable group drag/live bounds, atomic Undo/Redo, alignment/nudging/locking, and exact PDF save/reopen.");
    }

    static async Task SelectOriginal(IPage page, int? index = null, bool additive = false)
    {
        await page.Locator("#layer > .obj").EvaluateAllAsync<object>("""
            (nodes, {index, additive}) => {
              const layer = document.getElementById('layer');
              const el = index === null ? nodes.find(e => e.offsetWidth * e.offsetHeight < .9 * layer.offsetWidth * layer.offsetHeight)
                : nodes.find(e => e._obj.index === index);
              const b = el.getBoundingClientRect();
              const options = { bubbles: true, cancelable: true, pointerId: 17, clientX: b.x + b.width / 2, clientY: b.y + b.height / 2, shiftKey: additive };
              el.dispatchEvent(new PointerEvent('pointerdown', options));
              window.dispatchEvent(new PointerEvent('pointerup', options));
            }
            """, new { index, additive });
    }

    static async Task AlignSelected(IPage page, string direction)
    {
        await page.Locator("#editBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menu, new() { Name = "Edit", Exact = true })
            .GetByRole(AriaRole.Menuitem, new() { Name = "Align to page", Exact = true }).HoverAsync();
        await page.Locator(".cm.sub").GetByRole(AriaRole.Menuitem, new() { Name = direction, Exact = true }).ClickAsync();
    }

    static async Task CheckCentered(IPage page, bool horizontal)
    {
        Check(await page.EvaluateAsync<bool>("""
            horizontal => {
              const layer = document.getElementById('layer').getBoundingClientRect();
              const selected = document.querySelector('#layer > .selection-box, #layer > .obj.on, #layer > .it.on');
              const bounds = (document.querySelector('#layer > .selection-box') || selected).getBoundingClientRect();
              return horizontal ? Math.abs(bounds.x + bounds.width / 2 - layer.x - layer.width / 2) < 1
                : Math.abs(bounds.y + bounds.height / 2 - layer.y - layer.height / 2) < 1;
            }
            """, horizontal), "selected object is centered " + (horizontal ? "horizontally" : "vertically") + " on the visible page");
    }

    static async Task EditMenu(IPage page)
    {
        async Task Command(string name)
        {
            await page.Locator("#editBtn").ClickAsync();
            await page.GetByRole(AriaRole.Menu, new() { Name = "Edit", Exact = true })
                .GetByRole(AriaRole.Menuitem, new() { Name = name }).ClickAsync();
        }
        await page.Keyboard.PressAsync("Alt+e");
        var menu = page.GetByRole(AriaRole.Menu, new() { Name = "Edit", Exact = true });
        Check(await menu.GetByRole(AriaRole.Menuitem, new() { Name = "Undo" }).IsDisabledAsync(), "empty history disables Undo");
        Check(await menu.GetByRole(AriaRole.Menuitem, new() { Name = "Redo" }).IsDisabledAsync(), "empty history disables Redo");
        Check(await menu.GetByRole(AriaRole.Menuitem, new() { Name = "Cut" }).IsDisabledAsync(), "no selection disables Cut");
        await page.Keyboard.PressAsync("Escape");
        Check(await page.Locator("#editBtn").EvaluateAsync<bool>("el => document.activeElement === el"), "Escape restores Edit trigger focus");
        await page.Locator("[data-tool=rect]").ClickAsync();
        await page.Locator("#layer").ClickAsync(new() { Position = new() { X = 100, Y = 120 } });
        await SavedItems(page, 1);
        await Command("Duplicate");
        await SavedItems(page, 2);
        await Command("Undo");
        await SavedItems(page, 1);
        await Command("Redo");
        await SavedItems(page, 2);
        await page.Keyboard.PressAsync("Control+z");
        await SavedItems(page, 1);
        await page.Keyboard.PressAsync("Control+Shift+z");
        await SavedItems(page, 2);
        await page.Keyboard.PressAsync("Control+z");
        await SavedItems(page, 1);
        await page.Keyboard.PressAsync("Control+y");
        await SavedItems(page, 2);
        await page.Locator("#layer > .it").Last.ClickAsync();
        await Command("Copy");
        await Command("Deselect");
        await Command("Paste");
        await SavedItems(page, 3);
        await Command("Delete");
        await SavedItems(page, 2);
        await page.Locator("#layer > .it").Last.ClickAsync();
        await Command("Delete");
        await SavedItems(page, 1);
        await page.Locator("#layer > .it").First.ClickAsync();
        await Command("Delete");
        await SavedItems(page, 0);
        await page.Locator("#editBtn").ClickAsync();
        Check(await menu.GetByRole(AriaRole.Menuitem, new() { Name = "Redo" }).IsDisabledAsync(), "new edits invalidate object redo");
        await page.Keyboard.PressAsync("Escape");
        foreach (var width in new[] { 320, 390, 768, 1440 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth && ['fileBtn','editBtn','dl','ghLink'].every(id => { const r = document.getElementById(id).getBoundingClientRect(); return r.width > 0 && r.left >= 0 && r.right <= innerWidth; })"), "File/Edit menus and header fit at width " + width);
        }
        await page.WaitForFunctionAsync(Settled);
        Console.WriteLine("PASS: Edit menu history/clipboard/delete, disabled states, shortcuts, focus return and responsive header.");
    }

    public static async Task Profile(IPage page, bool enforce = true)
    {
        await page.Keyboard.PressAsync("Alt+n");
        await page.Locator(".mdl").WaitForAsync();
        var discard = page.GetByRole(AriaRole.Dialog, new() { Name = "Discard unsaved changes", Exact = true });
        if (await discard.IsVisibleAsync()) await discard.GetByRole(AriaRole.Button, new() { Name = "Discard changes", Exact = true }).ClickAsync();
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
                      let liveGeometry = true;
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
                          liveGeometry &&= ['x','y','w','h'].every(key =>
                            Math.abs(+panel.querySelector(`[data-k=${key}]`).value-selected._it[key]) < .02);
                        }
                        window.dispatchEvent(new PointerEvent('pointerup', final));
                        await new Promise(resolve => requestAnimationFrame(resolve));
                        const current = layer.querySelector('.it.on');
                        const state = await (await pdfHost.request('/api/state')).json();
                        const scale = parseFloat(layer.style.width)/state.pages[0].width;
                        const exact = Math.abs(current._it.x-startX-48/scale) < .001;
                        const percentile = (values, p) => [...values].sort((a,b) => a-b)[Math.ceil(values.length*p)-1];
                        return {objects:40,events:192,cpu:4,removed,panelRemoved,renders,saves,longTasks,exact,liveGeometry,
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
                    Check(result.GetProperty("liveGeometry").GetBoolean(), "geometry inputs must track every drag frame before release");
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

    static Task LiveGeometry(IPage page, string selector) => page.WaitForFunctionAsync("""
            selector => {
              const el = document.querySelector('#layer > .it.on, #layer > .lines:has(line), #layer > .obj.on');
              const it = el?._it, o = el?._obj;
              const e = o?.edit ?? {sx:1,sy:1,tx:0,ty:0};
              const values = it ?? (o && {x:o.x0*e.sx+e.tx,y:o.y0*e.sy+e.ty,w:(o.x1-o.x0)*e.sx,h:(o.y1-o.y0)*e.sy});
              const inputs = [...document.querySelectorAll('#props ' + selector)]
                .filter(input => ['x','y','w','h','x2','y2','size'].includes(input.dataset.k ?? input.dataset.o));
              return values && inputs.length > 0 && inputs
                .every(input => Math.abs(+input.value-values[input.dataset.k ?? input.dataset.o]) < .02);
            }
            """, selector);

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
