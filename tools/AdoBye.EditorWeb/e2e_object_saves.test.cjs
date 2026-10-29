const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const saves = html.slice(html.indexOf('let objectSave ='), html.indexOf('function selectObj('));
const flush = html.slice(html.indexOf('async function flush()'), html.indexOf('\nfunction props()'));
const act = html.slice(html.indexOf('async function act('), html.indexOf('\nconst upload ='));

function harness(fetch) {
  const messages = [];
  const context = vm.createContext({
    fetch, status: message => messages.push(message),
    $: () => null, document: { querySelectorAll: () => [] },
    refreshDirty: async () => {}, refresh: async () => {}, clearTimeout,
  });
  vm.runInContext(`let sel = 0, saveT = 0, state = { pageCount: 1 };
    ${saves}\n${flush}\n${act}
    globalThis.save = saveObj; globalThis.flush = flush; globalThis.act = act;
    globalThis.page = n => { sel = n; };`, context);
  return { ...context, messages };
}

function edit(tx) {
  return { index: 2, edit: { sx: 1, sy: 1, tx, ty: 0 } };
}

test('flush waits for an object-only move even without a pending overlay save', async () => {
  let release;
  const h = harness(() => new Promise(resolve => { release = resolve; }));
  const saving = h.save(edit(20));
  let flushed = false;
  const flushing = h.flush().then(() => { flushed = true; });
  await Promise.resolve();
  assert.equal(flushed, false);
  release({ ok: true });
  await Promise.all([saving, flushing]);
  assert.equal(flushed, true);
});

test('move, resize and reset are ordered and capture their original page and body', async () => {
  const requests = [];
  let release;
  const h = harness((url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    if (requests.length === 1) return new Promise(resolve => { release = resolve; });
    return Promise.resolve({ ok: true });
  });
  const o = edit(20);
  h.save(o);
  await Promise.resolve();
  o.edit = { sx: 2, sy: 3, tx: 30, ty: 40 };
  h.save(o);
  o.edit = null;
  h.save(o);
  h.page(1);
  assert.equal(requests.length, 1);
  release({ ok: true });
  await h.flush();
  assert.equal(requests.length, 3);
  assert.ok(requests.every(r => r.url === '/api/page/0/objects/2'));
  assert.equal(requests[0].body.tx, 20);
  assert.equal(requests[1].body.sx, 2);
  assert.equal(requests[2].body, null);
});

test('flush drains edits added while the previous transform is still saving', async () => {
  const releases = [];
  const h = harness(() => new Promise(resolve => releases.push(resolve)));
  h.save(edit(20));
  let flushed = false;
  const flushing = h.flush().then(() => { flushed = true; });
  await Promise.resolve();
  h.save(edit(30));
  releases[0]({ ok: true });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(releases.length, 2);
  assert.equal(flushed, false);
  releases[1]({ ok: true });
  await flushing;
  assert.equal(flushed, true);
});

test('document mutations wait for the pending transform before changing page identities', async () => {
  let release, mutated = false;
  const h = harness(() => new Promise(resolve => { release = resolve; }));
  h.save(edit(20));
  const action = h.act(async () => { mutated = true; });
  await Promise.resolve();
  assert.equal(mutated, false);
  release({ ok: true });
  await action;
  assert.equal(mutated, true);
});

for (const failure of ['http', 'network']) {
  test(`${failure} failures are visible and block save until the failed edit is retried`, async () => {
    let fail = true;
    const h = harness(async () => {
      if (fail && failure === 'network') throw new Error('Disconnected');
      return { ok: !fail, status: fail ? 500 : 200 };
    });
    await h.save(edit(20));
    assert.match(h.messages[0], /Object edit was not saved/);
    await assert.rejects(h.flush(), /Object edits have not been saved/);
    fail = false;
    await h.save({ ...edit(10), index: 3 });
    await assert.rejects(h.flush(), /Object edits have not been saved/);
    await h.save(edit(20));
    await h.flush();
  });
}
