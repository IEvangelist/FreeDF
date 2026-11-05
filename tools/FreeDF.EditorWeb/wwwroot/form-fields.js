// Form-field authoring: creation, property panel, alignment, tab order, copy/paste, flatten and FDF/XFDF exchange.
// All state lives on the server (page-level history); this module only draws the overlay and sends small JSON commands.

const KINDS = [
  ['text', 'Text field', { w: 160, h: 24 }], ['multiline', 'Multi-line text', { w: 160, h: 80 }], ['comb', 'Comb text (fixed cells)', { w: 160, h: 24, maxLength: 8 }],
  ['password', 'Password field', { w: 160, h: 24 }], ['checkbox', 'Check box', { w: 18, h: 18 }], ['radio', 'Radio button', { w: 16, h: 16 }],
  ['combo', 'Combo box', { w: 140, h: 24 }], ['list', 'List box', { w: 140, h: 72 }], ['button', 'Push button', { w: 100, h: 28 }], ['signature', 'Signature placeholder', { w: 200, h: 50 }],
];
const KIND_LABEL = Object.fromEntries(KINDS.map(([k, l]) => [k, l]));
const FONTS = [['Helv', 'Helvetica'], ['HeBo', 'Helvetica Bold'], ['HeOb', 'Helvetica Oblique'], ['HeBO', 'Helvetica Bold Oblique'], ['TiRo', 'Times Roman'], ['TiBo', 'Times Bold'],
  ['TiIt', 'Times Italic'], ['TiBI', 'Times Bold Italic'], ['Cour', 'Courier'], ['CoBo', 'Courier Bold'], ['CoOb', 'Courier Oblique'], ['CoBO', 'Courier Bold Oblique']];
const STYLES = [['S', 'Solid'], ['D', 'Dashed'], ['B', 'Beveled'], ['I', 'Inset'], ['U', 'Underline']];
const TEXTY = new Set(['text', 'multiline', 'comb', 'password']);
const key = f => `${f.name}\u0000${f.widget}`;
const fieldCss = `
.ffbox{position:absolute;box-sizing:border-box;border:1px dashed rgba(124,92,255,.85);background:rgba(124,92,255,.08);outline:none;pointer-events:none}
body:not(.ff-off) #layer .ffbox.live{pointer-events:auto;cursor:move}
.ffbox:focus-visible{outline:2px solid var(--accent,#7c5cff);outline-offset:1px}
.ffbox.on{border:1px solid var(--accent,#7c5cff);background:rgba(124,92,255,.16)}
.ffbox .fftab{position:absolute;left:-1px;top:-14px;background:var(--accent,#7c5cff);color:#fff;font:11px/14px system-ui,sans-serif;padding:0 4px;border-radius:3px}
.ffbox .ffh{position:absolute;width:9px;height:9px;background:#fff;border:1px solid var(--accent,#7c5cff);box-sizing:border-box}
.ffrub{position:absolute;border:1px dashed var(--accent,#7c5cff);background:rgba(124,92,255,.12);pointer-events:none}
#props .ffgrid{display:grid;grid-template-columns:1fr 1fr;gap:8px}
#props .ffbtns{display:flex;flex-wrap:wrap;gap:6px}`;

export function createFormFields(ctx) {
  const style = document.createElement('style'); style.textContent = fieldCss; document.head.appendChild(style);
  let list = [], selected = new Set(), armed = null, showTabs = false, focusKey = null, clip = null, busy = Promise.resolve(), editing = true, loadEpoch = 0;
  const pending = new Map(); let moveTimer = 0;
  const mode = document.createElement('div');
  mode.id = 'form-mode'; mode.className = 'form-mode'; mode.hidden = true;
  mode.setAttribute('role', 'group'); mode.setAttribute('aria-label', 'Form mode');
  const modeButtons = [false, true].map(edit => {
    const b = document.createElement('button');
    b.type = 'button'; b.textContent = edit ? 'Edit' : 'Fill out';
    b.title = edit ? 'Select, move, resize and edit form fields' : 'Fill out form values without moving fields';
    b.onclick = () => setMode(edit);
    mode.appendChild(b); return b;
  });
  function updateMode() {
    mode.hidden = !list.length;
    modeButtons.forEach((b, i) => { const on = editing === !!i; b.setAttribute('aria-pressed', String(on)); b.classList.toggle('on', on); });
  }
  function setMode(edit) {
    editing = edit; selected.clear(); arm(null); ctx.clearOtherSelection();
    updateMode(); ctx.repaint(); ctx.props();
    ctx.status(edit ? 'Edit form: select, move or resize fields. Choose Fill out to enter values.' : 'Fill out form: enter values on the page. Choose Edit to change field layout.');
  }
  updateMode();

  const byKey = k => list.find(f => key(f) === k);
  const sel = () => [...selected].map(byKey).filter(Boolean);
  const page = () => ctx.pageIndex();
  const post = (path, body, query = {}) => ctx.post(`/api/form/${path}${Object.keys(query).length ? '?' + new URLSearchParams(query) : ''}`, body === undefined ? undefined : JSON.stringify(body));
  const json = r => r.json();

  async function load(p = page()) {
    const epoch = ++loadEpoch, document = ctx.documentEpoch();
    if (!ctx.pageCount()) { list = []; selected.clear(); updateMode(); return; }
    const next = await (await ctx.request('/api/forms?page=' + p)).json();
    if (epoch !== loadEpoch || document !== ctx.documentEpoch() || p !== page()) return;
    list = next;
    list.sort((a, b) => a.tab - b.tab);
    for (const k of [...selected]) if (!byKey(k)) selected.delete(k);
    updateMode();
  }

  // Serialises commands so a quick burst of edits keeps the server history in order.
  function run(fn, success) {
    busy = busy.then(async () => {
      try {
        await ctx.flush(); const result = await fn();
        await load(); await ctx.afterMutation(); ctx.repaint(); ctx.props();
        if (success) ctx.status(typeof success === 'function' ? success(result) : success);
        return result;
      } catch (e) { ctx.status(e.message); try { await load(); } catch { /* keep the last list */ } ctx.repaint(); ctx.props(); }
    });
    return busy;
  }

  const defaults = (kind, x, y, w, h) => {
    const [, , d] = KINDS.find(k => k[0] === kind);
    const f = { kind, name: '', page: page(), x, y, w: w || d.w, h: h || d.h, tooltip: '', required: false, readOnly: false, value: '', defaultValue: '', maxLength: d.maxLength || 0,
      fontName: 'Helv', fontSize: 0, textColor: '#000000', borderColor: '#000000', backgroundColor: null, borderWidth: 1, borderStyle: 'S', alignment: 0, options: null, exportValue: 'Yes', caption: null, editable: false, multiSelect: false, widget: 0 };
    if (kind === 'combo' || kind === 'list') f.options = [{ export: 'Option 1' }, { export: 'Option 2' }, { export: 'Option 3' }];
    if (kind === 'button') Object.assign(f, { caption: 'Button', backgroundColor: '#dddddd', borderStyle: 'B', borderWidth: 2, alignment: 1 });
    if (kind === 'signature') Object.assign(f, { borderColor: '#1a73e8', borderStyle: 'D', backgroundColor: '#f1f6ff' });
    if (kind === 'radio') f.exportValue = 'Choice1';
    if (kind === 'checkbox') f.exportValue = 'Yes';
    return f;
  };

  async function create(kind, x, y, w, h) {
    const dto = defaults(kind, x, y, w, h);
    const added = await run(() => post('add', dto).then(json), r => `Added ${KIND_LABEL[kind].toLowerCase()} ${r?.name ?? ''}.`);
    if (added) { selected = new Set([`${added.name}\u0000${added.widget}`]); focusKey = [...selected][0]; ctx.clearOtherSelection(); ctx.repaint(); ctx.props(); }
  }

  function arm(kind) {
    if (kind && !editing) setMode(true);
    armed = kind;
    ctx.status(kind ? `Drag on the page to draw a ${KIND_LABEL[kind].toLowerCase()}, click for the default size, or press Enter to place it in the middle. Esc cancels.` : 'Ready.');
    ctx.layer()?.classList.toggle('draw', !!kind || ctx.tool() !== 'select');
  }

  function layerDown(ev) {
    if (!armed) { if (selected.size) { selected.clear(); ctx.props(); } return false; }
    ev.preventDefault();
    const kind = armed, S = ctx.scale(), start = ctx.toLayer(ev.clientX, ev.clientY), layer = ctx.layer();
    const rub = document.createElement('div'); rub.className = 'ffrub'; layer.appendChild(rub);
    let box = null;
    const move = e => {
      const p = ctx.toLayer(e.clientX, e.clientY);
      const x0 = Math.min(p[0], start[0]), y0 = Math.min(p[1], start[1]);
      box = [x0, y0, Math.abs(p[0] - start[0]), Math.abs(p[1] - start[1])];
      Object.assign(rub.style, { left: x0 + 'px', top: y0 + 'px', width: box[2] + 'px', height: box[3] + 'px' });
    };
    const up = () => {
      removeEventListener('pointermove', move); removeEventListener('pointerup', up); rub.remove();
      arm(null);
      const small = !box || box[2] / S < 6 || box[3] / S < 6;
      create(kind, (small ? start[0] : box[0]) / S, (small ? start[1] : box[1]) / S, small ? 0 : box[2] / S, small ? 0 : box[3] / S);
    };
    addEventListener('pointermove', move); addEventListener('pointerup', up);
    return true;
  }

  function placeCentre() {
    const g = ctx.pageGeometry(), kind = armed, d = KINDS.find(k => k[0] === kind)[2];
    arm(null); create(kind, (g.width - d.w) / 2, (g.height - d.h) / 2);
  }

  // ---- overlay ----
  function paint(layer) {
    if (!editing) return;
    if (ctx.otherSelection() && selected.size) selected.clear();
    if (armed) layer.classList.add('draw');
    const S = ctx.scale(), live = ctx.tool() === 'select';
    const order = [...list];
    for (const f of list) {
      const k = key(f), on = selected.has(k);
      const e = document.createElement('div');
      e.className = 'ffbox' + (on ? ' on' : '') + (live && !armed ? ' live' : '');
      e.dataset.ffKey = k; e.tabIndex = live ? 0 : -1; e.setAttribute('role', 'button'); e.setAttribute('aria-pressed', on);
      e.setAttribute('aria-label', `${KIND_LABEL[f.kind] ?? f.kind} ${f.name}. Enter edits properties, arrow keys move, Delete removes.`);
      e.title = `${KIND_LABEL[f.kind] ?? f.kind}: ${f.name}`;
      Object.assign(e.style, { left: f.x * S + 'px', top: f.y * S + 'px', width: f.w * S + 'px', height: f.h * S + 'px' });
      if (showTabs) { const t = document.createElement('span'); t.className = 'fftab'; t.textContent = order.indexOf(f) + 1; e.appendChild(t); }
      if (on && selected.size === 1 && !ctx.locked()) for (const [c, x, y, cur] of [['nw', 0, 0, 'nwse-resize'], ['ne', 1, 0, 'nesw-resize'], ['sw', 0, 1, 'nesw-resize'], ['se', 1, 1, 'nwse-resize']]) {
        const h = document.createElement('div'); h.className = 'ffh'; h.style.cssText = `left:calc(${x * 100}% - 5px);top:calc(${y * 100}% - 5px);cursor:${cur}`;
        h.onpointerdown = ev => startResize(ev, f, c); e.appendChild(h);
      }
      e.onpointerdown = ev => { if (live && !armed) down(ev, f); };
      e.onkeydown = ev => boxKey(ev, f);
      layer.appendChild(e);
      if (focusKey === k) { focusKey = null; queueMicrotask(() => e.isConnected && e.focus({ preventScroll: true })); }
    }
  }

  function selectField(f, additive) {
    ctx.clearOtherSelection();
    const k = key(f);
    if (additive) { selected.has(k) ? selected.delete(k) : selected.add(k); }
    else if (!selected.has(k)) selected = new Set([k]);
    ctx.repaint(); ctx.props();
  }

  function down(ev, f) {
    ev.stopPropagation(); ev.preventDefault();
    selectField(f, ev.shiftKey || ev.ctrlKey || ev.metaKey);
    if (ctx.locked() || !selected.has(key(f))) return;
    const S = ctx.scale(), start = ctx.toLayer(ev.clientX, ev.clientY), group = sel().map(g => ({ g, x: g.x, y: g.y }));
    let moved = false;
    const move = e => {
      const p = ctx.toLayer(e.clientX, e.clientY); let dx = (p[0] - start[0]) / S, dy = (p[1] - start[1]) / S;
      if (!moved && Math.hypot(dx, dy) * S < 3) return;
      moved = true;
      if (e.shiftKey) { if (Math.abs(dx) > Math.abs(dy)) dy = 0; else dx = 0; }
      for (const o of group) { o.g.x = o.x + dx; o.g.y = o.y + dy; }
      ctx.repaint();
    };
    const up = () => {
      removeEventListener('pointermove', move); removeEventListener('pointerup', up);
      if (moved) commitMoves(group.map(o => o.g));
    };
    addEventListener('pointermove', move); addEventListener('pointerup', up);
  }

  function startResize(ev, f, corner) {
    ev.stopPropagation(); ev.preventDefault();
    const S = ctx.scale(), start = ctx.toLayer(ev.clientX, ev.clientY), o = { x: f.x, y: f.y, w: f.w, h: f.h };
    const move = e => {
      const p = ctx.toLayer(e.clientX, e.clientY), dx = (p[0] - start[0]) / S, dy = (p[1] - start[1]) / S;
      const east = corner.includes('e'), south = corner.includes('s');
      const w = Math.max(6, o.w + (east ? dx : -dx)), h = Math.max(6, o.h + (south ? dy : -dy));
      f.w = w; f.h = h; f.x = east ? o.x : o.x + o.w - w; f.y = south ? o.y : o.y + o.h - h;
      ctx.repaint();
    };
    const up = () => { removeEventListener('pointermove', move); removeEventListener('pointerup', up); commitMoves([f]); };
    addEventListener('pointermove', move); addEventListener('pointerup', up);
  }

  // Moves and resizes are batched so one drag, nudge burst or alignment is a single undo step.
  function commitMoves(fields) {
    for (const f of fields) pending.set(key(f), { name: f.name, widget: f.widget, x: f.x, y: f.y, w: f.w, h: f.h });
    clearTimeout(moveTimer);
    moveTimer = setTimeout(() => {
      const batch = [...pending.values()]; pending.clear();
      run(() => post('move', batch), `Updated ${batch.length} field${batch.length === 1 ? '' : 's'}.`);
    }, 250);
  }

  function boxKey(ev, f) {
    const k = key(f);
    if (ev.key === 'Enter') { ev.preventDefault(); selectField(f, false); queueMicrotask(() => document.querySelector('#props [data-ff="name"]')?.focus()); return; }
    if (ev.key === ' ') { ev.preventDefault(); selectField(f, true); focusKey = k; return; }
    if (ev.key === 'Delete' || ev.key === 'Backspace') { ev.preventDefault(); ev.stopPropagation(); remove(selected.has(k) ? sel() : [f]); return; }
    const step = ev.shiftKey ? 10 : 1, mv = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[ev.key];
    if (mv) {
      ev.preventDefault(); ev.stopPropagation();
      if (ctx.locked()) { ctx.status('Positions and sizes are locked.'); return; }
      if (!selected.has(k)) selected = new Set([k]);
      const group = sel(); for (const g of group) { g.x += mv[0]; g.y += mv[1]; }
      focusKey = k; ctx.repaint(); commitMoves(group); return;
    }
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'c') { ev.preventDefault(); copy(); }
    else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'x') { ev.preventDefault(); copy(); remove(sel()); }
    else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'v') { ev.preventDefault(); paste(); }
    else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'd') { ev.preventDefault(); copy(); paste(); }
    else if (ev.key === 'Escape') { selected.clear(); ctx.repaint(); ctx.props(); }
  }

  function remove(fields) {
    if (!fields.length) return;
    const names = [...new Set(fields.map(f => f.name))];
    selected.clear();
    run(() => post('remove', { names }), `Removed ${names.length} field${names.length === 1 ? '' : 's'}.`);
  }
  function copy() { clip = JSON.parse(JSON.stringify(sel())); ctx.status(`Copied ${clip.length} field${clip.length === 1 ? '' : 's'}.`); }
  async function paste(offset = 12) {
    if (!clip?.length) { ctx.status('No copied form fields.'); return; }
    const items = clip.map(f => ({ ...f, widget: 0 }));
    const r = await run(() => post('paste', { page: page(), dx: offset, dy: offset, items }).then(json), x => `Pasted ${x?.created.length ?? 0} field(s).`);
    if (r) { selected = new Set(r.created.map(c => `${c.name}\u0000${c.widget}`)); ctx.repaint(); ctx.props(); }
  }

  // ---- alignment and distribution ----
  function arrange(mode) {
    const fs = sel(); if (fs.length < 2 || ctx.locked()) return;
    const l = Math.min(...fs.map(f => f.x)), t = Math.min(...fs.map(f => f.y)), r = Math.max(...fs.map(f => f.x + f.w)), b = Math.max(...fs.map(f => f.y + f.h));
    const sorted = (axis, size) => [...fs].sort((p, q) => p[axis] - q[axis]);
    switch (mode) {
      case 'left': fs.forEach(f => f.x = l); break;
      case 'right': fs.forEach(f => f.x = r - f.w); break;
      case 'centre': fs.forEach(f => f.x = (l + r) / 2 - f.w / 2); break;
      case 'top': fs.forEach(f => f.y = t); break;
      case 'bottom': fs.forEach(f => f.y = b - f.h); break;
      case 'middle': fs.forEach(f => f.y = (t + b) / 2 - f.h / 2); break;
      case 'width': fs.forEach(f => f.w = fs[0].w); break;
      case 'height': fs.forEach(f => f.h = fs[0].h); break;
      case 'hdist': case 'vdist': {
        if (fs.length < 3) { ctx.status('Select at least three fields to distribute.'); return; }
        const h = mode === 'hdist', a = h ? 'x' : 'y', s = h ? 'w' : 'h', o = sorted(a);
        const total = (h ? r - l : b - t) - o.reduce((n, f) => n + f[s], 0), gap = total / (o.length - 1);
        let pos = h ? l : t; for (const f of o) { f[a] = pos; pos += f[s] + gap; }
        break;
      }
    }
    ctx.repaint(); commitMoves(fs);
  }

  // ---- tab order ----
  const tabOrderNames = () => list.map(f => f.name);
  function moveInTab(f, delta) {
    const names = tabOrderNames(), i = list.indexOf(f), j = i + delta;
    if (j < 0 || j >= list.length) return;
    [names[i], names[j]] = [names[j], names[i]];
    run(() => post('order', { names }, { page: page() }), `Tab position ${j + 1} of ${list.length}.`);
  }
  const sortByPosition = () => run(() => post('order', { names: [...list].sort((a, b) => Math.abs(a.y - b.y) > 4 ? a.y - b.y : a.x - b.x).map(f => f.name) }, { page: page() }), 'Tab order set to reading order.');
  const setTabMode = mode => run(() => post('tab', undefined, { page: page(), mode }), `Page tab order: ${mode}.`);

  // ---- property panel ----
  const colour = (k, label, f, optional) => `<label class="fld"><span>${label}</span><div class="swatch"><input type="color" data-ff="${k}" value="${f[k] || '#000000'}"><code>${(f[k] || 'none').toUpperCase()}</code>${optional ? `<button type="button" class="ghost" data-ffnone="${k}" aria-label="No ${label.toLowerCase()}">${ctx.ic('x')}</button>` : ''}</div></label>`;
  const num = (k, label, f, step = 1, min) => `<label class="fld"><span>${label}</span><input type="number" step="${step}" ${min !== undefined ? `min="${min}"` : ''} data-ff="${k}" value="${+(+f[k] || 0).toFixed(2)}"${ctx.locked() && 'xywh'.includes(k) ? ' disabled title="Locked"' : ''}></label>`;
  const txt = (k, label, f) => `<label class="fld"><span>${label}</span><input type="text" data-ff="${k}" value="${ctx.esc(f[k] ?? '')}"></label>`;
  const chk = (k, label, f) => `<label class="chk"><span>${label}</span><input type="checkbox" data-ff="${k}" ${f[k] ? 'checked' : ''}></label>`;
  const sel1 = (k, label, f, opts) => `<label class="fld"><span>${label}</span><select data-ff="${k}">${opts.map(([v, l]) => `<option value="${v}"${String(f[k]) === String(v) ? ' selected' : ''}>${l}</option>`).join('')}</select></label>`;
  const optText = f => (f.options ?? []).map(o => o.display && o.display !== o.export ? `${o.export}=${o.display}` : o.export).join('\n');
  const parseOpts = s => s.split('\n').map(l => l.trim()).filter(Boolean).map(l => { const i = l.indexOf('='); return i > 0 ? { export: l.slice(0, i), display: l.slice(i + 1) } : { export: l }; });

  function props(p) {
    if (!editing) return false;
    if (ctx.otherSelection()) { selected.clear(); return false; }
    const fs = sel();
    if (!fs.length) return false;
    if (fs.length > 1) return multiProps(p, fs);
    const f = fs[0], k = f.kind, texty = TEXTY.has(k), choice = k === 'combo' || k === 'list';
    let h = `<section class="head"><span class="chip">${ctx.ic('square')}</span><h2>${KIND_LABEL[k]}</h2></section>`;
    h += `<section><h3>Field</h3>${txt('name', k === 'radio' ? 'Group name' : 'Name', f)}${txt('tooltip', 'Tooltip', f)}<div class="row">${chk('required', 'Required', f)}${chk('readOnly', 'Read-only', f)}</div></section>`;
    h += '<section><h3>Value</h3>';
    if (k === 'multiline') h += `<label class="fld"><span>Default value</span><textarea rows="3" data-ff="value">${ctx.esc(f.value ?? '')}</textarea></label>`;
    else if (texty) h += txt('value', 'Value', f);
    if (texty) h += num('maxLength', 'Max length (0 = none)', f, 1, 0);
    if (k === 'checkbox' || k === 'radio') h += txt('exportValue', 'Export value', f) + `<label class="chk"><span>Selected</span><input type="checkbox" data-ffcheck ${f.value && f.value === f.exportValue ? 'checked' : ''}></label>`;
    if (choice) {
      h += `<label class="fld"><span>Options (value or value=label, one per line)</span><textarea rows="4" data-ffopts>${ctx.esc(optText(f))}</textarea></label>`
        + `<label class="fld"><span>${k === 'list' && f.multiSelect ? 'Selected values (one per line)' : 'Selected value'}</span>${k === 'list' && f.multiSelect ? `<textarea rows="2" data-ff="value">${ctx.esc(f.value ?? '')}</textarea>` : `<input type="text" data-ff="value" value="${ctx.esc(f.value ?? '')}">`}</label>`
        + (k === 'combo' ? chk('editable', 'Editable', f) : chk('multiSelect', 'Multiple selection', f));
    }
    if (k === 'button') h += txt('caption', 'Caption', f);
    h += '</section>';
    if (k !== 'signature' && k !== 'checkbox' && k !== 'radio' && k !== 'button' || k === 'button')
      h += `<section><h3>Text</h3><div class="row">${sel1('fontName', 'Font', f, FONTS)}${num('fontSize', 'Size (0 = auto)', f, 0.5, 0)}</div>${colour('textColor', 'Color', f)}${k === 'button' ? '' : sel1('alignment', 'Alignment', f, [[0, 'Left'], [1, 'Centre'], [2, 'Right']])}</section>`;
    else if (k === 'checkbox' || k === 'radio') h += `<section><h3>Mark</h3>${colour('textColor', 'Color', f)}</section>`;
    h += `<section><h3>Appearance</h3>${colour('borderColor', 'Border', f, true)}${colour('backgroundColor', 'Background', f, true)}<div class="row">${num('borderWidth', 'Border width', f, 0.5, 0)}${sel1('borderStyle', 'Style', f, STYLES)}</div></section>`;
    h += `<section><h3>Position</h3><div class="row">${num('x', 'X', f)}${num('y', 'Y', f)}</div><div class="row">${num('w', 'Width', f)}${num('h', 'Height', f)}</div></section>`;
    const pos = list.indexOf(f) + 1;
    h += `<section><h3>Tab order</h3><p class="hint">Position ${pos} of ${list.length} on this page.</p><div class="ffbtns"><button type="button" data-ffact="tabup">Earlier</button><button type="button" data-ffact="tabdown">Later</button></div></section>`;
    h += `<section class="acts2"><button type="button" data-ffact="dup" title="Duplicate (Ctrl+D)">${ctx.ic('copy')}Duplicate</button>${k === 'radio' ? '<button type="button" data-ffact="radio">Add option</button>' : ''}<button type="button" class="danger" data-ffact="del" title="Delete (Del)">${ctx.ic('trash')}Delete</button></section>`;
    p.innerHTML = h;

    const read = () => {
      const g = { ...f };
      p.querySelectorAll('[data-ff]').forEach(el => {
        const n = el.dataset.ff;
        if (el.type === 'color') { if (el.dataset.touched === '1') g[n] = el.value; return; }
        g[n] = el.type === 'checkbox' ? el.checked : el.type === 'number' ? +el.value : el.value;
      });
      if (p.querySelector('[data-ffopts]')) g.options = parseOpts(p.querySelector('[data-ffopts]').value);
      const c = p.querySelector('[data-ffcheck]'); if (c) g.value = c.checked ? g.exportValue : 'Off';
      return g;
    };
    const apply = el => {
      if (!byKey(key(f))) return;
      const g = read();
      if (el?.dataset?.ffnone) g[el.dataset.ffnone] = null;
      run(() => post('update', g, { n: f.name, i: f.widget }).then(() => { selected = new Set([key(g)]); focusKey = key(g); }), `Updated ${g.name}.`);
    };
    p.querySelectorAll('[data-ff],[data-ffopts],[data-ffcheck]').forEach(el => el.onchange = () => { if (el.type === 'color') el.dataset.touched = '1'; apply(); });
    p.querySelectorAll('[data-ffnone]').forEach(b => b.onclick = () => apply(b));
    p.querySelectorAll('[data-ffact]').forEach(b => b.onclick = () => {
      const a = b.dataset.ffact;
      if (a === 'del') remove([f]);
      else if (a === 'dup') { copy(); paste(); }
      else if (a === 'tabup') moveInTab(f, -1);
      else if (a === 'tabdown') moveInTab(f, 1);
      else if (a === 'radio') {
        const n = list.filter(x => x.name === f.name).length + 1;
        run(() => post('add', { ...f, widget: 0, y: f.y + f.h + 8, exportValue: `Choice${n}`, value: 'Off' }).then(json), 'Added a radio option to the group.');
      }
    });
    return true;
  }

  function multiProps(p, fs) {
    p.innerHTML = `<section class="head"><span class="chip">${ctx.ic('square')}</span><h2>${fs.length} form fields</h2></section>
      <section><h3>Align</h3><div class="ffbtns">${[['left', 'Left'], ['centre', 'Centre'], ['right', 'Right'], ['top', 'Top'], ['middle', 'Middle'], ['bottom', 'Bottom']].map(([m, l]) => `<button type="button" data-ffarr="${m}">${l}</button>`).join('')}</div></section>
      <section><h3>Distribute and size</h3><div class="ffbtns"><button type="button" data-ffarr="hdist">Horizontally</button><button type="button" data-ffarr="vdist">Vertically</button><button type="button" data-ffarr="width">Same width</button><button type="button" data-ffarr="height">Same height</button></div><p class="hint">Alignment uses the selection bounds; sizes match the first field.</p></section>
      <section class="acts2"><button type="button" data-ffact="dup">${ctx.ic('copy')}Duplicate</button><button type="button" class="danger" data-ffact="del">${ctx.ic('trash')}Delete</button></section>`;
    p.querySelectorAll('[data-ffarr]').forEach(b => b.onclick = () => arrange(b.dataset.ffarr));
    p.querySelector('[data-ffact="dup"]').onclick = () => { copy(); paste(); };
    p.querySelector('[data-ffact="del"]').onclick = () => remove(fs);
    return true;
  }

  // ---- toolbar menu ----
  const importInput = Object.assign(document.createElement('input'), { type: 'file', accept: '.xfdf,.fdf,application/vnd.fdf,application/vnd.adobe.xfdf,text/xml', hidden: true });
  document.body.appendChild(importInput);
  importInput.onchange = async () => {
    const file = importInput.files[0]; importInput.value = ''; if (!file) return;
    const fd = new FormData(); fd.append('f', file);
    const r = await run(() => ctx.post('/api/form/import', fd).then(json), x => `Imported ${x?.applied ?? 0} value(s)${x?.skipped.length ? `; skipped ${x.skipped.length}` : ''}.`);
    if (r?.skipped.length) ctx.modal('Form data import', `<p>${r.applied} value(s) applied. Skipped:</p><ul>${r.skipped.slice(0, 50).map(s => `<li>${ctx.esc(s)}</li>`).join('')}</ul>`, 'Close', () => true);
  };

  const flatten = async (scope) => {
    const names = scope === 'selected' ? sel().map(f => f.name) : undefined;
    if (scope === 'selected' && !names.length) { ctx.status('Select the fields to flatten first.'); return; }
    if (!await window.editorUi.confirm({ title: 'Flatten form fields', message: 'Flatten bakes field appearances into the page and removes the fields. You can undo this with Ctrl+Z until you save.', confirmLabel: 'Flatten fields' })) return;
    selected.clear();
    run(() => post('flatten', scope === 'selected' ? { names } : undefined, { scope }).then(json), r => `Flattened ${r?.flattened ?? 0} field(s).`);
  };

  const button = document.createElement('button');
  button.id = 'formBtn'; button.type = 'button'; button.setAttribute('aria-haspopup', 'menu'); button.title = 'Form fields: add, align, tab order, flatten, import and export';
  button.innerHTML = `${ctx.ic('select')}<span class="lbl">Form</span>${ctx.ic('down')}`;
  button.onclick = () => {
    const has = !!ctx.pageCount(), r = button.getBoundingClientRect();
    ctx.openMenuAt(ctx.buildMenu([
      { label: 'Add field', disabled: !has, sub: KINDS.map(([k, l]) => ({ label: l, run: () => arm(k) })) },
      { label: 'Select all fields on page', disabled: !list.length, run: () => { setMode(true); selected = new Set(list.map(key)); ctx.clearOtherSelection(); ctx.repaint(); ctx.props(); } },
      '-',
      { label: 'Tab order', disabled: !has, sub: [
        { label: 'Row order', run: () => setTabMode('row') }, { label: 'Column order', run: () => setTabMode('column') }, { label: 'Structure order', run: () => setTabMode('structure') },
        { label: 'Unspecified', run: () => setTabMode('none') }, '-',
        { label: 'Reading order (top to bottom)', disabled: list.length < 2, run: sortByPosition },
        { label: showTabs ? 'Hide tab numbers' : 'Show tab numbers', run: () => { showTabs = !showTabs; ctx.repaint(); } }] },
      '-',
      { label: 'Import form data…', disabled: !has, run: () => importInput.click() },
      { label: 'Export form data', disabled: !has, sub: [{ label: 'XFDF (.xfdf)', run: () => ctx.download('/api/form/export?format=xfdf') }, { label: 'FDF (.fdf)', run: () => ctx.download('/api/form/export?format=fdf') }] },
      '-',
      { label: 'Flatten form', disabled: !has, sub: [{ label: 'All fields', run: () => flatten('all') }, { label: 'Read-only fields', run: () => flatten('readonly') }, { label: 'Selected fields', disabled: !selected.size, run: () => flatten('selected') }] },
    ]), r.left, r.bottom + 4);
  };
  const toolbar = ctx.toolbar(); toolbar.append(button, mode);

  function editEntries() {
    const fields = sel(), has = fields.length > 0, mod = navigator.platform.includes('Mac') ? '⌘' : 'Ctrl+';
    const multiple = fields.length > 1, locked = ctx.locked();
    return [
      { label: 'Cut', key: mod + 'X', disabled: !has, run: () => { copy(); remove(fields); } },
      { label: 'Copy', key: mod + 'C', disabled: !has, run: copy },
      { label: 'Paste', key: mod + 'V', disabled: !clip?.length, run: () => paste() },
      { label: 'Duplicate', key: mod + 'D', disabled: !has, run: () => { copy(); paste(); } },
      { label: 'Align fields', disabled: !multiple || locked, sub:
        [['left', 'Left'], ['centre', 'Center horizontally'], ['right', 'Right'], ['top', 'Top'], ['middle', 'Middle vertically'], ['bottom', 'Bottom']]
          .map(([mode, label]) => ({ label, run: () => arrange(mode) })) },
      { label: 'Distribute fields', disabled: fields.length < 3 || locked, sub: [
        { label: 'Horizontally', run: () => arrange('hdist') }, { label: 'Vertically', run: () => arrange('vdist') }] },
      { label: 'Delete', key: 'Del', disabled: !has, danger: true, run: () => remove(fields) },
    ];
  }

  addEventListener('keydown', e => {
    if (!armed) return;
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); arm(null); }
    else if (e.key === 'Enter' && !/INPUT|TEXTAREA|SELECT|BUTTON/.test(document.activeElement?.tagName)) { e.preventDefault(); e.stopPropagation(); placeCentre(); }
  }, true);

  return { load, paint, props, layerDown, editEntries, hasClipboard: () => !!clip?.length, editing: () => editing, reset: () => { editing = true; selected.clear(); armed = null; clip = null; list = []; updateMode(); }, clear: () => { selected.clear(); }, cancelArmed: () => arm(null), hasSelection: () => selected.size > 0 };
}
