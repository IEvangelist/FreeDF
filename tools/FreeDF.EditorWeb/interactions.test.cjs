const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const source = html.slice(html.indexOf('function drag(ev,'), html.indexOf('// Applies Shift'));
test('page-navigation shortcuts do not consume original-text or multi-selection keys', () => {
  const handlers = [], pages = [];
  const context = vm.createContext({
    document: { activeElement: { tagName: 'BODY', closest: () => null }, querySelector: () => null },
    state: { pageCount: 3 }, sel: 1, editing: false, cur: null, curObj: null, curSpan: null, multi: new Set(),
    addEventListener: (type, handler) => handlers.push(handler), goPage: page => pages.push(page),
  });
  vm.runInContext(html.slice(html.indexOf('// Global shortcuts'), html.indexOf('\nfunction scheduleLayoutBuild')), context);
  const navigate = () => {
    for (const key of ['ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageDown', 'PageUp'])
      handlers[0]({ key, preventDefault() {} });
  };
  for (const selection of [{ cur: {} }, { curObj: {} }, { curSpan: {} }, { multi: new Set([{}, {}]) }]) {
    Object.assign(context, { cur: null, curObj: null, curSpan: null, multi: new Set() }, selection);
    navigate(); assert.deepEqual(pages, []);
  }
  Object.assign(context, { multi: new Set() });
  navigate(); assert.deepEqual(pages, [0, 2, 0, 2, 2, 0]);
});

function harness(pendingSave = 0) {
  const listeners = new Map(), frames = new Map(), moves = [], calls = [], modifiers = [];
  let nextFrame = 0;
  const context = vm.createContext({
    toLayer: (x, y) => [x, y], pushUndo: () => calls.push('undo'),
    queueSave: () => calls.push('save'), props: reuse => calls.push(['props', reuse]),
    clearTimeout() {}, requestAnimationFrame: fn => { frames.set(++nextFrame, fn); return nextFrame; },
    cancelAnimationFrame: id => frames.delete(id),
    addEventListener: (type, fn) => listeners.set(type, fn),
    removeEventListener: type => listeners.delete(type),
  });
  vm.runInContext(`let saveT = ${pendingSave}, dragging = false; ${source}; globalThis.start = drag;`, context);
  const event = (type, x, y, extra = {}) => ({ type, clientX: x, clientY: y, pointerId: 7, preventDefault() {}, stopPropagation() {}, ...extra });
  return {
    moves, calls, frames, listeners, event, modifiers,
    start: () => context.start(event('pointerdown', 10, 20), () => {}, (point, start, e) => {
      moves.push({ point: [...point], shift: e.shiftKey });
      modifiers.push([e.shiftKey, e.altKey, e.ctrlKey, e.metaKey].map(Boolean));
    }),
    emit: (type, x, y, extra) => listeners.get(type)(event(type, x, y, extra)),
    frame: () => { const pending = [...frames.values()]; frames.clear(); pending.forEach(fn => fn()); },
  };
}

test('drag coalesces pointer bursts to one update per frame and commits the release position', () => {
  const h = harness(); h.start();
  for (let x = 11; x <= 100; x++) h.emit('pointermove', x, 30);
  assert.equal(h.moves.length, 0); assert.equal(h.frames.size, 1);
  h.frame();
  assert.deepEqual(h.moves, [{ point: [100, 30], shift: undefined }]);
  assert.equal(h.calls.filter(c => c[0] === 'props').length, 1);
  assert.equal(h.calls.filter(c => c === 'save').length, 0);
  h.emit('pointermove', 120, 40);
  h.emit('pointerup', 125, 45);
  assert.deepEqual(h.moves.at(-1).point, [125, 45]);
  assert.equal(h.frames.size, 0); assert.equal(h.listeners.size, 0);
  assert.equal(h.calls.filter(c => c === 'undo').length, 1);
  assert.equal(h.calls.filter(c => c === 'save').length, 1);
});

test('geometry controls update after each applied frame, not each pointer event', () => {
  const h = harness(); h.start();
  for (let frame = 1; frame <= 3; frame++) {
    for (let burst = 0; burst < 8; burst++) h.emit('pointermove', 10 + frame * 10 + burst, 30);
    assert.equal(h.calls.filter(c => c[0] === 'props').length, frame - 1);
    h.frame();
    assert.equal(h.calls.filter(c => c[0] === 'props').length, frame);
    assert.deepEqual(h.calls.at(-1), ['props', true]);
    assert.equal(h.calls.filter(c => c === 'save').length, 0);
  }
  h.emit('pointerup', 47, 30);
  assert.equal(h.calls.filter(c => c === 'save').length, 1);
});

test('plain selection and modifier keys without movement do not mutate or save', () => {
  const h = harness(); h.start();
  h.emit('keydown', 0, 0, { key: 'Shift', shiftKey: true }); h.frame();
  h.emit('pointerup', 10, 20);
  assert.equal(h.moves.length, 0);
  assert.deepEqual(h.calls, [['props', true]]);
});

test('drag modifiers reuse the last position and unrelated pointers do not end the gesture', () => {
  const h = harness(); h.start();
  h.emit('pointermove', 30, 40);
  h.emit('keydown', 0, 0, { key: 'Shift', shiftKey: true }); h.frame();
  assert.deepEqual(h.moves, [{ point: [30, 40], shift: true }]);
  h.emit('pointerup', 900, 900, { pointerId: 8 });
  assert.ok(h.listeners.has('pointermove'));
  h.emit('pointercancel', 0, 0);
  assert.deepEqual(h.moves.at(-1).point, [30, 40]);
  assert.equal(h.listeners.size, 0); assert.equal(h.frames.size, 0);
});

test('a pending save is resumed even when the new gesture is only a selection', () => {
  const h = harness(42); h.start();
  h.emit('pointerup', 10, 20);
  assert.equal(h.moves.length, 0);
  assert.equal(h.calls.filter(c => c === 'undo').length, 0);
  assert.equal(h.calls.filter(c => c === 'save').length, 1);
});

test('Ctrl and Command can toggle snapping mid-drag without losing Shift or Alt', () => {
  for (const [key, flag] of [['Control', 'ctrlKey'], ['Meta', 'metaKey']]) {
    const h = harness(); h.start();
    h.emit('pointermove', 60, 70, { shiftKey: true, altKey: true }); h.frame();
    h.emit('keydown', 0, 0, { key, [flag]: true, shiftKey: true, altKey: true }); h.frame();
    assert.deepEqual(h.modifiers.at(-1), [true, true, key === 'Control', key === 'Meta']);
    assert.deepEqual(h.moves.at(-1).point, [60, 70]);
    h.emit('keyup', 0, 0, { key, [flag]: false, shiftKey: true, altKey: true }); h.frame();
    assert.deepEqual(h.modifiers.at(-1), [true, true, false, false]);
    h.emit('pointerup', 60, 70);
    assert.equal(h.calls.filter(c => c === 'undo').length, 1);
  }
});

test('cancellation commits the last received position and removes all scheduled work', () => {
  const h = harness(); h.start();
  h.emit('pointermove', 60, 70);
  h.emit('pointercancel', -1, -1);
  assert.deepEqual(h.moves, [{ point: [60, 70], shift: undefined }]);
  assert.equal(h.calls.filter(c => c === 'save').length, 1);
  h.frame();
  assert.equal(h.moves.length, 1);
  assert.equal(h.listeners.size, 0);
});

test('unrelated pointer movement cannot replace the active gesture position', () => {
  const h = harness(); h.start();
  h.emit('pointermove', 60, 70, { pointerId: 8 }); h.frame();
  h.emit('pointerup', 10, 20);
  assert.equal(h.moves.length, 0);
  assert.deepEqual(h.calls, [['props', true]]);
});

test('geometry-only updates retain controls and skip unchanged values', () => {
  const writes = [], controls = ['x', 'y', 'w', 'h', 'size', 'text'].map(key => {
    let value = key === 'text' ? 'keep this text' : '0';
    return { dataset: { k: key }, get value() { return value; }, set value(v) { writes.push(key); value = String(v); } };
  });
  const item = { x: 12.345, y: 20, w: 80, h: 40, size: 14, text: 'keep this text' };
  const context = vm.createContext({
    $: () => ({ querySelectorAll: () => controls }),
    GEOM: new Set(['x', 'y', 'w', 'h']), cur: item, curObj: null, curSpan: null, locked: false, multi: new Set(), cropEdit: null,
  });
  const props = html.slice(html.indexOf('let propsTarget ='), html.indexOf('function updateBase()'));
  vm.runInContext(`${props}; propsTarget = cur; globalThis.sync = () => props(true);`, context);
  context.sync();
  assert.deepEqual(controls.map(c => c.value), ['12.35', '20', '80', '40', '14', 'keep this text']);
  assert.equal(writes.length, 5);
  context.sync();
  assert.equal(writes.length, 5);
  item.x = 50;
  context.sync();
  assert.equal(writes.length, 6);
  assert.equal(controls[0].value, '50');
});

function renderHarness({ current = true, cached = false } = {}) {
  const decodes = [], scheduled = [];
  const base = { naturalWidth: current ? 900 : 0, dataset: { loadedUrl: '/api/page/0.png?dpi=100&v=1&bare=true' } };
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const context = vm.createContext({
    $: id => id === 'base' ? base : { clientWidth: 1000, clientHeight: 1000 },
    state: { pages: [{ width: 612, height: 792, rotation: 0 }], version: 2 },
    host: { hasCachedImage: () => cached }, devicePixelRatio: 2,
    clearTimeout() {}, applyPageLayout() {},
    decodeBase: async (url, epoch) => {
      decodes.push(url);
      await pending;
      if (epoch !== context.epoch()) return false;
      base.dataset.loadedUrl = url;
      return true;
    },
    scheduleBaseRender: (url, epoch, delay) => scheduled.push({ url, delay }),
  });
  const build = html.slice(html.indexOf('async function build('), html.indexOf('// Client pixel position'));
  vm.runInContext(`let buildEpoch = 0, baseRenderTimer = 0, sel = 0, shownPage = ${current ? 0 : -1};
    let fitMode = '', zoom = 1, S = 1, preview = false;
    ${build}; globalThis.build = build; globalThis.epoch = () => buildEpoch;`, context);
  return { context, base, decodes, scheduled, release };
}

test('same-page edits keep the sharp image until a full-resolution replacement is ready', async () => {
  const h = renderHarness();
  const original = h.base.dataset.loadedUrl;
  const render = h.context.build(true);
  assert.equal(h.base.dataset.loadedUrl, original);
  assert.equal(h.decodes.length, 1);
  assert.ok(+new URL(h.decodes[0], 'https://editor.test').searchParams.get('dpi') > 40);
  assert.equal(h.decodes[0], h.base.dataset.targetUrl);
  assert.equal(h.scheduled.length, 0);
  h.release(); await render;
  assert.equal(h.base.dataset.loadedUrl, h.base.dataset.targetUrl);
});

test('layout changes retain the sharp image and debounce full-resolution rendering', async () => {
  const h = renderHarness();
  const original = h.base.dataset.loadedUrl;
  await h.context.build(false, 240);
  assert.equal(h.base.dataset.loadedUrl, original);
  assert.equal(h.decodes.length, 0);
  assert.equal(h.scheduled.length, 1);
  assert.equal(h.scheduled[0].url, h.base.dataset.targetUrl);
  assert.equal(h.scheduled[0].delay, 240);
});

test('first page visits still show a quick preview before sharpening', async () => {
  const h = renderHarness({ current: false });
  h.release(); await h.context.build();
  assert.equal(new URL(h.decodes[0], 'https://editor.test').searchParams.get('dpi'), '40');
  assert.equal(h.scheduled.length, 1);
  assert.equal(h.scheduled[0].url, h.base.dataset.targetUrl);
});

test('cached first visits go straight to the target resolution', async () => {
  const h = renderHarness({ current: false, cached: true });
  h.release(); await h.context.build();
  assert.equal(h.decodes[0], h.base.dataset.targetUrl);
  assert.equal(h.scheduled.length, 0);
});

test('a superseded edit cannot replace the image from a newer build', async () => {
  const h = renderHarness();
  const original = h.base.dataset.loadedUrl;
  const oldRender = h.context.build(true);
  await h.context.build(false);
  h.release(); await oldRender;
  assert.equal(h.base.dataset.loadedUrl, original);
  assert.equal(h.scheduled.length, 1);
});
