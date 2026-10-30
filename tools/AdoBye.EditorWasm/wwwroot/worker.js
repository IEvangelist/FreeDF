import { dotnet } from './_framework/dotnet.js';

function base64(bytes) {
  let text = '';
  for (let i = 0; i < bytes.length; i += 32768) text += String.fromCharCode(...bytes.subarray(i, i + 32768));
  return btoa(text);
}

try {
  const runtime = await dotnet.create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  self.onmessage = async ({ data: { id, url, method, body, fileName, image } }) => {
    const start = performance.now();
    try {
      const requestStart = performance.now();
      const u = new URL(url, self.location.href);
      if (body instanceof Blob) { fileName = body.name ?? fileName; body = base64(new Uint8Array(await body.arrayBuffer())); }
      else if (body != null) body = base64(new TextEncoder().encode(body));
      const command = JSON.stringify({ method, path: u.pathname, query: Object.fromEntries(u.searchParams), body, fileName });
      const uploadEncodeMs = performance.now() - requestStart;
      const dotnetStart = performance.now();
      const reply = JSON.parse(exports.BrowserEditor.Request(command));
      const dotnetMs = performance.now() - dotnetStart;
      if (reply.error || image) {
        self.postMessage({ id, reply, timings: { dotnetMs, uploadEncodeMs, workerMs: performance.now() - start } });
      } else {
        const decodeStart = performance.now();
        const binary = atob(reply.data), bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        delete reply.data;
        reply.bytes = bytes;
        self.postMessage({ id, reply, timings: { dotnetMs, uploadEncodeMs, responseDecodeMs: performance.now() - decodeStart, workerMs: performance.now() - start } }, [bytes.buffer]);
      }
    } catch (error) {
      self.postMessage({ id, error: error.message, timings: { workerMs: performance.now() - start } });
    }
  };
  self.postMessage({ ready: true });
} catch (error) {
  self.postMessage({ startupError: error.message });
}
