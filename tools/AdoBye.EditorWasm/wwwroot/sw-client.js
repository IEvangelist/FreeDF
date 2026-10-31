// Registers the offline/precache worker after the editor is interactive, so it never competes with startup.
export async function registerServiceWorker() {
  if (!('serviceWorker' in navigator) || !window.isSecureContext) return;
  const url = new URL('sw.js', document.baseURI);
  // Off switch: a build published with MANIFEST.kill removes every cache and registration and is never re-registered.
  const script = await fetch(url, { cache: 'no-store' }).then(r => r.ok ? r.text() : '', () => '');
  if (/const MANIFEST = \{\s*"kill"\s*:\s*true\s*\}/.test(script)) {
    for (const registration of await navigator.serviceWorker.getRegistrations()) await registration.unregister();
    for (const name of await caches.keys()) if (name.startsWith('adobye-editor-')) await caches.delete(name);
    return;
  }
  let registration;
  try { registration = await navigator.serviceWorker.register(url, { scope: new URL('./', url).pathname, updateViaCache: 'none' }); }
  catch (error) { console.warn('[PDF boot] offline cache unavailable:', error.message); return; }
  // Work that a reload would discard: unsaved edits, an unsaved recovery copy, an inline edit in progress, an open
  // dialog (it may hold typed input) or a request still running in the engine.
  let inFlight = 0, instrumented = false;
  const instrument = () => {
    const host = window.pdfHost;
    if (instrumented || !host) return;
    instrumented = true;
    for (const name of ['request', 'image', 'download']) {
      const original = host[name];
      if (typeof original !== 'function') continue;
      host[name] = function (...args) {
        inFlight++;
        const result = original.apply(this, args);
        const done = () => { inFlight--; };
        Promise.resolve(result).then(done, done);
        return result;
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
      || inFlight > 0;
  };  navigator.serviceWorker.addEventListener('message', event => {
    if (event.data?.type === 'QUERY_DIRTY') event.ports[0]?.postMessage({ clean: !dirty() });
    else if (event.data?.type === 'KILLED') applyUpdate('The offline cache was turned off.');
  });
  const banner = (message, actions = []) => {
    let element = document.getElementById('update-banner');
    if (!element) {
      element = document.createElement('div');
      element.id = 'update-banner'; element.setAttribute('role', 'status');
      element.style.cssText = 'position:fixed;inset:auto 16px 16px auto;z-index:1000;display:flex;gap:12px;align-items:center;padding:10px 14px;border-radius:8px;background:#1f2933;color:#fff;box-shadow:0 4px 16px #0006;font:14px system-ui,sans-serif';
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
    banner(`${reason} You have unsaved changes: save, then reload to finish updating.`, [['Reload now', () => location.reload(), true]]);
    if (waitingToReload) return;
    waitingToReload = true;
    const timer = setInterval(() => { if (!dirty()) { clearInterval(timer); location.reload(); } }, 1000);
  }
  const controlled = !!navigator.serviceWorker.controller;
  navigator.serviceWorker.addEventListener('controllerchange', () => { if (controlled) applyUpdate('A new version of the editor is ready.'); });
  const offer = worker => {
    if (!navigator.serviceWorker.controller || document.getElementById('update-banner')) return;
    const later = () => element.remove();
    const ask = () => {
      if (dirty()) { banner('A new version is ready. Finish or save your work in this tab, then reload.', [['Reload', ask, true], ['Later', later]]); return; }
      const channel = new MessageChannel();
      const timer = setTimeout(() => banner('The update check did not finish. Try Reload again.', [['Reload', ask, true], ['Later', later]]), 10000);
      channel.port1.onmessage = ({ data }) => {
        clearTimeout(timer);
        if (data.type === 'BLOCKED') banner('A new version is ready, but another editor tab has unsaved or in-progress work. Save it there, then reload.', [['Reload', ask, true], ['Later', later]]);
      };
      worker.postMessage({ type: 'REQUEST_ACTIVATE' }, [channel.port2]);
    };
    const element = banner('A new version of the editor is ready.', [['Reload', ask, true], ['Later', later]]);
  };  const track = worker => worker.addEventListener('statechange', () => { if (worker.state === 'installed') offer(worker); });
  if (registration.waiting) offer(registration.waiting);
  if (registration.installing) track(registration.installing);
  registration.addEventListener('updatefound', () => registration.installing && track(registration.installing));
  registration.update().catch(() => {});
  // Older builds' caches are kept only while a client is pinned to them; ask the worker to drop any that are now unused.
  navigator.serviceWorker.ready.then(ready => ready.active?.postMessage({ type: 'CLEANUP' }), () => {});
}
