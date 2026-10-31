const assert = require('node:assert/strict');
const { join } = require('node:path');
const { pathToFileURL } = require('node:url');
const { test } = require('node:test');

const wasm = join(__dirname, '..', 'AdoBye.EditorWasm', 'wwwroot');
const load = name => import(pathToFileURL(join(wasm, name)).href);

function fakeIndexedDb() {
  const store = new Map();
  return {
    store,
    open() {
      const db = {
        createObjectStore() {}, close() {},
        transaction() {
          const tx = {};
          const finish = () => queueMicrotask(() => tx.oncomplete());
          tx.objectStore = () => ({
            put(value, key) { store.set(key, value); finish(); return {}; },
            delete(key) { store.delete(key); finish(); return {}; },
            get(key) { finish(); return { result: store.get(key) }; },
          });
          return tx;
        },
      };
      const request = { result: db };
      setTimeout(() => { request.onupgradeneeded?.(); request.onsuccess(); });
      return request;
    },
  };
}

function fileHandle(name, { permission = 'granted', request = 'granted', failWith = null } = {}) {
  const handle = {
    kind: 'file', name, contents: 'original', writes: [], aborted: 0, permission, queries: 0, requests: 0,
    async getFile() { return { name }; },
    async queryPermission() { handle.queries++; return handle.permission; },
    async requestPermission() { handle.requests++; handle.permission = request; return request; },
    async createWritable(options) {
      handle.options = options;
      let pending = null;
      return {
        async write(blob) { if (failWith) throw failWith; pending = await blob.text(); },
        async close() { handle.contents = pending; handle.writes.push(pending); },
        async abort() { handle.aborted++; },
      };
    },
  };
  return handle;
}

async function setup(overrides = {}) {
  const { createFileAccess } = await load('fileaccess.js');
  const log = { saved: [], downloads: [], status: [], errors: [], prepares: 0 };
  const win = { indexedDB: fakeIndexedDb(), ...overrides };
  const access = createFileAccess({
    prepare: async () => { log.prepares++; return { blob: new Blob(['edited pdf']), version: 7 }; },
    saved: async (name, version) => log.saved.push([name, version]),
    download: async (name, prepared) => log.downloads.push([name, prepared.version]),
    status: text => log.status.push(text), error: e => log.errors.push(e.message), suggestedName: () => 'doc.pdf',
  }, win);
  return { access, log, win };
}

test('Save overwrites the opened file atomically and reports the exported version', async () => {
  const handle = fileHandle('original.pdf');
  const { access, log, win } = await setup({ showOpenFilePicker: async () => [handle], showSaveFilePicker: async () => { throw new Error('unused'); } });
  assert.equal(access.supported, true);
  await access.adopt((await access.pick()).handle);
  assert.equal(await access.save(), true);
  assert.deepEqual(handle.options, { keepExistingData: false });
  assert.equal(handle.contents, 'edited pdf');
  assert.deepEqual(log.saved, [['original.pdf', 7]]);
  assert.equal(win.indexedDB.store.get('current').handle, handle);
  assert.deepEqual(Object.keys(win.indexedDB.store.get('current')).sort(), ['handle', 'name']);
});

test('a prompt-state permission is re-requested before writing', async () => {
  const handle = fileHandle('p.pdf', { permission: 'prompt' });
  const { access, log } = await setup({ showOpenFilePicker() {}, showSaveFilePicker() {} });
  await access.adopt(handle);
  await access.save();
  assert.equal(handle.requests, 1);
  assert.equal(handle.contents, 'edited pdf');
  assert.equal(log.errors.length, 0);
});

test('denied permission downloads a copy, never writes, and keeps the handle for the next Save', async () => {
  const handle = fileHandle('denied.pdf', { permission: 'prompt', request: 'denied' });
  const { access, log } = await setup({ showOpenFilePicker() {}, showSaveFilePicker() {} });
  await access.adopt(handle);
  assert.equal(await access.save(), true);
  assert.deepEqual(handle.writes, []);
  assert.deepEqual(log.downloads, [['denied.pdf', 7]]);
  assert.deepEqual(log.saved, []);
  assert.match(log.status.at(-1), /Downloaded a copy — original not updated/);
  assert.equal(access.handle, handle);
});

test('a failed write aborts the stream, leaves the original, reports visibly and is not marked saved', async () => {
  const handle = fileHandle('full.pdf', { failWith: new DOMException('disk full', 'QuotaExceededError') });
  const { access, log } = await setup({ showOpenFilePicker() {}, showSaveFilePicker() {} });
  await access.adopt(handle);
  assert.equal(await access.save(), true);
  assert.equal(handle.contents, 'original');
  assert.equal(handle.aborted, 1);
  assert.deepEqual(log.saved, []);
  assert.match(log.errors[0], /Could not write full\.pdf: disk full.*still open and unsaved/);
});

test('revoked permission during the write is explained', async () => {
  const handle = fileHandle('revoked.pdf', { failWith: new DOMException('revoked', 'NotAllowedError') });
  const { access, log } = await setup({ showOpenFilePicker() {}, showSaveFilePicker() {} });
  await access.adopt(handle);
  await access.save();
  assert.match(log.errors[0], /Permission to write revoked\.pdf was revoked.*Save again/);
});

test('Save without a handle uses the save picker, adopts and persists the chosen file', async () => {
  const chosen = fileHandle('new.pdf');
  let options;
  const { access, log, win } = await setup({ showOpenFilePicker() {}, showSaveFilePicker: async o => { options = o; return chosen; } });
  assert.equal(await access.save(), true);
  assert.equal(options.suggestedName, 'doc.pdf');
  assert.equal(chosen.contents, 'edited pdf');
  assert.equal(access.handle, chosen);
  assert.equal(win.indexedDB.store.get('current').name, 'new.pdf');
  await access.save();
  assert.equal(chosen.writes.length, 2);
  assert.equal(log.saved.length, 2);
});

test('cancelling the picker is a no-op and an unusable picker asks for the download fallback', async () => {
  let error = new DOMException('cancel', 'AbortError');
  const { access, log } = await setup({ showOpenFilePicker() {}, showSaveFilePicker: async () => { throw error; } });
  assert.equal(await access.save(), true);
  assert.equal(log.prepares, 0);
  assert.equal(log.status.at(-1), 'Save cancelled.');
  error = new DOMException('frame', 'SecurityError');
  assert.equal(await access.saveAs(), false);
  assert.deepEqual(log.saved, []);
});

test('browsers without the API report unsupported', async () => {
  const { supportsFileAccess } = await load('fileaccess.js');
  assert.equal(supportsFileAccess({}), false);
  const { access } = await setup({});
  assert.equal(access.supported, false);
  assert.equal(await access.saveAs(), false);
});

test('the stored handle is restored only for the same document and cleared on request', async () => {
  const handle = fileHandle('keep.pdf');
  const { access, win } = await setup({ showOpenFilePicker() {}, showSaveFilePicker() {} });
  await access.adopt(handle);
  await access.clear();
  assert.equal(win.indexedDB.store.has('current'), false);
  await access.adopt(handle);
  assert.equal(await access.restore('other.pdf'), false);
  assert.equal(access.handle, null);
  assert.equal(win.indexedDB.store.has('current'), false);
  await access.adopt(handle);
  const { createFileAccess } = await load('fileaccess.js');
  const reloaded = createFileAccess({ status() {} }, win);
  assert.equal(await reloaded.restore('keep.pdf'), true);
  assert.equal(reloaded.handle, handle);
  assert.equal(await reloaded.restore('other.pdf'), false);
  assert.equal(win.indexedDB.store.has('current'), false);
});

test('drops resolve file handles, expand folders to sorted PDFs and only hand back a single file handle', async () => {
  const { access } = await setup({ showOpenFilePicker() {}, showSaveFilePicker() {} });
  const file = name => ({ kind: 'file', name, getFile: async () => ({ name }) });
  const folder = { kind: 'directory', async *values() { yield file('b.pdf'); yield file('notes.txt'); yield file('a.PDF'); } };
  const item = entry => ({ kind: 'file', getAsFileSystemHandle: async () => entry });
  const single = await access.resolveDrop({ files: [{ name: 'x.pdf' }], items: [item(file('x.pdf'))] });
  assert.equal(single.handle.name, 'x.pdf');
  assert.deepEqual(single.files.map(f => f.name), ['x.pdf']);
  const dir = await access.resolveDrop({ files: [], items: [item(folder)] });
  assert.equal(dir.handle, null);
  assert.deepEqual(dir.files.map(f => f.name), ['a.PDF', 'b.pdf']);
  const plain = await access.resolveDrop({ files: [{ name: 'p.pdf' }], items: [{ kind: 'file' }] });
  assert.deepEqual([plain.files.map(f => f.name), plain.handle], [['p.pdf'], null]);
});

test('local font metadata maps to sorted families with style flags and de-duplicated faces', async () => {
  const { mapLocalFonts, pickVariants, styleFlags } = await load('localfonts.js');
  const raw = [
    { family: 'Zed', fullName: 'Zed Regular', postscriptName: 'Zed-Regular', style: 'Regular' },
    { family: 'Alpha', fullName: 'Alpha Bold Italic', postscriptName: 'Alpha-BoldItalic', style: 'Bold Italic' },
    { family: 'Alpha', fullName: 'Alpha Light', postscriptName: 'Alpha-Light', style: 'Light' },
    { family: 'Alpha', fullName: 'Alpha Regular', postscriptName: 'Alpha-Regular', style: 'Regular' },
    { family: 'Alpha', fullName: 'Alpha Oblique', postscriptName: 'Alpha-Oblique', style: 'Oblique' },
    { family: 'Alpha', fullName: 'dup', postscriptName: 'Alpha-Regular', style: 'Regular' },
    { family: '', postscriptName: 'x' }, { family: 'NoPs' },
  ];
  const families = mapLocalFonts(raw);
  assert.deepEqual(families.map(f => f.family), ['Alpha', 'Zed']);
  assert.equal(families[0].styles.length, 4);
  assert.deepEqual(styleFlags('Black Oblique'), { bold: true, italic: true });
  assert.deepEqual(pickVariants(families[0]).map(s => s.postscriptName).sort(), ['Alpha-BoldItalic', 'Alpha-Oblique', 'Alpha-Regular']);
  assert.deepEqual(mapLocalFonts(undefined), []);
});

test('embedding reads only the needed faces, uploads their bytes locally and surfaces refusals with a substitute', async () => {
  const { createLocalFonts, mapLocalFonts, describeFontError } = await load('localfonts.js');
  const queries = [];
  const faces = { 'Fancy-Regular': new Blob(['regular']), 'Fancy-Bold': new Blob(['bold']) };
  const win = {
    indexedDB: fakeIndexedDb(),
    async queryLocalFonts(options) {
      queries.push(options);
      const all = [{ family: 'Fancy', postscriptName: 'Fancy-Regular', style: 'Regular' }, { family: 'Fancy', postscriptName: 'Fancy-Bold', style: 'Bold' }, { family: 'Other', postscriptName: 'Other-Regular', style: 'Regular' }];
      return all.filter(f => !options?.postscriptNames || options.postscriptNames.includes(f.postscriptName)).map(f => ({ ...f, blob: async () => faces[f.postscriptName] ?? new Blob(['x']) }));
    },
  };
  const uploads = [];
  let restricted = false;
  const controller = createLocalFonts({
    win, upload: async (blob, ps) => {
      uploads.push([await blob.text(), ps]);
      return restricted ? { accepted: false, family: 'Fancy', reason: 'Its license (OS/2 fsType) restricts embedding.', substitute: 'Times' } : { accepted: true, family: 'Fancy', bold: ps.endsWith('Bold'), italic: false };
    },
  });
  assert.equal(controller.supported, true);
  assert.equal(await controller.cached(), null);
  const families = await controller.refresh();
  assert.deepEqual(families.map(f => f.family), ['Fancy', 'Other']);
  assert.deepEqual((await controller.cached()).map(f => f.family), ['Fancy', 'Other']);
  assert.ok(!JSON.stringify(win.indexedDB.store.get('families')).includes('regular'), 'metadata only, no font bytes');
  const ok = await controller.embed(families[0]);
  assert.deepEqual(queries.at(-1).postscriptNames.sort(), ['Fancy-Bold', 'Fancy-Regular']);
  assert.deepEqual(uploads.map(u => u[0]).sort(), ['bold', 'regular']);
  assert.equal(ok.family, 'Fancy');
  restricted = true;
  const refused = await controller.embed(families[0]);
  assert.equal(refused.family, null);
  assert.equal(refused.refused.substitute, 'Times');
  assert.match(describeFontError(new DOMException('no', 'NotAllowedError')), /Permission to read installed fonts was not granted/);
  assert.equal(mapLocalFonts([]).length, 0);
});

test('without the Local Font Access API the controller reports unsupported', async () => {
  const { createLocalFonts, supportsLocalFonts } = await load('localfonts.js');
  assert.equal(supportsLocalFonts({}), false);
  assert.equal(createLocalFonts({ upload() {}, win: {} }).supported, false);
});

test('reload re-registers named families and reports the ones that are gone', async () => {
  const { createLocalFonts } = await load('localfonts.js');
  const win = {
    indexedDB: fakeIndexedDb(),
    async queryLocalFonts(options) {
      const all = [{ family: 'Here', postscriptName: 'Here-Regular', style: 'Regular' }];
      return all.filter(f => !options?.postscriptNames || options.postscriptNames.includes(f.postscriptName)).map(f => ({ ...f, blob: async () => new Blob(['x']) }));
    },
  };
  const controller = createLocalFonts({ win, upload: async () => ({ accepted: true, family: 'Here', bold: false, italic: false }) });
  const result = await controller.reload(['Here', 'Gone']);
  assert.deepEqual(result.restored, ['Here']);
  assert.equal(result.failed[0].family, 'Gone');
});
