using Microsoft.Playwright;

// The app shell must be exactly viewport-sized: the window never scrolls and header, thumbnails and status bar stay on screen.
static class EditorViewport
{
    const string LongName = "a-very-long-quarterly-board-report-with-appendices-and-redlines-final-final-v12.pdf";
    const string LongStatus = "Ready. PDFs stay on this device and are never uploaded. Recovery saved automatically; click a page to edit text, drag to draw shapes, and use Shift, Alt and Ctrl shortcuts for precise placement.";

    const string Probe = """
        () => {
          const problems = [], de = document.documentElement, se = document.scrollingElement;
          if (de.scrollWidth > innerWidth) problems.push(`html scrollWidth ${de.scrollWidth} > ${innerWidth}`);
          if (de.scrollHeight > innerHeight) problems.push(`html scrollHeight ${de.scrollHeight} > ${innerHeight}`);
          if (document.body.scrollWidth > innerWidth || document.body.scrollHeight > innerHeight) problems.push(`body ${document.body.scrollWidth}x${document.body.scrollHeight}`);
          if (scrollX !== 0 || scrollY !== 0 || se.scrollTop !== 0 || se.scrollLeft !== 0) problems.push(`window scrolled ${scrollX},${scrollY},${se.scrollTop},${se.scrollLeft}`);
          for (const sel of ['header', 'main', 'footer', '#thumbs', '#doc', '#fileBtn', '#status', '#dl']) {
            const el = document.querySelector(sel), r = el.getBoundingClientRect();
            if (getComputedStyle(el).visibility === 'hidden' || r.width === 0) continue;
            if (r.left < -0.5 || r.top < -0.5 || r.right > innerWidth + 0.5 || r.bottom > innerHeight + 0.5)
              problems.push(`${sel} outside viewport: ${[r.left, r.top, r.right, r.bottom].map(Math.round)} in ${innerWidth}x${innerHeight}`);
          }
          for (const el of document.body.querySelectorAll('*')) {
            if (problems.length > 12) break;
            let r = el.getBoundingClientRect();
            for (let a = el.parentElement; a && a !== document.body; a = a.parentElement) {
              if (getComputedStyle(a).overflowX === 'visible' && getComputedStyle(a).overflowY === 'visible') continue;
              const c = a.getBoundingClientRect();
              r = { left: Math.max(r.left, c.left), top: Math.max(r.top, c.top), right: Math.min(r.right, c.right), bottom: Math.min(r.bottom, c.bottom), width: r.width, height: r.height };
            }
            if (r.width === 0 || r.height === 0 || el.closest('.sprite, svg, [hidden], #base, #view, #thumbs, #props') || getComputedStyle(el).position === 'fixed') continue;
            if (r.bottom > innerHeight + 0.5 || r.right > innerWidth + 0.5 || r.left < -0.5 || r.top < -0.5)
              problems.push(`<${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}.${el.className}> box ${[r.left, r.top, r.right, r.bottom].map(Math.round)}`);
          }
          return problems;
        }
        """;

    public static async Task Run(IBrowser browser, string source, byte[] pdf, string? artifacts)
    {
        var matrix = new (int W, int H)[] { (1280, 720), (1440, 900), (2000, 1000), (2560, 1440) };
        foreach (var dpr in new[] { 1.0f, 1.25f, 1.5f, 2.0f })
        {
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 }, DeviceScaleFactor = dpr });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(90000);
            await page.GotoAsync(source);
            await page.WaitForFunctionAsync("document.getElementById('status').textContent.startsWith('Ready.')");
            await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = LongName, MimeType = "application/pdf", Buffer = pdf });
            await page.WaitForFunctionAsync("n => document.getElementById('name').title === n && !document.getElementById('view').inert && document.getElementById('base')?.naturalWidth > 0", LongName);
            await page.EvaluateAsync("""
                text => { const r = document.getElementById('recovery'); r.hidden = false; r.disabled = false; r.textContent = 'Recovery saved';
                  document.getElementById('status').textContent = text; }
                """, LongStatus);
            if (!await page.EvaluateAsync<bool>("() => { const d = document.createElement('div'); d.style.cssText = 'position:absolute;width:100px;height:100px;overflow:scroll'; document.body.append(d); const w = d.offsetWidth - d.clientWidth; d.remove(); return w > 0; }"))
                throw new Exception("FAIL: viewport probe must run with classic (non-overlay, non-hidden) scrollbars so 100vw-style overflow is observable");
            await page.SetViewportSizeAsync(1280, 720);
            await Scrolling(page, $"1280x720 dpr {dpr}");
            foreach (var (w, h) in matrix)
                foreach (var zoom in new[] { 0.8f, 1f, 1.25f, 1.5f })
                {
                    var where = $"{w}x{h} dpr {dpr} zoom {zoom:P0}";
                    await page.SetViewportSizeAsync((int)(w / zoom), (int)(h / zoom));
                    foreach (var collapsed in new[] { "", "hide-l", "hide-r", "hide-l hide-r" })
                    {
                        await page.EvaluateAsync("c => { const m = document.querySelector('main'); m.classList.remove('hide-l', 'hide-r'); if (c) m.classList.add(...c.split(' ')); }", collapsed);
                        await Assert(page, $"{where} [{collapsed}]");
                    }
                    await page.Keyboard.PressAsync("Tab");
                    await page.Keyboard.PressAsync("Tab");
                    await page.Locator(".thumb").Last.FocusAsync();
                    await Assert(page, where + " after keyboard focus");
                    await page.Locator("#fileBtn").ClickAsync();
                    await Assert(page, where + " with File menu open");
                    await page.Keyboard.PressAsync("Escape");
                    await Assert(page, where + " after closing menu");
                    await Popovers(page, where);
                }
            if (artifacts is not null && dpr == 1.0f)
            {
                await page.SetViewportSizeAsync(2000, 1000);
                Directory.CreateDirectory(artifacts);
                await page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, "editor-2000x1000.png") });
            }
        }
    }

    static async Task Scrolling(IPage page, string where)
    {
        await page.EvaluateAsync("() => document.querySelector('main').classList.remove('hide-l', 'hide-r')");
        for (var i = 0; i < 4; i++) await page.Locator("#zi").ClickAsync();
        var result = await page.EvaluateAsync<string[]>("""
            async () => {
              const out = [], tryScroll = (id) => { const el = document.getElementById(id); if (el.scrollHeight <= el.clientHeight) { out.push(`#${id} does not overflow (${el.scrollHeight} <= ${el.clientHeight})`); return; }
                el.scrollTop = 40; if (el.scrollTop <= 0) out.push(`#${id} scrollTop assignment ignored`); el.scrollTop = 0; };
              tryScroll('view'); tryScroll('thumbs');
              const thumbs = document.querySelectorAll('.thumb'); thumbs[thumbs.length - 1].scrollIntoView();
              document.getElementById('view').scrollTop = 0; document.getElementById('thumbs').scrollTop = 0;
              await new Promise(r => requestAnimationFrame(r));
              if (scrollX !== 0 || scrollY !== 0 || document.scrollingElement.scrollTop !== 0 || document.scrollingElement.scrollLeft !== 0) out.push('scrollIntoView scrolled the document');
              return out;
            }
            """);
        if (result.Length > 0) throw new Exception($"FAIL: viewport {where}: internal panels must scroll without scrolling the window: {string.Join("; ", result)}");
    }

    static async Task Popovers(IPage page, string where)
    {
        await page.Locator("#fileBtn").ClickAsync();
        await InViewport(page, ".cm", where + " File menu");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#fileBtn").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new("propert", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).First.ClickAsync();
        await InViewport(page, ".dlg", where + " dialog");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#layer").ClickAsync(new() { Button = MouseButton.Right, Position = new() { X = 20, Y = 20 } });
        await InViewport(page, ".cm", where + " context menu");
        await page.Keyboard.PressAsync("Escape");
        await page.EvaluateAsync("() => document.querySelectorAll('.cm, .mdl').forEach(e => e.remove())");
        await Assert(page, where + " after popovers");
    }

    static async Task InViewport(IPage page, string selector, string where)
    {
        await page.Locator(selector).First.WaitForAsync(new() { Timeout = 10000 });
        var bad = await page.EvaluateAsync<string>("""
            sel => { const els = [...document.querySelectorAll(sel)].filter(e => e.getBoundingClientRect().width > 0 && getComputedStyle(e).display !== 'none');
              for (const e of els) { const r = e.getBoundingClientRect(); if (r.left < -0.5 || r.top < -0.5 || r.right > innerWidth + 0.5 || r.bottom > innerHeight + 0.5) return `${sel} ${[r.left, r.top, r.right, r.bottom].map(Math.round)} in ${innerWidth}x${innerHeight}`; }
              return els.length ? '' : sel + ' not shown'; }
            """, selector);
        if (bad != "") throw new Exception($"FAIL: viewport {where} must render fully inside the window: {bad}");
    }

    static async Task Assert(IPage page, string where)
    {
        await page.EvaluateAsync("() => Promise.all(document.getAnimations().map(a => a.finished.catch(() => 0)))");
        var problems = await page.EvaluateAsync<string[]>(Probe);
        if (problems.Length > 0) throw new Exception($"FAIL: viewport {where}: window must never scroll and the shell must fit: {string.Join("; ", problems)}");
    }
}
