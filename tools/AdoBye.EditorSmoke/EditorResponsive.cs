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
            else if (coarse && e.matches('#tools .seg button, header button.icon, #dl') && (r.width < 39.5 || r.height < 39.5)) p.push(`touch target ${e.id || e.dataset.tool} is ${Math.round(r.width)}x${Math.round(r.height)} (< 40px)`);
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
        foreach (var zoom in new[] { 1.5f, 2f }) matrix.Add((1280, 800, 2f, zoom, "zoom+dpr2"));

        foreach (var (w, h, dpr, zoom, tag) in matrix)
        {
            var name = $"{w}x{h}-dpr{dpr}-zoom{zoom * 100:0}";
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
                ("save dialog", () => Click(page, "#dl")),
                ("properties dialog", async () => { await Click(page, "#fileBtn"); await page.GetByRole(AriaRole.Menuitem, new() { Name = "Document properties" }).First.EvaluateAsync("e => e.click()"); }),
                ("merge dialog", () => page.Locator("#merge").SetInputFilesAsync(new[] { new FilePayload { Name = "a.pdf", MimeType = "application/pdf", Buffer = pdf }, new FilePayload { Name = "b.pdf", MimeType = "application/pdf", Buffer = pdf } })),
            })
            {
                await open(); await page.Locator(".dlg").WaitForAsync(); await page.WaitForTimeoutAsync(350);
                if (await page.EvaluateAsync<string>(Fits, ".dlg") is { Length: > 0 } fit) issues.Add($"[{state}] {fit}");
                if (!await page.EvaluateAsync<bool>("() => { const b = document.querySelector('.dlg .dok'), r = b.getBoundingClientRect(); return r.bottom <= innerHeight + 1 && r.right <= innerWidth + 1 && r.top >= -1; }")) issues.Add($"[{state}] primary button is not reachable without scrolling the page");
                if (state == "save dialog" && artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, name + "-dialog.png") });
                await page.Keyboard.PressAsync("Escape"); await page.WaitForTimeoutAsync(100);
            }

            // Panels collapse into sheets on narrow screens.
            var viewWidth = (int)Math.Round(w / zoom);
            if (viewWidth <= 760)
            {
                foreach (var (toggle, panel) in new[] { ("#tl", "#thumbs"), ("#tr", "#props") })
                {
                    var open = await page.EvaluateAsync<bool>("p => !document.querySelector('main').classList.contains(p === '#thumbs' ? 'hide-l' : 'hide-r')", panel);
                    if (!open) await Click(page, toggle);
                    await page.WaitForTimeoutAsync(300);
                    var sheet = await page.EvaluateAsync<string>("p => { const e = document.querySelector(p), s = getComputedStyle(e), r = e.getBoundingClientRect(); return s.position === 'absolute' && r.width <= innerWidth * .9 + 1 && r.left >= -1 && r.right <= innerWidth + 1 && getComputedStyle(document.querySelector('main'), '::before').content !== 'none' ? '' : `not a sheet: position ${s.position}, width ${Math.round(r.width)} of ${innerWidth}, scrim ${getComputedStyle(document.querySelector('main'), '::before').content}`; }", panel);
                    if (sheet != "") issues.Add($"[{panel}] {sheet}");
                    await Check($"{panel} sheet open");
                    if (!open) await Click(page, toggle);
                }
                if (artifacts is not null) await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, name + "-sheet.png") });
            }

            if (coarse && w <= 820 && tag != "landscape") await Touch(page, issues);
            rows.Add($"| {name} | {tag} | {(issues.Count == 0 ? "ok" : issues.Count + " issue(s)")} |");
            Console.WriteLine($"{name}: {(issues.Count == 0 ? "ok" : "\n   " + string.Join("\n   ", issues))}");
            problems.AddRange(issues.Select(i => $"{name} {i}"));
        }

        var theme = await new HttpClient().GetStringAsync(site + "theme.css");
        if (!theme.Contains("env(safe-area-inset-left)") || !theme.Contains("env(safe-area-inset-bottom)")) problems.Add("theme.css does not apply safe-area insets to the header and status bar");
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
    static async Task Touch(IPage page, List<string> issues)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        Task Send(string type, double x, double y) => cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = type, ["touchPoints"] = type == "touchEnd" ? Array.Empty<object>() : new object[] { new { x, y } } });
        var box = await page.Locator("#page").BoundingBoxAsync();
        if (box is null) { issues.Add("[touch] page canvas missing"); return; }
        await page.Locator("[data-tool=rect]").TapAsync();
        double x0 = box.X + box.Width * .3, y0 = box.Y + box.Height * .3;
        await Send("touchStart", x0, y0); for (var i = 1; i <= 8; i++) await Send("touchMove", x0 + i * 12, y0 + i * 10); await Send("touchEnd", 0, 0);
        await page.WaitForTimeoutAsync(400);
        if (await page.Locator("#layer .it").CountAsync() != 1) issues.Add("[touch] dragging with a finger did not draw a rectangle");
        await page.Locator("[data-tool=select]").TapAsync();
        var item = await page.Locator("#layer .it").First.BoundingBoxAsync();
        if (item is null) return;
        double cx = item.X + item.Width / 2, cy = item.Y + item.Height / 2;
        await Send("touchStart", cx, cy); await page.WaitForTimeoutAsync(900); await Send("touchEnd", 0, 0); await page.WaitForTimeoutAsync(250);
        if (!await page.EvaluateAsync<bool>("() => !!document.querySelector('.cm')")) issues.Add("[touch] long-press did not open the context menu");
        await page.Keyboard.PressAsync("Escape");
        await Send("touchStart", cx, cy); for (var i = 1; i <= 6; i++) await Send("touchMove", cx + i * 8, cy + i * 8); await Send("touchEnd", 0, 0);
        await page.WaitForTimeoutAsync(300);
        var moved = await page.Locator("#layer .it").First.BoundingBoxAsync();
        if (moved is null || Math.Abs(moved.X - item.X) < 8) issues.Add("[touch] dragging an object with a finger did not move it");
        if (!await page.EvaluateAsync<bool>("() => getComputedStyle(document.getElementById('view')).touchAction !== 'none'")) issues.Add("[touch] the document area disables browser panning and pinch");
    }
}
