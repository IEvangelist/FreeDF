const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');
const source = readFileSync(join(__dirname, 'wwwroot', 'sw-client.js'), 'utf8').replace('export async function', 'async function');
function setup({ script = '', active = true, registrationError = null, waiting = false, pinCheck = async () => false } = {}) {
  const listeners = new Map(), deleted = [], unregistered = [], warnings = [], installingEvents = new Map();
  const nodes = new Map(), timers = new Map(), frames = [], activation = [];
  let nextTimer = 0, reloads = 0;
  const node = tag => ({
    tagName: tag, style: {}, attributes: {}, children: [], value: '',
    set textContent(value) { this.value = value; this.children = []; },
    get textContent() { return this.value + this.children.map(c => c.textContent).join(''); },
    setAttribute(key, value) { this.attributes[key] = value; },
    removeAttribute(key) { delete this.attributes[key]; },
    append(...children) { this.children.push(...children); for (const child of children) if (child.id) nodes.set(child.id, child); },
    replaceChildren(...children) { this.value = ''; this.children = children; },
    remove() { nodes.delete(this.id); },
  });
  const own = { scope: 'https://example.test/csharp-pdf/', active: active ? { state: 'activated', postMessage() {} } : null,
    waiting: waiting ? { state: 'installed', postMessage: (data, ports) => activation.push({ data, ports }) } : null,
    installing: active ? null : { state: 'installing', addEventListener: (name, callback) => installingEvents.set(name, callback) },
    unregister: async () => unregistered.push('own'), update: async () => {}, addEventListener() {} };
  const other = { scope: 'https://example.test/another-app/', unregister: async () => unregistered.push('other') };
  const win = { isSecureContext: true, dispatchEvent() {}, editorPwa: { pendingCount: 0 } };
  const navigator = { serviceWorker: { controller: waiting ? {} : null, ready: Promise.resolve(own),
    register: async () => { if (registrationError) throw new Error(registrationError); return own; },
    getRegistrations: async () => [own, other], addEventListener: (name, callback) => listeners.set(name, callback) } };
  const context = vm.createContext({
    window: win, navigator, URL, CustomEvent: class {},
    document: { baseURI: own.scope, getElementById: id => nodes.get(id), querySelector: () => null, createElement: node, body: node('body') },
    requestAnimationFrame: callback => frames.push(callback),
    setTimeout: (callback, ms) => { timers.set(++nextTimer, { callback, ms }); return nextTimer; },
    clearTimeout: id => timers.delete(id),
    setInterval: (callback, ms) => { timers.set(++nextTimer, { callback, ms }); return nextTimer; },
    clearInterval: id => timers.delete(id),
    location: { reload() { reloads++; } },
    MessageChannel: class { constructor() { this.port1 = { close() {} }; this.port2 = { close() {}, reply: data => this.port1.onmessage({ data }) }; } },
    fetch: async () => ({ ok: true, text: async () => script }), console: { warn: (...args) => warnings.push(args) },
    caches: { has: pinCheck, keys: async () => ['freedf-editor-old', 'freedf-pins', 'adobye-editor-old', 'adobye-pins', 'another-app-cache'], delete: async name => deleted.push(name) },
  });
  vm.runInContext(source, context);
  const fire = predicate => { for (const [id, timer] of [...timers]) if (predicate(timer.ms)) { timers.delete(id); timer.callback(); } };
  return { register: () => context.registerServiceWorker(), mirror: caches => context.mirrorPinsForRollback(caches), win, listeners, own, deleted, unregistered, warnings, installingEvents,
    banner: () => nodes.get('update-banner'), activation, reloads: () => reloads,
    paint: async () => { frames.splice(0).forEach(fn => fn()); fire(ms => ms === 0); await new Promise(setImmediate); },
    timeout: () => fire(ms => ms > 100),
    hiddenPaint: async () => { fire(ms => ms === 100); await new Promise(setImmediate); },
  };
}
test('the published kill switch unregisters only its own scope and caches', async () => {
  const f = setup({ script: 'const MANIFEST = {"kill":true};' });
  await f.register();
  assert.deepEqual(f.unregistered, ['own']);
  assert.deepEqual(f.deleted, ['freedf-editor-old', 'freedf-pins', 'adobye-editor-old', 'adobye-pins']);
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
test('new build pins are mirrored before an older worker can take over', async () => {
  const f = setup(), copied = [];
  const current = { keys: async () => ['pin/client', 'meta/current'], match: async key => new Response(key) };
  const legacy = { put: async (key, response) => copied.push([key, await response.text()]) };
  await f.mirror({ has: async name => name === 'freedf-pins', open: async name => name === 'freedf-pins' ? current : legacy });
  assert.deepEqual(copied, [['pin/client', 'pin/client'], ['meta/current', 'meta/current']]);
});
test('pin persistence failures are reported in the offline status', async () => {
  const f = setup(); await f.register();
  f.listeners.get('message')({ data: { type: 'PIN_STORAGE_ERROR' } });
  assert.match(f.win.editorOffline.message, /could not retain/);
  assert.equal(f.win.editorOffline.state, 'unavailable');
});

test('Reload acknowledges immediately, paints progress before cache work, and rejects duplicate clicks', async () => {
  let checks = 0, finish;
  const f = setup({ waiting: true, pinCheck: () => { checks++; return new Promise(resolve => { finish = resolve; }); } });
  await f.register();
  const reload = f.banner().children.find(e => e.textContent === 'Reload').onclick;
  const pending = reload();
  assert.match(f.banner().textContent, /^Updating/);
  assert.equal(f.banner().attributes['aria-busy'], 'true');
  assert.ok(f.banner().children.some(e => e.tagName === 'progress'));
  assert.equal(checks, 0);
  await reload();
  await f.paint(); assert.equal(checks, 1);
  finish(false); await pending;
  assert.equal(f.activation.length, 1);
  f.activation[0].ports[0].reply({ type: 'ACTIVATING' });
  assert.match(f.banner().textContent, /Loading the new version/);
  f.listeners.get('controllerchange')();
  assert.equal(f.reloads(), 0);
  assert.match(f.banner().textContent, /^Reloading/);
  await f.paint(); assert.equal(f.reloads(), 1);
});

test('cache stalls and activation stalls recover with a retry instead of leaving busy UI forever', async () => {
  let finish;
  const f = setup({ waiting: true, pinCheck: () => new Promise(resolve => { finish = resolve; }) });
  await f.register();
  const pending = f.banner().children.find(e => e.textContent === 'Reload').onclick();
  await f.paint(); f.timeout();
  assert.match(f.banner().textContent, /did not finish/);
  assert.equal(f.banner().attributes['aria-busy'], undefined);
  finish(false); await pending;
  assert.equal(f.activation.length, 0, 'timed-out cache work must not activate later');
  const g = setup({ waiting: true }); await g.register();
  const activating = g.banner().children.find(e => e.textContent === 'Reload').onclick();
  await g.paint(); await activating;
  g.activation[0].ports[0].reply({ type: 'ACTIVATING' }); g.timeout();
  assert.match(g.banner().textContent, /did not finish/);
  assert.ok(g.banner().children.some(e => e.textContent === 'Reload'));
});

test('hidden tabs finish an accepted update without waiting for suspended animation frames', async () => {
  const f = setup({ waiting: true }); await f.register();
  const pending = f.banner().children.find(e => e.textContent === 'Reload').onclick();
  await f.hiddenPaint(); await pending;
  assert.equal(f.activation.length, 1);
  f.listeners.get('controllerchange')();
  await f.hiddenPaint();
  assert.equal(f.reloads(), 1);
});

test('dirty tabs and cache errors leave actionable update messages without reloading', async () => {
  const f = setup({ waiting: true }); await f.register();
  const pending = f.banner().children.find(e => e.textContent === 'Reload').onclick();
  await f.paint(); await pending;
  f.activation[0].ports[0].reply({ type: 'BLOCKED' });
  assert.match(f.banner().textContent, /another editor tab/);
  assert.equal(f.banner().attributes['aria-busy'], undefined);
  assert.equal(f.reloads(), 0);
  const g = setup({ waiting: true, pinCheck: async () => { throw new Error('Storage denied'); } }); await g.register();
  const failing = g.banner().children.find(e => e.textContent === 'Reload').onclick();
  await g.paint(); await failing;
  assert.match(g.banner().textContent, /Storage denied/);
  assert.ok(g.banner().children.some(e => e.textContent === 'Reload'));
});

test('work started during asynchronous update stages is protected unless discard is explicitly confirmed', async () => {
  let finish;
  const f = setup({ waiting: true, pinCheck: () => new Promise(resolve => { finish = resolve; }) });
  await f.register();
  const pending = f.banner().children.find(e => e.textContent === 'Reload').onclick();
  await f.paint();
  f.win.editorPwa.pendingCount = 1; finish(false); await pending;
  assert.equal(f.activation.length, 0);
  assert.match(f.banner().textContent, /Finish or save your work/);
  const g = setup({ waiting: true }); await g.register();
  g.listeners.get('controllerchange')();
  g.win.editorPwa.pendingCount = 1;
  await g.paint();
  assert.equal(g.reloads(), 0); assert.match(g.banner().textContent, /unsaved changes/);
  g.win.confirm = () => true;
  g.banner().children.find(e => e.textContent === 'Reload now').onclick();
  await g.paint(); assert.equal(g.reloads(), 1);
});
