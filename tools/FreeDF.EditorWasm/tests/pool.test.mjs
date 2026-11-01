import test from 'node:test';
import assert from 'node:assert/strict';
import { createRenderPool } from '../wwwroot/pool.js';

const flush = async () => { for (let i = 0; i < 10; i++) await new Promise(resolve => setImmediate(resolve)); };

// Deterministic fake replicas: every call waits on a gate the test opens, so work can be held in flight.
function harness({ maxSlots = 3, hold = false } = {}) {
  const calls = [], gates = [];
  const clock = { t: 0, now() { return this.t; }, setTimeout: () => 0, clearTimeout() {} };
  class FakeCanvas {
    constructor(w, h) { this.w = w; this.h = h; }
    getContext() { return { putImageData() {} }; }
    async convertToBlob() { return new Blob([`png-${this.w}x${this.h}`], { type: 'image/png' }); }
  }
  const spawn = async () => ({
    terminate() {},
    loadedKey: null,
    async ensure(key) { this.loadedKey = key; },
    async call(message) {
      calls.push(message);
      if (hold) await new Promise(resolve => gates.push(resolve));
      if (message.op === 'size') return { width: 8, height: 1000 };
      if (message.op === 'band') return { rgba: new ArrayBuffer(8 * (message.y1 - message.y0) * 4), ms: 1 };
      return { png: new Uint8Array([1, 2, 3]).buffer };
    },
  });
  const pool = createRenderPool({
    snapshot: async () => ({ bytes: new Uint8Array(4), version: 7 }),
    spawn, clock, maxSlots,
    env: { OffscreenCanvas: FakeCanvas, ImageData: class { constructor(data, w, h) { this.data = data; this.width = w; this.height = h; } }, navigator: {} },
  });
  return { pool, calls, release: () => { while (gates.length) gates.shift()(); } };
}

const url = (page, dpi) => `/api/page/${page}.png?dpi=${dpi}&v=7&bare=false`;

test('a multi-page print batch (all pages at 150 dpi, one Promise.all) completes with every page rendered', async () => {
  const { pool } = harness();
  const blobs = await Promise.all(Array.from({ length: 12 }, (_, page) => pool.tryRender(url(page, 150))));
  assert.equal(blobs.length, 12);
  assert.ok(blobs.every(blob => blob instanceof Blob && blob.size > 0));
  assert.equal(pool.scheduler.stats.cancelled, 0);
});

test('a visible-page render is not aborted by another page or by neighbour prefetch', async () => {
  const { pool, release } = harness({ hold: true });
  const visible = pool.tryRender(url(0, 240));
  await flush();
  const neighbour = pool.tryRender(url(1, 40));
  const other = pool.tryRender(url(2, 240));
  await flush();
  assert.equal(pool.scheduler.stats.cancelled, 0);
  for (let i = 0; i < 20; i++) { release(); await flush(); }
  const results = await Promise.all([visible, neighbour, other]);
  assert.ok(results[0] instanceof Blob && results[2] instanceof Blob);
  assert.equal(pool.scheduler.stats.cancelled, 0);
});

test('only an explicit slot supersedes: a newer zoom render cancels the older one in the same slot, never other work', async () => {
  const { pool, release } = harness({ hold: true });
  const oldZoom = pool.tryRender(url(0, 200), { slot: 'view' }).then(() => 'done', error => error.name);
  const printed = pool.tryRender(url(1, 150));
  await flush();
  const newZoom = pool.tryRender(url(0, 250), { slot: 'view' });
  await flush();
  for (let i = 0; i < 30; i++) { release(); await flush(); }
  assert.equal(await oldZoom, 'AbortError');
  assert.ok(await newZoom instanceof Blob);
  assert.ok(await printed instanceof Blob, 'unslotted work survives');
});

test('explicit cancelSlot aborts queued slot work only', async () => {
  const { pool, release } = harness({ maxSlots: 1, hold: true });
  const a = pool.tryRender(url(0, 200), { slot: 'view' }).then(() => 'done', error => error.name);
  const b = pool.tryRender(url(1, 200)).then(() => 'done', error => error.name);
  await flush();
  pool.cancelSlot('view');
  for (let i = 0; i < 30; i++) { release(); await flush(); }
  assert.equal(await a, 'AbortError');
  assert.equal(await b, 'done');
});
