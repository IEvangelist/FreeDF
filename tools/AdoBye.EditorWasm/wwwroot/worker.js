import { dotnet } from './_framework/dotnet.js';

try {
  const runtime = await dotnet.create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  self.onmessage = ({ data: { id, command } }) => {
    try { self.postMessage({ id, reply: JSON.parse(exports.BrowserEditor.Request(command)) }); }
    catch (error) { self.postMessage({ id, error: error.message }); }
  };
  self.postMessage({ ready: true });
} catch (error) {
  self.postMessage({ startupError: error.message });
}
