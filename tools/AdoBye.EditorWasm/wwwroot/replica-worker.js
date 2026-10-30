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
  self.onmessage = ({ data: { id, op, key, pdf, page, dpi, y0, y1 } }) => {
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
          const rgba = replica.Band(page, dpi, y0, y1);
          const copy = rgba.slice();
          self.postMessage({ id, rgba: copy.buffer, ms: performance.now() - started }, [copy.buffer]);
          break;
        }
        case 'png': {
          const copy = replica.Png(page, dpi).slice();
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
