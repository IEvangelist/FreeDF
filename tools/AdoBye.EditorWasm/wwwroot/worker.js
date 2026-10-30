import { dotnet } from './_framework/dotnet.js';

try {
  const runtime = await dotnet.create();
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
