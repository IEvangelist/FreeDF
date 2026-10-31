const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const source = readFileSync(join(__dirname, 'wwwroot', 'host.js'), 'utf8')
  .replace('export async function start()', 'async function start()')
  .replace('import.meta.url', '"https://editor.test/host.js"');

async function harness(reply = () => ({ bytes: new Uint8Array([1, 2, 3]), contentType: 'image/png' }), pool = null) {
  const requests = [];
  class Worker {
    constructor() { queueMicrotask(() => this.onmessage({ data: { ready: true } })); }
    postMessage(command) {
      requests.push(command);
      Promise.resolve().then(() => reply(command)).then(
        result => this.onmessage({ data: { id: command.id, reply: result } }),
        error => this.onmessage({ data: { id: command.id, error: error.message } }));
    }
  }
  class FileReader {
    readAsDataURL(blob) {
      blob.arrayBuffer().then(bytes => {
        this.result = `data:${blob.type};base64,${Buffer.from(bytes).toString('base64')}`;
        this.onload();
      });
    }
  }
  const context = vm.createContext({
    Worker, URL, URLSearchParams, Blob, Response, FormData, File, FileReader, fetch, DOMException,
    performance, setTimeout, clearTimeout, console,
    createRenderPool: pool,
    location: { href: 'https://editor.test/', search: pool ? '?pool=1' : '?pool=0' },
    window: {},
  });
  const script = source.replace("await import('./pool.js')", '({ createRenderPool: globalThis.createRenderPool })');
  return { host: await vm.runInContext(`${script}\nstart()`, context), requests };
}

test('versioned Blob images coalesce and canonicalize query order and transient timestamps', async () => {
  const { host, requests } = await harness();
  const [a, b] = await Promise.all([
    host.imageBlob('/api/page/0.png?dpi=40&v=1&t=1'),
    host.imageBlob('/api/page/0.png?v=1&t=2&dpi=40'),
  ]);
  assert.equal(a, b);
  assert.equal(requests.length, 1);
  assert.equal(await host.image('/api/page/0.png?v=1&dpi=40'), 'data:image/png;base64,AQID');
  assert.equal(requests.length, 1);
  await host.imageBlob('/api/page/0.png?v=2&dpi=40');
  assert.equal(requests.length, 2);
});

test('image failures are visible and retryable; eviction does not disturb other keys', async () => {
  let fail = true;
  const { host, requests } = await harness(() => {
    if (fail) throw new Error('render failed');
    return { bytes: new Uint8Array([7]), contentType: 'image/png' };
  });
  const url = '/api/page/0.png?v=1';
  await assert.rejects(host.imageBlob(url), /render failed/);
  fail = false;
  await host.imageBlob(url);
  await host.imageBlob('/api/page/1.png?v=1');
  host.evictImage(url);
  assert.equal(host.hasCachedImage(url), false);
  assert.equal(host.hasCachedImage('/api/page/1.png?v=1'), true);
  await host.imageBlob(url);
  assert.equal(requests.length, 4);
});

test('compressed-byte cache is bounded and refreshes recently used entries', async () => {
  const { host, requests } = await harness(() => ({
    bytes: new Uint8Array(4 * 1024 * 1024), contentType: 'image/png',
  }));
  const url = i => `/api/page/${i}.png?v=1`;
  await host.imageBlob(url(0));
  await host.imageBlob(url(1));
  await host.imageBlob(url(0));
  await host.imageBlob(url(2));
  assert.equal(host.hasCachedImage(url(0)), true);
  assert.equal(host.hasCachedImage(url(1)), false);
  assert.equal(host.hasCachedImage(url(2)), true);
  assert.equal(requests.length, 3);
});

test('unversioned images are not reused and binary API responses preserve bytes and headers', async () => {
  const { host, requests } = await harness(() => ({
    bytes: new Uint8Array([0, 255, 13]), contentType: 'application/pdf', fileName: 'edited.pdf',
  }));
  await host.imageBlob('/api/page/0.png');
  await host.imageBlob('/api/page/0.png');
  const response = await host.request('/api/save');
  assert.deepEqual([...new Uint8Array(await response.arrayBuffer())], [0, 255, 13]);
  assert.equal(response.headers.get('Content-Type'), 'application/pdf');
  assert.equal(response.headers.get('X-File-Name'), 'edited.pdf');
  assert.equal(requests.length, 3);
});

test('pooled Blob requests coalesce without blocking their queued snapshot request', async () => {
  const blob = new Blob([new Uint8Array([9, 8, 7])], { type: 'image/png' });
  const { host, requests } = await harness(() => ({
    bytes: new Uint8Array([37, 80, 68, 70]), fileName: 'v3', contentType: 'application/pdf',
  }), ({ snapshot }) => ({
    enabled: true,
    async tryRender() {
      const source = await snapshot(true);
      assert.equal(source.version, 3);
      return blob;
    },
  }));
  const url = '/api/page/0.png?v=3&dpi=240&bare=true';
  const [first, second] = await Promise.all([host.imageBlob(url), host.imageBlob(url)]);
  assert.equal(first, blob);
  assert.equal(first, second);
  assert.equal(requests.length, 1);
  assert.equal(requests[0].url, '/api/snapshot?bare=true');
});

test('unavailable pooled renders retain primary bytes and normal caching', async () => {
  const { host, requests } = await harness(undefined, () => ({
    enabled: true, tryRender: async () => null,
  }));
  const url = '/api/page/0.png?v=3&dpi=240';
  assert.equal((await host.imageBlob(url)).size, 3);
  assert.equal((await host.imageBlob(url)).size, 3);
  assert.equal(requests.length, 1);
});

test('superseded queued images never reach the primary worker or populate its cache', async () => {
  let release, active = true;
  const gate = new Promise(resolve => { release = resolve; });
  const { host, requests } = await harness(async command => {
    if (command.url === '/api/open') await gate;
    return { bytes: new Uint8Array([1]), contentType: 'image/png' };
  });
  const opening = host.request('/api/open', { method: 'POST' });
  const url = '/api/page/66.png?dpi=40&v=1';
  const image = host.imageBlob(url, { current: () => active });
  const rejected = assert.rejects(image, { name: 'AbortError' });
  active = false; release();
  await opening; await rejected;
  assert.deepEqual(requests.map(x => x.url), ['/api/open']);
  assert.equal(host.hasCachedImage(url), false);
});

test('visible first paint uses primary bytes while exact refinement remains pooled', async () => {
  let pooled = 0;
  const { host, requests } = await harness(undefined, () => ({
    enabled: true,
    async tryRender() { pooled++; return new Blob([new Uint8Array([9])], { type: 'image/png' }); },
  }));
  const preview = '/api/page/0.png?v=3&dpi=40&bare=true';
  assert.equal((await host.imageBlob(preview, { pool: false })).size, 3);
  assert.equal((await host.imageBlob(preview)).size, 3);
  assert.equal(pooled, 0);
  assert.equal((await host.imageBlob('/api/page/0.png?v=3&dpi=240&bare=true')).size, 1);
  assert.equal(pooled, 1);
  assert.equal(requests.length, 1);
});

test('a superseded pooled fallback does not dispatch stale primary work', async () => {
  let release, active = true;
  const gate = new Promise(resolve => { release = resolve; });
  const { host, requests } = await harness(undefined, () => ({
    enabled: true, tryRender: async () => { await gate; return null; },
  }));
  const image = host.imageBlob('/api/page/66.png?v=1', { current: () => active });
  const rejected = assert.rejects(image, { name: 'AbortError' });
  active = false; release();
  await rejected;
  assert.equal(requests.length, 0);
});
