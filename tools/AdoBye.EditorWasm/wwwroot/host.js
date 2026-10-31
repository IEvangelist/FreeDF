export async function start() {
  const worker = new Worker(new URL('worker.js', import.meta.url), { type: 'module', name: 'PDF engine' });
  let nextId = 0, failed = null, bootRetries = 0, sequence = Promise.resolve();
  const pending = new Map();
  const imageCache = new Map();
  const imagePending = new Map();
  const imageCacheLimit = 8 * 1024 * 1024;
  let imageCacheSize = 0;
  const evictImage = url => {
    const key = imageKey(url);
    if (key === null) return;
    const cached = imageCache.get(key);
    if (!cached) return;
    imageCache.delete(key);
    imageCacheSize -= cached.size;
  };
  const imageKey = url => {
    const parsed = new URL(url, location.href);
    if (!/^\/api\/page\/\d+\.png$/.test(parsed.pathname) || !parsed.searchParams.has('v')) return null;
    parsed.searchParams.delete('t');
    return parsed.pathname + '?' + [...parsed.searchParams].sort(([a], [b]) => a.localeCompare(b))
      .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`).join('&');
  };
  let readyResolve, readyReject;
  const ready = new Promise((resolve, reject) => { readyResolve = resolve; readyReject = reject; });
  const fail = error => {
    if (failed) return;
    clearTimeout(startupTimeout);
    failed = error; readyReject(error);
    for (const { reject } of pending.values()) reject(error);
    pending.clear(); worker.terminate();
    window.dispatchEvent(new ErrorEvent('error', { error, message: error.message }));
  };
  const startupTimeout = setTimeout(() => fail(new Error('The PDF engine did not finish loading within 90 seconds. Check your connection and reload the editor.')), 90000);
  worker.onerror = event => fail(new Error(event.message || 'The PDF worker stopped. Reload the editor.'));
  worker.onmessageerror = () => fail(new Error('The PDF worker returned an unreadable response. Reload the editor.'));
  worker.onmessage = ({ data }) => {
    if (data.startupError) { fail(new Error(data.startupError)); return; }
    if (data.ready) { bootRetries = data.bootRetries; clearTimeout(startupTimeout); readyResolve(); return; }
    const request = pending.get(data.id);
    if (!request) return;
    pending.delete(data.id);
    const error = data.error || data.reply?.error;
    if (error) request.reject(new Error(error));
    else {
      if (window.profileWorkerStages) window.profileWorkerStages.push({ url: request.url, dispatchMs: performance.now() - request.started, ...data.timings, engine: data.reply?.performance });
      request.resolve(data.reply);
    }
  };
  await ready;
  const command = (url, method = 'GET', body = null, fileName = null, image = false, password = null) => {
    if (failed) return Promise.reject(failed);
    const id = ++nextId;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject, url, started: performance.now() });
      try { worker.postMessage({ id, url, method, body, fileName, image, password }); }
      catch (error) { pending.delete(id); reject(error); }
    });
  };
  const enqueue = operation => {
    const result = sequence.then(operation);
    sequence = result.catch(() => {});
    return result;
  };
  let renderPool = null;
  if (typeof Worker === 'function' && new URLSearchParams(location.search).get('pool') !== '0') {
    try {
      const { createRenderPool } = await import('./pool.js');
      renderPool = createRenderPool({
        snapshot: bare => enqueue(async () => {
          const reply = await command(`/api/snapshot?bare=${bare}`);
          return { bytes: reply.bytes, version: Number(/^v(\d+)$/.exec(reply.fileName ?? '')?.[1]) };
        }),
      });
      if (!renderPool.enabled) renderPool = null;
    } catch (error) { console.warn('Parallel rendering is unavailable; using one PDF engine.', error); }
  }
  const imageBlob = (url, { current = () => true, pool = true } = {}) => {
    const checkCurrent = () => {
      if (!current()) throw new DOMException('Page image request was superseded.', 'AbortError');
    };
    checkCurrent();
    const key = imageKey(url);
    if (key && imageCache.has(key)) {
      const cached = imageCache.get(key);
      imageCache.delete(key); imageCache.set(key, cached);
      return Promise.resolve(cached.data);
    }
    if (key && imagePending.has(key)) return imagePending.get(key);
    const primary = () => enqueue(async () => {
      checkCurrent();
      const reply = await command(url, 'GET', null, null, true);
      return reply.bytes
        ? new Blob([reply.bytes], { type: reply.contentType })
        : await (await fetch(`data:${reply.contentType};base64,${reply.data}`)).blob();
    });
    // Pool snapshots use the primary queue, so do not hold that queue while awaiting replicas.
    const pooled = (pool ? renderPool?.tryRender(url) : null)?.catch(error => {
      if (error?.name !== 'AbortError') console.warn('Render pool failed; using the primary PDF engine.', error);
      return null;
    });
    const pending = (pooled ? pooled.then(data => data || primary()) : primary()).then(data => {
      checkCurrent();
      const size = data.size;
      evictImage(url);
      if (key && size <= imageCacheLimit) {
        while (imageCacheSize + size > imageCacheLimit) {
          const [oldKey, old] = imageCache.entries().next().value;
          imageCache.delete(oldKey); imageCacheSize -= old.size;
        }
        imageCache.set(key, { data, size }); imageCacheSize += size;
      }
      return data;
    });
    if (key) {
      imagePending.set(key, pending);
      pending.finally(() => { if (imagePending.get(key) === pending) imagePending.delete(key); }).catch(() => {});
    }
    return pending;
  };
  return {
    execution: 'worker',
    bootRetries,
    get renderPool() { return renderPool; },
    request(url, options = {}) {
      return enqueue(async () => {
        let body = null, fileName = null;
        if (options.body instanceof FormData) {
          const file = [...options.body.values()].find(value => value instanceof File);
          if (!file) throw new Error('Choose a file.');
          body = file; fileName = file.name;
        } else if (options.body != null) {
          body = options.body;
        }
        const reply = await command(url, options.method, body, fileName, false, options.headers?.['X-Pdf-Password'] ?? null);
        return new Response(reply.bytes, {
          headers: { 'Content-Type': reply.contentType, ...(reply.fileName ? { 'X-File-Name': reply.fileName } : {}) },
        });
      });
    },
    imageBlob,
    async image(url, options) {
      const blob = await imageBlob(url, options), started = performance.now();
      const data = await new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(blob);
      });
      if (window.profileRenderStages) window.profileRenderStages.push({ stage: 'data-url', url, ms: performance.now() - started });
      return data;
    },
    hasCachedImage(url) { const key = imageKey(url); return key !== null && imageCache.has(key); },
    evictImage,
    async download(url) {
      const response = await this.request(url);
      const blobUrl = URL.createObjectURL(await response.blob());
      const a = document.createElement('a');
      a.href = blobUrl; a.download = response.headers.get('X-File-Name') || 'document.pdf';
      a.click(); setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
    },
  };
}
