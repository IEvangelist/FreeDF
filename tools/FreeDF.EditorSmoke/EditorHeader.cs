using Microsoft.Playwright;

// Header layout contract: brand -> File menu -> opened document, in DOM, focus and visual order.
static class EditorHeader
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }

    public static async Task State(IPage page, bool dirty)
    {
        var word = dirty ? "Unsaved" : "Saved";
        try
        {
            await page.WaitForFunctionAsync("""
                ([word, dirty]) => { const s = document.getElementById('dstate'), r = s.getBoundingClientRect();
                  return !s.hidden && s.textContent === word && s.dataset.dirty === String(dirty) && r.width >= 6 && r.height > 0 && getComputedStyle(s).display !== 'none'; }
                """, new object[] { word, dirty }, new() { Timeout = 10000 });
        }
        catch (TimeoutException) { throw new Exception($"FAIL: header status should read {word} (visible dot on mobile), got '{await page.Locator("#dstate").TextContentAsync()}' data-dirty={await page.Locator("#dstate").GetAttributeAsync("data-dirty")}"); }
    }

    public static async Task Empty(IPage page)
    {
        Check(await page.EvaluateAsync<bool>("document.getElementById('name').textContent === 'No document' && document.getElementById('doc').classList.contains('empty') && document.getElementById('dstate').hidden"), "Header shows a clear empty state with no status when no document is open");
        await Order(page, "empty");
    }

    public static async Task Open(IPage page, bool mobile, byte[] pdf)
    {
        var where = mobile ? "mobile" : "desktop";
        await Order(page, where);
        await State(page, false);
        var openStatus = await page.EvaluateAsync<bool>("""
            mobile => { const n = document.getElementById('name'), s = document.getElementById('dstate');
              return !document.getElementById('doc').classList.contains('empty') && n.title === n.textContent && n.textContent.length > 0 && !s.hidden && s.getBoundingClientRect().width >= 6 && ['Saved', 'Unsaved'].includes(s.textContent) && (mobile ? getComputedStyle(s).fontSize === '0px' : getComputedStyle(s).fontSize !== '0px'); }
            """, mobile);
        if (!openStatus)
        {
            var state = await page.EvaluateAsync<string>("""
                () => { const n = document.getElementById('name'), s = document.getElementById('dstate');
                  return JSON.stringify({ viewport: innerWidth, empty: document.getElementById('doc').classList.contains('empty'),
                    name: n.textContent, title: n.title, status: s.textContent, hidden: s.hidden,
                    statusWidth: s.getBoundingClientRect().width, fontSize: getComputedStyle(s).fontSize }); }
                """);
            throw new Exception($"FAIL: {where}: document name/title and status must be visible, with status font-size {(mobile ? "0px (dot only)" : "greater than 0px (text and dot)")}. Actual: {state}");
        }
        foreach (var size in new[] { "small", "large" })
        {
            await page.EvaluateAsync("size => document.documentElement.dataset.textSize = size", size);
            Check(await page.EvaluateAsync<bool>("""
                mobile => { const s = document.getElementById('dstate');
                  return !s.hidden && s.getBoundingClientRect().width >= 6
                    && (mobile ? getComputedStyle(s).fontSize === '0px' : getComputedStyle(s).fontSize !== '0px'); }
                """, mobile), where + $": {size} chrome text preserves the saved/unsaved status presentation");
        }
        await page.EvaluateAsync("() => delete document.documentElement.dataset.textSize");
        if (!mobile) return;
        const string longName = "a-very-long-quarterly-board-report-with-appendices-and-redlines-final-final-v12.pdf";
        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = longName, MimeType = "application/pdf", Buffer = pdf });
        await page.WaitForFunctionAsync("""
            name => { const img = document.getElementById('base');
              return document.getElementById('name').title === name && !document.getElementById('view').inert
                && img?.naturalWidth > 0 && img.dataset.loadedUrl === img.dataset.targetUrl; }
            """, longName);
        Check(await page.EvaluateAsync<bool>("""
            () => { const n = document.getElementById('name'), d = document.getElementById('doc').getBoundingClientRect();
              return n.scrollWidth > n.clientWidth && n.clientWidth >= 60 && d.right <= innerWidth && document.documentElement.scrollWidth <= innerWidth; }
            """), "mobile: long file name truncates inside the viewport and the full name stays in the tooltip");
    }

    static async Task Order(IPage page, string where)
    {
        Check(await page.EvaluateAsync<bool>("""
            () => {
              const lum = c => { const [r, g, b] = c.match(/[\d.]+/g).slice(0, 3).map(v => { v /= 255; return v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }); return .2126 * r + .7152 * g + .0722 * b; };
              const ratio = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + .05) / (Math.min(x, y) + .05); };
              const url = 'https://github.com/sponsors/IEvangelist?frequency=one-time', mobile = innerWidth <= 600;
              const plain = a => a.tagName === 'A' && a.href === url && a.target === '_blank' && a.rel.split(' ').sort().join() === 'noopener,noreferrer' && a.getAttribute('aria-label') === 'Sponsor (one-time) on GitHub';
              const h = document.getElementById('sponsor'), b = document.getElementById('sponsorBadge'), f = document.querySelector('footer'), fr = f.getBoundingClientRect(), br = b.getBoundingClientRect(), credit = document.getElementById('developerCredit').getBoundingClientRect();
              const bg = el => getComputedStyle(el).backgroundColor;
              const okBadge = mobile ? getComputedStyle(b).display === 'none'
                : plain(b) && br.right <= credit.left && credit.right <= fr.right && br.top >= fr.top + 2 && br.bottom <= fr.bottom - 2 && br.height >= 18
                  && ['status', 'recovery', 'keys'].every(id => { const e = document.getElementById(id); return e.hidden || getComputedStyle(e).display === 'none' || e.getBoundingClientRect().right <= br.left + 1; })
                  && ratio(getComputedStyle(b).color, bg(f)) >= 4.5;
              return plain(h) && ratio(getComputedStyle(h).color, bg(document.querySelector('header'))) >= 3 && okBadge
                && !document.querySelector('link[rel=preconnect],link[rel=prefetch],link[rel=dns-prefetch]'); }
            """), where + ": header heart and footer Sponsor badge are plain one-time anchors with AA contrast; badge precedes the developer credit without overlapping status/recovery/hints and is hidden on narrow screens");
        Check(await page.EvaluateAsync<bool>("""
            () => { const a = document.getElementById('sponsor'), r = a.getBoundingClientRect(), mobile = innerWidth <= 720;
              return a.title === a.getAttribute('aria-label')
                && r.width >= (mobile ? 44 : 30) && r.height >= (mobile ? 44 : 30)
                && before(document.getElementById('fs'), a) && before(a, document.getElementById('dl')); function before(x, y) { return !!(x.compareDocumentPosition(y) & Node.DOCUMENT_POSITION_FOLLOWING); } }
            """), where + ": header Sponsor heart keeps its tooltip, hit target and position beside the right-hand actions");
        Check(await page.EvaluateAsync<bool>("""
            () => { const url = 'https://github.com/IEvangelist/FreeDF', mobile = innerWidth <= 720, q = id => document.getElementById(id);
              const plain = a => a.tagName === 'A' && a.href === url && a.target === '_blank' && a.rel.split(' ').sort().join() === 'noopener,noreferrer' && a.getAttribute('aria-label') === 'View source on GitHub' && a.title === 'View source on GitHub';
              const h = q('ghLink'), dl = q('dl').getBoundingClientRect(), hr = h.getBoundingClientRect(), c = q('developerCredit'), a = q('developerLink'), cr = c.getBoundingClientRect(), ar = a.getBoundingClientRect(), foot = document.querySelector('footer').getBoundingClientRect();
              const mid = r => (r.top + r.bottom) / 2;
              const okHeader = plain(h) && getComputedStyle(h).display !== 'none' && hr.left >= dl.right - 1 && hr.left - dl.right <= 12 && Math.abs(mid(hr) - mid(dl)) < 8 && hr.width >= (mobile ? 44 : 30) && hr.height >= (mobile ? 44 : 30) && hr.right <= innerWidth && document.documentElement.scrollWidth <= innerWidth;
              const okFooter = c.textContent.trim().replace(/\s+/g, ' ') === 'Developed by David Pine' && a.textContent === 'David Pine' && a.href === 'https://davidpine.dev/' && a.target === '_blank' && a.rel.split(' ').sort().join() === 'noopener,noreferrer'
                && ar.width >= 24 && ar.height >= 24 && cr.left >= foot.left && cr.right <= foot.right && cr.top >= foot.top && cr.bottom <= foot.bottom && !q('ghFooter') && !document.querySelector('footer a[href="' + url + '"]');
              return okHeader && okFooter; }
            """), where + ": source link remains in the header on desktop and mobile; footer has only the visible developer credit linking to davidpine.dev, with no duplicate GitHub link");
        Check(await page.EvaluateAsync<bool>("""
            () => { const lum = c => { const [r, g, b] = c.match(/[\d.]+/g).slice(0, 3).map(v => { v /= 255; return v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }); return .2126 * r + .7152 * g + .0722 * b; };
              const ratio = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + .05) / (Math.min(x, y) + .05); };
              return ratio(getComputedStyle(document.getElementById('ghLink')).color, getComputedStyle(document.querySelector('header')).backgroundColor) >= 3
                && ratio(getComputedStyle(document.getElementById('developerCredit')).color, getComputedStyle(document.querySelector('footer')).backgroundColor) >= 4.5
                && ratio(getComputedStyle(document.getElementById('developerLink')).color, getComputedStyle(document.querySelector('footer')).backgroundColor) >= 4.5; }
            """), where + ": header GitHub mark meets 3:1 contrast and developer credit/link meet 4.5:1 contrast");
        Check(await page.EvaluateAsync<bool>("""
            () => { const q = id => document.getElementById(id); return !!(q('dl').compareDocumentPosition(q('ghLink')) & Node.DOCUMENT_POSITION_FOLLOWING) && !!(q('ghLink').compareDocumentPosition(q('tr')) & Node.DOCUMENT_POSITION_FOLLOWING); }
            """), where + ": header GitHub link follows Download in focus order");
        Check(await page.EvaluateAsync<bool>("""
            () => { const q = id => document.getElementById(id), brand = document.querySelector('header .brand'), file = q('fileBtn'), doc = q('doc'),
                before = (a, b) => !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
              return before(brand, file) && before(file, doc) && before(file, q('dl')) && before(doc, q('rl')); }
            """), where + ": DOM/focus order is brand, File, document, then remaining actions");
        Check(await page.EvaluateAsync<bool>("""
            () => { const r = id => document.getElementById(id).getBoundingClientRect(), file = r('fileBtn'), doc = r('doc'), brand = document.querySelector('header .brand').getBoundingClientRect();
              const brandShown = getComputedStyle(document.querySelector('header .brand')).display !== 'none';
              return (!brandShown || brand.right <= file.left) && file.right <= doc.left + 1 && doc.left - file.right < 24 && Math.abs((file.top + file.bottom) - (doc.top + doc.bottom)) < 8 && file.height >= 28; }
            """), where + ": File menu sits immediately right of the brand with the document beside it");
    }
}
