import { test } from 'node:test';
import assert from 'node:assert/strict';
import { start } from '../wwwroot/host.js';

test('duplicate in-flight image keys replace their byte accounting before eviction', async () => {
  const saved = new Map(['Worker', 'window', 'location'].map(k => [k, Object.getOwnPropertyDescriptor(globalThis, k)]));
  let renders = 0;
  class Worker {
    constructor() { queueMicrotask(() => this.onmessage({ data: { ready: true, bootRetries: 0 } })); }
    postMessage({ id, url }) {
      renders++;
      const reply = { contentType: 'image/png', data: 'a'.repeat(url.includes('large') ? 3 * 1024 * 1024 : 1024 * 1024) };
      queueMicrotask(() => this.onmessage({ data: { id, reply } }));
    }
    terminate() {}
  }
  try {
    Object.defineProperties(globalThis, {
      Worker: { configurable: true, value: Worker },
      window: { configurable: true, value: { dispatchEvent() {} } },
      location: { configurable: true, value: { href: 'https://example.test/csharp-pdf/', search: '' } },
    });
    const host = await start();
    const small = '/api/page/0.png?v=1';
    const large = '/api/page/1.png?v=1&large=true';
    await Promise.all([host.image(small), host.image(small), host.image(small)]);
    assert.equal(renders, 3, 'all three callers entered before the first cached reply');
    await host.image(large);
    assert.equal(host.hasCachedImage(small), false, 'the small entry is evicted to fit the large image');
    assert.equal(host.hasCachedImage(large), true);
    await host.image(large);
    assert.equal(renders, 4, 'a cache hit does not render again');
    host.evictImage(large);
    assert.equal(host.hasCachedImage(large), false);
    await host.image(small);
    assert.equal(host.hasCachedImage(small), true, 'explicit eviction also keeps accounting consistent');
  } finally {
    for (const [k, descriptor] of saved) {
      if (descriptor) Object.defineProperty(globalThis, k, descriptor);
      else delete globalThis[k];
    }
  }
});
