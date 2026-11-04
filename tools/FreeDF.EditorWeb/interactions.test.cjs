const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const source = html.slice(html.indexOf('function drag(ev,'), html.indexOf('// Applies Shift'));
function harness(pendingSave = 0) {
  const listeners = new Map(), frames = new Map(), moves = [], calls = [];
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
    moves, calls, frames, listeners, event,
    start: () => context.start(event('pointerdown', 10, 20), () => {}, (point, start, e) => moves.push({ point: [...point], shift: e.shiftKey })),
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
  h.emit('pointermove', 120, 40);
  h.emit('pointerup', 125, 45);
  assert.deepEqual(h.moves.at(-1).point, [125, 45]);
  assert.equal(h.frames.size, 0); assert.equal(h.listeners.size, 0);
  assert.equal(h.calls.filter(c => c === 'undo').length, 1);
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
