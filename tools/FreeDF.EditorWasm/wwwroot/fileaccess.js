// Save to the original file with the File System Access API. Only a FileSystemFileHandle is persisted (IndexedDB database
// "freedf-file-handles"); document bytes are written straight to the chosen file and never stored by this module.
// createWritable({ keepExistingData: false }) buffers to a swap file that replaces the target only when close() resolves,
// so a failed or aborted write leaves the original file untouched.

import { migrateLegacyStorage, readLegacyStorage, clearLegacyStorage } from './storage.js';

const PDF_TYPES = [{ description: 'PDF document', accept: { 'application/pdf': ['.pdf'] } }];
const DB = 'freedf-file-handles', STORE = 'handles', KEY = 'current';
const storage = { legacy: 'adobye-file-handles', current: DB, store: STORE, key: KEY };

export const supportsFileAccess = (win = globalThis) =>
  typeof win.showOpenFilePicker === 'function' && typeof win.showSaveFilePicker === 'function';

const idb = (win, mode, action) => new Promise((resolve, reject) => {
  const open = win.indexedDB.open(DB, 1);
  let blocked = false;
  open.onupgradeneeded = () => open.result.createObjectStore(STORE);
  open.onblocked = () => { blocked = true; reject(new Error('File handle storage is blocked. Close other editor tabs and retry.')); };
  open.onerror = () => reject(open.error);
  open.onsuccess = () => {
    const db = open.result;
    if (blocked) { db.close(); return; }
    db.onversionchange = () => db.close();
    const tx = db.transaction(STORE, mode), request = action(tx.objectStore(STORE));
    tx.oncomplete = () => { db.close(); resolve(request.result); };
    tx.onabort = tx.onerror = () => { db.close(); reject(tx.error ?? new Error('File handle storage failed.')); };
  };
});

export const isAbort = error => error?.name === 'AbortError';

export const describeWriteError = (name, error) => error?.name === 'NotAllowedError'
  ? `Permission to write ${name} was revoked. Choose Save again to re-grant access, or Save as to pick another file.`
  : `Could not write ${name}: ${error?.message || error?.name || 'unknown error'}.`;

/**
 * hooks: { prepare(): Promise<{ blob, version }>, saved(name, version): Promise, download(name, prepared): Promise,
 *          status(text), error(Error), suggestedName(): string }
 */
export function createFileAccess(hooks, win = globalThis) {
  let handle = null, saving = null;
  const supported = supportsFileAccess(win);
  let queue = Promise.resolve();
  // Writes serialize so the stored handle always matches the latest adopt/clear.
  const persist = (forgetLegacy = false) => (queue = queue.then(async () => {
    await migrateLegacyStorage(win, storage);
    await idb(win, 'readwrite', s => handle ? s.put({ handle, name: handle.name }, KEY) : s.delete(KEY));
    if (forgetLegacy) await clearLegacyStorage(win, storage);
    return true;
  }).catch(e => { hooks.status(`Remembering the file for next time failed: ${e.message}`); return false; }));

  async function writable(target) {
    const options = { mode: 'readwrite' };
    if (typeof target.queryPermission !== 'function') return true;
    if (await target.queryPermission(options) === 'granted') return true;
    return typeof target.requestPermission === 'function' && await target.requestPermission(options) === 'granted';
  }

  async function write(target, blob) {
    let stream;
    try {
      stream = await target.createWritable({ keepExistingData: false });
      await stream.write(blob);
      await stream.close();
    } catch (error) {
      try { await stream?.abort(); } catch { /* the original stays untouched either way */ }
      throw error;
    }
  }

  async function commit(target, prepared) {
    try { await write(target, prepared.blob); }
    catch (error) { hooks.error(new Error(describeWriteError(target.name, error) + ' Your changes are still open and unsaved.')); return false; }
    await hooks.saved(target.name, prepared.version);
    hooks.status(`Saved to ${target.name}.`);
    return true;
  }

  async function saveToHandle() {
    // requestPermission needs the user gesture, so it runs before anything is awaited.
    const granted = await writable(handle);
    const prepared = await hooks.prepare();
    if (!granted) {
      await hooks.download(handle.name, prepared);
      hooks.status(`Downloaded a copy — original not updated: permission to write ${handle.name} was not granted. The document is still unsaved; Save asks again.`);
      return true;
    }
    await commit(handle, prepared);
    return true;
  }

  async function saveAs() {
    if (!supported) return false;
    let chosen;
    try { chosen = await win.showSaveFilePicker({ suggestedName: hooks.suggestedName(), types: PDF_TYPES }); }
    catch (error) {
      if (isAbort(error)) { hooks.status('Save cancelled.'); return true; }
      return false;
    }
    const prepared = await hooks.prepare();
    if (!await writable(chosen)) { await hooks.download(chosen.name, prepared); hooks.status(`Downloaded a copy — original not updated: permission to write ${chosen.name} was not granted. The document is still unsaved.`); return true; }
    if (await commit(chosen, prepared)) { handle = chosen; await persist(); }
    return true;
  }

  const serial = operation => (saving ??= operation().finally(() => { saving = null; }));

  return {
    supported,
    get handle() { return handle; },
    /** Resolves true when the save was fully handled; false asks the caller to use its download fallback. */
    save: () => serial(() => handle ? saveToHandle() : saveAs()),
    saveAs: () => serial(saveAs),
    async pick() {
      let handles;
      try { handles = await win.showOpenFilePicker({ types: PDF_TYPES, multiple: false }); }
      catch (error) { if (isAbort(error)) return null; throw error; }
      return { handle: handles[0], file: await handles[0].getFile() };
    },
    async adopt(next) { handle = next?.kind === 'file' ? next : null; await persist(); },
    /** Forgets the handle (the document it belonged to is gone). */
    async clear() {
      handle = null;
      if (!await persist(true)) throw new Error('The previous file handle could not be cleared from local storage.');
    },
    /** Re-attaches the stored handle only when it belongs to the document being resumed. */
    async restore(name) {
      handle = null;
      try {
        await migrateLegacyStorage(win, storage);
        const record = await idb(win, 'readonly', s => s.get(KEY));
        const previous = await readLegacyStorage(win, storage);
        if (record?.handle && previous?.handle && !await record.handle.isSameEntry(previous.handle)) {
          hooks.status('Stored file handles differ. Use Save as to choose the file to update.');
          return false;
        }
        if (record?.handle && record.name === name) { handle = record.handle; return true; }
      } catch (error) {
        hooks.error(new Error(`Could not restore the previous file handle: ${error.message}. The stored handle has not been deleted.`));
      }
      return false;
    },
    /**
     * Must be called synchronously inside the drop event. Resolves the dropped files, expanding folders to
     * their top-level PDFs; a handle is returned only for a single dropped file.
     */
    resolveDrop(transfer) {
      const files = [...transfer.files];
      const items = [...transfer.items].filter(item => item.kind === 'file');
      if (!items.every(item => typeof item.getAsFileSystemHandle === 'function')) return Promise.resolve({ files, handle: null });
      const pending = items.map(item => item.getAsFileSystemHandle());
      return (async () => {
        const out = [];
        let single = null;
        for (const entry of await Promise.all(pending)) {
          if (entry?.kind === 'file') { out.push(await entry.getFile()); single = entry; }
          else if (entry?.kind === 'directory') {
            const pdfs = [];
            for await (const child of entry.values()) if (child.kind === 'file' && /\.pdf$/i.test(child.name)) pdfs.push(await child.getFile());
            out.push(...pdfs.sort((a, b) => a.name.localeCompare(b.name)));
          }
        }
        return { files: out.length ? out : files, handle: items.length === 1 && out.length === 1 ? single : null };
      })();
    },
  };
}
