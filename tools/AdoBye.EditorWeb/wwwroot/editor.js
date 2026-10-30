// One editor UI, with either HTTP or an entirely local WebAssembly transport.
async function startEditor() {
  try {
    const { start } = await import(new URL('host.js', document.baseURI));
    window.pdfHost = await start();
    window.dispatchEvent(new Event('editor-ready'));
  } catch (e) { showStartupError(e); }
}

function showStartupError(error) {
  console.error(error);
  document.getElementById('status').textContent = 'Editor could not start: ' + error.message;
  const alert = document.createElement('section');
  alert.setAttribute('role', 'alert');
  alert.style.cssText = 'grid-column:1/-1;margin:auto;padding:24px;max-width:min(36rem,100%);overflow-wrap:anywhere';
  const title = document.createElement('h2');
  title.textContent = 'Editor could not start';
  const detail = document.createElement('p');
  detail.textContent = error.message;
  const help = document.createElement('p');
  help.textContent = 'Check your connection and reload to try again. Your previous local recovery copy has not been deleted.';
  const reload = document.createElement('button');
  reload.type = 'button'; reload.className = 'primary'; reload.textContent = 'Reload editor';
  reload.onclick = () => location.reload();
  alert.append(title, detail, help, reload);
  const main = document.querySelector('main');
  main.replaceChildren(alert); main.inert = false;
  reload.focus();
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
