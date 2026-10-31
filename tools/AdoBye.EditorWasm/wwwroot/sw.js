// Versioned precache for the static editor. The publish script replaces the manifest placeholder with the
// exact files (and SHA-256 digests) of this build; the cache name embeds a digest of that manifest, so one
// worker only ever serves assets from one build. Nothing outside the manifest is ever cached (no PDF bytes).
const MANIFEST = null; /* __PRECACHE_MANIFEST__ */
const PREFIX = 'adobye-editor-';
const scope = self.registration.scope;
// MANIFEST.kill is the documented off switch: the worker removes every cache, unregisters itself and reloads its pages.
const kill = MANIFEST?.kill === true;
const cacheName = MANIFEST && !kill ? PREFIX + MANIFEST.version : null;
const entries = new Map((MANIFEST?.files ?? []).map(file => [new URL(file.url, scope).href, file.hash]));
const indexUrl = new URL('index.html', scope).href;

async function digest(buffer) {
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', buffer));
  let text = '';
  for (const byte of hash) text += String.fromCharCode(byte);
  return btoa(text);
}

async function fetchVerified(url, mode) {
  const response = await fetch(url, { cache: mode, credentials: 'same-origin', redirect: 'error' });
  if (!response.ok) throw new Error(`${url}: HTTP ${response.status}`);
  // Keep the network Response itself (not rebuilt bytes) so the browser's per-resource caches stay usable.
  const copy = response.clone();
  return { copy, ok: await digest(await response.arrayBuffer()) === entries.get(url) };
}

async function precache() {
  if (!cacheName) return;
  const cache = await caches.open(cacheName);
  const queue = [...entries.keys()];
  const worker = async () => {
    for (let url; (url = queue.shift()) !== undefined;) {
      if (await cache.match(url)) continue;
      // The page that registered us has usually just downloaded these, so try the HTTP cache first.
      let result = await fetchVerified(url, 'force-cache');
      if (!result.ok) result = await fetchVerified(url, 'reload');
      if (!result.ok) throw new Error(`${url} does not match this build's manifest.`);
      await cache.put(url, result.copy);
    }
  };
  await Promise.all(Array.from({ length: 6 }, worker));
}

self.addEventListener('install', event => kill ? event.waitUntil(self.skipWaiting()) : event.waitUntil(precache().catch(async error => {
  if (cacheName) await caches.delete(cacheName);
  throw error;
})));

// Old caches may be removed immediately: every controlled page reloads on controllerchange (sw-client.js),
// so no page keeps running an older build under this worker.
self.addEventListener('activate', event => event.waitUntil((async () => {
  for (const name of await caches.keys())
    if (name.startsWith(PREFIX) && name !== cacheName) await caches.delete(name);
  if (!kill) return;
  await self.registration.unregister();
  for (const client of await self.clients.matchAll({ type: 'window' })) client.navigate(client.url).catch(() => {});
})()));

self.addEventListener('message', event => {
  if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
  else if (event.data?.type === 'GET_VERSION') event.source?.postMessage({ type: 'VERSION', version: MANIFEST?.version ?? null, cache: cacheName });
});

async function serve(request, key) {
  const cache = await caches.open(cacheName);
  const cached = await cache.match(key);
  const bypass = request.cache === 'reload' || request.cache === 'no-store' || new URL(request.url).searchParams.has('bootRetry');
  if (cached && !bypass) return cached;
  // Missing or explicitly retried: only accept bytes that match this build, never a newer deployment.
  try {
    const result = await fetchVerified(key, bypass ? 'reload' : 'no-cache');
    if (result.ok) {
      // A full or evicted cache must not fail a response whose bytes already matched the build digest.
      await cache.put(key, result.copy.clone()).catch(() => {});
      return result.copy;
    }
  } catch (error) {
    if (cached) return cached;
    throw error;
  }
  self.registration.update().catch(() => {});
  return cached ?? new Response('This editor build was replaced. Reload to get the new version.', { status: 503 });
}

self.addEventListener('fetch', event => {
  const { request } = event;
  if (!cacheName || request.method !== 'GET' || request.headers.has('range')) return;
  const url = new URL(request.url);
  // Cross-origin requests and partial-content (Range) requests are never answered from, or stored in, the cache.
  if (url.origin !== location.origin) return;
  const bare = url.origin + url.pathname;
  if (request.mode === 'navigate') {
    if (bare === scope || bare === indexUrl) event.respondWith(serve(request, indexUrl));
    return;
  }
  if (entries.has(bare)) event.respondWith(serve(request, bare));
});
