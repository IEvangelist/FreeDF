// Shared annotation authoring: tool palette, drawing overlay, properties and comments. Loaded on demand from the Annotate button.
// All document changes go through the shared editor commands (/api/annot-*), which are recorded in the unified page history (Ctrl+Z).
const SVG = 'http://www.w3.org/2000/svg';
const TOOLS = [
  ['highlight', 'Highlight', 'text'], ['underline', 'Underline', 'text'], ['strikeout', 'Strikeout', 'text'], ['squiggly', 'Squiggly', 'text'], ['replace', 'Replace text', 'text'],
  ['note', 'Sticky note', 'click'], ['freetext', 'Free text', 'rect'], ['ink', 'Freehand ink', 'ink'], ['line', 'Line', 'line'], ['arrow', 'Arrow', 'line'],
  ['square', 'Rectangle', 'rect'], ['circle', 'Ellipse', 'rect'], ['cloud', 'Cloud', 'rect'], ['polygon', 'Polygon', 'points'], ['polyline', 'Polyline', 'points'],
  ['stamp', 'Stamp', 'rect'], ['image-stamp', 'Image stamp', 'rect'], ['attachment', 'File attachment', 'click'], ['caret', 'Insert caret', 'click'], ['redact', 'Mark for redaction', 'rect'],
];
const STAMPS = ['Approved', 'Draft', 'Confidential', 'Final', 'ForComment', 'NotApproved', 'AsIs', 'Expired', 'Experimental', 'TopSecret', 'Sold', 'Departmental', 'ForPublicRelease', 'NotForPublicRelease'];
const STATES = [['', 'No status'], ['None', 'None'], ['Accepted', 'Accepted'], ['Rejected', 'Rejected'], ['Cancelled', 'Cancelled'], ['Completed', 'Completed'], ['Marked', 'Marked'], ['Unmarked', 'Unmarked']];
const MAX_ATTACHMENT = 25 * 1024 * 1024, MAX_STAMP_EDGE = 1600;
const rgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255);
const hex = c => c?.length === 3 ? '#' + c.map(v => Math.round(v * 255).toString(16).padStart(2, '0')).join('') : '#000000';
const el = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) k.startsWith('on') ? e[k] = v : v === true ? e.setAttribute(k, '') : v !== false && v != null && e.setAttribute(k, v);
  e.append(...kids); return e;
};
const label = (text, input) => el('label', { class: 'fld' }, el('span', {}, text), input);

export function createAnnotations(ctx) {
  const $ = id => document.getElementById(id);
  const prefs = { color: '#ffd400', fill: '', opacity: 1, width: 2, author: localStorage.getItem('annot-author') ?? '', subject: '', stamp: 'Approved', font: 12, icon: 'Note' };
  let tool = null, list = [], selected = 0, overlay = null, draft = null, filter = { type: '', author: '', state: '' }, sort = 'page', pendingFile = null;
  const live = el('div', { id: 'annot-live', role: 'status', 'aria-live': 'polite', class: 'sr-only' });
  const style = el('style', {}, `.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}
#annot-dock{position:absolute;right:12px;top:52px;bottom:12px;width:340px;z-index:20;display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:10px;box-shadow:0 12px 40px #000a;overflow:hidden}
#annot-dock[hidden]{display:none}#annot-dock header{display:flex;align-items:center;gap:6px;padding:8px 10px;border-bottom:1px solid var(--line)}#annot-dock h2{margin:0;font-size:13px;flex:1}
#annot-dock .body{overflow:auto;display:flex;flex-direction:column;gap:8px;padding:8px 10px}#annot-dock .tools{display:flex;flex-wrap:wrap;gap:4px}
#annot-dock .tools button{height:28px;padding:0 8px;font-size:12px}#annot-dock .fld{display:flex;flex-direction:column;gap:3px;font-size:12px}#annot-dock .row{display:flex;gap:6px;flex-wrap:wrap}
#annot-dock ul[role=listbox]{list-style:none;margin:0;padding:0;border:1px solid var(--line);border-radius:6px;max-height:220px;overflow:auto}
#annot-dock li[role=option]{padding:6px 8px;font-size:12px;cursor:pointer;border-bottom:1px solid var(--line)}#annot-dock li[role=option].reply{padding-left:22px}
#annot-dock li[aria-selected=true]{background:var(--accent-bg);outline:1px solid var(--accent)}
#annot-overlay{position:absolute;left:0;top:0;transform-origin:0 0;z-index:6}#annot-overlay.draw{cursor:crosshair;pointer-events:auto}#annot-overlay:not(.draw){pointer-events:none}
.annot-hit{position:absolute;pointer-events:auto;cursor:pointer;outline:1px dashed transparent}.annot-hit:hover,.annot-hit.on{outline-color:var(--accent)}.annot-hit.on{outline-style:solid;background:#4c8dff22}`);

  const announce = text => { live.textContent = ''; setTimeout(() => { live.textContent = text; }, 30); ctx.status(text); };
  const page = () => ctx.state().pages[ctx.page()];
  const toPdf = ([x, y]) => [page().x0 + x, page().top - y];
  const toBox = (x, y) => [x - page().x0, page().top - y];
  const post = async (path, body) => (await ctx.request(path, { method: 'POST', body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body), headers: typeof body === 'string' ? undefined : { 'Content-Type': 'application/json' } })).json();
  const mutate = async (path, body, message) => {
    const result = await post(path, body);
    await ctx.sync();
    await reload();
    if (message) announce(message);
    return result;
  };
  const fail = e => { console.error(e); announce(e.message); };

  function common() {
    return { author: prefs.author, subject: prefs.subject || undefined, color: rgb(prefs.color), opacity: prefs.opacity };
  }

  function specFor(kind, geo) {
    const base = common();
    const { x0, y0, x1, y1 } = geo.rect ?? {};
    const rect = geo.rect && [x0, y0, x1, y1];
    switch (kind) {
      case 'highlight': case 'underline': case 'strikeout': case 'squiggly':
        return { ...base, subtype: { highlight: 'Highlight', underline: 'Underline', strikeout: 'StrikeOut', squiggly: 'Squiggly' }[kind], quadPoints: geo.quads, contents: geo.text, borderWidth: kind === 'highlight' ? undefined : 1 };
      case 'note': return { ...base, subtype: 'Text', rect: [geo.x, geo.y - 20, geo.x + 20, geo.y], icon: prefs.icon, contents: geo.text ?? '' };
      case 'freetext': return { ...base, subtype: 'FreeText', rect, contents: geo.text ?? 'Text', fontSize: prefs.font, textColor: rgb(prefs.color), color: undefined, interiorColor: prefs.fill ? rgb(prefs.fill) : undefined };
      case 'ink': return { ...base, subtype: 'Ink', inkList: [geo.points], borderWidth: prefs.width };
      case 'line': case 'arrow': return { ...base, subtype: 'Line', line: geo.points, lineEnd: kind === 'arrow' ? 'ClosedArrow' : undefined, borderWidth: prefs.width };
      case 'square': case 'circle': case 'cloud':
        return { ...base, subtype: kind === 'circle' ? 'Circle' : 'Square', rect, cloud: kind === 'cloud' || undefined, borderWidth: prefs.width, interiorColor: prefs.fill ? rgb(prefs.fill) : undefined };
      case 'polygon': case 'polyline': return { ...base, subtype: kind === 'polygon' ? 'Polygon' : 'PolyLine', vertices: geo.points, borderWidth: prefs.width, interiorColor: kind === 'polygon' && prefs.fill ? rgb(prefs.fill) : undefined };
      case 'stamp': return { ...base, subtype: 'Stamp', rect, stampName: prefs.stamp, color: undefined };
      case 'image-stamp': return { subtype: 'Stamp', rect, author: prefs.author, subject: prefs.subject || undefined, image: geo.image, stampName: 'Custom' };
      case 'attachment': return { ...base, subtype: 'FileAttachment', rect: [geo.x, geo.y - 20, geo.x + 20, geo.y], attachment: geo.attachment, contents: geo.attachment.fileName, color: [0.3, 0.3, 0.3] };
      case 'caret': return { ...base, subtype: 'Caret', rect: [geo.x - 6, geo.y - 10, geo.x + 6, geo.y], color: [0, 0, 1], contents: geo.text ?? '' };
      case 'redact': return { subtype: 'Redact', author: prefs.author, quadPoints: [x0, y1, x1, y1, x0, y0, x1, y0], interiorColor: [0, 0, 0] };
    }
  }

  async function create(kind, boxGeo) {
    // boxGeo is in page-box space (y down); the server stores PDF user space (y up).
    let geo = {};
    if (boxGeo.rect) {
      const [a, b] = [toPdf([boxGeo.rect[0], boxGeo.rect[1]]), toPdf([boxGeo.rect[2], boxGeo.rect[3]])];
      geo.rect = { x0: Math.min(a[0], b[0]), y0: Math.min(a[1], b[1]), x1: Math.max(a[0], b[0]), y1: Math.max(a[1], b[1]) };
    }
    if (boxGeo.points) geo.points = boxGeo.points.flatMap(p => toPdf(p));
    if (boxGeo.at) [geo.x, geo.y] = toPdf(boxGeo.at);
    Object.assign(geo, boxGeo.extra ?? {});
    if (kind === 'replace') {
      const text = $('annot-replacement').value;
      const r = await mutate('/api/annot-replace?page=' + ctx.page(), { quadPoints: geo.quads, text, author: prefs.author }, `Replace text marked with “${text}”.`);
      selected = r.caret; return;
    }
    const spec = specFor(kind, geo);
    const r = await mutate('/api/annot-add?page=' + ctx.page(), spec, `${TOOLS.find(t => t[0] === kind)[1]} added on page ${ctx.page() + 1}.`);
    selected = r.number; renderList(); props();
  }

  async function textQuads(rect) {
    const [x0, y0, x1, y1] = rect;
    const r = await (await ctx.request(`/api/annot-text?page=${ctx.page()}&x=${Math.min(x0, x1)}&y=${Math.min(y0, y1)}&w=${Math.abs(x1 - x0)}&h=${Math.abs(y1 - y0)}`)).json();
    return r.quads?.length ? r : null;
  }

  // ---- drawing overlay ----
  function ensureOverlay() {
    const pg = $('page'); if (!pg) return null;
    if (!overlay || overlay.parentNode !== pg) { overlay = el('div', { id: 'annot-overlay' }); pg.appendChild(overlay); overlay.onpointerdown = down; }
    const layer = $('layer');
    Object.assign(overlay.style, { width: layer.style.width, height: layer.style.height, transform: layer.style.transform });
    overlay.classList.toggle('draw', !!tool);
    return overlay;
  }

  function drawHits() {
    const o = ensureOverlay(); if (!o) return;
    o.querySelectorAll('.annot-hit').forEach(n => n.remove());
    const S = ctx.scale();
    for (const a of list.filter(a => a.page === ctx.page() && a.parentNumber == null && a.spec.rect)) {
      const [rx0, ry0, rx1, ry1] = a.spec.rect, [bx, by] = toBox(Math.min(rx0, rx1), Math.max(ry0, ry1));
      const hit = el('div', { class: 'annot-hit' + (a.number === selected ? ' on' : ''), title: `${a.spec.subtype}${a.spec.author ? ' by ' + a.spec.author : ''}`, 'data-n': a.number });
      Object.assign(hit.style, { left: bx * S + 'px', top: by * S + 'px', width: Math.abs(rx1 - rx0) * S + 'px', height: Math.abs(ry1 - ry0) * S + 'px' });
      hit.onpointerdown = e => { if (tool) return; e.stopPropagation(); select(a.number); beginMove(e, a); };
      o.appendChild(hit);
    }
  }

  function beginMove(e, a) {
    const S = ctx.scale(), start = ctx.toLayer(e.clientX, e.clientY); let dx = 0, dy = 0;
    const hit = e.currentTarget;
    const move = ev => { const p = ctx.toLayer(ev.clientX, ev.clientY); dx = (p[0] - start[0]) / S; dy = (p[1] - start[1]) / S; hit.style.transform = `translate(${dx * S}px,${dy * S}px)`; };
    const up = async () => {
      removeEventListener('pointermove', move); removeEventListener('pointerup', up);
      if (Math.abs(dx) + Math.abs(dy) < 2) return;
      try { await mutate(`/api/annot-transform?page=${ctx.page()}&n=${a.number}&sx=1&sy=1&tx=${dx}&ty=${-dy}`, undefined, 'Annotation moved.'); } catch (err) { fail(err); }
    };
    addEventListener('pointermove', move); addEventListener('pointerup', up);
  }

  function svgDraft(shape) {
    draft?.svg.remove();
    const o = ensureOverlay(), svg = document.createElementNS(SVG, 'svg');
    svg.setAttribute('width', o.style.width); svg.setAttribute('height', o.style.height); svg.style.cssText = 'position:absolute;left:0;top:0;pointer-events:none';
    svg.appendChild(shape); o.appendChild(svg); return svg;
  }
  const S_ = () => ctx.scale();
  const node = (name, attrs) => { const n = document.createElementNS(SVG, name); for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v); return n; };

  function down(e) {
    if (!tool || e.button !== 0) return;
    const kind = tool, type = TOOLS.find(t => t[0] === kind)[2], S = S_();
    const at = () => { const p = ctx.toLayer(e.clientX, e.clientY); return [p[0] / S, p[1] / S]; };
    const p0 = at();
    e.preventDefault(); overlay.setPointerCapture?.(e.pointerId);
    if (type === 'click') { finish(kind, { at: p0 }).catch(fail); return; }
    if (type === 'points') { addPoint(kind, p0); return; }
    const pts = [p0];
    const stroke = { stroke: prefs.color, 'stroke-width': Math.max(prefs.width * S, 1), fill: 'none', opacity: prefs.opacity };
    const shape = type === 'ink' ? node('polyline', stroke) : type === 'line' ? node('line', stroke) : node('rect', { ...stroke, fill: type === 'text' ? prefs.color + '55' : 'none', stroke: type === 'text' ? 'none' : prefs.color });
    draft = { svg: svgDraft(shape) };
    const move = ev => {
      const p = ctx.toLayer(ev.clientX, ev.clientY).map(v => v / S);
      if (type === 'ink') { pts.push(p); shape.setAttribute('points', pts.map(q => q.map(v => v * S).join(',')).join(' ')); }
      else if (type === 'line') { shape.setAttribute('x1', p0[0] * S); shape.setAttribute('y1', p0[1] * S); shape.setAttribute('x2', p[0] * S); shape.setAttribute('y2', p[1] * S); pts[1] = p; }
      else { pts[1] = p; const [x, y] = [Math.min(p0[0], p[0]), Math.min(p0[1], p[1])]; Object.entries({ x: x * S, y: y * S, width: Math.abs(p[0] - p0[0]) * S, height: Math.abs(p[1] - p0[1]) * S }).forEach(([k, v]) => shape.setAttribute(k, v)); }
    };
    const up = () => {
      removeEventListener('pointermove', move); removeEventListener('pointerup', up);
      draft?.svg.remove(); draft = null;
      if (pts.length < 2) return;
      if (type === 'ink') finish(kind, { points: simplify(pts) }).catch(fail);
      else if (type === 'line') finish(kind, { points: pts }).catch(fail);
      else finish(kind, { rect: [pts[0][0], pts[0][1], pts[1][0], pts[1][1]] }).catch(fail);
    };
    addEventListener('pointermove', move); addEventListener('pointerup', up);
  }

  // Drops points closer than 1.5 units; the server smooths the stroke with Catmull-Rom curves.
  const simplify = pts => pts.filter((p, i) => i === 0 || i === pts.length - 1 || Math.hypot(p[0] - pts[i - 1][0], p[1] - pts[i - 1][1]) > 1.5);

  let poly = null;
  function addPoint(kind, p) {
    const S = S_();
    poly ??= { kind, pts: [] };
    poly.pts.push(p);
    const shape = node(kind === 'polygon' ? 'polygon' : 'polyline', { points: poly.pts.map(q => q.map(v => v * S).join(',')).join(' '), stroke: prefs.color, 'stroke-width': Math.max(prefs.width * S, 1), fill: 'none' });
    draft = { svg: svgDraft(shape) };
    announce(`Point ${poly.pts.length} added. Press Enter or double-click to finish, Escape to cancel.`);
  }
  async function finishPoly() {
    if (!poly || poly.pts.length < 2) { cancelPoly(); return; }
    const { kind, pts } = poly; cancelPoly();
    await finish(kind, { points: pts });
  }
  function cancelPoly() { poly = null; draft?.svg.remove(); draft = null; }

  async function finish(kind, geo) {
    try {
      if (kind === 'attachment' && !geo.extra) {
        const file = pendingFile ?? await pickFile(''); pendingFile = null; if (!file) return;
        if (file.size > MAX_ATTACHMENT) { announce('Attachments are limited to 25 MB; ' + file.name + ' is ' + Math.ceil(file.size / 1048576) + ' MB.'); return; }
        geo.extra = { attachment: { fileName: file.name, data: file.size ? await toBase64(file) : '', mimeType: file.type || undefined } };
      }
      if (kind === 'image-stamp' && !geo.extra) {
        const file = pendingFile ?? await pickFile('image/png,image/jpeg'); pendingFile = null; if (!file) return;
        geo.extra = { image: await decodeImage(file) };
        if (geo.at) geo.rect = [geo.at[0], geo.at[1], geo.at[0] + 120, geo.at[1] + 120 * geo.extra.image.height / geo.extra.image.width];
      }
      if (['highlight', 'underline', 'strikeout', 'squiggly', 'replace'].includes(kind)) {
        const q = await textQuads(geo.rect);
        if (!q) { announce('No text found in that selection.'); return; }
        geo = { extra: { quads: q.quads, text: q.text } };
      }
      if (kind === 'note' || kind === 'caret' || kind === 'freetext') {
        const text = kind === 'caret' ? '' : prompt(kind === 'note' ? 'Note text' : 'Text', kind === 'freetext' ? 'Text' : '');
        if (text === null) return;
        geo.extra = { ...(geo.extra ?? {}), text };
      }
      if (kind === 'freetext' && geo.at) geo.rect = [geo.at[0], geo.at[1], geo.at[0] + 160, geo.at[1] + 48];
      if (kind === 'note' && geo.at) { /* click point is the icon's top-left */ }
      await create(kind, geo);
    } catch (e) { fail(e); }
  }

  const toBase64 = async file => { const bytes = new Uint8Array(await file.arrayBuffer()); let s = ''; for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000)); return btoa(s); };
  const pickFile = accept => new Promise(resolve => { const i = el('input', { type: 'file', accept }); i.onchange = () => resolve(i.files[0] ?? null); i.oncancel = () => resolve(null); i.click(); });
  async function decodeImage(file) {
    const bmp = await createImageBitmap(file), k = Math.min(1, MAX_STAMP_EDGE / Math.max(bmp.width, bmp.height));
    const w = Math.max(1, Math.round(bmp.width * k)), h = Math.max(1, Math.round(bmp.height * k)), c = new OffscreenCanvas(w, h), g = c.getContext('2d');
    g.drawImage(bmp, 0, 0, w, h);
    const d = g.getImageData(0, 0, w, h).data, n = w * h, rgbBytes = new Uint8Array(n * 3), alpha = new Uint8Array(n);
    for (let i = 0; i < n; i++) { rgbBytes.set([d[i * 4], d[i * 4 + 1], d[i * 4 + 2]], i * 3); alpha[i] = d[i * 4 + 3]; }
    const b64 = u => { let s = ''; for (let i = 0; i < u.length; i += 0x8000) s += String.fromCharCode(...u.subarray(i, i + 0x8000)); return btoa(s); };
    return { width: w, height: h, rgb: b64(rgbBytes), alpha: b64(alpha) };
  }

  // ---- UI ----
  const dock = el('section', { id: 'annot-dock', 'aria-label': 'Annotations and comments', hidden: true });
  function build() {
    const bar = el('div', { class: 'tools', role: 'toolbar', 'aria-label': 'Annotation tools' });
    for (const [id, name] of TOOLS) bar.append(el('button', { type: 'button', 'data-annot-tool': id, 'aria-pressed': 'false', title: name, onclick: () => setTool(tool === id ? null : id) }, name));
    const ctl = (type, key, extra = {}) => el('input', { type, value: prefs[key], ...extra, oninput: e => { prefs[key] = type === 'number' || type === 'range' ? +e.target.value : e.target.value; if (key === 'author') localStorage.setItem('annot-author', prefs.author); } });
    const stampSel = el('select', { onchange: e => { prefs.stamp = e.target.value; } }, ...STAMPS.map(s => el('option', { value: s }, s)));
    const iconSel = el('select', { onchange: e => { prefs.icon = e.target.value; } }, ...['Note', 'Comment', 'Help', 'Insert', 'Key', 'NewParagraph', 'Paragraph'].map(s => el('option', { value: s }, s)));
    const fillClear = el('input', { type: 'checkbox', id: 'annot-fill-on', onchange: e => { prefs.fill = e.target.checked ? fillInput.value : ''; } });
    const fillInput = el('input', { type: 'color', value: '#ffffff', oninput: e => { if (fillClear.checked) prefs.fill = e.target.value; } });
    const btn = (text, fn, attrs = {}) => el('button', { type: 'button', onclick: () => Promise.resolve(fn()).catch(fail), ...attrs }, text);
    const filterType = el('select', { 'aria-label': 'Filter by type', onchange: e => { filter.type = e.target.value; renderList(); } }, el('option', { value: '' }, 'All types'), ...TOOLS.map(t => el('option', { value: t[0] }, t[1])));
    const filterAuthor = el('select', { id: 'annot-filter-author', 'aria-label': 'Filter by author', onchange: e => { filter.author = e.target.value; renderList(); } }, el('option', { value: '' }, 'All authors'));
    const filterState = el('select', { 'aria-label': 'Filter by status', onchange: e => { filter.state = e.target.value; renderList(); } }, el('option', { value: '' }, 'Any status'), ...STATES.slice(1).map(([v, t]) => el('option', { value: v }, t)));
    const sortSel = el('select', { 'aria-label': 'Sort comments', onchange: e => { sort = e.target.value; renderList(); } }, ...[['page', 'By page'], ['date', 'By date'], ['author', 'By author'], ['type', 'By type']].map(([v, t]) => el('option', { value: v }, t)));
    const lb = el('ul', { id: 'annot-list', role: 'listbox', 'aria-label': 'Comments', tabindex: '0', onkeydown: listKeys });
    dock.replaceChildren(
      el('header', {}, el('h2', {}, 'Annotate'), btn('Undo', undoStep, { title: 'Undo last annotation change' }), btn('Redo', redoStep), btn('Close', () => toggle(false), { 'aria-label': 'Close annotations' })),
      el('div', { class: 'body' }, bar,
        el('div', { class: 'row' }, label('Color', ctl('color', 'color')), label('Opacity', ctl('range', 'opacity', { min: 0.1, max: 1, step: 0.05 })), label('Width', ctl('number', 'width', { min: 0, max: 40, step: 0.5, style: 'width:64px' }))),
        el('div', { class: 'row' }, label('Fill', el('span', {}, fillClear, fillInput)), label('Font size', ctl('number', 'font', { min: 6, max: 96, style: 'width:64px' })), label('Stamp', stampSel), label('Note icon', iconSel)),
        el('div', { class: 'row' }, label('Author', ctl('text', 'author')), label('Subject', ctl('text', 'subject'))),
        label('Replacement text (Replace tool)', el('input', { type: 'text', id: 'annot-replacement' })),
        el('div', { class: 'row' }, btn('Mark text matching Find…', markFromFind), btn('Add selected tool at page centre', placeCentre)),
        el('div', { id: 'annot-props' }),
        el('div', { class: 'row' }, filterType, filterAuthor, filterState, sortSel),
        el('span', { id: 'annot-count', class: 'sr-only' }), lb,
        el('div', { class: 'row' }, btn('Flatten page', flatten), btn('Flatten all', () => flatten(true)), btn('Apply redactions', applyRedactions)),
        el('div', { class: 'row' }, btn('Export XFDF', exportXfdf), btn('Import XFDF…', importXfdf))),
      live);
  }

  function setTool(id) {
    cancelPoly(); tool = id;
    dock.querySelectorAll('[data-annot-tool]').forEach(b => { const on = b.dataset.annotTool === id; b.setAttribute('aria-pressed', on); b.classList.toggle('on', on); });
    ensureOverlay();
    if (id) announce(`${TOOLS.find(t => t[0] === id)[1]} tool selected. Drag on the page, or use “Add selected tool at page centre” for the keyboard.`);
  }

  async function placeCentre() {
    if (!tool) { announce('Choose an annotation tool first.'); return; }
    const p = page(), cx = p.width / 2, cy = p.height / 2, type = TOOLS.find(t => t[0] === tool)[2];
    if (type === 'text') { await markFromFind(); return; }
    const geo = { rect: [cx - 60, cy - 30, cx + 60, cy + 30], at: [cx, cy], points: [[cx - 60, cy + 30], [cx + 60, cy - 30]] };
    if (type === 'points') geo.points = [[cx - 60, cy + 30], [cx + 60, cy + 30], [cx, cy - 40]];
    if (type === 'ink') geo.points = [[cx - 60, cy], [cx - 30, cy - 25], [cx, cy], [cx + 30, cy + 25], [cx + 60, cy]];
    await finish(tool, geo);
  }

  async function markFromFind() {
    const q = prompt('Mark text containing'); if (!q) return;
    const hits = await (await ctx.request('/api/find?q=' + encodeURIComponent(q))).json();
    const mine = hits.filter(h => h.page === ctx.page());
    if (!mine.length) { announce('No matches on this page.'); return; }
    const h = mine[0], kind = ['highlight', 'underline', 'strikeout', 'squiggly', 'replace'].includes(tool) ? tool : 'highlight';
    await finish(kind, { rect: [h.x, h.y - h.size, h.x + h.w, h.y + h.size * 0.3] });
  }

  function props() {
    const box = $('annot-props'); if (!box) return;
    const a = list.find(x => x.number === selected);
    if (!a) { box.replaceChildren(); return; }
    const s = a.spec, subj = el('input', { type: 'text', value: s.subject ?? '' }), text = el('textarea', { rows: 3 }, s.contents ?? '');
    const col = el('input', { type: 'color', value: hex(s.color) }), op = el('input', { type: 'range', min: 0.1, max: 1, step: 0.05, value: s.opacity ?? 1 });
    const bw = el('input', { type: 'number', min: 0, step: 0.5, value: s.borderWidth ?? 1, style: 'width:64px' });
    const replyText = el('input', { type: 'text', 'aria-label': 'Reply text' }), state = el('select', { 'aria-label': 'Review status' }, ...STATES.map(([v, t]) => el('option', { value: v }, t)));
    box.replaceChildren(
      el('h3', {}, `${s.subtype} #${a.number}`),
      label('Comment', text), label('Subject', subj),
      el('div', { class: 'row' }, label('Color', col), label('Opacity', op), label('Width', bw)),
      el('div', { class: 'row' },
        el('button', { type: 'button', onclick: () => mutate(`/api/annot-update?page=${a.page}&n=${a.number}`, { contents: text.value, subject: subj.value, color: rgb(col.value), opacity: +op.value, borderWidth: +bw.value }, 'Annotation updated.').catch(fail) }, 'Apply'),
        el('button', { type: 'button', onclick: () => remove(a) }, 'Delete')),
      el('div', { class: 'row' }, label('Reply', replyText), label('Status', state),
        el('button', { type: 'button', onclick: () => mutate(`/api/annot-reply?page=${a.page}&n=${a.number}`, { author: prefs.author, text: replyText.value, state: state.value || undefined }, 'Reply added.').catch(fail) }, 'Reply')));
  }

  async function remove(a) { await mutate(`/api/annot-delete?page=${a.page}&n=${a.number}`, undefined, 'Annotation deleted.'); selected = 0; props(); }

  const created = a => a.spec.created ? Date.parse(a.spec.created) : 0;
  const describe = a => {
    const s = a.spec, text = (s.contents ?? '').replace(/\s+/g, ' ').slice(0, 60);
    return `Page ${a.page + 1}, ${s.subtype === 'Text' && a.parentNumber != null ? 'reply' : s.subtype}${s.author ? ' by ' + s.author : ''}${s.state ? ', ' + s.state : ''}${text ? ': ' + text : ''}`;
  };
  function visible() {
    const type = filter.type ? TOOLS.find(t => t[0] === filter.type)?.[0] : '';
    const subtypeOf = { highlight: 'Highlight', underline: 'Underline', strikeout: 'StrikeOut', squiggly: 'Squiggly', replace: 'Caret', note: 'Text', freetext: 'FreeText', ink: 'Ink', line: 'Line', arrow: 'Line', square: 'Square', circle: 'Circle', cloud: 'Square', polygon: 'Polygon', polyline: 'PolyLine', stamp: 'Stamp', 'image-stamp': 'Stamp', attachment: 'FileAttachment', caret: 'Caret', redact: 'Redact' };
    const states = a => [a.spec.state, ...list.filter(r => r.parentNumber === a.number).map(r => r.spec.state)].filter(Boolean);
    const tops = list.filter(a => a.parentNumber == null && (!type || a.spec.subtype === subtypeOf[type]) && (!filter.author || a.spec.author === filter.author) && (!filter.state || states(a).includes(filter.state)));
    const key = { page: a => a.page * 1e6 - (a.spec.rect?.[3] ?? 0), date: a => created(a), author: a => a.spec.author ?? '', type: a => a.spec.subtype };
    return tops.sort((a, b) => { const x = key[sort](a), y = key[sort](b); return x < y ? -1 : x > y ? 1 : 0; });
  }
  function renderList() {
    const lb = $('annot-list'); if (!lb) return;
    const authors = [...new Set(list.map(a => a.spec.author).filter(Boolean))], fa = $('annot-filter-author');
    if (fa) { const cur = fa.value; fa.replaceChildren(el('option', { value: '' }, 'All authors'), ...authors.map(x => el('option', { value: x }, x))); fa.value = authors.includes(cur) ? cur : ''; filter.author = fa.value; }
    const rows = [];
    for (const a of visible()) { rows.push(a); rows.push(...list.filter(r => r.parentNumber === a.number)); }
    lb.replaceChildren(...rows.map(a => el('li', { role: 'option', id: 'annot-opt-' + a.number, class: a.parentNumber != null ? 'reply' : '', 'aria-selected': a.number === selected, 'data-n': a.number, onclick: () => select(a.number, true) }, describe(a))));
    lb.setAttribute('aria-activedescendant', selected && rows.some(r => r.number === selected) ? 'annot-opt-' + selected : '');
    $('annot-count').textContent = `${rows.length} comments`;
    drawHits();
  }
  async function select(n, jump = false) {
    selected = n; const a = list.find(x => x.number === n);
    if (a && jump && a.page !== ctx.page()) await ctx.goPage(a.page);
    renderList(); props();
    if (a) announce(describe(a));
  }
  function listKeys(e) {
    const opts = [...e.currentTarget.querySelectorAll('[role=option]')], i = opts.findIndex(o => +o.dataset.n === selected);
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); const o = opts[Math.max(0, Math.min(opts.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))]; if (o) select(+o.dataset.n); }
    else if (e.key === 'Enter' && i >= 0) { e.preventDefault(); select(selected, true); }
    else if (e.key === 'Delete' && i >= 0) { e.preventDefault(); const a = list.find(x => x.number === selected); if (a) remove(a).catch(fail); }
  }

  async function reload() { list = await (await ctx.request('/api/annots')).json(); renderList(); props(); }
  async function undoStep() { const r = await post('/api/page-undo').catch(fail); if (r) { await ctx.sync(); await reload(); announce(`Undid ${r.label}.`); } }
  async function redoStep() { const r = await post('/api/page-redo').catch(fail); if (r) { await ctx.sync(); await reload(); announce(`Redid ${r.label}.`); } }
  async function flatten(all = false) {
    if (!confirm(all ? 'Flatten annotations on every page into the page content?' : 'Flatten annotations on this page into the page content?')) return;
    const r = await post('/api/annot-flatten?page=' + (all ? -1 : ctx.page())); await ctx.sync(); await reload(); announce(`${r.flattened} annotations flattened.`);
  }
  async function applyRedactions() {
    if (!confirm('Redaction permanently removes the marked content when you save. Convert the marks on this page into redactions?')) return;
    const r = await post('/api/annot-redact-apply?page=' + ctx.page()); await ctx.sync(); await reload(); announce(`${r.applied} redaction marks applied.`);
  }
  async function exportXfdf() {
    const blob = await (await ctx.request('/api/annot-export')).blob();
    const a = el('a', { href: URL.createObjectURL(blob), download: 'annotations.xfdf' }); a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }
  async function importXfdf() {
    const f = await pickFile('.xfdf,.xml'); if (!f) return;
    const r = await post('/api/annot-import', await f.text()); await ctx.sync(); await reload(); announce(`${r.created} annotations imported.`);
  }

  addEventListener('keydown', e => {
    if (!tool && !poly) return;
    if (e.key === 'Escape') { if (poly) cancelPoly(); else setTool(null); }
    else if (e.key === 'Enter' && poly) { e.preventDefault(); finishPoly().catch(fail); }
  });
  addEventListener('dblclick', e => { if (poly && overlay?.contains(e.target)) { poly.pts.pop(); finishPoly().catch(fail); } });

  build();
  $('center').append(dock);
  async function toggle(show = dock.hidden) {
    dock.hidden = !show;
    document.getElementById('annot')?.setAttribute('aria-pressed', show);
    if (show) { await reload(); ensureOverlay(); dock.querySelector('button').focus(); } else setTool(null);
  }
  return { toggle, pageChanged: () => { ensureOverlay(); drawHits(); }, setTool, reload, list: () => list };
}
