const pending = new WeakMap();

function openDatabase(win, name, store, existingOnly = false) {
  return new Promise((resolve, reject) => {
    const request = existingOnly ? win.indexedDB.open(name) : win.indexedDB.open(name, 1);
    let missing = false, blocked = false;
    request.onupgradeneeded = () => {
      if (existingOnly) { missing = true; request.transaction.abort(); }
      else request.result.createObjectStore(store);
    };
    request.onblocked = () => {
      blocked = true;
      reject(new Error('Local storage is blocked. Close other editor tabs and retry.'));
    };
    request.onerror = event => {
      if (missing) { event.preventDefault(); resolve(null); }
      else reject(request.error);
    };
    request.onsuccess = () => {
      const db = request.result;
      if (blocked) { db.close(); return; }
      db.onversionchange = () => db.close();
      resolve(db);
    };
  });
}

function read(db, store, key) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(store), request = tx.objectStore(store).get(key);
    tx.oncomplete = () => resolve(request.result);
    tx.onabort = tx.onerror = () => reject(tx.error ?? request.error ?? new Error('Local storage read failed.'));
  });
}

function copyIfMissing(db, store, key, value) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(store, 'readwrite'), target = tx.objectStore(store), request = target.get(key);
    let failure;
    request.onsuccess = () => {
      if (request.result !== undefined) return;
      try { target.put(value, key); }
      catch (error) { failure = error; tx.abort(); }
    };
    tx.oncomplete = () => resolve();
    tx.onabort = tx.onerror = () => reject(failure ?? tx.error ?? request.error ?? new Error('Local storage migration failed.'));
  });
}

export async function readLegacyStorage(win, { legacy, store, key }) {
  const db = await openDatabase(win, legacy, store, true);
  if (!db) return undefined;
  try { return await read(db, store, key); }
  finally { db.close(); }
}

export function migrateLegacyStorage(win, { legacy, current, store, key }) {
  const marker = `freedf-storage-migrated:${current}`;
  let migrations = pending.get(win);
  if (!migrations) { migrations = new Map(); pending.set(win, migrations); }
  if (migrations.has(marker)) return migrations.get(marker);
  const migration = (async () => {
    if (win.localStorage.getItem(marker) === '1') return;
    const value = await readLegacyStorage(win, { legacy, store, key });
    if (value !== undefined) {
      const db = await openDatabase(win, current, store);
      try { await copyIfMissing(db, store, key, value); }
      finally { db.close(); }
    }
    win.localStorage.setItem(marker, '1');
  })();
  migrations.set(marker, migration);
  return migration.finally(() => { if (migrations.get(marker) === migration) migrations.delete(marker); });
}

export async function clearLegacyStorage(win, { legacy, store, key }) {
  const db = await openDatabase(win, legacy, store, true);
  if (!db) return;
  try {
    await new Promise((resolve, reject) => {
      const tx = db.transaction(store, 'readwrite'), request = tx.objectStore(store).delete(key);
      tx.oncomplete = () => resolve();
      tx.onabort = tx.onerror = () => reject(tx.error ?? request.error ?? new Error('Previous local storage could not be cleared.'));
    });
  } finally { db.close(); }
}
