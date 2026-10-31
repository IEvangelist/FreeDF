// Contents (document outline) panel: a virtualized ARIA tree that follows the active page.
import { buildOutlineIndex, currentOutlineItem, ancestorsOf, visibleRows, initiallyExpanded } from './outline-map.mjs';

const ROW = 28;
const OVERSCAN = 6;

export function createOutlinePanel({ tabPages, tabContents, thumbs, contents, load, goTo }) {
  contents.innerHTML = '<div class="ct-empty" role="status" hidden></div><div class="ct-tree" role="tree" tabindex="0" aria-label="Document contents"><div class="ct-spacer"></div></div>';
  const empty = contents.querySelector('.ct-empty'), tree = contents.querySelector('.ct-tree'), spacer = tree.firstElementChild;

  let index = buildOutlineIndex([]), expanded = new Set(), rows = [], rowPos = new Map();
  let page = 0, pageCount = 0, version = null, loadedVersion = null, loading = 0;
  let current = -1, pinned = -1, focusId = -1, frame = 0, forceReveal = false, shown = false, signature = '';
  const pool = new Map();

  const setRows = () => {
    rows = visibleRows(index, expanded);
    rowPos = new Map(rows.map((id, i) => [id, i]));
    spacer.style.height = rows.length * ROW + 'px';
  };

  const rowEl = id => {
    let el = pool.get(id);
    if (!el) { el = document.createElement('div'); el.className = 'ct-row'; el.setAttribute('role', 'treeitem'); el.id = 'ct-' + id; el.dataset.id = id; pool.set(id, el); }
    const n = index.nodes[id], parent = n.first >= 0;
    if (el.dataset.built !== '1') {
      el.dataset.built = '1';
      el.style.paddingLeft = 6 + n.depth * 14 + 'px';
      el.title = n.title + (n.page >= 0 ? ` (page ${n.page + 1})` : '');
      el.setAttribute('aria-level', n.depth + 1); el.setAttribute('aria-setsize', n.size); el.setAttribute('aria-posinset', n.pos);
      el.classList.toggle('nodest', n.page < 0);
      el.innerHTML = '<span class="ct-tw" aria-hidden="true"></span><span class="ct-t"></span><span class="ct-p"></span>';
      el.querySelector('.ct-t').textContent = n.title || 'Untitled';
      el.querySelector('.ct-p').textContent = n.page >= 0 ? String(n.page + 1) : '';
      el.querySelector('.ct-tw').classList.toggle('leaf', !parent);
    }
    if (parent) el.setAttribute('aria-expanded', expanded.has(id)); else el.removeAttribute('aria-expanded');
    el.classList.toggle('open', parent && expanded.has(id));
    const isCurrent = id === current;
    if (isCurrent) el.setAttribute('aria-current', 'location'); else el.removeAttribute('aria-current');
    el.classList.toggle('cur', isCurrent);
    el.classList.toggle('kb', id === focusId);
    return el;
  };

  const renderWindow = () => {
    if (!shown || !rows.length) { for (const el of [...spacer.children]) el.remove(); return; }
    const first = Math.max(0, Math.floor(tree.scrollTop / ROW) - OVERSCAN);
    const last = Math.min(rows.length, Math.ceil((tree.scrollTop + tree.clientHeight) / ROW) + OVERSCAN);
    const wanted = new Set();
    for (let i = first; i < last; i++) {
      const id = rows[i], el = rowEl(id);
      el.style.transform = `translateY(${i * ROW}px)`;
      if (el.parentNode !== spacer) spacer.appendChild(el);
      wanted.add(el);
    }
    for (const el of [...spacer.children]) if (!wanted.has(el)) el.remove();
    if (focusId >= 0 && wanted.has(pool.get(focusId))) tree.setAttribute('aria-activedescendant', 'ct-' + focusId); else tree.removeAttribute('aria-activedescendant');
  };

  const reveal = id => {
    const i = rowPos.get(id);
    if (i === undefined) return;
    const top = i * ROW, view = tree.clientHeight;
    if (top >= tree.scrollTop && top + ROW <= tree.scrollTop + view) return;
    tree.scrollTop = Math.max(0, Math.min(top - (view - ROW) / 2, rows.length * ROW - view));
  };

  const apply = () => {
    frame = 0;
    if (!shown) return;
    const next = index.nodes.length ? currentOutlineItem(index, page, pinned) : -1;
    if (pinned >= 0 && index.nodes[pinned]?.page !== page) pinned = -1;
    const changed = next !== current || forceReveal;
    current = next;
    if (current >= 0 && changed) {
      let grew = false;
      for (const a of ancestorsOf(index, current)) if (!expanded.has(a)) { expanded.add(a); grew = true; }
      if (grew) setRows();
    }
    renderWindow();
    if (current >= 0 && changed) { reveal(current); renderWindow(); }
    forceReveal = false;
  };
  const schedule = () => { if (!frame) frame = requestAnimationFrame(apply); };

  const show = (items) => {
    index = buildOutlineIndex(items); expanded = initiallyExpanded(index); pinned = -1; focusId = -1; current = -1; pool.clear();
    for (const el of [...spacer.children]) el.remove();
    setRows();
    const none = !index.nodes.length;
    empty.hidden = !none; tree.hidden = none;
    if (none) empty.textContent = pageCount ? 'This document has no contents. Bookmarks added to a PDF appear here.' : 'Open a PDF to see its contents.';
    forceReveal = true; tree.scrollTop = 0;
    schedule();
  };

  const refetch = async () => {
    const token = ++loading, wanted = version;
    if (!pageCount) { loadedVersion = wanted; signature = ''; show([]); return; }
    let items = [];
    try { items = await load(); } catch (error) { console.warn('Contents could not be loaded.', error); }
    if (token !== loading) return;
    loadedVersion = wanted;
    const sig = JSON.stringify(items);
    if (sig === signature && index.nodes.length) { schedule(); return; }
    signature = sig; show(items);
  };

  const toggle = (id, open) => {
    if (index.nodes[id].first < 0) return;
    if (open) expanded.add(id); else expanded.delete(id);
    setRows(); renderWindow();
  };
  const move = id => {
    if (id < 0 || !rowPos.has(id)) return;
    focusId = id; reveal(id); renderWindow();
  };
  const activate = async id => {
    const n = index.nodes[id];
    focusId = id;
    if (n.page >= 0) { pinned = id; await goTo(n.page); } else if (n.first >= 0) toggle(id, !expanded.has(id));
    schedule();
  };

  tree.addEventListener('click', e => {
    const el = e.target.closest('.ct-row');
    if (!el) return;
    const id = +el.dataset.id;
    if (e.target.closest('.ct-tw') && index.nodes[id].first >= 0) { focusId = id; toggle(id, !expanded.has(id)); return; }
    activate(id).catch(error => console.error(error));
  });
  tree.addEventListener('focus', () => { if (focusId < 0 || !rowPos.has(focusId)) focusId = current >= 0 && rowPos.has(current) ? current : rows[0] ?? -1; renderWindow(); });
  tree.addEventListener('scroll', schedule, { passive: true });
  tree.addEventListener('keydown', e => {
    if (e.ctrlKey || e.metaKey || e.altKey || !rows.length) return;
    const i = rowPos.get(focusId) ?? -1, n = index.nodes[focusId];
    let handled = true;
    switch (e.key) {
      case 'ArrowDown': move(rows[Math.min(rows.length - 1, i + 1)]); break;
      case 'ArrowUp': move(rows[Math.max(0, i - 1)]); break;
      case 'Home': move(rows[0]); break;
      case 'End': move(rows[rows.length - 1]); break;
      case 'ArrowRight': if (n?.first >= 0) { if (!expanded.has(focusId)) toggle(focusId, true); else move(n.first); } break;
      case 'ArrowLeft': if (n?.first >= 0 && expanded.has(focusId)) toggle(focusId, false); else if (n && n.parent >= 0) move(n.parent); break;
      case 'Enter': case ' ': if (n) activate(focusId).catch(error => console.error(error)); break;
      default: handled = false;
    }
    if (handled) { e.preventDefault(); e.stopPropagation(); }
  });
  new ResizeObserver(() => { if (shown) schedule(); }).observe(tree);

  const select = name => {
    shown = name === 'contents';
    tabPages.setAttribute('aria-selected', String(!shown)); tabContents.setAttribute('aria-selected', String(shown));
    tabPages.tabIndex = shown ? -1 : 0; tabContents.tabIndex = shown ? 0 : -1;
    thumbs.hidden = shown; contents.hidden = !shown;
    try { localStorage.setItem('left-tab', name); } catch { /* storage may be unavailable */ }
    if (shown) { if (loadedVersion !== version) refetch(); else { forceReveal = true; schedule(); } }
  };
  tabPages.onclick = () => select('pages');
  tabContents.onclick = () => select('contents');
  for (const tab of [tabPages, tabContents]) tab.onkeydown = e => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    e.preventDefault();
    const target = tab === tabPages ? tabContents : tabPages;
    target.focus(); target.click();
  };
  let saved = null;
  try { saved = localStorage.getItem('left-tab'); } catch { /* ignore */ }
  select(saved === 'contents' ? 'contents' : 'pages');

  return {
    update(state, activePage) {
      page = activePage; pageCount = state.pageCount;
      const key = state.version + ':' + state.pageCount;
      if (key !== version) {
        version = key;
        if (shown) refetch();
      }
      if (shown) schedule();
    },
  };
}
