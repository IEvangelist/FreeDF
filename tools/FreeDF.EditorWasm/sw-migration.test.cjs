const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const scope = 'https://example.test/csharp-pdf/';
const key = (kind, name) => new URL(`${kind}/${name}`, scope).href;

function harness(kill = false) {
  const stores = new Map(), handlers = new Map(), messages = [], warnings = [];
  const url = input => typeof input === 'string' ? input : input.url;
  const caches = {
    async open(name) {
      if (!stores.has(name)) {
        const values = new Map();
        stores.set(name, {
          async put(request, response) { values.set(url(request), response.clone()); },
          async match(request) { return values.get(url(request))?.clone(); },
          async delete(request) { return values.delete(url(request)); },
          async keys() { return [...values.keys()].map(value => new Request(value)); },
        });
      }
      return stores.get(name);
    },
    async has(name) { return stores.has(name); },
    async delete(name) { return stores.delete(name); },
    async keys() { return [...stores.keys()]; },
  };
  const live = new Set(['old-client', 'new-client']);
  const clients = {
    async matchAll() { return [...live].map(id => ({ id, postMessage: message => messages.push([id, message]) })); },
    async get(id) { return live.has(id) ? { postMessage: message => messages.push([id, message]) } : null; },
  };
  const registration = { scope, unregistered: false, async unregister() { this.unregistered = true; } };
  const self = { registration, clients, addEventListener: (name, handler) => handlers.set(name, handler) };
  const source = fs.readFileSync(path.join(__dirname, 'wwwroot', 'sw.js'), 'utf8')
    .replace('const MANIFEST = null; /* __PRECACHE_MANIFEST__ */',
      `const MANIFEST = ${JSON.stringify(kill ? { kill: true } : { version: 'new', files: [] })};`);
  vm.runInNewContext(source + '\nself.test = { migrateLegacyPins, retirePrevious, dropUnpinnedCaches, pinOf, setPin, servePinned };',
    { self, caches, URL, Request, Response, console: { warn: (...args) => warnings.push(args) }, setTimeout, clearTimeout });
  return { caches, handlers, messages, warnings, live, clients, registration, worker: self.test };
}

test('legacy pins survive the rename, protect old clients, and remain usable for rollback', async () => {
  const h = harness();
  const legacyPins = await h.caches.open('adobye-pins');
  const currentPins = await h.caches.open('freedf-pins');
  const oldCache = await h.caches.open('adobye-editor-old');
  const asset = new URL('worker.js', scope).href;
  await oldCache.put(asset, new Response('old worker'));
  await legacyPins.put(key('meta', 'current'), new Response('adobye-editor-old'));
  await legacyPins.put(key('pin', 'old-client'), new Response('adobye-editor-old'));
  await currentPins.put(key('pin', 'new-client'), new Response('freedf-editor-new'));

  await h.worker.migrateLegacyPins();
  await h.worker.retirePrevious();
  assert.equal(await h.worker.pinOf('old-client'), 'adobye-editor-old');
  assert.equal(await (await h.worker.servePinned('adobye-editor-old', asset)).text(), 'old worker');
  await h.worker.dropUnpinnedCaches();
  assert.equal(await h.caches.has('adobye-editor-old'), true);

  await h.worker.setPin('new-client', 'freedf-editor-new');
  assert.equal(await (await legacyPins.match(key('pin', 'new-client'))).text(), 'freedf-editor-new');
  assert.equal(await (await legacyPins.match(key('meta', 'current'))).text(), 'freedf-editor-new');

  h.live.delete('old-client');
  await h.worker.dropUnpinnedCaches();
  assert.equal(await h.caches.has('adobye-editor-old'), false);
  assert.equal(await legacyPins.match(key('pin', 'old-client')), undefined);
  assert.equal(await (await currentPins.match(key('pin', 'new-client'))).text(), 'freedf-editor-new');
});

test('the kill switch removes both editor cache identities but leaves unrelated caches', async () => {
  const h = harness(true);
  for (const name of ['adobye-editor-old', 'freedf-editor-new', 'adobye-pins', 'freedf-pins', 'unrelated-app'])
    await h.caches.open(name);
  let activation;
  h.handlers.get('activate')({ waitUntil(promise) { activation = promise; } });
  await activation;
  assert.deepEqual(await h.caches.keys(), ['unrelated-app']);
  assert.equal(h.registration.unregistered, true);
  assert.equal(h.messages.length, 2);
  assert.ok(h.messages.every(([, message]) => message.type === 'KILLED'));
});

test('pin write failures are logged and surfaced without discarding the active tab', async () => {
  const h = harness();
  const pins = await h.caches.open('freedf-pins');
  pins.put = async () => { throw new Error('storage full'); };
  await h.worker.setPin('new-client', 'freedf-editor-new');
  assert.equal(await h.worker.pinOf('new-client'), 'freedf-editor-new');
  assert.equal(h.warnings.length, 1);
  assert.equal(h.messages[0][1].type, 'PIN_STORAGE_ERROR');
});

test('a pending navigation client does not block a response when pin storage is full', async () => {
  const h = harness();
  const pins = await h.caches.open('freedf-pins');
  pins.put = async () => { throw new Error('storage full'); };
  let release;
  h.clients.get = () => new Promise(resolve => { release = resolve; });
  const pending = [], request = h.worker.setPin('new-client', 'freedf-editor-new', { waitUntil: promise => pending.push(promise) });
  const completed = await Promise.race([request.then(() => true), new Promise(resolve => setTimeout(() => resolve(false), 100))]);
  assert.equal(completed, true, 'waiting for the navigating client must not delay the response');
  release({ postMessage: message => h.messages.push(['new-client', message]) });
  await Promise.all(pending);
  assert.equal(h.messages[0][1].type, 'PIN_STORAGE_ERROR');
});
