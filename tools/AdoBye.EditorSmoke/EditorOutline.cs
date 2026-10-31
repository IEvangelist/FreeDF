using Microsoft.Playwright;
using PortableDocument.Document;

// Contents panel contract: the outline entry for the active page is highlighted, its ancestors are expanded, it stays visible, and nothing scrolls the document or steals focus.
static class EditorOutline
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Outline: " + message); }
    static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", name);

    static List<(string Title, int Page)> Flatten(IEnumerable<PdfOutlineItem> items)
    {
        var list = new List<(string, int)>();
        void Walk(IEnumerable<PdfOutlineItem> level) { foreach (var i in level) { list.Add((i.Title, i.PageIndex ?? -1)); Walk(i.Children); } }
        Walk(items);
        return list;
    }

    static (string Title, int Page)? Expected(List<(string Title, int Page)> items, int page)
    {
        (string Title, int Page)? best = null;
        foreach (var item in items) if (item.Page >= 0 && item.Page <= page && (best is null || item.Page >= best.Value.Page)) best = item;
        return best;
    }

    static async Task Settled(IPage page, int pageIndex) =>
        await page.WaitForFunctionAsync("n => document.getElementById('pn').value === String(n) && !document.getElementById('view').inert", pageIndex + 1);

    static async Task Assert(IPage page, List<(string Title, int Page)> items, int active, string how, string? focusId = null, (string Title, int Page)? pinned = null)
    {
        var expected = pinned ?? Expected(items, active);
        await Settled(page, active);
        var title = expected is { } e ? (e.Title.Length == 0 ? "Untitled" : e.Title) : null;
        try
        {
            await page.WaitForFunctionAsync("""
                ([title, shownPage]) => { const cur = [...document.querySelectorAll('.ct-tree [aria-current="location"]')];
                  if (title === null) return cur.length === 0;
                  return cur.length === 1 && cur[0].querySelector('.ct-t').textContent === title && cur[0].querySelector('.ct-p').textContent === shownPage; }
                """, new object?[] { title, expected is { } x ? (x.Page + 1).ToString() : "" }, new() { Timeout = 10000 });
        }
        catch (TimeoutException)
        {
            throw new Exception($"Outline: {how}: page {active + 1} should highlight '{title}' but highlights '{await page.EvaluateAsync<string>("[...document.querySelectorAll('.ct-tree [aria-current]')].map(e => e.textContent).join('|')")}'");
        }
        var report = await page.EvaluateAsync<string>("""
            () => { const tree = document.querySelector('.ct-tree'), row = tree.querySelector('[aria-current="location"]'), t = tree.getBoundingClientRect(), se = document.scrollingElement;
              const problems = [];
              if (row) { const r = row.getBoundingClientRect(); if (r.top < t.top - 1 || r.bottom > t.bottom + 1) problems.push('current row is outside the visible panel'); if (!row.classList.contains('cur') || getComputedStyle(row).backgroundColor === 'rgba(0, 0, 0, 0)') problems.push('no active style'); }
              if (se.scrollTop !== window.__scroll[0] || se.scrollLeft !== window.__scroll[1]) problems.push('document scroll position changed');
              const items = [...tree.querySelectorAll('[role=treeitem]')];
              if (items.length > 120) problems.push('outline is not virtualized (' + items.length + ' rows)');
              return problems.join('; '); }
            """);
        Check(report.Length == 0, $"{how}: page {active + 1}: {report}");
        if (focusId is not null)
            Check(await page.EvaluateAsync<string>("document.activeElement?.id ?? ''") == focusId, $"{how}: focus stayed on #{focusId}");
    }

    static async Task OpenContents(IPage page, bool mobile)
    {
        if (mobile && await page.EvaluateAsync<bool>("document.querySelector('main').classList.contains('hide-l')")) await page.Locator("#tl").ClickAsync();
        await page.Locator("#tabContents").ClickAsync();
        Check(await page.EvaluateAsync<bool>("document.getElementById('tabContents').getAttribute('aria-selected') === 'true' && document.getElementById('thumbs').hidden && !document.getElementById('contents').hidden"), "Contents tab shows the tree and hides thumbnails");
    }

    static async Task OpenPages(IPage page, bool mobile)
    {
        if (mobile && await page.EvaluateAsync<bool>("document.querySelector('main').classList.contains('hide-l')")) await page.Locator("#tl").ClickAsync();
        await page.Locator("#tabPages").ClickAsync();
        Check(await page.EvaluateAsync<bool>("!document.getElementById('thumbs').hidden && document.getElementById('contents').hidden"), "Pages tab restores thumbnails");
    }

    static async Task Load(IPage page, string path, string name)
    {
        await page.Locator("#open").SetInputFilesAsync(path);
        await page.WaitForFunctionAsync("n => document.getElementById('name').textContent === n && document.getElementById('base')?.naturalWidth > 0", name);
        await page.EvaluateAsync("window.__scroll = [document.scrollingElement.scrollTop, document.scrollingElement.scrollLeft]");
    }

    public static async Task Run(IPage page, string shots, string siteRoot)
    {
        Directory.CreateDirectory(shots);
        var collection = Path.Combine(siteRoot, "samples", "27-full-collection.pdf");
        foreach (var (viewport, mobile) in new[] { ((1440, 1000), false), ((390, 800), true) })
        {
            await page.SetViewportSizeAsync(viewport.Item1, viewport.Item2);
            var label = mobile ? "mobile" : "desktop";
            foreach (var (file, name) in new[] { ("12-navigation.pdf", "12-navigation.pdf"), ("27-full-collection.pdf", "27-full-collection.pdf") })
            {
                var path = file == "27-full-collection.pdf" ? collection : Sample(file);
                if (!File.Exists(path)) throw new FileNotFoundException("Required outline fixture is missing from the published site or repository.", path);
                var doc = PdfDocument.Open(path);
                var items = Flatten(doc.GetOutlines());
                Check(items.Count > 0, file + " has an outline");
                await Load(page, path, name);
                Console.WriteLine($"outline smoke: {label} {file} ({items.Count} items, {doc.Pages.Count} pages)");
                await OpenContents(page, mobile);
                await page.Locator("#pn").FillAsync("1");
                await page.Locator("#pn").PressAsync("Enter");
                await Settled(page, 0);
                await page.WaitForFunctionAsync("document.querySelectorAll('.ct-tree [role=treeitem]').length > 0");
                Check(await page.EvaluateAsync<bool>("document.querySelector('.ct-tree').getAttribute('role') === 'tree' && [...document.querySelectorAll('.ct-tree [role=treeitem]')].every(e => e.hasAttribute('aria-level'))"), label + " " + file + ": tree and treeitem roles");
                var last = doc.Pages.Count - 1;
                await Assert(page, items, 0, $"{label} {file} initial");

                // Keyboard while the Contents tab keeps focus: PageDown/End/Home move the page, the tree follows, focus stays put.
                await page.Locator("#tabContents").FocusAsync();
                for (var p = 1; p <= Math.Min(6, last); p++)
                {
                    await page.Keyboard.PressAsync("PageDown");
                    await Assert(page, items, p, $"{label} {file} PageDown", "tabContents");
                }
                await page.Keyboard.PressAsync("End");
                await Assert(page, items, last, $"{label} {file} End", "tabContents");
                await page.Keyboard.PressAsync("Home");
                await Assert(page, items, 0, $"{label} {file} Home", "tabContents");

                // Go to page box.
                var mid = last / 2;
                await page.Locator("#pn").FillAsync((mid + 1).ToString());
                await page.Locator("#pn").PressAsync("Enter");
                await Assert(page, items, mid, $"{label} {file} Go to page");

                // Thumbnail click.
                foreach (var target in new[] { Math.Min(last, 3), last, Math.Max(0, last / 3) })
                {
                    await OpenPages(page, mobile);
                    await page.Locator(".thumb").Nth(target).ClickAsync();
                    await Settled(page, target);
                    await OpenContents(page, mobile);
                    await Assert(page, items, target, $"{label} {file} thumbnail");
                }

                // Outline click navigates and keeps the chosen entry highlighted while on its page.
                var rows = page.Locator(".ct-tree [role=treeitem]:not(.nodest)");
                var pick = Math.Min(await rows.CountAsync() - 1, 2);
                var pickedPage = int.Parse((await rows.Nth(pick).Locator(".ct-p").TextContentAsync())!) - 1;
                var pickedTitle = (await rows.Nth(pick).Locator(".ct-t").TextContentAsync())!;
                await rows.Nth(pick).ClickAsync();
                await Assert(page, items, pickedPage, $"{label} {file} outline click", pinned: (pickedTitle == "Untitled" ? "" : pickedTitle, pickedPage));

                // Keyboard navigation inside the tree: arrows move, Enter activates.
                await page.Locator(".ct-tree").FocusAsync();
                await page.Keyboard.PressAsync("Home");
                Check(await page.EvaluateAsync<int>("document.querySelector('.ct-tree').scrollTop") == 0 && await page.EvaluateAsync<bool>("document.activeElement.classList.contains('ct-tree')"), label + ": Home stays inside the tree");
                await page.Keyboard.PressAsync("ArrowDown");
                var activeId = await page.EvaluateAsync<string>("document.querySelector('.ct-tree').getAttribute('aria-activedescendant')");
                Check(!string.IsNullOrEmpty(activeId), label + ": aria-activedescendant follows arrow keys");
                var firstTarget = await page.EvaluateAsync<string>("document.getElementById(document.querySelector('.ct-tree').getAttribute('aria-activedescendant')).querySelector('.ct-p').textContent");
                if (firstTarget.Length > 0)
                {
                    await page.Keyboard.PressAsync("Enter");
                    await Settled(page, int.Parse(firstTarget) - 1);
                }
                Check(await page.EvaluateAsync<bool>("document.activeElement.classList.contains('ct-tree')"), label + ": tree keeps focus after Enter");

                if (file.StartsWith("27", StringComparison.Ordinal) || items.Count > 3)
                    await page.ScreenshotAsync(new() { Path = Path.Combine(shots, $"outline-{label}-{Path.GetFileNameWithoutExtension(file)}.png") });
                await OpenPages(page, mobile);
            }
        }

        await page.SetViewportSizeAsync(1440, 1000);
        await Load(page, collection, "27-full-collection.pdf");
        await OpenContents(page, false);
        await Large(page);
        await Empty(page);
    }

    static async Task Large(IPage page)
    {
        await page.EvaluateAsync("""
            () => {
              window.realRequest = pdfHost.request;
              const items = Array.from({ length: 10000 }, (_, i) => ({ title: 'Item ' + i, page: Math.floor(i / 200), open: false, children: i % 50 === 0 ? [{ title: 'Child of ' + i, page: Math.floor(i / 200) }] : undefined }));
              pdfHost.request = (url, options) => url.split('?')[0] === '/api/outlines' ? Promise.resolve(new Response(JSON.stringify(items), { headers: { 'content-type': 'application/json' } })) : window.realRequest(url, options);
            }
            """);
        try
        {
            await page.Locator("#pn").FillAsync("1");
            await page.Locator("#pn").PressAsync("Enter");
            await Settled(page, 0);
            await page.Locator("#rr").ClickAsync();
            await page.WaitForFunctionAsync("document.querySelector('.ct-tree [aria-current]')?.querySelector('.ct-t').textContent === 'Item 199'");
            Check(await page.EvaluateAsync<int>("document.querySelectorAll('.ct-tree [role=treeitem]').length") < 120, "10k-item outline renders a window of rows");
            await page.Locator("#tabContents").FocusAsync();
            var started = DateTime.UtcNow;
            for (var i = 0; i < 25; i++) await page.Keyboard.PressAsync("PageDown");
            await Settled(page, 25);
            await page.WaitForFunctionAsync("document.querySelector('.ct-tree [aria-current]')?.querySelector('.ct-t').textContent === 'Item 5199'");
            Check(await page.EvaluateAsync<bool>("""
                () => { const row = document.querySelector('.ct-tree [aria-current]'), t = document.querySelector('.ct-tree').getBoundingClientRect(), r = row.getBoundingClientRect(); return r.top >= t.top - 1 && r.bottom <= t.bottom + 1 && document.activeElement.id === 'tabContents'; }
                """), "10k outline: current row visible without stealing focus");
            Check(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "10k outline tracks page changes promptly");
        }
        finally { await page.EvaluateAsync("pdfHost.request = window.realRequest; delete window.realRequest"); }
    }

    public static async Task Empty(IPage page)
    {
        await page.EvaluateAsync("""
            () => {
              window.realRequest = pdfHost.request;
              pdfHost.request = (url, options) => url.split('?')[0] === '/api/outlines' ? Promise.resolve(new Response('[]', { headers: { 'content-type': 'application/json' } })) : window.realRequest(url, options);
            }
            """);
        try
        {
            await page.Locator("#rl").ClickAsync();
            await page.WaitForFunctionAsync("(() => { const e = document.querySelector('.ct-empty'); return !e.hidden && e.textContent.includes('no contents') && document.querySelector('.ct-tree').hidden; })()");
        }
        finally { await page.EvaluateAsync("pdfHost.request = window.realRequest; delete window.realRequest"); }
    }
}
