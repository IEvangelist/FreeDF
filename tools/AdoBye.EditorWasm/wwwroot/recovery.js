function openStorage() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open('adobye-editor', 1);
    let blocked = false;
    request.onupgradeneeded = () => request.result.createObjectStore('recovery');
    request.onblocked = () => {
      blocked = true;
      reject(new Error('Local recovery storage is blocked. Close other editor tabs and reload.'));
    };
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      if (blocked) { request.result.close(); return; }
      request.result.onversionchange = () => request.result.close();
      resolve(request.result);
    };
  });
}

function transaction(db, mode, action) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction('recovery', mode, mode === 'readwrite' ? { durability: 'strict' } : undefined);
    const request = action(tx.objectStore('recovery'));
    tx.oncomplete = () => resolve(request.result);
    tx.onabort = tx.onerror = () => reject(tx.error ?? request.error ?? new Error('Local recovery transaction failed.'));
  });
}

export function validateUi(ui) {
  if (!ui || !Number.isInteger(ui.sel) || ui.sel < 0 || !Number.isFinite(ui.zoom) || ui.zoom <= 0 ||
      typeof ui.preview !== 'boolean' || ![null, 'page', 'width'].includes(ui.fitMode) || !Array.isArray(ui.undo) ||
      ui.undo.some(entry => {
        if (typeof entry !== 'string') return true;
        const snapshot = JSON.parse(entry);
        return !Array.isArray(snapshot.items) || !Array.isArray(snapshot.ed);
      }) || (ui.clip !== null && (typeof ui.clip !== 'object' || typeof ui.clip.type !== 'string')))
    throw new Error('The retained editor controls/history are corrupt. Discard and clear the record, or keep it and download your current document.');
}

export async function createRecovery({ capture, ui, flush, button, onError }) {
  const db = await openStorage();
  let enabled = false, paused = false, clearing = false, revision = 0, timer, sequence = Promise.resolve();
  let payload = null, name = '', coreDirty = false;
  button.hidden = false;
  const show = (text, detail = text) => { button.textContent = text; button.title = detail; };
  const fail = error => {
    show('Retry recovery', `Local recovery failed: ${error.message}`);
    onError(new Error(`Local recovery failed: ${error.message}. Your edit is still in memory; download a copy or retry recovery.`));
  };
  const enqueue = operation => {
    const result = sequence.then(operation);
    sequence = result.catch(fail);
    return result;
  };
  const write = () => enqueue(async () => {
    if (!enabled || paused) return;
    const current = revision;
    await flush();
    if (current !== revision) return;
    if (coreDirty || !payload) {
      const state = await capture.state();
      if (current !== revision) return;
      if (!state.pageCount || !state.recoverySupported) {
        await transaction(db, 'readwrite', store => store.delete('current'));
        payload = null;
        show(state.pageCount ? 'Recovery off (encrypted)' : 'No local recovery',
          state.pageCount ? 'Encrypted input: local recovery is disabled. Decrypted PDF bytes and passwords are never persisted.' : 'No document retained.');
        return;
      }
      const next = await capture.document();
      if (current !== revision) return;
      payload = next; name = state.fileName; coreDirty = false;
    }
    const controls = structuredClone(ui());
    validateUi(controls);
    const updatedAt = Date.now();
    await transaction(db, 'readwrite', store => store.put({ format: 1, payload, name, ui: controls, updatedAt }, 'current'));
    if (current === revision)
      show('Recovery saved', `Editable state saved locally at ${new Date(updatedAt).toLocaleTimeString()}. Click to clear it. Browser storage may be evicted; download important documents.`);
  });
  const schedule = () => {
    if (!enabled || paused) return;
    revision++;
    show('Recovery pending', 'Local changes are not durable yet. Keep this tab open until Recovery saved appears.');
    clearTimeout(timer);
    timer = setTimeout(() => { write().catch(() => {}); }, 1500);
  };
  const clear = () => {
    paused = true; clearing = true; revision++; clearTimeout(timer);
    return enqueue(async () => {
      await transaction(db, 'readwrite', store => store.delete('current'));
      payload = null; clearing = false;
      show('Recovery off', 'The retained edit was cleared. Recovery stays off for this document; opening or creating another document enables it.');
    });
  };
  button.onclick = () => {
    if (button.textContent === 'Retry recovery') {
      if (clearing) clear().catch(() => {});
      else { coreDirty = true; write().catch(() => {}); }
    }
    else if (confirm('Clear the locally retained edit? The open document stays in memory. Recovery will stay off until another document is opened or created.'))
      clear().catch(() => {});
  };
  return {
    load: () => transaction(db, 'readonly', store => store.get('current')),
    start(record) {
      enabled = true; paused = false; payload = record?.payload ?? null; name = record?.name ?? '';
      button.disabled = false;
      show(record ? 'Recovery saved' : 'No local recovery');
      if (record) { coreDirty = true; schedule(); }
    },
    changed(url, method) {
      if (!enabled || method === 'GET') return;
      const path = url.split('?')[0];
      if (['/api/new', '/api/open', '/api/close'].includes(path)) { paused = false; clearing = false; }
      if (['/api/asset', '/api/asset-cancel', '/api/asset-history', '/api/font', '/api/recovery', '/api/inspect'].includes(path)) return;
      coreDirty = true; schedule();
    },
    uiChanged: schedule,
    pending() { if (enabled && !paused) { coreDirty = true; schedule(); } },
    clear,
  };
}
