// One-click sample documents. Loaded lazily by index.html once the editor is ready; nothing here runs at startup.
// Samples are same-origin static files listed by samples/manifest.json. Bytes are size- and sha256-verified before
// they reach the editor's normal open path, so the privacy invariant (no uploads, no third parties) is unchanged.
export const FULL_COLLECTION = '27-full-collection';
export const SHOWCASE = '28-benchmarks-brand-spec';
const NAME = /^\d\d-[a-z0-9-]+$/;

export const esc = text => String(text).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
export const formatSize = bytes => bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(bytes / 1024)) + ' KB';

export async function sha256Hex(bytes) {
  if (!globalThis.crypto?.subtle) throw new Error('This browser context cannot verify downloads (secure context required), so the sample was not opened.');
  return [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map(b => b.toString(16).padStart(2, '0')).join('');
}

export function parseManifest(json) {
  if (json?.version !== 1 || !Array.isArray(json.samples)) throw new Error('The sample list is not in a supported format.');
  return json.samples.map(s => {
    if (!NAME.test(s?.name) || s.file !== s.name + '.pdf' || !Number.isSafeInteger(s.size) || s.size <= 0 || !/^[0-9a-f]{64}$/.test(s.sha256))
      throw new Error('The sample list contains an invalid entry.');
    return { name: s.name, file: s.file, title: String(s.title ?? s.name), description: String(s.description ?? ''), size: s.size,
      pages: Number.isSafeInteger(s.pages) ? s.pages : 0, sha256: s.sha256, encrypted: !!s.encrypted, requiresPassword: !!s.requiresPassword };
  });
}

// Accepts #sample=NAME or ?sample=NAME; a bare number such as 27 resolves to its unique sample.
export function deepLinkName(location) {
  const value = new URLSearchParams(location.hash.replace(/^#/, '')).get('sample') ?? new URLSearchParams(location.search).get('sample');
  return value ? value.replace(/\.pdf$/i, '') : null;
}
export function findSample(entries, name) {
  return entries.find(e => e.name === name) ?? (/^\d\d$/.test(name) ? entries.find(e => e.name.startsWith(name + '-')) : undefined);
}
export function stripDeepLink(url) {
  const next = new URL(url);
  const hash = new URLSearchParams(next.hash.replace(/^#/, '')); hash.delete('sample');
  next.hash = hash.toString();
  next.searchParams.delete('sample');
  return next.href;
}

// Streams the entry's bytes, reporting progress, and refuses anything whose size or sha256 differs from the manifest.
export async function fetchVerified(entry, { base = document.baseURI, fetchImpl = (...a) => fetch(...a), onProgress = () => {}, signal } = {}) {
  const url = new URL('samples/' + entry.file, base);
  if (url.origin !== new URL(base).origin) throw new Error('Samples are only loaded from this site.');
  let response;
  try { response = await fetchImpl(url, { signal }); }
  catch (error) { if (error?.name === 'AbortError') throw error; throw new Error(`Could not download ${entry.file}. Check your connection and try again.`); }
  if (!response.ok) throw new Error(`Could not download ${entry.file} (HTTP ${response.status}).`);
  const chunks = [];
  let received = 0;
  const reader = response.body?.getReader();
  if (reader) {
    for (;;) {
      let step;
      try { step = await reader.read(); }
      catch (error) { if (error?.name === 'AbortError') throw error; throw new Error(`The download of ${entry.file} was interrupted. Try again.`); }
      if (step.done) break;
      received += step.value.length;
      if (received > entry.size) throw new Error(`${entry.file} is larger than expected, so it was not opened.`);
      chunks.push(step.value); onProgress(received, entry.size);
    }
  } else {
    const whole = new Uint8Array(await response.arrayBuffer());
    received = whole.length; chunks.push(whole); onProgress(received, entry.size);
  }
  if (received !== entry.size) throw new Error(`${entry.file} was incomplete (${received} of ${entry.size} bytes). Try again.`);
  const bytes = new Uint8Array(received);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  if (await sha256Hex(bytes) !== entry.sha256) throw new Error(`${entry.file} failed its integrity check (sha256), so it was not opened.`);
  return bytes;
}

const css = `
.sample-cta { margin-top: 10px; height: 38px; padding: 0 20px; font-size: 14px; }
.sample-secondary { border-color: var(--line-2); }
.sample-hint { font-size: 12px; max-width: 42ch; padding: 0 12px; }
.sample-load { position: fixed; right: 16px; bottom: 44px; z-index: 60; width: min(24rem, calc(100vw - 32px)); display: grid; gap: 8px; padding: 12px 14px;
  background: var(--raised, #1b1e25); color: var(--fg, #e8eaf0); border: 1px solid var(--line-2, #333845); border-radius: var(--r-lg, 10px); box-shadow: 0 8px 28px #0008; }
.sample-load progress { width: 100%; }
.sample-load [role=alert] { color: var(--danger, #ff6b7a); overflow-wrap: anywhere; }
.cm.sub { max-height: calc(100vh - 96px); overflow-y: auto; }
.sample-load .row { display: flex; gap: 8px; justify-content: flex-end; }`;

export function createSamples({ open, isDirty = () => false, confirmReplace = () => true, status = () => {}, capabilities = () => globalThis.pdfEditorCapabilities,
  doc = document, location: loc = globalThis.location, history: hist = globalThis.history, fetchImpl = (...a) => fetch(...a) } = {}) {
  const base = doc.baseURI;
  let entries = null, manifestPromise = null, active = null, panel = null;
  doc.head.append(Object.assign(doc.createElement('style'), { textContent: css }));

  // Capability contract (additive): the host/crypto code may set window.pdfEditorCapabilities = { openEncrypted: true } once the editor can
  // ask for passwords. Until then only samples that need a password (manifest requiresPassword) are blocked; encrypted samples that open
  // with an empty password (18) are attempted and, if this build cannot decrypt them, the open error is shown with Retry.
  const canOpenEncrypted = () => capabilities()?.openEncrypted === true;
  const blocked = e => e.requiresPassword && !canOpenEncrypted();
  function ensureManifest() {
    manifestPromise ??= (async () => {
      let response;
      try { response = await fetchImpl(new URL('samples/manifest.json', base)); }
      catch { throw new Error('Could not load the sample list. Check your connection and try again.'); }
      if (!response.ok) throw new Error(`The sample list is not available here (HTTP ${response.status}).`);
      entries = parseManifest(await response.json());
      renderCta();
      return entries;
    })().catch(error => { manifestPromise = null; throw error; });
    return manifestPromise;
  }

  function showPanel(opener) {
    if (!panel) {
      panel = doc.createElement('div'); panel.className = 'sample-load'; panel.setAttribute('role', 'group'); panel.setAttribute('aria-label', 'Sample download');
      panel.innerHTML = '<span data-s role="status" aria-live="polite"></span><progress hidden></progress><span data-e role="alert"></span><div class="row"><button type="button" data-retry hidden>Retry</button><button type="button" data-close>Cancel</button></div>';
      panel.querySelector('[data-close]').onclick = () => { active?.abort(); hidePanel(); };
      panel.onkeydown = e => { if (e.key === 'Escape') { e.stopPropagation(); panel.querySelector('[data-close]').click(); } };
      doc.body.append(panel);
    }
    panel.opener = opener;
    return panel;
  }
  function hidePanel() {
    const opener = panel?.opener;
    panel?.remove(); panel = null;
    if (opener?.isConnected) opener.focus();
  }
  function progress(text, value, max) {
    const bar = panel.querySelector('progress'), s = panel.querySelector('[data-s]');
    s.textContent = text; panel.querySelector('[data-e]').textContent = '';
    bar.hidden = value === undefined; if (value !== undefined) { bar.max = max; bar.value = value; bar.setAttribute('aria-label', text); }
    panel.querySelector('[data-retry]').hidden = true; panel.querySelector('[data-close]').textContent = 'Cancel';
  }
  function fail(error, retry) {
    panel.querySelector('[data-s]').textContent = '';
    panel.querySelector('progress').hidden = true;
    panel.querySelector('[data-e]').textContent = error.message;
    const again = panel.querySelector('[data-retry]');
    again.hidden = false; again.onclick = retry; panel.querySelector('[data-close]').textContent = 'Dismiss';
    again.focus(); status(error.message);
  }

  async function load(name, { confirmed = false } = {}) {
    if (active) return;
    const opener = doc.activeElement;
    if (!confirmed && !confirmReplace()) return;
    const wasDirty = isDirty(), control = new AbortController();
    active = control; renderCta();
    showPanel(opener); progress('Looking up the sample…');
    try {
      const entry = findSample(await ensureManifest(), name);
      if (!entry) throw new Error(name === FULL_COLLECTION
        ? 'The full sample collection is not published here. Generate it with: dotnet run --project tools/FreeDF.Samples -- generate samples 27-'
        : `Unknown sample "${name}".`);
      if (blocked(entry)) throw new Error(`${entry.file} needs a password, and this build has no password prompt yet.`);
      const label = `Downloading ${entry.file}`;
      progress(`${label} (${formatSize(entry.size)})…`, 0, entry.size);
      let spoken = -1;
      const bytes = await fetchVerified(entry, { base, fetchImpl, signal: control.signal, onProgress: (done, total) => {
        const quarter = Math.floor(done / total * 4);
        if (!panel) return;
        if (quarter === spoken) panel.querySelector('progress').value = done;
        else { spoken = quarter; progress(`${label}: ${formatSize(done)} of ${formatSize(total)}`, done, total); }
      } });
      if (control.signal.aborted) return;
      if (!wasDirty && isDirty() && !confirmReplace()) { hidePanel(); return; }
      progress(`Opening ${entry.file}…`);
      try { await open(new File([bytes], entry.file, { type: 'application/pdf' })); }
      catch (error) { throw entry.encrypted ? new Error(`${entry.file} is encrypted and this build could not open it: ${error.message}`) : error; }
      hidePanel();
      status(`Opened ${entry.title} (${entry.pages} pages). It stayed on this device.`);
    } catch (error) {
      if (error?.name === 'AbortError' || control.signal.aborted) return;
      active = null; renderCta(); fail(error, () => load(name, { confirmed: true }));
      return;
    } finally { if (active === control) { active = null; renderCta(); } }
  }

  let cta = null, collection = null, hint = null;
  function renderCta() {
    const empty = doc.getElementById('empty');
    if (!empty) return;
    if (!cta?.isConnected) {
      cta = doc.createElement('button'); cta.type = 'button'; cta.id = 'sampleOpen'; cta.className = 'primary sample-cta';
      cta.textContent = 'Open the showcase'; cta.onclick = () => load(SHOWCASE);
      collection = doc.createElement('button'); collection.type = 'button'; collection.id = 'sampleCollection'; collection.className = 'sample-secondary';
      collection.textContent = 'Open the full sample collection'; collection.onclick = () => load(FULL_COLLECTION);
      hint = doc.createElement('span'); hint.className = 'sample-hint';
      empty.append(cta, collection, hint);
    }
    const showcase = entries && findSample(entries, SHOWCASE);
    hint.textContent = showcase ? `Showcase: ${showcase.pages} pages, ${formatSize(showcase.size)}. Downloaded from this site only when you click; nothing is uploaded.`
      : 'Downloaded from this site only when you click; nothing is uploaded.';
    for (const button of [cta, collection]) { button.disabled = !!active; button.setAttribute('aria-busy', String(!!active)); }
  }
  new MutationObserver(renderCta).observe(doc.getElementById('view'), { childList: true });
  renderCta();
  // Warm the small manifest as soon as the File menu is likely to open (hover or keyboard focus), so the submenu is complete on first open.
  for (const type of ['pointerenter', 'focus']) doc.getElementById('fileBtn')?.addEventListener(type, () => ensureManifest().catch(() => {}));

  function menuEntries() {
    ensureManifest().catch(() => {});
    if (!entries) return [{ label: '28 Benchmarks, brand and spec parity', run: () => load(SHOWCASE) }, { label: 'Full sample collection', run: () => load(FULL_COLLECTION) }, '-', { label: 'Other samples load after the list is ready', disabled: true }];
    const item = e => {
      const off = blocked(e);
      return { label: esc(e.name === FULL_COLLECTION ? `${e.title} (${e.pages} pages)` : `${e.name.slice(0, 2)} ${e.title}`) + (off ? ' — requires password support' : ''),
        key: formatSize(e.size), disabled: off, run: () => load(e.name) };
    };
    return entries.flatMap((e, i) => i === 2 ? ['-', item(e)] : [item(e)]);
  }

  async function deepLink() {
    const name = deepLinkName(loc);
    if (!name) return;
    hist.replaceState(hist.state, '', stripDeepLink(loc.href));
    await load(name);
  }
  addEventListener('hashchange', () => { deepLink().catch(() => {}); });

  return { load, menuEntries, deepLink, ensureManifest };
}
