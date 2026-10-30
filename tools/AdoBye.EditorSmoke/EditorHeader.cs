using Microsoft.Playwright;

// Header layout contract: brand -> File menu -> opened document, in DOM, focus and visual order.
static class EditorHeader
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }

    public static async Task Empty(IPage page)
    {
        Check(await page.EvaluateAsync<bool>("document.getElementById('name').textContent === 'No document' && document.getElementById('doc').classList.contains('empty') && document.getElementById('dstate').hidden"), "Header shows a clear empty state with no status when no document is open");
        await Order(page, "empty");
    }

    public static async Task Open(IPage page, bool mobile, byte[] pdf)
    {
        var where = mobile ? "mobile" : "desktop";
        await Order(page, where);
        Check(await page.EvaluateAsync<bool>("""
            mobile => { const n = document.getElementById('name'), s = document.getElementById('dstate');
              return !document.getElementById('doc').classList.contains('empty') && n.title === n.textContent && n.textContent.length > 0 && (mobile || (!s.hidden && ['Saved', 'Unsaved'].includes(s.textContent))); }
            """, mobile), where + ": open document shows its name with tooltip" + (mobile ? "" : " and saved/unsaved status"));
        if (!mobile) return;
        const string longName = "a-very-long-quarterly-board-report-with-appendices-and-redlines-final-final-v12.pdf";
        await page.Locator("#open").SetInputFilesAsync(new FilePayload { Name = longName, MimeType = "application/pdf", Buffer = pdf });
        await page.WaitForFunctionAsync($"document.getElementById('name').title === '{longName}'");
        Check(await page.EvaluateAsync<bool>("""
            () => { const n = document.getElementById('name'), d = document.getElementById('doc').getBoundingClientRect();
              return n.scrollWidth > n.clientWidth && n.clientWidth >= 60 && d.right <= innerWidth && document.documentElement.scrollWidth <= innerWidth; }
            """), "mobile: long file name truncates inside the viewport and the full name stays in the tooltip");
    }

    static async Task Order(IPage page, string where)
    {
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
