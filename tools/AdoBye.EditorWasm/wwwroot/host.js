export async function start() {
  const worker = new Worker(new URL('worker.js', import.meta.url), { type: 'module', name: 'PDF engine' });
  let nextId = 0, failed = null, sequence = Promise.resolve();
  const pending = new Map();
  const imageCache = new Map();
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
    failed = error; readyReject(error);
    for (const { reject } of pending.values()) reject(error);
    pending.clear(); worker.terminate();
    window.dispatchEvent(new ErrorEvent('error', { error, message: error.message }));
  };
  worker.onerror = event => fail(new Error(event.message || 'The PDF worker stopped. Reload the editor.'));
  worker.onmessageerror = () => fail(new Error('The PDF worker returned an unreadable response. Reload the editor.'));
  worker.onmessage = ({ data }) => {
    if (data.startupError) { fail(new Error(data.startupError)); return; }
    if (data.ready) { readyResolve(); return; }
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
  const command = (url, method = 'GET', body = null, fileName = null, image = false) => {
    if (failed) return Promise.reject(failed);
    const id = ++nextId;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject, url, started: performance.now() });
      try { worker.postMessage({ id, url, method, body, fileName, image }); }
      catch (error) { pending.delete(id); reject(error); }
    });
  };
  const enqueue = operation => {
    const result = sequence.then(operation);
    sequence = result.catch(() => {});
    return result;
  };
  return {
    execution: 'worker',
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
        const reply = await command(url, options.method, body, fileName);
        return new Response(reply.bytes, {
          headers: { 'Content-Type': reply.contentType, ...(reply.fileName ? { 'X-File-Name': reply.fileName } : {}) },
        });
      });
    },
    image(url) {
      const key = imageKey(url);
      if (key && imageCache.has(key)) {
        const cached = imageCache.get(key);
        imageCache.delete(key); imageCache.set(key, cached);
        return Promise.resolve(cached.data);
      }
      return enqueue(async () => {
        const reply = await command(url, 'GET', null, null, true);
        const started = performance.now();
        const data = `data:${reply.contentType};base64,${reply.data}`;
        if (window.profileRenderStages) window.profileRenderStages.push({ stage: 'data-url', url, ms: performance.now() - started });
        const size = data.length * 2;
        if (key && size <= imageCacheLimit) {
          while (imageCacheSize + size > imageCacheLimit) {
            const [oldKey, old] = imageCache.entries().next().value;
            imageCache.delete(oldKey); imageCacheSize -= old.size;
          }
          imageCache.set(key, { data, size }); imageCacheSize += size;
        }
        return data;
      });
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
