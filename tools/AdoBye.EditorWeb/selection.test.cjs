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
