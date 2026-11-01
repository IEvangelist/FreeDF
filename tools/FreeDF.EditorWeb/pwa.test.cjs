const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
let createPwa;
test.before(async () => {
  ({ createPwa } = await import('data:text/javascript;base64,' + Buffer.from(readFileSync(require('node:path').join(__dirname, 'wwwroot', 'pwa.js'))).toString('base64')));
});
function setup(overrides = {}) {
  const listeners = new Map(), messages = [], errors = [], opened = [], merged = [];
  const node = () => ({ hidden: false, textContent: '', setAttribute() {} });
  const button = node(), elements = Object.fromEntries(['[data-app-state]', '[data-app-error]', '[data-app-open]', '[data-app-update]', '.dok'].map(key => [key, node()]));
  const panel = { isConnected: true, querySelector: key => elements[key], remove() { this.isConnected = false; } };
  let submit, body, consumer;
  const win = {
    navigator: { onLine: true }, matchMedia: () => ({ matches: false, addEventListener() {} }),
    addEventListener: (name, callback) => listeners.set(name, callback),
    launchQueue: { setConsumer: callback => { consumer = callback; } }, setTimeout,
  };
  const hooks = {
    ready: Promise.resolve(), busy: () => false, hasDocument: () => false, confirmReplace: () => true,
    open: async (file, handle) => { opened.push([file.name, handle]); return true; },
    merge: files => merged.push(files.map(file => file.name)), status: text => messages.push(text),
    onError: error => errors.push(error.message),
    dialog: (title, html, label, callback) => { body = html; submit = callback; panel.isConnected = true; return panel; },
    ...overrides,
  };
  const doc = { baseURI: 'https://example.test/csharp-pdf/', getElementById: () => button };
  const app = createPwa(hooks, win, doc);
  return { win, app, button, elements, panel, opened, merged, errors, messages,
    launch: files => consumer({ files }), emit: (name, event = {}) => listeners.get(name)(event),
    submit: () => submit(), body: () => body };
}
const pdf = (name = 'received.pdf') => ({ kind: 'file', getFile: async () => ({ name }) });

test('install prompt is retained until an explicit action and cleared after installation', async () => {
  const f = setup(); let prevented = false, prompted = 0;
  f.emit('beforeinstallprompt', { preventDefault() { prevented = true; }, prompt: async () => { prompted++; }, userChoice: Promise.resolve({ outcome: 'accepted' }) });
  assert.ok(prevented); assert.equal(prompted, 0);
  f.app.show(); assert.equal(f.elements['.dok'].textContent, 'Install app');
  await f.submit(); assert.equal(prompted, 1);
  f.emit('appinstalled'); assert.ok(f.app.installed);
  assert.equal(f.elements['.dok'].textContent, 'Close');
});
test('manual installation instructions and offline state remain available without a prompt', () => {
  const f = setup(); f.app.show();
  assert.match(f.body(), /Install FreeDF/);
  assert.doesNotMatch(f.body(), /AdoBye/);
  assert.match(f.body(), /Share &gt; Add to Home Screen|Share > Add to Home Screen/);
  f.win.editorOffline = { state: 'ready' }; f.win.navigator.onLine = false; f.emit('offline');
  assert.match(f.elements['[data-app-state]'].textContent, /^Offline\. App ready/);
  f.win.editorOffline = { state: 'unavailable', message: 'Storage full' }; f.emit('editor-offline');
  assert.match(f.elements['[data-app-state]'].textContent, /Storage full/);
});
test('an OS launch waits for editor readiness and preserves its save handle', async () => {
  let ready; const f = setup({ ready: new Promise(resolve => { ready = resolve; }) });
  const handle = pdf(), launch = f.launch([handle]);
  await Promise.resolve(); assert.equal(f.opened.length, 0); assert.equal(f.app.pendingCount, 1);
  ready(); await launch;
  assert.deepEqual(f.opened, [['received.pdf', handle]]); assert.equal(f.app.pendingCount, 0);
});
test('existing documents and in-progress dialogs keep received files pending without replacement', async () => {
  for (const overrides of [{ hasDocument: () => true }, { busy: () => true }]) {
    const f = setup(overrides); await f.launch([pdf()]);
    assert.equal(f.opened.length, 0); assert.equal(f.app.pendingCount, 1);
    assert.match(f.button.title, /1 waiting/);
  }
});
test('canceling replacement or password entry retains the received PDF', async () => {
  const f = setup({ confirmReplace: () => false }); await f.launch([pdf()]);
  assert.equal(f.opened.length, 0); assert.equal(f.app.pendingCount, 1);
  const g = setup({ open: async () => false }); await g.launch([pdf()]);
  assert.equal(g.app.pendingCount, 1); assert.match(g.messages.at(-1), /still waiting/);
});
test('multiple PDFs open the first and offer the rest in the existing merge dialog', async () => {
  const f = setup(); await f.launch([pdf('first.pdf'), pdf('second.pdf'), pdf('third.pdf')]);
  assert.equal(f.opened[0][0], 'first.pdf');
  assert.deepEqual(f.merged, [['second.pdf', 'third.pdf']]); assert.equal(f.app.pendingCount, 0);
});
test('invalid files and denied file access fail visibly without losing the launch', async () => {
  for (const handle of [pdf('not-pdf.txt'), { kind: 'file', getFile: async () => { throw new Error('Permission denied'); } }]) {
    const f = setup(); await f.launch([handle]);
    assert.equal(f.opened.length, 0); assert.equal(f.errors.length, 1); assert.equal(f.app.pendingCount, 1);
  }
});
test('update checks never update a different app scope or promise offline network checks', async () => {
  const f = setup(); let updates = 0;
  f.win.navigator.serviceWorker = { getRegistration: async () => ({ scope: 'https://example.test/other/', update: async () => { updates++; } }) };
  f.app.show(); const update = f.elements['[data-app-update]'];
  await update.onclick({ currentTarget: update }); assert.equal(updates, 0); assert.match(f.errors[0], /not registered/);
  f.win.navigator.serviceWorker.getRegistration = async () => ({ scope: 'https://example.test/csharp-pdf/', update: async () => { updates++; } });
  f.win.navigator.onLine = false;
  await update.onclick({ currentTarget: update }); assert.equal(updates, 0); assert.match(f.errors[1], /Connect/);
  f.win.navigator.onLine = true;
  await update.onclick({ currentTarget: update }); assert.equal(updates, 1); assert.equal(update.disabled, false);
});
