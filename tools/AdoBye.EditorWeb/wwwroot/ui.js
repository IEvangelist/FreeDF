// Progressive UI layer: theme and motion settings, accessibility semantics, menu keyboard patterns, tooltips, toasts,
// command palette, resizable panels and keyboard access to page content. It adds behaviour around the editor's own
// DOM and never delays or replaces its handlers. All state stays on this device; nothing here makes a request.
(() => {
  'use strict';
  const doc = document.documentElement, $ = id => document.getElementById(id), q = (s, r = document) => r.querySelector(s), qa = (s, r = document) => [...r.querySelectorAll(s)];
  const store = { get: k => { try { return localStorage.getItem(k); } catch { return null; } }, set: (k, v) => { try { v == null ? localStorage.removeItem(k) : localStorage.setItem(k, v); } catch { /* storage may be blocked */ } } };
  const mod = /Mac|iPhone|iPad/.test(navigator.platform) ? '\u2318' : 'Ctrl+';
  const el = (tag, props = {}, ...kids) => { const e = Object.assign(document.createElement(tag), props); e.append(...kids); return e; };
  let uid = 0; const nextId = p => `${p}-${++uid}`;

  // Theme and motion (the head script applies stored values before first paint).
  const theme = () => store.get('editor.theme') || 'system';
  const setTheme = v => { store.set('editor.theme', v === 'system' ? null : v); v === 'system' ? delete doc.dataset.theme : doc.dataset.theme = v; };
  const osReduced = matchMedia('(prefers-reduced-motion: reduce)');
  const setMotion = on => { store.set('editor.motion', on ? 'reduce' : null); on ? doc.dataset.motion = 'reduce' : delete doc.dataset.motion; };
  // Explicit opt-in to animation even when the OS asks for reduced motion (the user chose it here, in the app).
  const setFullMotion = on => { store.set('editor.motion', on ? 'full' : null); on ? doc.dataset.motion = 'full' : delete doc.dataset.motion; };

  // Announcements and toasts. Toasts are visual only: the status bar is already the polite live region.
  const live = el('div', { id: 'announce', className: 'sr-only', role: 'status' }); live.setAttribute('aria-live', 'polite');
  const toasts = el('div', { className: 'toasts' }); toasts.setAttribute('aria-hidden', 'true');
  const say = t => { live.textContent = ''; requestAnimationFrame(() => { live.textContent = t; }); };
  function toast(message, kind) {
    const t = el('div', { className: 'toast' + (kind === 'error' ? ' error' : '') }, message); t.style.setProperty('--ms', (kind === 'error' ? 6000 : 3500) + 'ms');
    while (toasts.children.length > 2) toasts.firstChild.remove();
    toasts.append(t);
    setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 200); }, kind === 'error' ? 6000 : 3500);
  }
  const COLORS = [['pink', 'Pink', '#be185d'], ['cyan', 'Cyan', '#0e7490'], ['magenta', 'Magenta', '#a21caf'], ['blue', 'Blue', '#2563eb'], ['violet', 'Violet', '#6d28d9'], ['green', 'Green', '#15803d'], ['orange', 'Orange', '#c2410c'], ['red', 'Red', '#b91c1c'], ['teal', 'Teal', '#0f766e']];
  const SHADES = [['light', 'Light', 'var(--shade-light)'], ['medium', 'Medium', 'var(--shade-medium)'], ['dark', 'Dark', 'var(--shade-dark)']];
  const validColor = v => COLORS.some(x => x[0] === v);
  const validShade = v => SHADES.some(x => x[0] === v);
  const primary = () => validColor(doc.dataset.primary) ? doc.dataset.primary : 'pink';
  const accent = () => validShade(doc.dataset.accent) ? doc.dataset.accent : 'medium';
  const setColor = (role, v, fallback) => {
    if (!(role === 'primary' ? validColor(v) : validShade(v))) v = fallback;
    store.set('editor.' + role, v === fallback ? null : v);
    v === fallback ? delete doc.dataset[role] : doc.dataset[role] = v;
    if (role === 'primary') { const meta = q('meta[name="theme-color"]'); if (meta) meta.content = COLORS.find(x => x[0] === v)[2]; }
  };
  const setPrimary = v => setColor('primary', v, 'pink'), setAccent = v => setColor('accent', v, 'medium');
  // Before v2 the accent also drove primary actions. Retain that primary for saved choices.
  if (!store.get('editor.colors.v2') && !store.get('editor.primary') && validColor(store.get('editor.accent'))) doc.dataset.primary = store.get('editor.accent');
  setPrimary(primary()); setAccent(accent()); store.set('editor.colors.v2', '1');
  window.editorUi = { toast, say, setTheme, setMotion, setFullMotion, setPrimary, primary, setAccent, accent };

  // Focus handling shared by the palette and About dialogs.
  // Only elements this layer made inert are released again; the editor's own inert states (startup, recovery gating) are never overwritten.
  const inertMine = new Set();
  function modalOn(container) {
    for (const e of qa('body > header, body > main, body > footer')) if (!e.inert) { e.inert = true; inertMine.add(e); }
    container.dataset.uiModal = '';
  }
  function modalOff() {
    if (q('.mdl, [data-ui-modal]')) return;
    for (const e of inertMine) e.inert = false; inertMine.clear();
  }
  function trapTab(root, e) {
    if (e.key !== 'Tab') return;
    const c = qa('button, input, a[href], [tabindex]:not([tabindex="-1"])', root).filter(x => !x.disabled && x.getClientRects().length);
    if (!c.length) return;
    const i = c.indexOf(document.activeElement), n = i + (e.shiftKey ? -1 : 1);
    if (n < 0 || n >= c.length) { e.preventDefault(); c[(n + c.length) % c.length].focus(); }
  }
  function overlay(cls, label, build) {
    const opener = document.activeElement, scrim = el('div', { className: cls }), box = el('div', { className: 'pal' });
    box.setAttribute('role', 'dialog'); box.setAttribute('aria-modal', 'true'); box.setAttribute('aria-label', label);
    const close = () => { scrim.remove(); modalOff(); if (opener?.isConnected) opener.focus(); };
    scrim.append(box); scrim.onpointerdown = e => { if (e.target === scrim) close(); };
    scrim.onkeydown = e => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); } trapTab(box, e); };
    document.body.append(scrim); modalOn(scrim); build(box, close);
    return close;
  }

  // Command palette: lists the enabled toolbar controls, so it can never drift from the real UI.
  const split = t => { const m = /^(.*?)\s*\(([^)]+)\)$/.exec(t); return m && /^[A-Z0-9[\]]/.test(m[2]) ? [m[1], m[2]] : [t, '']; };
  function commands() {
    const list = [];
    for (const b of qa('header button, header a[href], #tools button[title], #tools label.btn[title]')) {
      const t = b.title || b.dataset.tip || b.getAttribute('aria-label'); if (!t || b.disabled || b.closest('[hidden]') || b.id === 'more') continue;
      const [label, key] = split(t); list.push({ label, key, run: () => b.click() });
    }
    const sys = (name, v) => ({ label: `Theme: ${name}`, run: () => setTheme(v) });
    list.push(sys('Light', 'light'), sys('Dark', 'dark'), sys('Match system', 'system'),
      { label: doc.dataset.motion === 'reduce' ? 'Motion: allow animations' : 'Motion: reduce', run: () => setMotion(doc.dataset.motion !== 'reduce') },
      { label: 'Keyboard shortcuts and about', key: '?', run: about });
    return list;
  }
  function palette() {
    if (q('.pal-scrim')) return;
    overlay('pal-scrim', 'Command palette', (box, close) => {
      const input = el('input', { type: 'text', placeholder: 'Type a command', autocomplete: 'off', spellcheck: false }), ul = el('ul', { id: nextId('pal') });
      input.setAttribute('role', 'combobox'); input.setAttribute('aria-expanded', 'true'); input.setAttribute('aria-controls', ul.id); input.setAttribute('aria-label', 'Command');
      ul.setAttribute('role', 'listbox'); ul.setAttribute('aria-label', 'Commands');
      const all = commands(); let shown = [], at = 0;
      const none = el('div', { className: 'none', textContent: 'No matching commands', hidden: true }); none.setAttribute('role', 'status');
      const draw = () => {
        const f = input.value.trim().toLowerCase();
        shown = all.filter(c => !f || c.label.toLowerCase().includes(f)); at = Math.min(at, Math.max(shown.length - 1, 0));
        ul.replaceChildren(...shown.map((c, i) => {
          const li = el('li', {}, c.label, ...(c.key ? [el('kbd', { textContent: c.key })] : [])); li.id = `${ul.id}-${i}`; li.setAttribute('role', 'option');
          li.setAttribute('aria-selected', i === at); li.onpointerdown = e => e.preventDefault(); li.onclick = () => run(c); return li;
        }));
        if (!shown.length) ul.replaceChildren();
        ul.hidden = !shown.length; none.hidden = !!shown.length; input.setAttribute('aria-expanded', !!shown.length);
        input.setAttribute('aria-activedescendant', shown.length ? `${ul.id}-${at}` : ''); q('[aria-selected="true"]', ul)?.scrollIntoView({ block: 'nearest' });
      };
      const run = c => { close(); setTimeout(c.run, 0); };
      input.oninput = () => { at = 0; draw(); };
      input.onkeydown = e => {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); if (shown.length) at = (at + (e.key === 'ArrowDown' ? 1 : -1) + shown.length) % shown.length; draw(); }
        else if (e.key === 'Enter' && shown[at]) { e.preventDefault(); run(shown[at]); }
      };
      box.append(input, ul, none); draw(); input.focus();
    });
  }

  function about() {
    if (q('.pal-scrim')) return;
    overlay('pal-scrim', 'Keyboard shortcuts and about', (box, close) => {
      const rows = [['Command palette', mod + 'K'], ['File menu', 'Alt+F or the File button'], ['Select, text, rectangle, ellipse, line, redact', 'V  T  R  E  L  X'], ['Pages and properties panels', '[  ]'], ['Previous and next page', 'PageUp  PageDown'],
        ['Move the focused page in the list', 'Alt+Arrow Up / Down'], ['Move the selected object', 'Arrow keys (Shift for 10)'], ['Next and previous page object', 'Tab, then Arrow keys'], ['Find', mod + 'F'], ['Undo and redo', mod + 'Z  ' + mod + 'Y'], ['Close menus and dialogs', 'Esc']];
      const dl = el('dl'); for (const [k, v] of rows) dl.append(el('dt', { textContent: k }), el('dd', {}, el('kbd', { textContent: v })));
      const done = el('button', { type: 'button', className: 'btn-outline', textContent: 'Close', onclick: close });
      box.classList.add('about');
      box.append(el('h2', { textContent: 'FreeDF PDF editor', style: 'margin:0;padding:16px 20px 0;font-size:1.0625rem' }),
        el('div', { className: 'dbody', style: 'padding:12px 20px' }, el('p', { textContent: 'Documents are opened and edited in this browser tab. Nothing is uploaded.' }), dl,
          el('p', {}, 'Open source on ', el('a', { href: 'https://github.com/IEvangelist/csharp-pdf', target: '_blank', rel: 'noopener noreferrer', textContent: 'GitHub' }), '. ',
            el('a', { href: 'https://github.com/sponsors/IEvangelist?frequency=one-time', target: '_blank', rel: 'noopener noreferrer', textContent: 'Sponsor the project' }), '.')),
        el('div', { className: 'dfoot' }, done));
      done.focus();
    });
  }

  // Appearance menu reuses the editor's .cm menu styling and keyboard handling.
  function appearanceMenu(anchor) {
    if (!anchor.getClientRects().length) anchor = $('more') || anchor;
    lastOutside = anchor;
    q('.cm')?.remove();
    const m = el('div', { className: 'cm has-check' }); m.setAttribute('role', 'menu'); m.setAttribute('aria-label', 'Appearance');
    const item = (role, label, checked, run, disabled) => {
      const b = el('button', { type: 'button', className: 'cm-i', disabled: !!disabled }, el('span', { textContent: label })); b.setAttribute('role', role); if (role !== 'menuitem') b.setAttribute('aria-checked', !!checked);
      b.onclick = () => { m.remove(); run(); anchor.focus(); }; return b;
    };
    const t = theme();
    const swatches = (label, current, set, choices) => {
      const sw = el('div', { className: 'cm-sw' }); sw.setAttribute('role', 'group'); sw.setAttribute('aria-label', label);
      for (const [v, name, hex] of choices) {
        const b = el('button', { type: 'button', title: name }); b.setAttribute('role', 'menuitemradio'); b.setAttribute('aria-checked', v === current); b.setAttribute('aria-label', name); b.style.setProperty('--sw', hex);
        b.onclick = () => { set(v); for (const o of qa('button', sw)) o.setAttribute('aria-checked', o === b); };
        sw.append(b);
      }
      return sw;
    };
    m.append(el('div', { className: 'cm-h', textContent: 'Theme' }), ...[['system', 'Match system'], ['light', 'Light'], ['dark', 'Dark']].map(([v, l]) => item('menuitemradio', l, t === v, () => setTheme(v))), el('div', { className: 'cm-sep' }),
      osReduced.matches ? item('menuitemcheckbox', 'Allow animations (system asks to reduce)', doc.dataset.motion === 'full', () => setFullMotion(doc.dataset.motion !== 'full')) : item('menuitemcheckbox', 'Reduce motion', doc.dataset.motion === 'reduce', () => setMotion(doc.dataset.motion !== 'reduce')),
      el('div', { className: 'cm-sep' }), el('div', { className: 'cm-h', textContent: 'Primary color' }), swatches('Primary color', primary(), setPrimary, COLORS),
      el('div', { className: 'cm-h', textContent: 'Accent shade' }), swatches('Accent shade', accent(), setAccent, SHADES),
      item('menuitem', 'Reset PinkInk colors', false, () => { setPrimary('pink'); setAccent('medium'); }),
      el('div', { className: 'cm-sep' }), item('menuitem', 'Commands\u2026', false, palette), item('menuitem', 'Keyboard shortcuts and about\u2026', false, about));
    document.body.append(m); const r = anchor.getBoundingClientRect();
    m.style.maxHeight = Math.max(0, innerHeight - r.bottom - 8) + 'px';
    m.style.left = Math.max(4, Math.min(r.right - m.offsetWidth, innerWidth - m.offsetWidth - 4)) + 'px'; m.style.top = r.bottom + 4 + 'px';
    (q('[aria-checked="true"]', m) || q('.cm-i', m)).focus();
  }

  // Menus: roving focus, submenus, typeahead, and focus return. Runs before the editor's own handler.
  // Remember the last focused control outside overlays so closing a menu or dialog can return focus to it.
  let lastOutside = null;
  document.addEventListener('focusin', e => { if (!e.target.closest?.('.cm, .mdl, .pal-scrim, .tip')) lastOutside = e.target; });
  const restoreFocus = () => { if ((!document.activeElement || document.activeElement === document.body) && lastOutside?.isConnected) lastOutside.focus(); };
  // A swatch row is one stop in the vertical order (its checked swatch); Left/Right move within it.
  const items = menu => qa(':scope > .cm-i:not(:disabled), :scope > .cm-sw > button[aria-checked="true"]', menu);
  const closeMenus = () => qa('.cm').forEach(m => m.remove());
  addEventListener('keydown', e => {
    const open = q('.cm'); if (!open || e.ctrlKey || e.metaKey || e.altKey) return;
    const cur = document.activeElement?.closest?.('.cm'), menu = cur || open, k = e.key, list = items(menu), i = list.indexOf(document.activeElement);
    const sw = document.activeElement?.closest?.('.cm-sw');
    if (sw && (k === 'ArrowLeft' || k === 'ArrowRight')) { e.preventDefault(); e.stopImmediatePropagation(); const b = qa('button', sw), j = b.indexOf(document.activeElement); b[(j + (k === 'ArrowRight' ? 1 : -1) + b.length) % b.length].focus(); return; }
    const rep = sw ? q('button[aria-checked="true"]', sw) : null, at = rep ? list.indexOf(rep) : i;
    const go = n => { e.preventDefault(); e.stopImmediatePropagation(); for (const o of qa('[data-open]', menu)) { delete o.dataset.open; } list[(n + list.length) % list.length]?.focus(); };
    if (k === 'ArrowDown') go(at + 1); else if (k === 'ArrowUp') go(at < 0 ? list.length - 1 : at - 1); else if (k === 'Home') go(0); else if (k === 'End') go(list.length - 1);
    else if (k === 'Tab') closeMenus();
    else if ((k === 'ArrowRight' || k === 'Enter' || k === ' ') && document.activeElement?.classList.contains('has-sub')) {
      e.preventDefault(); e.stopImmediatePropagation(); const s = q(':scope > .cm.sub', document.activeElement);
      if (s) { document.activeElement.dataset.open = ''; document.activeElement.dispatchEvent(new PointerEvent('pointerenter')); items(s)[0]?.focus(); }
    } else if (k === 'ArrowLeft' && menu.classList.contains('sub')) {
      e.preventDefault(); e.stopImmediatePropagation(); const p = menu.parentElement; delete p.dataset.open; p.focus();
    }     else if (k !== 'Enter' && k !== ' ' && k.length === 1 && /\S/.test(k)) {
      e.preventDefault(); e.stopImmediatePropagation();
      const n = list.findIndex((b, j) => j > i && b.textContent.trim().toLowerCase().startsWith(k.toLowerCase())), w = n < 0 ? list.findIndex(b => b.textContent.trim().toLowerCase().startsWith(k.toLowerCase())) : n;
      if (w >= 0) list[w].focus();
    }
  }, true);

  // Tooltips: replaces the native title delay with a consistent one; titles are restored the moment it hides.
  let tip = null, tipFor = null, tipTimer = 0;
  const hideTip = () => { clearTimeout(tipTimer); tip?.remove(); tip = null; if (tipFor?.dataset.tip) { tipFor.title = tipFor.dataset.tip; delete tipFor.dataset.tip; } tipFor = null; };
  function showTip(target) {
    const text = target.title; if (!text || target.closest('.cm, .pal, .mdl')) return;
    hideTip(); tipFor = target; target.dataset.tip = text; target.removeAttribute('title');
    if (!target.getAttribute('aria-label') && !target.textContent.trim()) target.setAttribute('aria-label', text);
    tip = el('div', { className: 'tip', role: 'tooltip', textContent: text }); document.body.append(tip);
    const r = target.getBoundingClientRect(), w = tip.offsetWidth, h = tip.offsetHeight;
    tip.style.left = Math.max(4, Math.min(r.left + r.width / 2 - w / 2, innerWidth - w - 4)) + 'px';
    tip.style.top = (r.bottom + 6 + h > innerHeight ? r.top - h - 6 : r.bottom + 6) + 'px';
  }
  document.addEventListener('pointerover', e => {
    if (e.pointerType === 'touch') return;
    const t = e.target.closest?.('button[title], a[title], label[title]'); if (!t || t === tipFor) return;
    hideTip(); tipTimer = setTimeout(() => t.isConnected && t.matches(':hover') && showTip(t), 450);
  });
  document.addEventListener('pointerout', e => { if (e.target.closest?.('[title], [data-tip]') && !e.relatedTarget?.closest?.('[data-tip]')) hideTip(); });
  document.addEventListener('focusin', e => { const t = e.target; if (t.matches?.('button[title]:focus-visible, a[title]:focus-visible')) showTip(t); else hideTip(); });
  for (const ev of ['pointerdown', 'keydown', 'scroll', 'blur']) addEventListener(ev, hideTip, true);

  // Names, roles and landmarks that the editor's markup does not state itself.
  function labelControls(root) {
    for (const b of qa('button[title]:not([aria-label]), a[title]:not([aria-label]), label.btn[title]:not([aria-label])', root)) if (!b.textContent.trim()) b.setAttribute('aria-label', b.title);
    // WCAG 2.5.3: a name derived from the tooltip must still contain the visible text.
    for (const b of qa('button[title][aria-label]', root)) {
      const text = b.textContent.trim(), name = b.getAttribute('aria-label');
      if (text && name === b.title && !name.toLowerCase().includes(text.toLowerCase())) b.setAttribute('aria-label', `${text}, ${name}`);
    }
    const zv = $('zv'); if (zv && zv.getAttribute('aria-label') !== `Zoom ${zv.textContent}`) zv.setAttribute('aria-label', `Zoom ${zv.textContent}`);
    for (const c of qa('#props :is(input, textarea, select):not([aria-label]):not([aria-labelledby]):not([type=hidden])', root)) {
      if (c.labels?.length) continue;
      const name = c.title || q('h3', c.closest('section'))?.textContent || c.dataset.k || c.name; if (name) c.setAttribute('aria-label', name);
    }
  }
  function landmarks() {
    const set = (s, a) => { const e = q(s); if (e) for (const k in a) if (e.getAttribute(k) !== a[k]) e.setAttribute(k, a[k]); };
    set('#left', { role: 'region', 'aria-label': 'Pages and contents' });
    set('#props', { 'aria-label': 'Properties' });
    set('#tools', { role: 'toolbar', 'aria-label': 'Editor tools' });
    set('#view', { role: 'region', 'aria-label': 'Page canvas', tabindex: '0' });
    set('#doc', { role: 'group', 'aria-label': 'Document' });
    set('#nav', { role: 'group', 'aria-label': 'Page navigation' });
    set('#findbar', { role: 'search', 'aria-label': 'Find in document' });
    set('#fq', { 'aria-label': 'Find in document' });
    set('#pt', { 'aria-hidden': 'true' });
    set('#layer', { role: 'group', 'aria-label': 'Page content' });
    const base = $('base'); if (base && !base.alt) base.alt = 'Current PDF page';
  }
  function thumbs() {
    const list = qa('#thumbs > .thumb'); let any = false;
    list.forEach((t, i) => {
      const sel = t.classList.contains('sel'); any ||= sel;
      t.setAttribute('role', 'group'); t.setAttribute('aria-current', sel ? 'page' : 'false'); t.setAttribute('aria-label', `Page ${i + 1} of ${list.length}`);
      t.tabIndex = sel ? 0 : -1; q('img', t)?.setAttribute('alt', '');
      for (const b of qa('.mini', t)) b.setAttribute('aria-label', `${b.title} ${i + 1}`);
    });
    if (!any && list[0]) list[0].tabIndex = 0;
  }
  let lastPage = '';
  function pageStatus() {
    const n = $('pn')?.value, t = $('pt')?.textContent;
    const text = n && t && t !== '/ 0' ? `Page ${n} ${t.replace('/', 'of')}` : '';
    const base = $('base'), alt = `${text}, ${$('name')?.textContent ?? 'document'}`; if (base && text && base.alt !== alt) base.alt = alt;
    if (text && text !== lastPage && lastPage) say(text); lastPage = text;
    const name = $('name')?.textContent, has = name && !$('doc')?.classList.contains('empty');
    const title = has ? `${name} - FreeDF` : 'FreeDF - PDF Editor'; if (document.title !== title) document.title = title;
  }
  const welcome = () => {
      const e = $('empty'); if (!e || e.dataset.ui) return; e.dataset.ui = '1';
      const note = el('span', { className: 'welcome-note' }, 'Open with ', el('kbd', { textContent: mod + 'O' }), ', or press ', el('kbd', { textContent: mod + 'K' }), ' for commands. Files stay on this device.');
      e.append(note);
      e.querySelector('strong')?.setAttribute('role', 'heading'); e.querySelector('strong')?.setAttribute('aria-level', '2');
    };

  // Keyboard access to page objects: one tab stop for the group, arrow keys within it, Enter selects.
  const parts = () => qa('#layer > .it, #layer > .obj, #layer > .span');
  const label = p => (p.textContent || p.title || 'Page item').trim();
  function rove() {
    if (document.activeElement?.closest?.('#layer') || q('#layer .wgt')) return; // form widgets own Tab order on form pages
    const all = parts(), first = all.find(p => p.tabIndex === 0) || all[0]; if (!first) return;
    first.tabIndex = 0; if (!first.getAttribute('role')) { first.setAttribute('role', 'button'); first.setAttribute('aria-label', label(first)); }
  }
  const prep = p => { p.tabIndex = -1; p.setAttribute('role', 'button'); p.setAttribute('aria-label', label(p)); };
  function activate(p) {
    const r = p.getBoundingClientRect(), o = { bubbles: true, cancelable: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, button: 0, pointerId: 41, isPrimary: true, pointerType: 'mouse' };
    p.dispatchEvent(new PointerEvent('pointerdown', o)); window.dispatchEvent(new PointerEvent('pointerup', o));
    requestAnimationFrame(() => { const on = q('#layer > .on'); if (on) { prep(on); on.focus(); } });
  }
  document.addEventListener('keydown', e => {
    const p = e.target.closest?.('#layer > .it, #layer > .obj, #layer > .span'); if (!p || e.ctrlKey || e.metaKey) return;
    if (e.key === 'Enter' || e.key === ' ') { if (p.isContentEditable || p.onkeydown) return; e.preventDefault(); e.stopPropagation(); activate(p); return; }
    const all = parts(), i = all.indexOf(p), d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
    const to = e.key === 'Home' ? 0 : e.key === 'End' ? all.length - 1 : d ? i + d : -1;
    if (to < 0 || to >= all.length || e.altKey || e.shiftKey) { if (d) { e.preventDefault(); e.stopPropagation(); } return; }
    e.preventDefault(); e.stopPropagation(); p.tabIndex = -1; prep(all[to]); all[to].tabIndex = 0; all[to].focus();
  }, true);

  // Pages list: roving focus, and Alt+Arrow reorders (the keyboard alternative to dragging a thumbnail).
  // Focus a page thumbnail once the editor has finished updating the list (it rebuilds thumbnails after reorders).
  function focusThumb(index, ready) {
    const start = performance.now();
    const tick = () => { const n = qa('#thumbs > .thumb')[index]; if (n && ready(n)) { n.tabIndex = 0; n.focus(); return; } if (performance.now() - start < 2000) requestAnimationFrame(tick); };
    requestAnimationFrame(tick);
  }
  document.addEventListener('keydown', e => {
    const t = e.target.closest?.('#thumbs > .thumb'); if (!t || e.target !== t) return;
    const all = qa('#thumbs > .thumb'), i = all.indexOf(t);
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); e.stopPropagation(); t.click(); return; }
    const d = { ArrowDown: 1, ArrowUp: -1 }[e.key]; if (!d) { if (e.key === 'Home' || e.key === 'End') { e.preventDefault(); e.stopPropagation(); all.at(e.key === 'Home' ? 0 : -1).focus(); } return; }
    e.preventDefault(); e.stopPropagation(); const n = all[i + d]; if (!n) return;
    if (e.altKey) {
      all.forEach(x => x.dataset.stale = '1');
      t.ondragstart?.(); n.ondrop?.({ preventDefault() { } }); say(`Moved page ${i + 1} to position ${i + d + 1}`);
      focusThumb(i + d, x => !x.dataset.stale);
    } else { n.click(); focusThumb(i + d, x => x.classList.contains('sel')); }
  }, true);

  // Touch long-press opens the page context menu (the pointer alternative to right-click).
  let pressTimer = 0, pressAt = null;
  const cancelPress = () => { clearTimeout(pressTimer); pressAt = null; };
  document.addEventListener('pointerdown', e => {
    if (e.pointerType !== 'touch' || !e.target.closest?.('#layer')) return;
    const x = e.clientX, y = e.clientY; pressAt = [x, y]; clearTimeout(pressTimer);
    pressTimer = setTimeout(() => { const t = pressAt && document.elementFromPoint(x, y); if (t?.closest('#layer')) t.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: x, clientY: y })); }, 650);
  }, true);
  document.addEventListener('pointermove', e => { if (pressAt && Math.hypot(e.clientX - pressAt[0], e.clientY - pressAt[1]) > 8) cancelPress(); }, true);
  for (const type of ['pointerup', 'pointercancel', 'contextmenu']) document.addEventListener(type, cancelPress, true);

  // Sheet scrim (narrow screens) closes the open panel; the empty state doubles as a click target for Open.
  document.addEventListener('click', e => {
    const m = q('main');
    if (e.target === m && matchMedia('(max-width: 760px)').matches) for (const [c, b] of [['hide-l', 'tl'], ['hide-r', 'tr']]) if (!m.classList.contains(c)) $(b).click();
    const em = e.target.closest?.('#empty');
    if (em && !e.target.closest('button, a, kbd')) { const i = $('open'); if (i) { i.value = ''; i.click(); } }
  });

  // Resizable side panels; the grid already reads --lw and --rw, so this only writes those properties.
  function resizers() {
    const main = q('main'); if (!main || q('.rz')) return;
    for (const [side, prop, min, max, def] of [['l', '--lw', 160, 420, 208], ['r', '--rw', 220, 520, 280]]) {
      const key = `panel-w-${side}`, h = el('div', { className: `rz ${side}`, tabIndex: 0 });
      h.setAttribute('role', 'separator'); h.setAttribute('aria-orientation', 'vertical'); h.setAttribute('aria-label', side === 'l' ? 'Resize pages panel' : 'Resize properties panel');
      h.setAttribute('aria-valuemin', min); h.setAttribute('aria-valuemax', max);
      const apply = w => { w = Math.max(min, Math.min(max, Math.round(w))); doc.style.setProperty(prop, w + 'px'); h.setAttribute('aria-valuenow', w); h.style[side === 'l' ? 'left' : 'right'] = w + 'px'; return w; };
      const saved = +store.get(key); apply(saved || def);
      h.onpointerdown = e => { e.preventDefault(); h.setPointerCapture(e.pointerId); h.classList.add('drag'); };
      h.onpointermove = e => { if (!h.hasPointerCapture(e.pointerId)) return; const r = main.getBoundingClientRect(); apply(side === 'l' ? e.clientX - r.left : r.right - e.clientX); };
      h.onpointerup = h.onlostpointercapture = () => { h.classList.remove('drag'); store.set(key, h.getAttribute('aria-valuenow')); };
      h.onkeydown = e => { const d = { ArrowLeft: -16, ArrowRight: 16 }[e.key]; if (!d) return; e.preventDefault(); e.stopPropagation(); store.set(key, apply(+h.getAttribute('aria-valuenow') + d * (side === 'l' ? 1 : -1))); };
      h.ondblclick = () => { apply(def); store.set(key, null); };
      main.append(h);
    }
  }

  // Narrow screens: secondary header actions move into one overflow menu instead of scrolling the header sideways.
  function overflow() {
    const ids = ['rl', 'rr', 'del', 'title', 'pr', 'fb', 'fs', 'appearance', 'appBtn'], dl = $('dl'); if (!dl || $('more')) return;
    for (const id of ids) $(id)?.classList.add('hdr2');
    const more = el('button', { id: 'more', type: 'button', className: 'icon', title: 'More actions' }); more.setAttribute('aria-haspopup', 'menu');
    more.innerHTML = '<svg class="i"><use href="#i-more"/></svg>'; more.setAttribute('aria-label', 'More actions');
    more.onclick = () => {
      q('.cm')?.remove();
      const m = el('div', { className: 'cm' }); m.setAttribute('role', 'menu'); m.setAttribute('aria-label', 'More actions');
      for (const id of ids) {
        const b = $(id); if (!b || b.hidden) continue; const t = (b.dataset.tip || b.title || b.getAttribute('aria-label') || id).replace(/\s*\(.*\)$/, '');
        const i = el('button', { type: 'button', className: 'cm-i', disabled: b.disabled }, el('span', { textContent: t })); i.setAttribute('role', 'menuitem');
        i.onclick = () => { m.remove(); id === 'appearance' ? appearanceMenu(more) : b.click(); }; m.append(i);
      }
      document.body.append(m); const r = more.getBoundingClientRect();
      m.style.left = Math.max(4, Math.min(r.right - m.offsetWidth, innerWidth - m.offsetWidth - 4)) + 'px'; m.style.top = r.bottom + 4 + 'px'; q('.cm-i:not(:disabled)', m)?.focus();
    };
    dl.before(more);
  }

  // Side panels start collapsed on the empty screen and open (with the panels' slide-in) once a document loads.
  const userPanel = { l: false, r: false };
  const panelBtn = { l: 'tl', r: 'tr' };
  const isEmpty = () => $('doc')?.classList.contains('empty') ?? false;
  function setPanelClass(side, open) {
    const m = q('main'), b = $(panelBtn[side]); if (!m || !b) return;
    m.classList.toggle('hide-' + side, !open); b.classList.toggle('on', open); b.setAttribute('aria-pressed', open);
  }
  function syncPanels() {
    const empty = isEmpty(), w = innerWidth;
    for (const side of ['l', 'r']) {
      if (userPanel[side]) continue;
      if (empty) setPanelClass(side, false);
      else { const pref = store.get('panel-' + side); setPanelClass(side, pref ? pref === '1' : w >= (side === 'l' ? 760 : 1000)); }
    }
    for (const side of ['l', 'r']) { const b = $(panelBtn[side]), m = q('main'); b.setAttribute('aria-expanded', !m.classList.contains('hide-' + side)); b.setAttribute('aria-controls', side === 'l' ? 'thumbs' : 'props'); }
  }
  let wasEmpty = null;
  function panelsOnDocChange() { const e = isEmpty(); if (e !== wasEmpty) { if (!e) { userPanel.l = userPanel.r = false; } wasEmpty = e; } syncPanels(); }

  // Floating page pager: pinned to the bottom-right of the page viewport; drives the existing prev/next controls.
  let pager = null;
  function makePager() {
    const host = $('center'); if (!host || pager) return;
    const mk = (id, label, d) => { const b = el('button', { type: 'button', id, className: 'icon' }); b.setAttribute('aria-label', label); b.innerHTML = `<svg class="i" aria-hidden="true"><use href="#i-${d}"/></svg>`; return b; };
    const prev = mk('pgPrev', 'Previous page', 'left'), next = mk('pgNext', 'Next page', 'right'), count = el('span', { className: 'pg-count' });
    prev.onclick = () => { if (!$('pp').disabled) $('pp').click(); }; next.onclick = () => { if (!$('np').disabled) $('np').click(); };
    pager = el('div', { id: 'pager' }, prev, count, next); pager.setAttribute('role', 'group'); pager.setAttribute('aria-label', 'Page navigation (floating)');
    pager.dataset.show = 'false'; pager.inert = true; host.append(pager);
  }
  function updatePager() {
    makePager(); if (!pager) return;
    const n = +$('pn')?.value || 0, total = +(/\d+/.exec($('pt')?.textContent ?? '')?.[0] ?? 0), show = !isEmpty() && total > 0;
    if (show !== (pager.dataset.show === 'true')) { pager.dataset.show = show; pager.inert = !show; }
    const [prev, next, count] = [pager.children[0], pager.children[2], pager.children[1]];
    count.textContent = show ? `${n} / ${total}` : ''; prev.disabled = !show || n <= 1; next.disabled = !show || n >= total;
    const v = $('view'); pager.style.setProperty('--sbw', (v.offsetWidth - v.clientWidth) + 'px'); pager.style.setProperty('--sbh', (v.offsetHeight - v.clientHeight) + 'px');
  }

  // Wiring.
  const errorRe = /not saved|could not|unsupported|cannot|failed|unavailable|error|not allowed|do not allow|did not/i, okRe = /^(Saved|Merged|Opened|Copied|Undid|Redid|Exported)/;
  let lastStatus = '';
  function onStatus() {
    const t = $('status')?.textContent || ''; if (t === lastStatus) return; lastStatus = t;
    if (errorRe.test(t)) toast(t, 'error'); else if (okRe.test(t)) toast(t);
  }
  let queued = false;
  function sweep() { queued = false; landmarks(); thumbs(); labelControls(); welcome(); rove(); pageStatus(); updatePager(); }
  const schedule = () => { if (!queued) { queued = true; requestAnimationFrame(sweep); } };

  function init() {
    document.body.append(live, toasts);
    const skip = el('a', { className: 'skip', href: '#view', textContent: 'Skip to document' }); skip.onclick = e => { e.preventDefault(); $('view')?.focus(); };
    document.body.prepend(skip);
    const ap = $('appearance'); if (ap) ap.onclick = () => appearanceMenu(ap);
    resizers(); overflow(); sweep();
    for (const side of ['l', 'r']) $(panelBtn[side]).addEventListener('click', () => { if (isEmpty()) userPanel[side] = true; requestAnimationFrame(syncPanels); });
    new MutationObserver(panelsOnDocChange).observe($('doc'), { attributes: true, attributeFilter: ['class'] });
    new MutationObserver(syncPanels).observe(q('main'), { attributes: true, attributeFilter: ['class'] });
    addEventListener('resize', () => requestAnimationFrame(panelsOnDocChange));
    addEventListener('editor-ready', () => setTimeout(panelsOnDocChange, 0), { once: true });
    panelsOnDocChange();
    const mo = new MutationObserver(schedule);
    // Never observe the object layer's subtree: it is rebuilt on every repaint, and sweeping there taxes zoom and Save.
    mo.observe($('view'), { childList: true });
    for (const id of ['thumbs', 'props']) mo.observe($(id), { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
    $('view').addEventListener('focusin', e => { if (e.target === $('view')) rove(); });
    mo.observe($('zv'), { childList: true, characterData: true, subtree: true });
    mo.observe($('doc') || document.body, { childList: true, subtree: true, characterData: true });
    new MutationObserver(onStatus).observe($('status'), { childList: true, characterData: true, subtree: true });
    new MutationObserver(recs => {
      for (const r of recs) for (const n of r.removedNodes) if (n.nodeType === 1 && n.classList.contains('cm') && !q('.cm')) restoreFocus();
      for (const r of recs) for (const n of r.addedNodes) if (n.nodeType === 1 && n.classList.contains('mdl')) {
        const d = q('.dlg', n), h = q('h3', d); if (!d) continue; d.setAttribute('aria-modal', 'true'); if (h) { h.id ||= nextId('dlgh'); d.setAttribute('aria-labelledby', h.id); d.removeAttribute('aria-label'); }
        modalOn(n); delete n.dataset.uiModal;
        labelControls(n);
      }
      if (!q('.mdl') && !q('[data-ui-modal]')) { modalOff(); restoreFocus(); }
    }).observe(document.body, { childList: true });
    addEventListener('resize', schedule);
  }

  // Palette and shortcuts.
  addEventListener('keydown', e => {
    if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k') { e.preventDefault(); e.stopImmediatePropagation(); q('.pal-scrim') ? 0 : palette(); }
    else if (e.key === '?' && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement?.tagName) && !document.activeElement?.isContentEditable && !q('.mdl, .pal-scrim')) { e.preventDefault(); about(); }
    else if (e.key === 'Escape' && matchMedia('(max-width: 760px)').matches && !q('.cm, .mdl, .pal-scrim')) {
      const m = q('main'); for (const [c, b] of [['hide-l', 'tl'], ['hide-r', 'tr']]) if (!m.classList.contains(c) && document.activeElement?.closest(c === 'hide-l' ? '#left' : '#props')) { e.preventDefault(); $(b).click(); $(b).focus(); }
    }
  }, true);
  init();
})();
