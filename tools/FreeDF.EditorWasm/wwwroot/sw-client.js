// Registers the offline/precache worker after the editor is interactive, so it never competes with startup.
async function mirrorPinsForRollback(cacheStorage) {
  if (!await cacheStorage.has('freedf-pins')) return;
  const current = await cacheStorage.open('freedf-pins'), legacy = await cacheStorage.open('adobye-pins');
  for (const request of await current.keys()) {
    const response = await current.match(request);
    if (response) await legacy.put(request, response);
  }
}

export async function registerServiceWorker() {
  const offlineState = (state, message = '') => {
    window.editorOffline = { state, message };
    window.dispatchEvent(new CustomEvent('editor-offline'));
  };
  if (!('serviceWorker' in navigator) || !window.isSecureContext) {
    offlineState('unavailable', 'This browser needs service workers and HTTPS (or localhost).');
    return;
  }
  offlineState('preparing');
  const url = new URL('sw.js', document.baseURI);
  const scope = new URL('./', url).href;
  // The kill switch belongs to this app, not other service workers on the same origin.
  const script = await fetch(url, { cache: 'no-store' }).then(r => r.ok ? r.text() : '', () => '');
  if (/const MANIFEST = \{\s*"kill"\s*:\s*true\s*\}/.test(script)) {
    for (const registration of await navigator.serviceWorker.getRegistrations()) if (registration.scope === scope) await registration.unregister();
    for (const name of await caches.keys())
      if (name.startsWith('freedf-editor-') || name.startsWith('adobye-editor-') || name === 'freedf-pins' || name === 'adobye-pins') await caches.delete(name);
    offlineState('unavailable', 'The publisher turned off the offline cache.');
    return;
  }
  let registration;
  try { registration = await navigator.serviceWorker.register(url, { scope: new URL('./', url).pathname, updateViaCache: 'none' }); }
  catch (error) { offlineState('unavailable', error.message); console.warn('[PDF boot] offline cache unavailable:', error.message); return; }
  // Work that a reload would discard: unsaved edits, an unsaved recovery copy, an inline edit in progress, an open
  // dialog (it may hold typed input) or a request still running in the engine.
  let inFlight = 0, instrumented = false;
  const instrument = () => {
    const host = window.pdfHost;
    if (instrumented || !host) return;
    instrumented = true;
    for (const name of ['request', 'image', 'imageBlob', 'download']) {
      const original = host[name];
      if (typeof original !== 'function') continue;
      host[name] = function (...args) {
        inFlight++;
        const done = () => { inFlight--; };
        try {
          const result = original.apply(this, args);
          Promise.resolve(result).then(done, done);
          return result;
        } catch (error) { done(); throw error; }
      };
    }
  };
  instrument();
  const dirty = () => {
    instrument();
    return document.getElementById('dstate')?.dataset.dirty === 'true'
      || document.getElementById('recovery')?.textContent.startsWith('Recovery pending') === true
      || document.activeElement?.isContentEditable === true
      || document.querySelector('[contenteditable]:not([contenteditable="false"]), .mdl') !== null
      || (window.editorPwa?.pendingCount ?? 0) > 0
      || inFlight > 0;
  };  navigator.serviceWorker.addEventListener('message', event => {
    if (event.data?.type === 'QUERY_DIRTY') event.ports[0]?.postMessage({ clean: !dirty() });
    else if (event.data?.type === 'KILLED') { offlineState('unavailable', 'The publisher turned off the offline cache.'); applyUpdate('The offline cache was turned off.'); }
    else if (event.data?.type === 'PIN_STORAGE_ERROR') offlineState('unavailable', 'The browser could not retain the offline build for this tab. Save your work before updating.');
  });
  const banner = (message, actions = []) => {
    let element = document.getElementById('update-banner');
    if (!element) {
      element = document.createElement('div');
      element.id = 'update-banner'; element.setAttribute('role', 'status');
      element.style.cssText = 'position:fixed;inset:auto 16px 16px auto;z-index:1000;display:flex;flex-wrap:wrap;max-width:calc(100vw - 32px);gap:12px;align-items:center;padding:10px 14px;border:1px solid var(--line);border-radius:var(--radius);background:var(--raised);color:var(--fg);box-shadow:var(--shadow-lg);font:14px system-ui,sans-serif';
      document.body.append(element);
    }
    const text = document.createElement('span');
    text.textContent = message;
    element.replaceChildren(text, ...actions.map(([label, handler, primary]) => {
      const button = document.createElement('button');
      button.type = 'button'; button.textContent = label; if (primary) button.className = 'primary';
      button.onclick = handler;
      return button;
    }));
    return element;
  };
  // A new version or kill switch took over: reload when clean, otherwise wait (persistent notice) until the work is saved.
  let waitingToReload = false;
  function applyUpdate(reason) {
    if (!dirty()) { location.reload(); return; }
    banner(`${reason} You have unsaved changes: save, then reload to finish updating.`, [['Reload now', () => {
      if (!dirty() || window.confirm('Reload and discard unsaved or in-progress work in this tab?')) location.reload();
    }, true]]);
    if (waitingToReload) return;
    waitingToReload = true;
    const timer = setInterval(() => { if (!dirty()) { clearInterval(timer); location.reload(); } }, 1000);
  }
  const controlled = !!navigator.serviceWorker.controller;
  navigator.serviceWorker.addEventListener('controllerchange', () => { if (controlled) applyUpdate('A new version of the editor is ready.'); });
  const offer = worker => {
    if (!navigator.serviceWorker.controller || document.getElementById('update-banner')) return;
    const later = () => element.remove();
    const ask = async () => {
      if (dirty()) { banner('A new version is ready. Finish or save your work in this tab, then reload.', [['Reload', ask, true], ['Later', later]]); return; }
      try {
        await mirrorPinsForRollback(caches);
      } catch (error) {
        banner(`The update could not preserve open tabs: ${error.message}`, [['Retry', ask, true], ['Later', later]]);
        return;
      }
      const channel = new MessageChannel();
      const timer = setTimeout(() => banner('The update check did not finish. Try Reload again.', [['Reload', ask, true], ['Later', later]]), 10000);
      channel.port1.onmessage = ({ data }) => {
        clearTimeout(timer);
        if (data.type === 'BLOCKED') banner('A new version is ready, but another editor tab has unsaved or in-progress work. Save it there, then reload.', [['Reload', ask, true], ['Later', later]]);
      };
      worker.postMessage({ type: 'REQUEST_ACTIVATE' }, [channel.port2]);
    };
    const element = banner('A new version of the editor is ready.', [['Reload', ask, true], ['Later', later]]);
  };
  const markReady = () => { if (registration.active?.state === 'activated') offlineState('ready'); };
  const track = worker => worker.addEventListener('statechange', () => {
    if (worker.state === 'installed') offer(worker);
    if (worker.state === 'activated') markReady();
    if (worker.state === 'redundant' && !registration.active) offlineState('unavailable', 'The build could not be cached. Check storage space and your connection, then reload.');
  });
  markReady();
  if (registration.waiting) offer(registration.waiting);
  if (registration.installing) track(registration.installing);
  registration.addEventListener('updatefound', () => registration.installing && track(registration.installing));
  registration.update().catch(error => console.warn('Offline app update check failed; the installed build is unchanged.', error));
  // Older builds' caches are kept only while a client is pinned to them; ask the worker to drop any that are now unused.
  navigator.serviceWorker.ready.then(ready => {
    if (ready.scope === scope) { markReady(); ready.active?.postMessage({ type: 'CLEANUP' }); }
  }, error => { offlineState('unavailable', error.message); console.warn('Offline app activation failed.', error); });
}
