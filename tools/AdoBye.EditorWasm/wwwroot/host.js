function base64(bytes) {
  let text = '';
  for (let i = 0; i < bytes.length; i += 32768) text += String.fromCharCode(...bytes.subarray(i, i + 32768));
  return btoa(text);
}

export async function start() {
  const worker = new Worker(new URL('worker.js', import.meta.url), { type: 'module', name: 'PDF engine' });
  let nextId = 0, failed = null, sequence = Promise.resolve();
  const pending = new Map();
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
    else request.resolve(data.reply);
  };
  await ready;
  const command = (url, method = 'GET', body = null, fileName = null) => {
    if (failed) return Promise.reject(failed);
    const u = new URL(url, location.href), id = ++nextId;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject });
      try { worker.postMessage({ id, command: JSON.stringify({ method, path: u.pathname, query: Object.fromEntries(u.searchParams), body, fileName }) }); }
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
          body = base64(new Uint8Array(await file.arrayBuffer())); fileName = file.name;
        } else if (options.body != null) {
          body = base64(new TextEncoder().encode(options.body));
        }
        const reply = await command(url, options.method, body, fileName);
        return new Response(Uint8Array.from(atob(reply.data), c => c.charCodeAt(0)), {
          headers: { 'Content-Type': reply.contentType, ...(reply.fileName ? { 'X-File-Name': reply.fileName } : {}) },
        });
      });
    },
    image(url) {
      return enqueue(async () => {
        const reply = await command(url);
        return `data:${reply.contentType};base64,${reply.data}`;
      });
    },
    async download(url) {
      const response = await this.request(url);
      const blobUrl = URL.createObjectURL(await response.blob());
      const a = document.createElement('a');
      a.href = blobUrl; a.download = response.headers.get('X-File-Name') || 'document.pdf';
      a.click(); setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
    },
  };
}
