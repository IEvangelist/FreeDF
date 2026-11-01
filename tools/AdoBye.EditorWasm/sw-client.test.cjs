const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');
const source = readFileSync(join(__dirname, 'wwwroot', 'sw-client.js'), 'utf8').replace('export async function', 'async function');
function setup({ script = '', active = true, registrationError = null } = {}) {
  const listeners = new Map(), deleted = [], unregistered = [], warnings = [], installingEvents = new Map();
  const own = { scope: 'https://example.test/csharp-pdf/', active: active ? { state: 'activated', postMessage() {} } : null,
    installing: active ? null : { state: 'installing', addEventListener: (name, callback) => installingEvents.set(name, callback) },
    unregister: async () => unregistered.push('own'), update: async () => {}, addEventListener() {} };
  const other = { scope: 'https://example.test/another-app/', unregister: async () => unregistered.push('other') };
  const win = { isSecureContext: true, dispatchEvent() {}, editorPwa: { pendingCount: 0 } };
  const navigator = { serviceWorker: { controller: null, ready: Promise.resolve(own),
    register: async () => { if (registrationError) throw new Error(registrationError); return own; },
    getRegistrations: async () => [own, other], addEventListener: (name, callback) => listeners.set(name, callback) } };
  const context = vm.createContext({
    window: win, navigator, URL, CustomEvent: class {}, document: { baseURI: own.scope, getElementById: () => null, querySelector: () => null },
    fetch: async () => ({ ok: true, text: async () => script }), console: { warn: (...args) => warnings.push(args) },
    caches: { keys: async () => ['adobye-editor-old', 'adobye-pins', 'another-app-cache'], delete: async name => deleted.push(name) },
  });
  vm.runInContext(source, context);
  return { register: () => context.registerServiceWorker(), win, listeners, own, deleted, unregistered, warnings, installingEvents };
}
test('the published kill switch unregisters only its own scope and caches', async () => {
  const f = setup({ script: 'const MANIFEST = {"kill":true};' });
  await f.register();
  assert.deepEqual(f.unregistered, ['own']);
  assert.deepEqual(f.deleted, ['adobye-editor-old', 'adobye-pins']);
  assert.equal(f.win.editorOffline.state, 'unavailable');
});
test('offline readiness is reported only after activation and failures are visible', async () => {
  const active = setup(); await active.register(); assert.equal(active.win.editorOffline.state, 'ready');
  const preparing = setup({ active: false }); await preparing.register();
  assert.equal(preparing.win.editorOffline.state, 'preparing');
  preparing.own.installing.state = 'redundant'; preparing.installingEvents.get('statechange')();
  assert.equal(preparing.win.editorOffline.state, 'unavailable');
  const failure = setup({ registrationError: 'Storage denied' }); await failure.register();
  assert.equal(failure.win.editorOffline.message, 'Storage denied'); assert.equal(failure.warnings.length, 1);
});
test('received PDF launches defer updates even before a document has been opened', async () => {
  const f = setup(); await f.register(); let clean;
  const query = () => f.listeners.get('message')({ data: { type: 'QUERY_DIRTY' }, ports: [{ postMessage: value => { clean = value.clean; } }] });
  query(); assert.equal(clean, true);
  f.win.editorPwa.pendingCount = 2; query(); assert.equal(clean, false);
  f.win.editorPwa.pendingCount = 0; query(); assert.equal(clean, true);
});
