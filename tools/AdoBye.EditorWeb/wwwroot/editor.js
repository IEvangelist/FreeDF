// One editor UI, with either HTTP or an entirely local WebAssembly transport.
async function startEditor() {
  const script = document.createElement('script');
  script.src = new URL('_framework/blazor.webassembly.js', document.baseURI);
  script.setAttribute('autostart', 'false');
  script.onload = async () => {
    try {
      const { start } = await import(new URL('host.js', document.baseURI));
      window.pdfHost = await start();
      window.dispatchEvent(new Event('editor-ready'));
    } catch (e) { showStartupError(e); }
  };
  script.onerror = () => showStartupError(new Error('WebAssembly could not load. Check your connection and reload.'));
  document.head.appendChild(script);
}

function showStartupError(error) {
  console.error(error);
  document.getElementById('status').textContent = 'Editor could not start: ' + error.message;
  document.getElementById('empty').textContent = 'Editor could not start. Reload to try again.';
}

// The server supplies no host.js. A tiny generated marker selects WASM without probing an API.
if (document.querySelector('meta[name="editor-host"]')?.content === 'wasm') {
  document.getElementById('status').textContent = 'Loading WebAssembly editor. PDFs stay on this device.';
  document.querySelector('main').inert = true;
  document.querySelector('header').inert = true;
  startEditor();
} else {
  queueMicrotask(() => window.dispatchEvent(new Event('editor-ready')));
}
