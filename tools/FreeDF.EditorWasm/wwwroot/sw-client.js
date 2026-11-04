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
    element.removeAttribute('aria-busy');
    element.replaceChildren(text, ...actions.map(([label, handler, primary]) => {
      const button = document.createElement('button');
      button.type = 'button'; button.textContent = label; if (primary) button.className = 'primary';
      button.onclick = handler;
      return button;
    }));
    return element;
  };
  const progress = message => {
    const element = banner(message);
    element.setAttribute('aria-busy', 'true');
    const indicator = document.createElement('progress');
    indicator.setAttribute('aria-label', 'Updating the editor');
    indicator.style.cssText = 'width:100%;height:4px';
    element.append(indicator);
  };
  const painted = () => new Promise(resolve => {
    // Hidden tabs can suspend animation frames, but must still finish an accepted update.
    const timer = setTimeout(resolve, 100);
    requestAnimationFrame(() => { clearTimeout(timer); setTimeout(resolve, 0); });
  });
  let reloading = false;
  async function reload(discard = false) {
    if (reloading) return;
    reloading = true;
    progress('Reloading... Opening the updated editor.');
    await painted();
    if (!discard && dirty()) { reloading = false; applyUpdate('A new version of the editor is ready.'); return; }
    location.reload();
  }
  // A new version or kill switch took over: reload when clean, otherwise wait (persistent notice) until the work is saved.
  let waitingToReload = false;
  function applyUpdate(reason) {
    if (!dirty()) { reload(); return; }
    banner(`${reason} You have unsaved changes: save, then reload to finish updating.`, [['Reload now', () => {
      if (!dirty()) reload();
      else if (window.confirm('Reload and discard unsaved or in-progress work in this tab?')) reload(true);
    }, true]]);
    if (waitingToReload) return;
    waitingToReload = true;
    const timer = setInterval(() => { if (!dirty()) { clearInterval(timer); reload(); } }, 1000);
  }
  const controlled = !!navigator.serviceWorker.controller;
  let finishUpdate = () => {};
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (controlled) { finishUpdate(); applyUpdate('A new version of the editor is ready.'); }
  });
  const offer = worker => {
    if (!navigator.serviceWorker.controller || document.getElementById('update-banner')) return;
    let attempt = 0, pending = false;
    const later = () => element.remove();
    const ask = async () => {
      if (pending || reloading) return;
      if (dirty()) { banner('A new version is ready. Finish or save your work in this tab, then reload.', [['Reload', ask, true], ['Later', later]]); return; }
      if (worker.state === 'activated') { applyUpdate('A new version of the editor is ready.'); return; }
      pending = true;
      const current = ++attempt;
      let channel;
      const finish = () => {
        if (current !== attempt) return;
        attempt++; pending = false; clearTimeout(timer);
        channel?.port1.close(); channel?.port2.close();
      };
      const failed = message => {
        if (current !== attempt || reloading) return;
        finish();
        banner(message, [['Reload', ask, true], ['Later', later]]);
      };
      // Include cache preservation in the deadline, not just the worker's reply.
      const timer = setTimeout(() => failed('The update did not finish. Try Reload again.'), 15000);
      finishUpdate = finish;
      progress('Updating... Preparing the new version.');
      try {
        await painted();
        await mirrorPinsForRollback(caches);
        if (current !== attempt || reloading) return;
        if (dirty()) { failed('A new version is ready. Finish or save your work in this tab, then reload.'); return; }
        progress('Updating... Checking open tabs and activating the new version.');
        channel = new MessageChannel();
        channel.port1.onmessage = ({ data }) => {
          if (current !== attempt || reloading) return;
          if (data.type === 'BLOCKED') failed('A new version is ready, but another editor tab has unsaved or in-progress work. Save it there, then reload.');
          else if (data.type === 'ACTIVATING') progress('Updating... Loading the new version.');
        };
        worker.postMessage({ type: 'REQUEST_ACTIVATE' }, [channel.port2]);
      } catch (error) {
        failed(`The update could not finish: ${error.message}`);
      }
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
