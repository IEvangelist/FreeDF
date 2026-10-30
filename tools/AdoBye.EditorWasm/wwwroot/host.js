function base64(bytes) {
  let text = '';
  for (let i = 0; i < bytes.length; i += 32768) text += String.fromCharCode(...bytes.subarray(i, i + 32768));
  return btoa(text);
}

function command(url, method = 'GET', body = null, fileName = null) {
  const u = new URL(url, location.href);
  const reply = JSON.parse(DotNet.invokeMethod('AdoBye.EditorWasm', 'Request',
    JSON.stringify({ method, path: u.pathname, query: Object.fromEntries(u.searchParams), body, fileName })));
  if (reply.error) throw new Error(reply.error);
  return reply;
}

export async function start() {
  const ready = new Promise(resolve => { window.editorRuntimeReady = resolve; });
  await Blazor.start();
  await ready;
  delete window.editorRuntimeReady;
  return {
    async request(url, options = {}) {
      let body = null, fileName = null;
      if (options.body instanceof FormData) {
        const file = [...options.body.values()].find(value => value instanceof File);
        if (!file) throw new Error('Choose a file.');
        body = base64(new Uint8Array(await file.arrayBuffer())); fileName = file.name;
      } else if (options.body != null) {
        body = base64(new TextEncoder().encode(options.body));
      }
      const reply = command(url, options.method, body, fileName);
      return new Response(Uint8Array.from(atob(reply.data), c => c.charCodeAt(0)), {
        headers: { 'Content-Type': reply.contentType, ...(reply.fileName ? { 'X-File-Name': reply.fileName } : {}) },
      });
    },
    image(url) {
      const reply = command(url);
      return `data:${reply.contentType};base64,${reply.data}`;
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
