const backoff = [250, 750, 1750];
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

async function loadRuntime() {
  for (let attempt = 0; ; attempt++) {
    try {
      // Failed module imports are cached by URL, so retry only the failed import at a fresh URL.
      const url = new URL('./_framework/dotnet.js', import.meta.url);
      if (attempt) url.searchParams.set('bootRetry', attempt);
      return (await import(url.href)).dotnet;
    } catch (error) {
      if (!(error instanceof TypeError) || attempt === backoff.length) throw error;
      await pause(backoff[attempt]);
    }
  }
}

async function loadResource(name, uri, integrity) {
  let failure;
  for (let attempt = 0; attempt <= backoff.length; attempt++) {
    let response;
    try {
      response = await fetch(uri, {
        method: 'GET', credentials: 'same-origin', redirect: 'error', integrity,
        cache: attempt ? 'reload' : 'force-cache', signal: AbortSignal.timeout(15000),
      });
    } catch (error) {
      if (!(error instanceof TypeError) && error.name !== 'TimeoutError' && error.name !== 'AbortError') throw error;
      failure = error;
    }
    if (response?.ok) return response;
    if (response) {
      failure = new Error(`${name}: HTTP ${response.status}`);
      await response.body?.cancel();
      if (response.status < 500) throw failure;
    }
    if (attempt < backoff.length) await pause(backoff[attempt]);
  }
  throw new Error(`Could not load ${name} after four attempts. ${failure.message}`);
}

try {
  const dotnet = await loadRuntime();
  const runtime = await dotnet
    // Own the retry budget instead of multiplying it by the runtime's two immediate retries.
    .withConfig({ enableDownloadRetry: false })
    .withResourceLoader((type, name, uri, integrity) =>
      type === 'dotnetjs' ? undefined : loadResource(name, uri, integrity))
    .create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  self.onmessage = ({ data: { id, command } }) => {
    const start = performance.now();
    try {
      const requestStart = performance.now();
      const reply = JSON.parse(exports.BrowserEditor.Request(command));
      const dotnetMs = performance.now() - requestStart;
      self.postMessage({ id, reply, timings: { dotnetMs, workerMs: performance.now() - start } });
    } catch (error) {
      self.postMessage({ id, error: error.message, timings: { workerMs: performance.now() - start } });
    }
  };
  self.postMessage({ ready: true });
} catch (error) {
  self.postMessage({ startupError: error.message });
}
