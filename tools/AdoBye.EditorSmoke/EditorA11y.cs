using System.Text.Json;
using Microsoft.Playwright;

// Accessibility gate: axe-core on every major screen, theme and viewport, plus keyboard, focus-ring, target-size and
// text-spacing assertions that axe cannot make. axe-core is a dev/test dependency (tools/AdoBye.EditorSmoke/a11y, `npm ci`)
// and is injected into the page by this test only; it is never part of the published site.
static class EditorA11y
{
    sealed record Row(string Screen, string Theme, int Serious, int Minor, string Notes);

    const string AxeRun = """
        async forced => {
          const exempt = v => v.id === 'label-content-name-mismatch';
          const r = await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'] },
            rules: forced ? { 'color-contrast': { enabled: false } } : {}, resultTypes: ['violations'] });
          return JSON.stringify(r.violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.map(n => ({ target: n.target.join(' '), html: n.html.slice(0, 140), exempt:
            // A menuitem that owns an open submenu has the submenu text in its visible text but not its name; the name is the item label (ARIA authoring practice).
            exempt(v) && /^<div[^>]*class="cm-i[^"]*has-sub/.test(n.html) })) })));
        }
        """;

    const string TargetProbe = """
        () => [...document.querySelectorAll('button, a[href], input:not([type=hidden]), select, textarea, summary, [role=button], [role=menuitem], [tabindex="0"]')]
          .filter(e => { const r = e.getBoundingClientRect(), s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && !e.closest('#layer, .wgt, .sr-only, .skip, [hidden], .sprite, #drop-never, .hd') && !(e.matches('input[type=file]')); })
          .filter(e => { const r = e.getBoundingClientRect(); return r.width < 23.5 || r.height < 23.5; })
          .map(e => (e.id || e.className || e.tagName) + ' ' + Math.round(e.getBoundingClientRect().width) + 'x' + Math.round(e.getBoundingClientRect().height))
        """;

    const string FocusProbe = """
        sel => {
          const out = [];
          const lum = c => { const f = v => { v /= 255; return v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }; return .2126 * f(c[0]) + .7152 * f(c[1]) + .0722 * f(c[2]); };
          const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
          const ctx = canvas.getContext('2d', { willReadFrequently: true });
          const rgb = s => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = s; ctx.fillRect(0, 0, 1, 1); const p = [...ctx.getImageData(0, 0, 1, 1).data]; return [p[0], p[1], p[2], p[3] / 255]; };
          const bgOf = e => { for (let n = e; n; n = n.parentElement) { const c = rgb(getComputedStyle(n).backgroundColor); if (c && c[3] > .5) return c; } return [255, 255, 255, 1]; };
          for (const e of document.querySelectorAll(sel)) {
            e.focus({ focusVisible: true }); if (document.activeElement !== e) continue;
            const s = getComputedStyle(e), w = parseFloat(s.outlineWidth), c = rgb(s.outlineColor);
            if (s.outlineStyle === 'none' || !c || w < 2) { out.push((e.id || e.className) + ': no 2px focus outline'); continue; }
            const a = lum(c) + .05, b = lum(bgOf(parseFloat(s.outlineOffset) > 0 ? (e.parentElement ?? e) : e)) + .05, ratio = Math.max(a, b) / Math.min(a, b);
            if (ratio < 3) out.push((e.id || e.className) + ': focus ring contrast ' + ratio.toFixed(2));
          }
          return out;
        }
        """;

    const string SpacingStyle = "*{line-height:1.5 !important;letter-spacing:.12em !important;word-spacing:.16em !important}p{margin-bottom:2em !important}";

    const string ClipProbe = """
        () => [...document.querySelectorAll('header button, #tools button, footer button, .dfoot button, .dlg label, #props label, .cm-i')]
          .filter(e => { const r = e.getBoundingClientRect(), s = getComputedStyle(e); return r.width > 0 && s.overflow !== 'visible' && s.textOverflow !== 'ellipsis' && e.scrollWidth > e.clientWidth + 2; })
          .map(e => (e.id || e.className) + ' ' + e.scrollWidth + '>' + e.clientWidth)
        """;

    static string? FindAxe()
    {
        var env = Environment.GetEnvironmentVariable("EDITOR_AXE_PATH");
        if (env is not null && File.Exists(env)) return env;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                foreach (var rel in new[] { "tools/AdoBye.EditorSmoke/a11y/node_modules/axe-core/axe.min.js", "a11y/node_modules/axe-core/axe.min.js" })
                {
                    var path = Path.Combine(dir.FullName, rel);
                    if (File.Exists(path)) return path;
                }
        return null;
    }

    public static async Task Run(IBrowser browser, string site, byte[] pdf, string? artifacts)
    {
        var axePath = FindAxe() ?? throw new Exception("axe-core is not installed. Run `npm ci` in tools/AdoBye.EditorSmoke/a11y (dev/test only), or set EDITOR_AXE_PATH.");
        var axeSource = await File.ReadAllTextAsync(axePath);
        var rows = new List<Row>();
        var failures = new List<string>();

        void Fail(string message) { failures.Add(message); Console.Error.WriteLine("FAIL: " + message); }
        void Expect(bool ok, string message) { Console.WriteLine((ok ? "PASS " : "FAIL ") + message); if (!ok) failures.Add(message); }

        async Task Axe(IPage page, string screen, string theme, bool forced = false)
        {
            await page.WaitForTimeoutAsync(300);
            if (!await page.EvaluateAsync<bool>("() => !!window.axe")) await page.AddScriptTagAsync(new() { Content = axeSource });
            using var doc = JsonDocument.Parse(await page.EvaluateAsync<string>(AxeRun, forced));
            int serious = 0, minor = 0; var notes = new List<string>();
            foreach (var v in doc.RootElement.EnumerateArray())
            {
                var impact = v.GetProperty("impact").GetString();
                var nodes = v.GetProperty("nodes").EnumerateArray().Where(n => !n.GetProperty("exempt").GetBoolean()).ToList();
                if (nodes.Count == 0) continue;
                if (impact is "serious" or "critical")
                {
                    serious += nodes.Count;
                    var detail = $"{screen} [{theme}] {v.GetProperty("id").GetString()} ({impact}): " + string.Join(" | ", nodes.Take(3).Select(n => n.GetProperty("target").GetString()));
                    notes.Add(detail); Fail(detail);
                }
                else minor += nodes.Count;
            }
            rows.Add(new(screen, theme, serious, minor, string.Join("; ", notes)));
            Console.WriteLine($"axe {theme,-9} {screen,-34} serious/critical={serious} minor/moderate={minor}");
            if (artifacts is not null && serious > 0)
                await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"a11y-fail-{theme}-{screen.Replace(' ', '-')}.png") });
        }

        async Task<(IBrowserContext Context, IPage Page)> Fresh(string theme, int w = 1440, int h = 900, bool touch = false, ForcedColors? forced = null, ReducedMotion? motion = null)
        {
            var context = await browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = w, Height = h }, AcceptDownloads = true, HasTouch = touch,
                ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light, ForcedColors = forced, ReducedMotion = motion
            });
            var page = await context.NewPageAsync(); page.SetDefaultTimeout(90000);
            await page.GotoAsync(site);
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            return (context, page);
        }

        static Task Click(IPage page, string selector) => page.EvaluateAsync("s => document.querySelector(s).click()", selector);
        static async Task Esc(IPage page) { await page.Keyboard.PressAsync("Escape"); await page.WaitForTimeoutAsync(80); }
        static async Task Open(IPage page, byte[] pdf, string name = "a11y-sample.pdf")
        {
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "application/pdf", Buffer = pdf });
            await page.WaitForFunctionAsync("n => document.getElementById('name').title === n && !document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0", name);
            await page.WaitForTimeoutAsync(400);
        }
        static async Task FileMenu(IPage page, string item)
        {
            await Click(page, "#fileBtn");
            await page.GetByRole(AriaRole.Menuitem, new() { Name = item }).First.EvaluateAsync("e => e.click()");
        }
        async Task SubMenu(IPage page, string parent, string child)
        {
            await Click(page, "#fileBtn");
            await page.GetByRole(AriaRole.Menuitem, new() { Name = parent }).First.HoverAsync(new() { Force = true });
            await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextString = child }).First.EvaluateAsync("e => e.click()");
        }

        // ---- Full scenario in each theme ----
        foreach (var theme in new[] { "light", "dark" })
        {
            var (context, page) = await Fresh(theme);
            await Axe(page, "welcome", theme);
            await Click(page, "#appBtn"); await Axe(page, "app and offline status", theme);
            Expect(!await page.Locator("[data-app-open]").IsVisibleAsync(), "App hides Open received PDFs when no files are pending");
            await Esc(page);
            await Open(page, pdf);
            await Axe(page, "document open", theme);
            await Click(page, "#fileBtn"); await Axe(page, "file menu", theme);
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Insert" }).HoverAsync(); await Axe(page, "file submenu", theme); await Esc(page);
            await Click(page, "#zv"); await Axe(page, "zoom menu", theme); await Esc(page);
            await Click(page, "#appearance"); await Axe(page, "appearance menu", theme); await Esc(page);
            await page.Keyboard.PressAsync("Control+k"); await Axe(page, "command palette", theme); await Esc(page);
            await page.Keyboard.PressAsync("Shift+/"); await Axe(page, "shortcuts and sponsor/about", theme); await Esc(page);
            await Click(page, "#fb"); await Axe(page, "find bar", theme); await Esc(page);
            await Click(page, "#dl"); await Axe(page, "save dialog", theme); await Esc(page);
            await FileMenu(page, "Document properties"); await Axe(page, "document properties dialog", theme); await Esc(page);
            await FileMenu(page, "New"); await Axe(page, "new document dialog", theme); await Esc(page);
            await SubMenu(page, "Export", "Pages as images"); await Axe(page, "export images dialog", theme); await Esc(page);
            await SubMenu(page, "Export", "password-protected"); await Axe(page, "protect dialog", theme); await Esc(page);
            await page.Locator("#merge").SetInputFilesAsync(new[] { new FilePayload { Name = "a.pdf", MimeType = "application/pdf", Buffer = pdf }, new FilePayload { Name = "b.pdf", MimeType = "application/pdf", Buffer = pdf } });
            await page.Locator(".mdl").WaitForAsync(); await Axe(page, "merge dialog", theme); await Esc(page);
            await page.Locator("#layer .span").First.ClickAsync(new() { Force = true }); await Axe(page, "text selected (properties panel)", theme);
            await page.Locator("[data-tool=rect]").ClickAsync(); await page.Mouse.MoveAsync(500, 300); await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(650, 420); await page.Mouse.UpAsync();
            await Axe(page, "shape selected (properties panel)", theme);
            await Click(page, "#annot"); await Axe(page, "annotations dock", theme); await Click(page, "#annot");
            if (await page.Locator("#formBtn").CountAsync() > 0) { await Click(page, "#formBtn"); await Axe(page, "form mode menu", theme); await Esc(page); }
            await page.Locator("[data-tool=select]").ClickAsync();
            await Click(page, "#tl"); await Axe(page, "pages panel collapsed", theme); await Click(page, "#tl");
            await Click(page, "#tr"); await Axe(page, "properties panel collapsed", theme); await Click(page, "#tr");
            await page.Keyboard.PressAsync("Control+k"); await page.Keyboard.TypeAsync("sam"); await Axe(page, "command palette filtered", theme); await Esc(page);
            // Resume/recovery prompt: edit, wait for the durable copy, reload.
            await page.WaitForFunctionAsync("document.getElementById('recovery').textContent === 'Recovery saved'");
            await page.ReloadAsync();
            await page.GetByRole(AriaRole.Dialog, new() { Name = "Resume previous edit", Exact = true }).WaitForAsync();
            await Axe(page, "resume previous edit dialog", theme);
            await context.CloseAsync();
        }

        // ---- Forced colors (Windows high contrast): contrast is the system's job, structure is still checked ----
        {
            var (context, page) = await Fresh("light", forced: ForcedColors.Active);
            Expect(await page.EvaluateAsync<bool>("matchMedia('(forced-colors: active)').matches"), "forced-colors emulation is active");
            await Axe(page, "welcome", "forced", true); await Open(page, pdf);
            await Axe(page, "document open", "forced", true);
            await Click(page, "#fileBtn"); await Axe(page, "file menu", "forced", true); await Esc(page);
            await Click(page, "#dl"); await Axe(page, "save dialog", "forced", true); await Esc(page);
            await context.CloseAsync();
        }

        // ---- Reduced motion: no animation or transition may run ----
        {
            var (context, page) = await Fresh("light", motion: ReducedMotion.Reduce);
            await Open(page, pdf); await Click(page, "#dl"); await page.Locator(".dlg").WaitForAsync();
            var running = await page.EvaluateAsync<string>("() => JSON.stringify(document.getAnimations().filter(a => a.playState === 'running').map(a => a.animationName || a.transitionProperty))");
            Expect(running == "[]", "prefers-reduced-motion: no running animations or transitions (" + running + ")");
            Expect(await page.EvaluateAsync<bool>("() => getComputedStyle(document.querySelector('.dlg')).animationName === 'none' && getComputedStyle(document.querySelector('#dl')).transitionDuration === '0s'"), "prefers-reduced-motion: dialog animation and button transitions are removed");
            await context.CloseAsync();
        }
        {
            var (context, page) = await Fresh("light");
            await page.EvaluateAsync("() => window.editorUi.setMotion(true)");
            Expect(await page.EvaluateAsync<bool>("() => document.documentElement.dataset.motion === 'reduce' && getComputedStyle(document.querySelector('#fileBtn')).transitionDuration === '0s'"), "manual Reduce motion setting removes transitions");
            await page.ReloadAsync(); await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            Expect(await page.EvaluateAsync<bool>("() => document.documentElement.dataset.motion === 'reduce'"), "manual Reduce motion setting persists");
            await context.CloseAsync();
        }

        // ---- Reflow, zoom and mobile widths ----
        foreach (var (label, w, h, touch) in new[] { ("zoom 200% (1280x800)", 640, 400, false), ("zoom 400% (1280x800)", 320, 200, false), ("mobile 320x568", 320, 568, true), ("mobile 375x667", 375, 667, true), ("mobile 390x844", 390, 844, true), ("tablet 820x1180", 820, 1180, true) })
        {
            var (context, page) = await Fresh("light", w, h, touch);
            await Open(page, pdf);
            await Axe(page, $"document @ {label}", "light");
            await Click(page, "#fileBtn"); await Axe(page, $"file menu @ {label}", "light"); await Esc(page);
            if (w <= 820) { await Click(page, "#tl"); await Axe(page, $"pages sheet @ {label}", "light"); await Click(page, "#tl"); }
            await Click(page, "#dl"); await Axe(page, $"save dialog @ {label}", "light"); await Esc(page);
            var overflow = await page.EvaluateAsync<string>("() => document.documentElement.scrollWidth > innerWidth + 1 ? 'horizontal scroll ' + document.documentElement.scrollWidth + '>' + innerWidth : ''");
            Expect(overflow == "", $"no horizontal page scroll @ {label} {overflow}");
            await context.CloseAsync();
        }

        // ---- Non-axe criteria ----
        foreach (var (theme, w, h) in new[] { ("light", 1440, 900), ("dark", 1440, 900), ("light", 390, 844) })
        {
            var (context, page) = await Fresh(theme, w, h, w < 600);
            await Open(page, pdf);
            var small = await page.EvaluateAsync<string[]>(TargetProbe);
            Expect(small.Length == 0, $"WCAG 2.2 target size >= 24x24 [{theme} {w}x{h}] {string.Join(", ", small)}");
            var ring = await page.EvaluateAsync<string[]>(FocusProbe, "header button, #tools button, #pn, #fileBtn, a.sponsor, #sponsorBadge, #props button, #thumbs .thumb, #view");
            Expect(ring.Length == 0, $"visible focus ring >= 2px and 3:1 [{theme} {w}x{h}] {string.Join(", ", ring)}");
            await page.AddStyleTagAsync(new() { Content = SpacingStyle }); await page.WaitForTimeoutAsync(200);
            var clipped = await page.EvaluateAsync<string[]>(ClipProbe);
            Expect(clipped.Length == 0, $"text-spacing override clips nothing [{theme} {w}x{h}] {string.Join(", ", clipped)}");
            await context.CloseAsync();
        }

        await Keyboard(browser, site, pdf, Fresh, Open, Click, Expect);

        Console.WriteLine();
        Console.WriteLine("| Screen | Theme | Serious/critical | Other (moderate/minor) |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var r in rows) Console.WriteLine($"| {r.Screen} | {r.Theme} | {r.Serious} | {r.Minor} |");
        if (artifacts is not null) await File.WriteAllTextAsync(Path.Combine(artifacts, "a11y-results.md"), "| Screen | Theme | Serious/critical | Other |\n|---|---|---|---|\n" + string.Join("\n", rows.Select(r => $"| {r.Screen} | {r.Theme} | {r.Serious} | {r.Minor} |")));
        if (failures.Count > 0) throw new Exception($"Accessibility: {failures.Count} failure(s):\n" + string.Join("\n", failures));
    }

    static async Task Keyboard(IBrowser browser, string site, byte[] pdf,
        Func<string, int, int, bool, ForcedColors?, ReducedMotion?, Task<(IBrowserContext Context, IPage Page)>> fresh,
        Func<IPage, byte[], string, Task> open, Func<IPage, string, Task> click, Action<bool, string> expect)
    {
        var (context, page) = await fresh("light", 1440, 900, false, null, null);
        await page.Keyboard.PressAsync("Tab");
        expect(await page.EvaluateAsync<bool>("() => document.title === 'FreeDF - PDF Editor' && document.querySelector('.brand strong').textContent === 'FreeDF' && document.querySelector('h1').textContent === 'FreeDF PDF editor'"), "branding: FreeDF title, wordmark and accessible heading");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.className === 'skip'"), "keyboard: first Tab stop is the skip link");
        await page.Keyboard.PressAsync("Enter");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.id === 'view'"), "keyboard: skip link moves focus to the document region");
        expect(await page.EvaluateAsync<bool>("() => document.documentElement.lang === 'en' && !!document.title && !!document.querySelector('main') && !!document.querySelector('header') && !!document.querySelector('footer') && document.querySelectorAll('h1').length === 1"), "landmarks, lang, title and a single h1 exist");

        await page.Keyboard.PressAsync("Control+k");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.matches('.pal input') && document.querySelector('header').inert && document.querySelector('main').inert"), "keyboard: command palette traps focus and makes the background inert");
        await page.Keyboard.TypeAsync("dark"); await page.Keyboard.PressAsync("Enter"); await page.WaitForTimeoutAsync(100);
        expect(await page.EvaluateAsync<bool>("() => document.documentElement.dataset.theme === 'dark' && !document.querySelector('header').inert && localStorage.getItem('editor.theme') === 'dark'"), "keyboard: palette switches theme, persists it and restores the background");
        await page.EvaluateAsync("() => window.editorUi.setTheme('system')");

        expect(await page.EvaluateAsync<bool>("() => window.editorUi.primary() === 'pink' && window.editorUi.accent() === 'medium' && getComputedStyle(document.documentElement).getPropertyValue('--primary').trim() === '#be185d' && document.querySelector('meta[name=theme-color]').content === '#be185d'"), "colors: default pink primary and Medium accent shade");
        await click(page, "#appearance");
        var primaryRow = page.GetByRole(AriaRole.Group, new() { Name = "Primary color", Exact = true });
        var accentRow = page.GetByRole(AriaRole.Group, new() { Name = "Accent shade", Exact = true });
        await primaryRow.GetByRole(AriaRole.Menuitemradio, new() { Name = "Magenta", Exact = true }).FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await accentRow.GetByRole(AriaRole.Menuitemradio, new() { Name = "Dark", Exact = true }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        expect(await primaryRow.GetByRole(AriaRole.Menuitemradio, new() { Name = "Magenta", Exact = true }).GetAttributeAsync("aria-checked") == "true", "colors: keyboard activation changes independent primary role");
        await page.Keyboard.PressAsync("Escape");
        await page.ReloadAsync(); await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
        expect(await page.EvaluateAsync<bool>("() => document.documentElement.dataset.accent === 'dark' && document.documentElement.dataset.primary === 'magenta' && getComputedStyle(document.documentElement).getPropertyValue('--primary').trim() === '#a21caf' && localStorage.getItem('editor.accent') === 'dark' && localStorage.getItem('editor.primary') === 'magenta' && document.querySelector('meta[name=theme-color]').content === '#a21caf'"), "colors: primary and shade persist across reloads");
        expect(await page.EvaluateAsync<bool>("() => { const sw = document.createElement('span'); sw.style.color = 'var(--accent)'; document.body.append(sw); const before = getComputedStyle(sw).color; window.editorUi.setPrimary('cyan'); const changed = before !== getComputedStyle(sw).color && window.editorUi.accent() === 'dark'; sw.remove(); return changed; }"), "colors: changing primary recolors the accent without changing shade");
        await click(page, "#appearance");
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Reset PinkInk colors", Exact = true }).ClickAsync();
        expect(await page.EvaluateAsync<bool>("() => window.editorUi.primary() === 'pink' && window.editorUi.accent() === 'medium' && localStorage.getItem('editor.primary') === null && localStorage.getItem('editor.accent') === null"), "colors: reset restores PinkInk defaults");
        var contrastErrors = await page.EvaluateAsync<string[]>("""
            () => {
              const probe = document.createElement('span'), canvas = document.createElement('canvas');
              canvas.width = canvas.height = 1; document.body.append(probe);
              const ctx = canvas.getContext('2d'), errors = [];
              const resolve = value => { probe.style.color = value; return getComputedStyle(probe).color; };
              const pixel = (...layers) => {
                ctx.clearRect(0, 0, 1, 1);
                for (const layer of layers) { ctx.fillStyle = resolve(layer); ctx.fillRect(0, 0, 1, 1); }
                return [...ctx.getImageData(0, 0, 1, 1).data].slice(0, 3);
              };
              const lum = rgb => rgb.map(v => { v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; })
                .reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
              const ratio = (a, b) => (Math.max(lum(a), lum(b)) + .05) / (Math.min(lum(a), lum(b)) + .05);
              for (const theme of ['light', 'dark']) {
                editorUi.setTheme(theme);
                for (const primary of ['pink', 'cyan', 'magenta', 'blue', 'violet', 'green', 'orange', 'red', 'teal']) {
                  editorUi.setPrimary(primary);
                  for (const shade of ['light', 'medium', 'dark']) {
                    editorUi.setAccent(shade);
                    if (ratio(pixel('var(--ring)'), pixel('var(--panel)')) < 3)
                      errors.push(`${theme}/${primary}/${shade}: focus contrast`);
                    if (ratio(pixel('var(--accent-2)'), pixel('var(--panel)', 'var(--accent-bg)')) < 4.5)
                      errors.push(`${theme}/${primary}/${shade}: selected text contrast`);
                  }
                }
              }
              probe.remove(); editorUi.setPrimary('pink'); editorUi.setAccent('medium'); editorUi.setTheme('system');
              return errors;
            }
            """);
        expect(contrastErrors.Length == 0, "colors: every primary/shade pair passes focus and selected-text contrast in both themes: " + string.Join(", ", contrastErrors));
        await page.EvaluateAsync("() => { window.__resizeObserved = new Promise(resolve => addEventListener('resize', resolve, { once: true })); }");
        await page.SetViewportSizeAsync(320, 568);
        await page.EvaluateAsync("async () => { await window.__resizeObserved; delete window.__resizeObserved; }");
        await page.Locator("#more").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Appearance and motion", Exact = true }).ClickAsync();
        expect(await page.EvaluateAsync<bool>("() => { const r = document.querySelector('.cm.has-check').getBoundingClientRect(), a = document.getElementById('more').getBoundingClientRect(); return r.top >= a.bottom && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth; }"), "colors: small-screen menu anchors to More and fits the viewport");
        await page.Keyboard.PressAsync("Escape");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.id === 'more'"), "colors: small-screen menu restores focus to More");
        await page.SetViewportSizeAsync(1440, 900);

        await open(page, pdf, "keys.pdf");
        await page.Locator("#fileBtn").FocusAsync(); await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("ArrowDown"); await page.Keyboard.PressAsync("ArrowDown");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.textContent.includes('Open sample')"), "keyboard: arrow keys move through the File menu");
        await page.Keyboard.PressAsync("ArrowRight");
        expect(await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('.cm.sub')"), "keyboard: ArrowRight enters a submenu");
        await page.Keyboard.PressAsync("ArrowLeft");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.classList.contains('has-sub')"), "keyboard: ArrowLeft returns to the parent item");
        await page.Keyboard.PressAsync("End"); await page.Keyboard.PressAsync("Home");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.textContent.startsWith('New')"), "keyboard: Home/End jump within a menu");
        await page.Keyboard.PressAsync("Escape");
        expect(await page.EvaluateAsync<bool>("() => !document.querySelector('.cm') && document.activeElement.id === 'fileBtn'"), "keyboard: Escape closes the menu and returns focus to its button");

        await page.Locator("#dl").FocusAsync(); await page.Keyboard.PressAsync("Enter"); await page.Locator(".dlg").WaitForAsync();
        expect(await page.EvaluateAsync<bool>("() => { const d = document.querySelector('.dlg'); return d.getAttribute('aria-modal') === 'true' && !!d.getAttribute('aria-labelledby') && document.querySelector('main').inert && document.querySelector('header').inert; }"), "dialog: modal, labelled and background inert");
        for (var i = 0; i < 8; i++) await page.Keyboard.PressAsync("Tab");
        expect(await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('.dlg')"), "dialog: Tab never leaves the dialog");
        await page.Keyboard.PressAsync("Escape"); await page.WaitForTimeoutAsync(150);
        expect(await page.EvaluateAsync<bool>("() => !document.querySelector('.mdl') && !document.querySelector('main').inert && document.activeElement.id === 'dl'"), "dialog: Escape closes it, removes inert and returns focus to the opener");

        await page.Locator("#fileBtn").FocusAsync(); await page.Keyboard.PressAsync("Enter");
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Insert" }).HoverAsync(); await page.Locator(".cm.sub .cm-i").Filter(new() { HasTextString = "Duplicate current" }).ClickAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('.thumb').length === 2"); await page.WaitForTimeoutAsync(500);
        await page.Locator("#rr").ClickAsync(); await page.WaitForTimeoutAsync(800);
        await page.Locator(".thumb").Nth(1).FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowUp"); await page.WaitForTimeoutAsync(1500);
        expect(await page.EvaluateAsync<bool>("() => { const t = [...document.querySelectorAll('.thumb img')].map(i => +i.getAttribute('width') > +i.getAttribute('height')); return t[0] && !t[1] && document.querySelectorAll('.thumb')[0] === document.activeElement; }"), "keyboard: Alt+Arrow reorders pages (alternative to dragging) and keeps focus on the moved page");
        await page.Keyboard.PressAsync("ArrowDown"); await page.WaitForTimeoutAsync(500);
        expect(await page.EvaluateAsync<bool>("() => document.querySelectorAll('.thumb')[1].classList.contains('sel') && document.activeElement === document.querySelectorAll('.thumb')[1] && document.getElementById('announce').textContent.startsWith('Page 2')"), "keyboard: ArrowDown selects the next page, keeps focus and announces it politely");

        await page.EvaluateAsync("() => document.querySelector('#layer > [tabindex=\"0\"]').focus()");
        await page.Keyboard.PressAsync("ArrowRight");
        expect(await page.EvaluateAsync<bool>("() => document.activeElement.parentElement.id === 'layer'"), "keyboard: arrow keys move between page objects");
        await page.Keyboard.PressAsync("Enter"); await page.WaitForTimeoutAsync(500);
        expect(await page.EvaluateAsync<bool>("() => !!document.querySelector('#layer > .on') && !!document.querySelector('#props h2')"), "keyboard: Enter selects a page object and shows its properties");

        await page.EvaluateAsync("() => { document.querySelector('.rz.r').focus(); }");
        var before = await page.EvaluateAsync<int>("() => +document.querySelector('.rz.r').getAttribute('aria-valuenow')");
        await page.Keyboard.PressAsync("ArrowLeft");
        expect(await page.EvaluateAsync<int>("() => +document.querySelector('.rz.r').getAttribute('aria-valuenow')") > before, "keyboard: the properties panel resize handle responds to arrow keys");
        await context.CloseAsync();
    }
}
