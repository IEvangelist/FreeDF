import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { fetchVerified, parseManifest, deepLinkName, findSample, stripDeepLink, formatSize, esc, SHOWCASE, FULL_COLLECTION } from './wwwroot/samples.js';

const base = 'https://example.test/csharp-pdf/';
const bytes = new TextEncoder().encode('%PDF-1.7 sample bytes');
const entry = { name: '27-full-collection', file: '27-full-collection.pdf', size: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') };
const serve = (body, init) => async url => { serve.last = String(url); return new Response(body, init); };

test('accepts bytes that match the manifest size and sha256, same-origin only', async () => {
  const progress = [];
  const result = await fetchVerified(entry, { base, fetchImpl: serve(bytes), onProgress: (done, total) => progress.push([done, total]) });
  assert.deepEqual([...result], [...bytes]);
  assert.equal(serve.last, base + 'samples/27-full-collection.pdf');
  assert.deepEqual(progress.at(-1), [bytes.length, bytes.length]);
});

test('rejects tampered content of the right size', async () => {
  const tampered = bytes.slice(); tampered[10] ^= 1;
  await assert.rejects(fetchVerified(entry, { base, fetchImpl: serve(tampered) }), /integrity check/);
});

test('rejects truncated and oversized downloads', async () => {
  await assert.rejects(fetchVerified(entry, { base, fetchImpl: serve(bytes.slice(0, 5)) }), /incomplete/);
  await assert.rejects(fetchVerified(entry, { base, fetchImpl: serve(new Uint8Array([...bytes, 1])) }), /larger than expected/);
});

test('reports HTTP and network failures', async () => {
  await assert.rejects(fetchVerified(entry, { base, fetchImpl: serve('nope', { status: 404 }) }), /HTTP 404/);
  await assert.rejects(fetchVerified(entry, { base, fetchImpl: async () => { throw new TypeError('offline'); } }), /Check your connection/);
});

test('manifest entries are validated', () => {
  const good = { name: '01-spec-showcase', file: '01-spec-showcase.pdf', title: 'T', description: 'D', size: 10, pages: 1, sha256: 'a'.repeat(64) };
  assert.equal(parseManifest({ version: 1, samples: [good] })[0].name, '01-spec-showcase');
  for (const bad of [{ ...good, file: '../x.pdf' }, { ...good, name: '../x', file: '../x.pdf' }, { ...good, sha256: 'xyz' }, { ...good, size: 0 }])
    assert.throws(() => parseManifest({ version: 1, samples: [bad] }), /invalid entry/);
  assert.throws(() => parseManifest({ version: 2, samples: [] }), /supported format/);
});

test('deep links resolve from hash or query and are removed after use', () => {
  const at = (hash = '', search = '') => ({ hash, search });
  assert.equal(deepLinkName(at('#sample=27-full-collection')), '27-full-collection');
  assert.equal(deepLinkName(at('#sample=28')), '28');
  assert.equal(deepLinkName(at('', '?sample=27-full-collection.pdf')), '27-full-collection');
  assert.equal(deepLinkName(at('#other=1')), null);
  const entries = [{ name: SHOWCASE }, { name: FULL_COLLECTION }, { name: '01-spec-showcase' }];
  assert.equal(findSample(entries, '28').name, SHOWCASE);
  assert.equal(findSample(entries, '27').name, '27-full-collection');
  assert.equal(findSample(entries, '../x'), undefined);
  assert.equal(stripDeepLink(base + '?sample=27&keep=1#sample=27&x=2'), base + '?keep=1#x=2');
  assert.equal(stripDeepLink(base + '#sample=27'), base);
});

test('formatting helpers are safe', () => {
  assert.equal(formatSize(2.3 * 1048576), '2.3 MB');
  assert.equal(formatSize(1500), '1 KB');
  assert.equal(esc('<b>"&\'</b>'), '&lt;b&gt;&quot;&amp;&#39;&lt;/b&gt;');
});
