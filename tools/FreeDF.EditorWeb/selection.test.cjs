const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const source = html.slice(html.indexOf('let selectionEpoch ='), html.indexOf('let neighborPrefetchEpoch ='));
function harness(request) {
  const context = vm.createContext({ request });
  vm.runInContext(`let state = {version:1}; ${source}
    globalThis.page = pageSelection; globalThis.fields = pageFields;
    globalThis.invalidate = () => { state.version++; selectionCache.clear(); fieldReply = null; };`, context);
  return context;
}

test('selection promises are shared by page and version, then invalidated on mutation', async () => {
  const requests = [];
  const h = harness(async url => { requests.push(url); return { json: async () => ({ url }) }; });
  assert.equal(h.page(0), h.page(0));
  await Promise.all([h.page(0), h.page(1)]);
  assert.equal(requests.length, 2);
  h.invalidate();
  await h.page(0);
  assert.equal(requests.length, 3);
});

test('failed metadata requests are visible and retryable, including fields', async () => {
  let fail = true, count = 0;
  const h = harness(async () => {
    count++;
    if (fail) throw new Error('metadata failed');
    return { json: async () => [] };
  });
  await assert.rejects(h.page(0), /metadata failed/);
  await assert.rejects(h.fields(), /metadata failed/);
  fail = false;
  await h.page(0);
  assert.equal(h.fields(), h.fields());
  await h.fields();
  assert.equal(count, 4);
});

test('selection prefetch remains bounded to eight page replies', async () => {
  let count = 0;
  const h = harness(async () => { count++; return { json: async () => [] }; });
  for (let page = 0; page < 9; page++) await h.page(page);
  await h.page(8);
  assert.equal(count, 9);
  await h.page(0);
  assert.equal(count, 10);
});

test('thumbnail document epochs stop obsolete requests without blocking ordinary image assets', async () => {
  const calls = [];
  const context = vm.createContext({
    host: { image: async url => { calls.push(url); return 'thumbnail'; } },
    imageUrl: async url => { calls.push(url); return 'asset'; },
    pagePresentationReady: async current => current(),
  });
  const images = html.slice(html.indexOf('let thumbnailObserver'), html.indexOf('function queueThumbnail'));
  vm.runInContext(`let documentEpoch = 2; ${images}
    globalThis.load = setImage;`, context);
  const stale = { isConnected: true, dataset: { documentEpoch: '1' }, decode: async () => {} };
  await context.load(stale, '/api/page/66.png?v=1');
  assert.equal(calls.length, 0);
  const live = { isConnected: true, dataset: { documentEpoch: '2' }, decode: async () => {}, style: {} };
  await context.load(live, '/api/page/0.png?v=2');
  assert.equal(live.src, 'thumbnail');
  const asset = { isConnected: true, dataset: {}, decode: async () => {}, style: {} };
  await context.load(asset, '/api/asset/a');
  assert.equal(asset.src, 'asset');
  assert.deepEqual(calls, ['/api/page/0.png?v=2', '/api/asset/a']);
});

test('an older refresh cannot overwrite a newer local-file save status', async () => {
  const start = html.indexOf('async function act(');
  const act = html.slice(start, html.indexOf('const upload =', start));
  const hook = html.match(/status: (text => \{ fileStatusEpoch\+\+; status\(text\); \}), error:/)[1];
  for (const message of [null, 'Saved to fresh.pdf', 'Downloaded a copy - original not updated']) {
    let entered, finish;
    const refreshing = new Promise(resolve => { entered = resolve; });
    const refreshed = new Promise(resolve => { finish = resolve; });
    const context = vm.createContext({
      setTimeout, host: true, flush: async () => {}, refresh: () => { entered(); return refreshed; },
      reportError: error => { throw error; },
    });
    vm.runInContext(`let fileStatusEpoch = 0;
      const status = text => globalThis.message = text;
      ${act}
      globalThis.run = act; globalThis.fileStatus = ${hook};`, context);
    const running = context.run(async () => {});
    await refreshing;
    if (message) context.fileStatus(message);
    finish();
    await running;
    assert.equal(context.message, message ?? 'Ready. PDFs stay on this device. Save downloads a copy.');
  }
});

test('protected saves retain the protection dialog instead of entering the file picker', async () => {
  const start = html.indexOf('async function saveDoc()');
  const saves = html.slice(start, html.indexOf('function saveAs()', start));
  for (const protectedDocument of [false, true]) {
    let fileSaves = 0, dialogs = 0;
    const context = vm.createContext({
      state: { pageCount: 1, protection: protectedDocument ? { owner: true } : null },
      fa: { supported: true, save: async () => { fileSaves++; return true; }, saveAs: async () => { fileSaves++; return true; } },
      flush: async () => {}, request: async () => ({ json: async () => ({ saved: false }) }),
      saveAs: () => { dialogs++; }, reportError: error => { throw error; },
    });
    vm.runInContext(`${saves}\nglobalThis.save = saveDoc; globalThis.saveCopy = saveAsFile;`, context);
    await context.save();
    await context.saveCopy();
    assert.equal(fileSaves, protectedDocument ? 0 : 2);
    assert.equal(dialogs, protectedDocument ? 2 : 0);
  }
});
