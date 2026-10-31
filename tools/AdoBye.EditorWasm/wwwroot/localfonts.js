// Installed-font enumeration with the Local Font Access API. Font metadata (family, style, PostScript name) may be cached in
// IndexedDB ("adobye-local-fonts"); font bytes are only ever handed to the in-page PDF engine, never stored or sent anywhere.

const DB = 'adobye-local-fonts', STORE = 'metadata', KEY = 'families', FORMAT = 1;
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
    open.onupgradeneeded = () => open.result.createObjectStore(STORE);
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result, tx = db.transaction(STORE, mode), request = action(tx.objectStore(STORE));
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
      try {
        const record = await database(win, 'readonly', s => s.get(KEY));
        return record?.format === FORMAT && Array.isArray(record.families) ? record.families : null;
      } catch { return null; }
    },
    /** Prompts for permission when needed, so call it from a user gesture. */
    async refresh() {
      const families = mapLocalFonts(await win.queryLocalFonts());
      try { await database(win, 'readwrite', s => s.put({ format: FORMAT, savedAt: Date.now(), families }, KEY)); } catch { /* the cache is an optimisation */ }
      return families;
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

/** Adds the installed-fonts section to the font picker popover. */
export function attachPicker(pop, { controller, choose, refreshFonts, esc, famCss }) {
  const query = pop.querySelector('input'), box = document.createElement('div');
  box.className = 'flocal';
  box.innerHTML = '<div class="fprev" aria-hidden="true"></div><div class="fl fll" role="listbox" aria-label="Installed fonts" hidden></div><div class="fmsg" role="status"></div><button type="button" class="fpl"></button>';
  const [preview, list, message, button] = box.children;
  preview.textContent = SAMPLE;
  pop.append(box);
  let families = null;
  const say = (text, substitute) => {
    message.textContent = text ?? '';
    if (!substitute) return;
    const use = document.createElement('button');
    use.type = 'button'; use.className = 'fpl'; use.textContent = `Use ${substitute} instead`;
    use.onclick = () => { pop.remove(); choose(substitute); };
    message.append(' ', use);
  };
  const render = () => {
    if (!families) return;
    const t = query.value.trim().toLowerCase(), shown = families.filter(f => f.family.toLowerCase().includes(t));
    list.hidden = false;
    list.innerHTML = `<div class="fh">Installed on this device</div>` + (shown.map(f =>
      `<button type="button" role="option" class="fo flo" data-local="${esc(f.family)}" style="font-family:${esc(famCss(f.family))}">${esc(f.family)}</button>`).join('') || '<div class="fh">No matching installed fonts</div>');
    list.querySelectorAll('.flo').forEach(b => b.onclick = () => embed(families.find(f => f.family === b.dataset.local), b));
  };
  async function embed(entry, option) {
    say(`Embedding ${entry.family}…`); option.disabled = true;
    try {
      const result = await controller.embed(entry);
      if (result.family) { await refreshFonts(); pop.remove(); choose(result.family); return; }
      say(`${entry.family} cannot be embedded. ${result.refused.reason}`, result.refused.substitute);
    } catch (error) { say(describeFontError(error)); }
    finally { option.disabled = false; }
  }
  const show = value => { if (value) preview.style.fontFamily = value; };
  pop.addEventListener('mouseover', e => show(e.target.closest('.fo')?.style.fontFamily));
  pop.addEventListener('focusin', e => show(e.target.closest('.fo')?.style.fontFamily));
  query.addEventListener('input', render);
  if (!controller.supported) {
    button.hidden = true;
    say('This browser cannot list installed fonts, so standard PDF fonts and the fonts embedded in the document are offered.');
    return;
  }
  button.textContent = 'Installed fonts on this device…';
  button.onclick = async () => {
    try { say('Reading installed font names on this device…'); families = await controller.refresh(); button.textContent = 'Refresh installed fonts'; say(''); render(); }
    catch (error) { say(describeFontError(error)); }
  };
  controller.cached().then(cached => { if (cached && !families) { families = cached; button.textContent = 'Refresh installed fonts'; render(); } });
}
