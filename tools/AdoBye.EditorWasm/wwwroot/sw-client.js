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
  // Any controlled tab reloads when a new version takes over, so no tab keeps an older build under the new worker.
  const controlled = !!navigator.serviceWorker.controller;
  navigator.serviceWorker.addEventListener('controllerchange', () => { if (controlled) location.reload(); });
  const offer = worker => {
    if (!navigator.serviceWorker.controller || document.getElementById('update-banner')) return;
    const banner = document.createElement('div');
    banner.id = 'update-banner'; banner.setAttribute('role', 'status');
    banner.style.cssText = 'position:fixed;inset:auto 16px 16px auto;z-index:1000;display:flex;gap:12px;align-items:center;padding:10px 14px;border-radius:8px;background:#1f2933;color:#fff;box-shadow:0 4px 16px #0006;font:14px system-ui,sans-serif';
    const text = document.createElement('span');
    text.textContent = 'A new version of the editor is ready.';
    const reload = document.createElement('button');
    reload.type = 'button'; reload.textContent = 'Reload'; reload.className = 'primary';
    reload.onclick = () => { worker.postMessage({ type: 'SKIP_WAITING' }); };
    const later = document.createElement('button');
    later.type = 'button'; later.textContent = 'Later';
    later.onclick = () => banner.remove();
    banner.append(text, reload, later);
    document.body.append(banner);
  };
  const track = worker => worker.addEventListener('statechange', () => { if (worker.state === 'installed') offer(worker); });
  if (registration.waiting) offer(registration.waiting);
  if (registration.installing) track(registration.installing);
  registration.addEventListener('updatefound', () => registration.installing && track(registration.installing));
  registration.update().catch(() => {});
}
