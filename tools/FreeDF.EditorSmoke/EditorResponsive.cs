using System.Text.Json;
using Microsoft.Playwright;

// Responsive gate: the shell, menus, dialogs and side-panel sheets must fit with no overflow or clipping across viewport size,
// orientation, device pixel ratio and browser zoom, and touch gestures must work on mobile emulation.
// Screenshots of every configuration go to EDITOR_SMOKE_ARTIFACTS (not committed).
static class EditorResponsive
{
    const string Probe = """
        coarse => {
          const p = [], de = document.documentElement;
          if (de.scrollWidth > innerWidth + 1) p.push(`page scrolls horizontally (${de.scrollWidth} > ${innerWidth})`);
          const h = document.querySelector('header');
          if (h.scrollWidth > h.clientWidth + 1) p.push(`header content is cut off or scrolls (${h.scrollWidth} > ${h.clientWidth})`);
          if (h.getBoundingClientRect().height > 64) p.push(`header wraps (${Math.round(h.getBoundingClientRect().height)}px tall)`);
          for (const id of ['tl', 'tr']) {
            const e = document.getElementById(id);
            if (e.closest('header') || !e.closest('#tools') || e.getBoundingClientRect().top < h.getBoundingClientRect().bottom) p.push(`${id} is not on the second toolbar row`);
          }
          for (const sel of ['#dl', '#fileBtn', '#tl', '#tr', '#status', '#sponsor', '#doc']) {
            const e = document.querySelector(sel); if (!e) continue; const r = e.getBoundingClientRect();
            if (r.width === 0 || getComputedStyle(e).visibility === 'hidden') continue;
            if (r.right > innerWidth + 1 || r.left < -1) p.push(`${sel} is outside the viewport (${Math.round(r.left)}..${Math.round(r.right)} of ${innerWidth})`);
          }
          const footer = document.querySelector('footer').getBoundingClientRect();
          if (footer.bottom > innerHeight + 1 && innerHeight >= 440) p.push('status bar is below the viewport');
          for (const e of document.querySelectorAll('header button, header a, #tools button, #tools label.btn, footer a, footer button')) {
            const r = e.getBoundingClientRect(); if (r.width === 0 || r.height === 0) continue;
            if (r.width < 23.5 || r.height < 23.5) p.push(`target ${e.id || e.className} is ${Math.round(r.width)}x${Math.round(r.height)} (< 24px)`);
            else if (coarse && e.matches('#tools .seg button, header button.icon, #dl') && (r.width < 35.5 || r.height < 35.5)) p.push(`touch target ${e.id || e.dataset.tool} is ${Math.round(r.width)}x${Math.round(r.height)} (< 36px)`);
            if (r.right > innerWidth + 1) p.push(`${e.id || e.className} extends past the right edge`);
          }
          const tools = document.getElementById('tools');
          if (getComputedStyle(tools).overflowX === 'auto' && tools.scrollWidth > tools.clientWidth + 1 && innerWidth <= 900) p.push(`tool bar scrolls sideways at ${innerWidth}px (${tools.scrollWidth} > ${tools.clientWidth})`);
          const view = document.getElementById('view').getBoundingClientRect();
          if (innerHeight >= 440 && view.height < 160) p.push(`document area is only ${Math.round(view.height)}px tall`);
          return p;
        }
        """;

    const string Fits = """
        sel => { const e = document.querySelector(sel); if (!e) return 'missing ' + sel; const r = e.getBoundingClientRect();
          return r.left >= -1 && r.top >= -1 && r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1 ? '' : `${sel} outside viewport: ${[r.left, r.top, r.right, r.bottom].map(Math.round)} in ${innerWidth}x${innerHeight}`; }
        """;

    static Task Click(IPage page, string selector) => page.EvaluateAsync("s => document.querySelector(s).click()", selector);

    public static async Task Run(IBrowser browser, string site, byte[] pdf, string? artifacts)
    {
        if (artifacts is not null) Directory.CreateDirectory(artifacts);
        await LayoutPreferences(browser, site);
        var problems = new List<string>();
        var rows = new List<string>();
        var sizes = new (int W, int H)[] { (320, 568), (375, 667), (390, 844), (768, 1024), (820, 1180), (1024, 768), (1280, 800), (1440, 900), (1920, 1080), (2560, 1440) };
        var matrix = new List<(int W, int H, float Dpr, float Zoom, string Orientation)>();
        foreach (var (w, h) in sizes)
        {
            foreach (var dpr in new[] { 1f, 2f }) matrix.Add((w, h, dpr, 1f, "portrait-or-native"));
            if (w < 1024) matrix.Add((h, w, 2f, 1f, "landscape"));
            else foreach (var zoom in new[] { 1.5f, 2f, 4f }) if (w / zoom >= 320) matrix.Add((w, h, 1f, zoom, "zoom"));
        }

        static async Task LayoutPreferences(IBrowser browser, string site)
        {
            var pdf = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "12-navigation.pdf"));
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 } });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(90000);
            var errors = new List<string>();
            page.PageError += (_, error) => errors.Add(error);
            async Task Open()
            {
                await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "layout.pdf", MimeType = "application/pdf", Buffer = pdf });
                await page.WaitForFunctionAsync("document.getElementById('name').title === 'layout.pdf' && !document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0");
            }
            async Task Check(string expression, string message)
            {
                if (!await page.EvaluateAsync<bool>(expression)) throw new Exception("Layout preferences: " + message);
            }
            await page.GotoAsync(site);
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            await Check("document.getElementById('pageProgress').hidden", "progress must be hidden without a document");
            await Open();
            foreach (var (side, panel, delta) in new[] { ("l", "#left", 60), ("r", "#props", -60) })
            {
                var handle = page.Locator(".rz." + side);
                var box = await handle.BoundingBoxAsync() ?? throw new Exception("Missing resize handle");
                var initial = await page.Locator(panel).BoundingBoxAsync() ?? throw new Exception("Missing panel");
                var hitX = box.X + box.Width / 2;
                var hitY = box.Y + box.Height / 2;
                await page.Mouse.MoveAsync(hitX, hitY);
                await page.Mouse.DownAsync();
                await page.Mouse.MoveAsync(hitX + delta, hitY, new() { Steps = 8 });
                await page.Mouse.UpAsync();
                var resized = await page.Locator(panel).BoundingBoxAsync() ?? throw new Exception("Missing resized panel");
                if (Math.Abs(resized.Width - initial.Width - 60) > 2) throw new Exception("Panel drag did not resize " + side);
                await handle.FocusAsync();
                await page.Keyboard.PressAsync(side == "l" ? "ArrowRight" : "ArrowLeft");
                await Check($"Number(localStorage.getItem('panel-w-{side}')) === {Math.Round(initial.Width) + 76}", "keyboard resize must persist");
            }
            await page.Locator("#appearance").ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Layout and density" }).ClickAsync();
            await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Menus and toolbars", Exact = true }), "comfortable");
            await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Sidebars and controls", Exact = true }), "compact");
            await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Interface text size", Exact = true }), "large");
            await Check("getComputedStyle(document.documentElement).fontSize === '18px' && getComputedStyle(document.querySelector('#props section')).paddingTop === '8px' && getComputedStyle(document.getElementById('tl')).height === '38px'", "density must change actual text, sidebar and toolbar geometry independently");
            await page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
            await page.Locator("#appearance").ClickAsync();
            await Check("getComputedStyle(document.querySelector('.cm-i')).paddingTop === '10px' && document.querySelector('.cm-i').getBoundingClientRect().height >= 38", "menu density must survive the complete stylesheet cascade");
            await page.Keyboard.PressAsync("Escape");
            var total = await page.EvaluateAsync<int>("Number(document.getElementById('pt').textContent.replace(/\\D/g,''))");
            if (total <= 1) throw new Exception("Layout fixture must have multiple pages");
            await page.Locator("#np").ClickAsync();
            await page.WaitForFunctionAsync("document.getElementById('pageProgress').getAttribute('aria-valuenow') === '2'");
            await Check($"!document.getElementById('pageProgress').hidden && document.getElementById('pageProgress').getAttribute('aria-valuemax') === '{total}' && Math.abs(parseFloat(document.querySelector('.page-progress-fill').style.width) - 200 / {total}) < .01", "progress must reflect the page fraction");
            await page.Locator("#pn").FillAsync(total.ToString());
            await page.Locator("#pn").PressAsync("Enter");
            await page.WaitForFunctionAsync("document.querySelector('.page-progress-fill').style.width === '100%'");
            await page.Locator("#tl").ClickAsync();
            await page.SetViewportSizeAsync(820, 1000);
            await page.SetViewportSizeAsync(1440, 1000);
            await Check("localStorage.getItem('panel-l') === '0' && document.querySelector('main').classList.contains('hide-l')", "viewport changes must not clear collapsed state");
            await page.ReloadAsync();
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            await Open();
            await Check("document.documentElement.dataset.menuDensity === 'comfortable' && document.documentElement.dataset.sidebarDensity === 'compact' && document.documentElement.dataset.textSize === 'large' && document.querySelector('main').classList.contains('hide-l') && document.querySelector('.rz.l').getAttribute('aria-valuenow') === '284' && document.querySelector('.rz.r').getAttribute('aria-valuenow') === '356'", "reload must preserve widths, collapsed state and independent density choices");
            await page.Locator("#tl").ClickAsync();
            await page.Locator("#tabContents").ClickAsync();
            await page.WaitForFunctionAsync("document.querySelectorAll('.ct-row').length > 1");
            await Check("Math.abs(document.querySelectorAll('.ct-row')[1].getBoundingClientRect().top - document.querySelectorAll('.ct-row')[0].getBoundingClientRect().top - 24) < 1 && getComputedStyle(document.querySelector('.ct-row')).height === '24px'", "virtualized outline rows must use the selected sidebar density");
            await page.Locator("#appearance").ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Layout and density" }).ClickAsync();
            await EditorChoices.Choose(page, page.GetByRole(AriaRole.Combobox, new() { Name = "Sidebars and controls", Exact = true }), "comfortable");
            await page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("getComputedStyle(document.querySelector('.ct-row')).height === '36px' && Math.abs(document.querySelectorAll('.ct-row')[1].getBoundingClientRect().top - document.querySelectorAll('.ct-row')[0].getBoundingClientRect().top - 36) < 1");
            await page.Keyboard.PressAsync("Alt+n");
            await page.Locator(".dlg .dok").ClickAsync();
            await page.WaitForFunctionAsync("document.getElementById('pt').textContent === '/ 1' && document.getElementById('pageProgress').hidden");
            if (errors.Count > 0) throw new Exception("Layout preferences browser errors: " + string.Join("\n", errors));
            Console.WriteLine("Layout preferences: second-row toggles, drag/keyboard resize, persistence, density, virtualized outline and paging progress ok.");
        }
        foreach (var zoom in new[] { 1.5f, 2f }) matrix.Add((1280, 800, 2f, zoom, "zoom+dpr2"));

        foreach (var (w, h, dpr, zoom, tag) in matrix)
        {
            var name = $"{w}x{h}-dpr{dpr}-zoom{zoom * 100:0}";
            if (Environment.GetEnvironmentVariable("EDITOR_RESPONSIVE_FILTER") is { Length: > 0 } filter && !name.Contains(filter)) continue;
            var coarse = w < 1024 && h < 1300 || w < 600;
            await using var context = await browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = (int)Math.Round(w / zoom), Height = (int)Math.Round(h / zoom) }, DeviceScaleFactor = dpr * zoom,
                HasTouch = coarse, IsMobile = coarse, AcceptDownloads = true
            });
            var page = await context.NewPageAsync(); page.SetDefaultTimeout(90000);
            await page.GotoAsync(site);
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            var issues = new List<string>();
            async Task Check(string state)
            {
                foreach (var issue in await page.EvaluateAsync<string[]>(Probe, coarse)) issues.Add($"[{state}] {issue}");
            }
            await Check("welcome");
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = "responsive.pdf", MimeType = "application/pdf", Buffer = pdf });
            await page.WaitForFunctionAsync("document.getElementById('name').title === 'responsive.pdf' && !document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0");
            await page.WaitForTimeoutAsync(400);
            await Check("document");
            if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, name + "-document.png") });

            // Menu fits (scrolls inside itself when the viewport is shorter than the menu).
            await Click(page, "#fileBtn"); await page.Locator(".cm:not(.sub)").WaitForAsync();
            if (await page.EvaluateAsync<string>(Fits, ".cm:not(.sub)") is { Length: > 0 } menu) issues.Add("[file menu] " + menu);
            if (artifacts is not null && w >= 1024 == false) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, name + "-menu.png") });
            await page.Keyboard.PressAsync("Escape");

            // Dialogs fit and their buttons stay reachable.
            foreach (var (state, open) in new (string, Func<Task>)[]
            {
                ("new dialog", () => page.Keyboard.PressAsync("Alt+n")),
                ("save dialog", () => Click(page, "#dl")),
                ("properties dialog", async () => { await Click(page, "#fileBtn"); await page.GetByRole(AriaRole.Menuitem, new() { Name = "Document properties" }).First.EvaluateAsync("e => e.click()"); }),
                ("merge dialog", () => page.Locator("#merge").SetInputFilesAsync(new[] { new FilePayload { Name = "a.pdf", MimeType = "application/pdf", Buffer = pdf }, new FilePayload { Name = "b.pdf", MimeType = "application/pdf", Buffer = pdf } })),
            })
            {
                await open(); await page.Locator(".dlg").WaitForAsync(); await page.WaitForTimeoutAsync(350);
                if (await page.EvaluateAsync<string>(Fits, ".dlg") is { Length: > 0 } fit) issues.Add($"[{state}] {fit}");
                if (!await page.EvaluateAsync<bool>("() => { const b = document.querySelector('.dlg .dok'), r = b.getBoundingClientRect(); return r.bottom <= innerHeight + 1 && r.right <= innerWidth + 1 && r.top >= -1; }")) issues.Add($"[{state}] primary button is not reachable without scrolling the page");
                if (state is "new dialog" or "save dialog" && artifacts is not null)
                    await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, name + "-" + state.Replace(' ', '-') + ".png") });
                await page.Keyboard.PressAsync("Escape"); await page.WaitForTimeoutAsync(100);
            }

            // Panels collapse into sheets on narrow screens.
            var viewWidth = (int)Math.Round(w / zoom);
            if (viewWidth <= 760)
            {
                foreach (var (toggle, panel) in new[] { ("#tl", "#left"), ("#tr", "#props") })
                {
                    var open = await page.EvaluateAsync<bool>("p => !document.querySelector('main').classList.contains(p === '#left' ? 'hide-l' : 'hide-r')", panel);
                    if (!open) await Click(page, toggle);
                    await page.WaitForTimeoutAsync(300);
                    var sheet = await page.EvaluateAsync<string>("p => { const e = document.querySelector(p), s = getComputedStyle(e), r = e.getBoundingClientRect(); return s.position === 'absolute' && r.width >= Math.min(120, innerWidth / 2) && r.width <= innerWidth * .9 + 1 && r.left >= -1 && r.right <= innerWidth + 1 && getComputedStyle(document.querySelector('main'), '::before').content !== 'none' ? '' : `not a sheet: position ${s.position}, width ${Math.round(r.width)} of ${innerWidth}, scrim ${getComputedStyle(document.querySelector('main'), '::before').content}`; }", panel);
                    if (sheet != "") issues.Add($"[{panel}] {sheet}");
                    var placement = await page.EvaluateAsync<string>("p => { const r = document.querySelector(p).getBoundingClientRect(), t = document.getElementById('tools').getBoundingClientRect(), m = document.querySelector('main').getBoundingClientRect(); return Math.abs(r.top - t.bottom) <= 1 && Math.abs(r.bottom - m.bottom) <= 1 ? '' : `sheet bounds ${Math.round(r.top)}..${Math.round(r.bottom)} should span toolbar bottom ${Math.round(t.bottom)} to main bottom ${Math.round(m.bottom)}`; }", panel);
                    if (placement != "") issues.Add($"[{panel}] {placement}");
                    var width = await page.EvaluateAsync<string>("p => { const r = document.querySelector(p).getBoundingClientRect(), h = document.querySelector(p === '#left' ? '.rz.l' : '.rz.r'); const expected = Math.min(Number(h.getAttribute('aria-valuenow')), innerWidth * .88); return Math.abs(r.width - expected) <= 1 ? '' : `sheet width ${Math.round(r.width)} should use saved resize width ${Math.round(expected)}`; }", panel);
                    if (width != "") issues.Add($"[{panel}] {width}");
                    await Check($"{panel} sheet open");
                    if (!open) await Click(page, toggle);
                }
                if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, name + "-sheet.png") });
            }

            if (coarse && w is >= 360 and <= 820 && tag != "landscape") await Touch(browser, site, w, h, dpr, issues);
            rows.Add($"| {name} | {tag} | {(issues.Count == 0 ? "ok" : issues.Count + " issue(s)")} |");
            Console.WriteLine($"{name}: {(issues.Count == 0 ? "ok" : "\n   " + string.Join("\n   ", issues))}");
            problems.AddRange(issues.Select(i => $"{name} {i}"));
        }

        var theme = await new HttpClient().GetStringAsync(site + "editor.css");
        if (!theme.Contains("env(safe-area-inset-left)") || !theme.Contains("env(safe-area-inset-bottom)")) problems.Add("the merged stylesheet (theme.css rules) does not apply safe-area insets to the header and status bar");
        var viewportMeta = await ViewportMeta(browser, site);
        if (viewportMeta.Contains("user-scalable=no") || viewportMeta.Contains("maximum-scale")) problems.Add("viewport meta blocks pinch zoom: " + viewportMeta);

        Console.WriteLine($"Responsive matrix: {matrix.Count} configurations, {problems.Count} issue(s).");
        if (artifacts is not null) await File.WriteAllTextAsync(Path.Combine(artifacts, "responsive-results.md"), "| Configuration | Kind | Result |\n|---|---|---|\n" + string.Join("\n", rows));
        if (problems.Count > 0) throw new Exception("Responsive: " + problems.Count + " issue(s):\n" + string.Join("\n", problems));
    }

    static async Task<string> ViewportMeta(IBrowser browser, string site)
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync(); await page.GotoAsync(site);
        return await page.EvaluateAsync<string>("() => document.querySelector('meta[name=viewport]').content");
    }

    // Touch gestures through CDP: drag-draw, drag-move, long-press context menu and pinch availability.
    static async Task Touch(IBrowser browser, string site, int w, int h, float dpr, List<string> issues)
    {
        // A fresh context and a blank new page keep earlier checks and page text from influencing the gestures.
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = w, Height = h }, DeviceScaleFactor = dpr, HasTouch = true, IsMobile = true });
        var page = await context.NewPageAsync(); page.SetDefaultTimeout(90000);
        await page.GotoAsync(site);
        await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
        await page.Keyboard.PressAsync("Alt+n");
        await page.Locator(".dlg .dok").ClickAsync();
        await page.WaitForFunctionAsync("document.getElementById('base')?.naturalWidth > 0 && !document.getElementById('view').inert");
        await page.WaitForTimeoutAsync(400);
        var cdp = await context.NewCDPSessionAsync(page);
        Task Send(string type, double x, double y) => cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = type, ["touchPoints"] = type == "touchEnd" ? Array.Empty<object>() : new object[] { new { x, y } } });
        var vis = await page.EvaluateAsync<double[]>("() => { const p = document.getElementById('page').getBoundingClientRect(), v = document.getElementById('view').getBoundingClientRect(); const l = Math.max(p.left, v.left), t = Math.max(p.top, v.top); return [l, t, Math.min(p.right, v.right) - l, Math.min(p.bottom, v.bottom) - t]; }");
        var box = new { X = vis[0], Y = vis[1], Width = vis[2], Height = vis[3] };
        if (box.Width < 60 || box.Height < 60) { issues.Add("[touch] visible page area is too small to gesture on"); return; }
        await page.Locator("[data-tool=rect]").TapAsync();
        double x0 = box.X + box.Width * .3, y0 = box.Y + box.Height * .3;
        await Send("touchStart", x0, y0); for (var i = 1; i <= 8; i++) await Send("touchMove", x0 + i * box.Width * .07, y0 + i * box.Height * .06); await Send("touchEnd", 0, 0);
        await page.WaitForTimeoutAsync(400);
        if (await page.Locator("#layer .it").CountAsync() != 1) issues.Add("[touch] dragging with a finger did not draw a rectangle");
        await page.Locator("[data-tool=select]").TapAsync();
        var item = await page.Locator("#layer .it").First.BoundingBoxAsync();
        if (item is null) return;
        double cx = item.X + item.Width / 2, cy = item.Y + item.Height / 2;
        var menuOpened = false;
        for (var attempt = 0; attempt < 2 && !menuOpened; attempt++)
        {
            await Send("touchStart", cx, cy); await page.WaitForTimeoutAsync(900); await Send("touchEnd", 0, 0); await page.WaitForTimeoutAsync(250);
            menuOpened = await page.EvaluateAsync<bool>("() => !!document.querySelector('.cm')");
        }
        if (!menuOpened) issues.Add("[touch] long-press did not open the context menu: " + await page.EvaluateAsync<string>("([x, y]) => { const e = document.elementFromPoint(x, y); return `${e?.tagName}.${e?.className} tool=${document.querySelector('[data-tool].on')?.dataset.tool} items=${document.querySelectorAll('#layer .it').length} point=${Math.round(x)},${Math.round(y)} vp=${innerWidth}x${innerHeight}`; }", new[] { cx, cy }));
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(150);
        await Send("touchStart", cx, cy); for (var i = 1; i <= 6; i++) await Send("touchMove", cx + i * 10, cy + i * 10); await Send("touchEnd", 0, 0);
        await page.WaitForTimeoutAsync(300);
        var moved = await page.Locator("#layer .it").First.BoundingBoxAsync();
        if (moved is null || Math.Abs(moved.X - item.X) + Math.Abs(moved.Y - item.Y) < 4) issues.Add("[touch] dragging an object with a finger did not move it");
        if (!await page.EvaluateAsync<bool>("() => getComputedStyle(document.getElementById('view')).touchAction !== 'none'")) issues.Add("[touch] the document area disables browser panning and pinch");
    }
}
