// Read-only render replica: a second .NET runtime that opens the primary editor's saved snapshot and renders pages or horizontal bands.
// Protocol (all additive; the primary PDF engine worker is untouched):
//   in : { id, op: 'load',  key, pdf: ArrayBuffer }                -> { id, pages }
//        { id, op: 'size',  page, dpi }                            -> { id, width, height }
//        { id, op: 'band',  page, dpi, y0, y1 }                    -> { id, rgba: ArrayBuffer }   (transferred)
//        { id, op: 'png',   page, dpi }                            -> { id, png: ArrayBuffer }    (transferred)
//   out: { ready: true } once booted, { startupError } if boot failed, { id, error } per failed request.
try {
  const { dotnet } = await import(new URL('./_framework/dotnet.js', import.meta.url).href);
  const runtime = await dotnet.withConfig({ enableDownloadRetry: true }).create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const replica = exports.BrowserReplica;
  // Fallback fonts / CJK chunks: same files, manifest and lazy fetch protocol as worker.js (cached by the HTTP cache, nothing fetched at startup).
  let manifestLoaded;
  const fetchOptions = { credentials: 'same-origin', redirect: 'error' };
  const loadManifest = () => manifestLoaded ??= (async () => {
    try {
      const response = await fetch(new URL('fallback-fonts/manifest.json', import.meta.url), { ...fetchOptions, signal: AbortSignal.timeout(20000) });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      replica.ProvideFontManifest({ bytes: new Uint8Array(await response.arrayBuffer()) });
      return true;
    } catch { return false; }
  })();
  async function supplyFonts() {
    const paths = JSON.parse(replica.TakeFontRequests());
    if (paths.length === 0) return 0;
    const verified = await loadManifest();
    await Promise.all(paths.map(async path => {
      try {
        if (!verified) throw new Error('font manifest unavailable');
        const response = await fetch(new URL(`fallback-fonts/${path}`, import.meta.url), { ...fetchOptions, signal: AbortSignal.timeout(20000) });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        replica.ProvideFontAsset(path, { bytes: new Uint8Array(await response.arrayBuffer()) });
      } catch { replica.FontAssetUnavailable(path); }
    }));
    return paths.length;
  }
  // Renders are repeatable: run again once requested font assets have arrived.
  async function withFonts(run) {
    let result = run();
    for (let attempt = 0; attempt < 4 && await supplyFonts() > 0; attempt++) result = run();
    return result;
  }
  self.onmessage = async ({ data: { id, op, key, pdf, page, dpi, y0, y1 } }) => {
    try {
      const started = performance.now();
      switch (op) {
        case 'load': {
          const pages = replica.Load(new Uint8Array(pdf));
          self.postMessage({ id, pages, key, ms: performance.now() - started });
          break;
        }
        case 'size': {
          const [width, height] = replica.Size(page, dpi).split(',').map(Number);
          self.postMessage({ id, width, height });
          break;
        }
        case 'band': {
          const rgba = await withFonts(() => replica.Band(page, dpi, y0, y1));
          const copy = rgba.slice();
          self.postMessage({ id, rgba: copy.buffer, ms: performance.now() - started }, [copy.buffer]);
          break;
        }
        case 'png': {
          const copy = (await withFonts(() => replica.Png(page, dpi))).slice();
          self.postMessage({ id, png: copy.buffer, ms: performance.now() - started }, [copy.buffer]);
          break;
        }
        default: throw new Error('Unknown replica operation: ' + op);
      }
    } catch (error) {
      self.postMessage({ id, error: String(error?.message ?? error) });
    }
  };
  self.postMessage({ ready: true });
} catch (error) {
  self.postMessage({ startupError: String(error?.message ?? error) });
}
