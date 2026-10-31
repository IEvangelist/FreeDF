const backoff = [250, 750, 1750];
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
let bootRetries = 0;
function retry(name, reason) {
  bootRetries++;
  console.warn(`[PDF boot] retry ${bootRetries}: ${name} (${reason})`);
}

async function loadRuntime() {
  for (let attempt = 0; ; attempt++) {
    try {
      // Failed module imports are cached by URL, so retry only the failed import at a fresh URL.
      const url = new URL('./_framework/dotnet.js', import.meta.url);
      if (attempt) url.searchParams.set('bootRetry', attempt);
      return (await import(url.href)).dotnet;
    } catch (error) {
      if (!(error instanceof TypeError) || attempt === backoff.length) throw error;
      retry('dotnet.js', error.name);
      await pause(backoff[attempt]);
    }
  }
}

// A fetch with integrity only resolves once its whole body has arrived, so this is an idle limit on the whole boot:
// it restarts whenever any asset completes. Large assets on slow links legitimately take far longer than 15 s.
const waiting = new Map();
const arm = controller => {
  clearTimeout(waiting.get(controller));
  waiting.set(controller, setTimeout(() => controller.abort(new DOMException('No asset finished downloading for 45 seconds.', 'TimeoutError')), 45000));
};
const progressed = () => { for (const controller of waiting.keys()) arm(controller); };

async function loadResource(name, uri, integrity) {
  let failure;
  for (let attempt = 0; attempt <= backoff.length; attempt++) {
    let response;
    const controller = new AbortController();
    try {
      arm(controller);
      response = await fetch(uri, {
        method: 'GET', credentials: 'same-origin', redirect: 'error', integrity,
        cache: attempt ? 'reload' : 'force-cache', signal: controller.signal,
      });
      progressed();
    } catch (error) {
      if (!(error instanceof TypeError) && error.name !== 'TimeoutError' && error.name !== 'AbortError') throw error;
      failure = error;
    } finally { clearTimeout(waiting.get(controller)); waiting.delete(controller); }
    if (response?.ok) return response;
    if (response) {
      failure = new Error(`${name}: HTTP ${response.status}`);
      await response.body?.cancel();
      if (response.status < 500) throw failure;
    }
    if (attempt < backoff.length) {
      retry(name, response ? `HTTP ${response.status}` : failure.name);
      await pause(backoff[attempt]);
    }
  }
  throw new Error(`Could not load ${name} after four attempts. ${failure.message}`);
}

function base64(bytes) {
  let text = '';
  for (let i = 0; i < bytes.length; i += 32768) text += String.fromCharCode(...bytes.subarray(i, i + 32768));
  return btoa(text);
}

try {
  const t0 = performance.now();
  const dotnet = await loadRuntime();
  const t1 = performance.now();
  const runtime = await dotnet
    // Own the retry budget instead of multiplying it by the runtime's two immediate retries. A fixed application
    // culture always selects the one ICU data file we publish (browser language must not change what is downloaded).
    .withConfig({ enableDownloadRetry: false, applicationCulture: 'en-US' })
    .withResourceLoader((type, name, uri, integrity) =>
      type === 'dotnetjs' ? undefined : loadResource(name, uri, integrity))
    .create();
  const t2 = performance.now();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  self.onmessage = async ({ data: { id, url, method, body, fileName, image } }) => {
    const start = performance.now();
    try {
      const requestStart = performance.now();
      const u = new URL(url, self.location.href);
      let uploadEncodeMs = 0, uploadPrepareMs = 0, uploadBytes = 0, binaryUpload = false, reply, dotnetMs;
      if (body instanceof Blob) {
        fileName = body.name ?? fileName;
        const bytes = new Uint8Array(await body.arrayBuffer());
        uploadBytes = bytes.byteLength;
        const query = JSON.stringify(Object.fromEntries(u.searchParams));
        uploadPrepareMs = performance.now() - requestStart;
        binaryUpload = true;
        const dotnetStart = performance.now();
        reply = JSON.parse(exports.BrowserEditor.RequestBinary(method, u.pathname, query, { bytes }, fileName));
        dotnetMs = performance.now() - dotnetStart;
      } else {
        if (body != null) body = base64(new TextEncoder().encode(body));
        const command = JSON.stringify({ method, path: u.pathname, query: Object.fromEntries(u.searchParams), body, fileName });
        uploadEncodeMs = performance.now() - requestStart;
        const dotnetStart = performance.now();
        reply = JSON.parse(exports.BrowserEditor.Request(command));
        dotnetMs = performance.now() - dotnetStart;
      }
      const timings = { dotnetMs, uploadEncodeMs, uploadPrepareMs, uploadBytes, binaryUpload, workerMs: performance.now() - start };
      if (reply.error || image) {
        self.postMessage({ id, reply, timings });
      } else {
        const decodeStart = performance.now();
        const binary = atob(reply.data), bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        delete reply.data;
        reply.bytes = bytes;
        timings.responseDecodeMs = performance.now() - decodeStart;
        self.postMessage({ id, reply, timings }, [bytes.buffer]);
      }
    } catch (error) {
      self.postMessage({ id, error: error.message, timings: { workerMs: performance.now() - start } });
    }
  };
  self.postMessage({ ready: true, bootRetries, boot: { origin: performance.timeOrigin, startMs: t0, runtimeImportMs: t1 - t0, createMs: t2 - t1, exportsMs: performance.now() - t2 } });
} catch (error) {
  self.postMessage({ startupError: error.message });
}
