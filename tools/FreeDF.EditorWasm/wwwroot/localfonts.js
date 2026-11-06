// Installed-font enumeration with the Local Font Access API. Font metadata (family, style, PostScript name) may be cached in
// IndexedDB ("freedf-local-fonts"); font bytes are only ever handed to the in-page PDF engine, never stored or sent anywhere.

import { migrateLegacyStorage } from './storage.js';

const DB = 'freedf-local-fonts', STORE = 'metadata', KEY = 'families', FORMAT = 1;
const storage = { legacy: 'adobye-local-fonts', current: DB, store: STORE, key: KEY };
const SAMPLE = 'The quick brown fox jumps over the lazy dog 0123456789';

export const supportsLocalFonts = (win = globalThis) => typeof win.queryLocalFonts === 'function';

export const styleFlags = style => ({ bold: /bold|black|heavy/i.test(style ?? ''), italic: /italic|oblique/i.test(style ?? '') });

/** Groups FontData-like records ({ family, fullName, postscriptName, style }) into sorted families, dropping unusable and duplicate faces. */
export function mapLocalFonts(raw) {
  const families = new Map(), seen = new Set();
  for (const f of raw ?? []) {
    if (!f?.family || !f.postscriptName || seen.has(f.postscriptName)) continue;
    seen.add(f.postscriptName);
    const style = f.style || 'Regular';
    if (!families.has(f.family)) families.set(f.family, { family: f.family, styles: [] });
    families.get(f.family).styles.push({ style, fullName: f.fullName || `${f.family} ${style}`, postscriptName: f.postscriptName, ...styleFlags(style) });
  }
  return [...families.values()]
    .map(entry => ({ ...entry, styles: entry.styles.sort((a, b) => a.style.localeCompare(b.style)) }))
    .sort((a, b) => a.family.localeCompare(b.family, undefined, { sensitivity: 'base' }));
}

const PLAIN = ['regular', 'normal', 'roman', 'book', 'italic', 'bold', 'bold italic', 'oblique', 'bold oblique'];

/** At most one face per regular/bold/italic/bold-italic slot, preferring the plain style names. */
export function pickVariants(entry) {
  const slots = new Map();
  for (const s of entry.styles) {
    const key = `${s.bold ? 'b' : ''}${s.italic ? 'i' : ''}`, current = slots.get(key);
    const rank = x => { const i = PLAIN.indexOf(x.style.toLowerCase()); return i >= 0 ? i : 100 + x.style.length; };
    if (!current || rank(s) < rank(current)) slots.set(key, s);
  }
  return [...slots.values()];
}

export const describeFontError = error => error?.name === 'NotAllowedError' || error?.name === 'SecurityError'
  ? 'Permission to read installed fonts was not granted. Standard PDF fonts and the fonts already embedded in the document remain available.'
  : `Installed fonts could not be read: ${error?.message || error?.name || 'unknown error'}.`;

function database(win, mode, action) {
  return new Promise((resolve, reject) => {
    const open = win.indexedDB.open(DB, 1);
    let blocked = false;
    open.onupgradeneeded = () => open.result.createObjectStore(STORE);
    open.onblocked = () => { blocked = true; reject(new Error('Font metadata storage is blocked. Close other editor tabs and retry.')); };
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result;
      if (blocked) { db.close(); return; }
      db.onversionchange = () => db.close();
      const tx = db.transaction(STORE, mode), request = action(tx.objectStore(STORE));
      tx.oncomplete = () => { db.close(); resolve(request.result); };
      tx.onabort = tx.onerror = () => { db.close(); reject(tx.error ?? new Error('Font metadata storage failed.')); };
    };
  });
}

/** upload(blob, postscriptName) → { accepted, family, bold, italic, reason, substitute } from the local PDF engine. */
export function createLocalFonts({ upload, win = globalThis }) {
  const supported = supportsLocalFonts(win);
  return {
    supported,
    async cached() {
      await migrateLegacyStorage(win, storage);
      const record = await database(win, 'readonly', s => s.get(KEY));
      return record?.format === FORMAT && Array.isArray(record.families) ? record.families : null;
    },
    /** Prompts for permission when needed, so call it from a user gesture. */
    async refresh() {
      await migrateLegacyStorage(win, storage);
      const families = mapLocalFonts(await win.queryLocalFonts());
      try { await database(win, 'readwrite', s => s.put({ format: FORMAT, savedAt: Date.now(), families }, KEY)); } catch { /* the cache is an optimisation */ }
      return families;
    },
    /** Re-reads families that were registered before a reload; throws with the reasons when any stay unavailable. */
    async reload(names) {
      const families = await this.refresh(), restored = [], failed = [];
      for (const name of names) {
        const entry = families.find(f => f.family === name);
        if (!entry) { failed.push({ family: name, reason: 'It is not installed on this device.' }); continue; }
        const result = await this.embed(entry);
        if (result.family) restored.push(name); else failed.push({ family: name, reason: result.refused.reason });
      }
      return { restored, failed };
    },
    /** Reads the chosen family's faces and registers them with the engine. */
    async embed(entry) {
      const wanted = pickVariants(entry);
      const faces = await win.queryLocalFonts({ postscriptNames: wanted.map(w => w.postscriptName) });
      if (!faces.length) throw new Error(`${entry.family} is no longer installed. Refresh the list.`);
      const results = [];
      for (const face of faces) results.push(await upload(await face.blob(), face.postscriptName));
      const accepted = results.filter(r => r.accepted), refused = results.find(r => !r.accepted);
      const preferred = accepted.find(r => !r.bold && !r.italic) ?? accepted[0];
      return preferred ? { family: preferred.family, refused: null } : { family: null, refused };
    },
  };
}

/** Adds an independently searchable installed-fonts section to the font sidebar. */
export function attachPicker(panel, { controller, choose, current, refreshFonts, esc, famCss }) {
  const box = document.createElement('details');
  box.className = 'flocal';
  box.innerHTML = '<summary>Installed fonts on this device</summary><p class="hint">Font names stay on this device. Only a chosen family is read and embedded in the PDF, subject to its license.</p><label class="fld"><span>Search installed fonts</span><input type="search" aria-label="Search installed fonts" autocomplete="off" disabled></label><div class="fprev" aria-hidden="true"></div><div class="fl fll" role="group" aria-label="Installed font families" hidden></div><div class="fmsg" role="status"></div><button type="button" class="fpl"></button>';
  const query = box.querySelector('input'), preview = box.querySelector('.fprev'), list = box.querySelector('.fll'), message = box.querySelector('.fmsg'), button = box.querySelector('.fpl');
  preview.textContent = SAMPLE;
  panel.append(box);
  let families = null, busy = false, busyFocus = null;
  const back = panel.closest('.font-panel')?.querySelector('.font-back');
  const setBusy = value => {
    if (value) {
      busyFocus = box.contains(document.activeElement) ? document.activeElement : null;
      if (busyFocus) back?.focus();
    }
    busy = value; button.disabled = value; query.disabled = value || !families;
    list.querySelectorAll('.flo').forEach(el => el.disabled = value);
    if (!value && busyFocus?.isConnected && document.activeElement === back)
      (busyFocus === button && families ? query : busyFocus).focus();
  };
  const say = (text, substitute) => {
    message.textContent = text ?? '';
    if (!substitute) return;
    const use = document.createElement('button');
    use.type = 'button'; use.className = 'fpl'; use.textContent = `Use ${substitute} instead`;
    use.onclick = () => choose(substitute);
    message.append(' ', use);
  };
  const render = () => {
    if (!families) return;
    const t = query.value.trim().toLowerCase(), shown = families.filter(f => f.family.toLowerCase().includes(t));
    query.disabled = busy; list.hidden = false;
    list.innerHTML = shown.map(f =>
      `<button type="button" class="fo flo${f.family === current ? ' on' : ''}" aria-pressed="${f.family === current}" data-local="${esc(f.family)}" style="font-family:${esc(famCss(f.family))}" title="${esc(f.family)}">${esc(f.family)}</button>`).join('') || '<div class="fh">No matching installed fonts</div>';
    list.querySelectorAll('.flo').forEach(b => { b.tabIndex = -1; b.disabled = busy; b.onclick = () => embed(families.find(f => f.family === b.dataset.local)); });
    const first = list.querySelector('.on') || list.querySelector('.flo'); if (first) first.tabIndex = 0;
  };
  async function embed(entry) {
    say(`Embedding ${entry.family}…`); setBusy(true);
    try {
      const result = await controller.embed(entry);
      if (result.family) { await refreshFonts(); choose(result.family); return; }
      say(`${entry.family} cannot be embedded. ${result.refused.reason}`, result.refused.substitute);
    } catch (error) { say(describeFontError(error)); }
    finally { setBusy(false); }
  }
  const show = value => { if (value) preview.style.fontFamily = value; };
  panel.addEventListener('mouseover', e => show(e.target.closest('.fo')?.style.fontFamily));
  panel.addEventListener('focusin', e => show(e.target.closest('.fo')?.style.fontFamily));
  query.addEventListener('input', render);
  if (!controller.supported) {
    button.hidden = true;
    say('This browser cannot list installed fonts. PDF fonts above remain available, and existing text keeps its original font until you change it.');
    return;
  }
  button.textContent = 'Load installed fonts';
  button.onclick = async () => {
    setBusy(true);
    try { say('Reading installed font names on this device…'); families = await controller.refresh(); button.textContent = 'Refresh installed fonts'; say(''); render(); }
    catch (error) { say(describeFontError(error)); }
    finally { setBusy(false); }
  };
  controller.cached().then(cached => { if (cached && !families) { families = cached; button.textContent = 'Refresh installed fonts'; render(); } })
    .catch(error => say(`Installed font metadata could not be restored: ${error.message}`));
}
