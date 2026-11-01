// Installation and OS file launches use the same local open/save paths as the browser editor.
export function createPwa(hooks, win = window, doc = document) {
  const button = doc.getElementById('appBtn');
  const standalone = win.matchMedia('(display-mode: standalone)');
  let offer = null, installed = standalone.matches || win.navigator.standalone === true;
  let panel = null, opening = false, pending = [];
  const esc = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const offline = () => {
    const cache = win.editorOffline;
    const connection = win.navigator.onLine ? 'Online.' : 'Offline.';
    if (cache?.state === 'ready') return `${connection} App ready for offline use. Samples and fallback fonts need one online use before they are cached.`;
    if (cache?.state === 'unavailable') return `${connection} Offline app unavailable: ${cache.message}`;
    return `${connection} Preparing the offline app. Keep this tab open until it is ready.`;
  };
  function sync() {
    button.hidden = false;
    button.title = pending.length ? `Open received PDFs (${pending.reduce((n, batch) => n + batch.length, 0)} waiting)` : offer ? 'Install app and offline status' : 'App and offline status';
    button.setAttribute('aria-label', button.title);
    const state = panel?.querySelector('[data-app-state]');
    if (state) state.textContent = offline();
    const install = panel?.querySelector('.dok');
    if (install) install.textContent = offer && !installed ? 'Install app' : 'Close';
    const received = panel?.querySelector('[data-app-open]');
    if (received) received.hidden = !pending.length;
  }
  function showError(error) {
    const text = panel?.querySelector('[data-app-error]');
    if (text) text.textContent = error.message;
    hooks.onError(error);
  }
  async function openPending() {
    if (opening || !pending.length) return;
    if (hooks.busy()) { hooks.status('Finish the current action, then choose App > Open received PDFs.'); return; }
    if (!hooks.confirmReplace()) return;
    opening = true;
    const batch = pending[0];
    try {
      const files = await Promise.all(batch.map(async handle => {
        if (handle.kind !== 'file') throw new Error('Only PDF files can be opened by this app.');
        const file = await handle.getFile();
        if (!/\.pdf$/i.test(file.name)) throw new Error(`${file.name}: choose a PDF file.`);
        return file;
      }));
      if (!await hooks.open(files[0], batch[0])) {
        hooks.status('The PDF was not opened. Received files are still waiting in App.');
        return;
      }
      pending.shift();
      if (files.length > 1) hooks.merge(files.slice(1));
    } catch (error) { showError(error); }
    finally { opening = false; sync(); }
  }
  function show() {
    if (panel?.isConnected) return;
    panel = hooks.dialog('App and offline use',
      `<p data-app-state role="status">${esc(offline())}</p>
       <p>${installed ? 'FreeDF is running as an installed app.' : 'Install FreeDF for its own window and offline editing. If no Install button is offered, use your browser menu: Install app or Add to Home Screen. In Safari on iPhone or iPad, use Share > Add to Home Screen.'}</p>
       <p>PDFs are processed on this device. Save or download a copy before closing. Local recovery is separate from the app cache; browser storage can still be cleared or evicted.</p>
       <button type="button" data-app-update>Check for updates</button>
       <button type="button" data-app-open ${pending.length ? '' : 'hidden'}>Open received PDFs</button>
       <p data-app-error role="alert"></p>`,
      offer && !installed ? 'Install app' : 'Close', async () => {
        if (!offer || installed) return;
        const prompt = offer;
        offer = null;
        try {
          await prompt.prompt();
          const choice = await prompt.userChoice;
          if (choice.outcome === 'accepted') hooks.status('Installation accepted. Your browser will finish installing the app.');
        } catch (error) { showError(error); return false; }
        finally { sync(); }
      });
    panel.querySelector('[data-app-open]').onclick = () => {
      panel.remove(); panel = null;
      // Let the existing modal observer remove background inertness before opening a PDF.
      win.setTimeout(openPending, 0);
    };
    panel.querySelector('[data-app-update]').onclick = async event => {
      const control = event.currentTarget;
      const result = panel.querySelector('[data-app-error]');
      control.disabled = true;
      try {
        const scope = new URL('./', doc.baseURI).href;
        const registration = await win.navigator.serviceWorker?.getRegistration(scope);
        if (!registration || registration.scope !== scope) throw new Error('The offline app is not registered yet.');
        if (!win.navigator.onLine) throw new Error('Connect to the network to check for updates. Offline editing still works.');
        await registration.update();
        result.textContent = registration.waiting ? 'An update is waiting. Close this dialog to use the update banner.' : 'Update check complete. A new build, if available, will appear in the update banner after it is downloaded.';
      } catch (error) { showError(error); }
      finally { control.disabled = false; }
    };
    sync();
  }
  button.onclick = show;
  win.addEventListener('beforeinstallprompt', event => { event.preventDefault(); offer = event; sync(); });
  win.addEventListener('appinstalled', () => { installed = true; offer = null; sync(); hooks.status('FreeDF installed. Offline readiness is shown in App.'); });
  standalone.addEventListener('change', event => { installed = event.matches || win.navigator.standalone === true; sync(); });
  for (const event of ['online', 'offline', 'editor-offline']) win.addEventListener(event, sync);
  if (win.launchQueue?.setConsumer) win.launchQueue.setConsumer(async launch => {
    if (!launch.files?.length) return;
    pending.push([...launch.files]); sync();
    await hooks.ready;
    // Never replace an existing document or interrupt recovery, a dialog, or an edit on an OS launch.
    if (!hooks.hasDocument() && !hooks.busy()) await openPending();
    else hooks.status('PDFs received from your device. Choose App > Open received PDFs when ready.');
  });
  sync();
  return { show, get installed() { return installed; }, get pendingCount() { return pending.reduce((n, batch) => n + batch.length, 0); } };
}
