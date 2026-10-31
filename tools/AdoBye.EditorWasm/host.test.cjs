const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const source = readFileSync(join(__dirname, 'wwwroot', 'host.js'), 'utf8')
  .replace('export async function start()', 'async function start()')
  .replace('import.meta.url', '"https://editor.test/host.js"');

async function harness(reply = () => ({ bytes: new Uint8Array([1, 2, 3]), contentType: 'image/png' })) {
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
    Worker, URL, URLSearchParams, Blob, Response, FormData, File, FileReader, fetch,
    performance, setTimeout, clearTimeout, location: { href: 'https://editor.test/', search: '' },
    window: {},
  });
  return { host: await vm.runInContext(`${source}\nstart()`, context), requests };
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
