const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const source = (from, to) => html.slice(html.indexOf(from), html.indexOf(to));

function harness(values = {}) {
  const frames = new Map(), listeners = new Map(), snapshots = [], saved = [], messages = [];
  const layer = { children: [], dataset: {}, querySelector: () => null };
  let queued = 0, sequence = 0;
  const h = vm.createContext({
    multi: new Set(), items: [], objs: [], spans: [], cur: null, curObj: null, curSpan: null,
    grid: { snap: false, spacing: 12 },
    formUi: { clear() {} }, locked: false, dragging: false, saveT: 0, S: 2, geom: { Lw: 1200, Lh: 1600 },
    $: () => layer, paint() {}, props() {}, paintItem() {}, fontFamily: () => 'Helvetica',
    crypto: { randomUUID: () => 'promoted' }, status: message => messages.push(message),
    pushUndo: () => snapshots.push(JSON.stringify({ items: h.items, edits: h.objs.map(o => o.edit) })),
    queueSave: () => queued++, saveObj: o => saved.push(o),
    clearTimeout() {}, toLayer: (x, y) => [x, y],
    addEventListener: (name, callback) => listeners.set(name, callback),
    removeEventListener: name => listeners.delete(name),
    requestAnimationFrame: callback => { frames.set(++sequence, callback); return sequence; },
    cancelAnimationFrame: id => frames.delete(id),
    ...values,
  });
  vm.runInContext([
    source('function snapPoint(', 'function paintGrid('),
    source('function spanItem(', '// Only actual edits'),
    source('const bboxOf =', 'function commit('),
    source('const oBox =', 'let objectSave ='),
    source('function toggleSelection(', 'function focusTextProperties('),
    source('function drag(', '// Applies Shift'),
    source('function alignmentDelta(', 'function paste('),
  ].join('\n'), h);
  const event = (type, x = 0, y = 0, extra = {}) => ({ type, clientX: x, clientY: y, button: 0, pointerId: 7, preventDefault() {}, stopPropagation() {}, ...extra });
  return {
    h, snapshots, saved, messages, layer, queued: () => queued,
    down: (target, extra) => h.multiPointerDown(event('pointerdown', 20, 40, extra), target),
    emit: (type, x, y, extra) => listeners.get(type)?.(event(type, x, y, extra)),
    frame: () => { const callbacks = [...frames.values()]; frames.clear(); callbacks.forEach(callback => callback()); },
  };
}

test('adding and removing selection never changes PDF data, saves or undo history', () => {
  const item = { type: 'rect', x: 10, y: 20, w: 40, h: 30 }, span = { index: 4, text: 'Original', x: 50, y: 60, width: 40, size: 12 };
  const { h, snapshots, saved, queued, down, emit } = harness({ items: [item], spans: [span], cur: item });
  const before = JSON.stringify([item, span]);
  down(span, { shiftKey: true });
  emit('pointerup', 20, 40, { shiftKey: true });
  assert.equal(h.multi.size, 2);
  assert.equal(h.cur, null);
  down(span, { shiftKey: true });
  emit('pointerup', 20, 40, { shiftKey: true });
  assert.equal(h.multi.size, 0);
  assert.equal(h.cur, item);
  assert.equal(h.items.length, 1);
  assert.equal(JSON.stringify([item, span]), before);
  assert.equal(snapshots.length + saved.length + queued(), 0);
});

test('a PDF graphic group and its child or text cannot be translated twice', () => {
  const parent = { kind: 'form', formRef: 8 }, child = { kind: 'path', parentForm: 8 };
  const text = { index: 80000003 }, overlay = { orig: 80000003, type: 'text' };
  for (const nested of [child, text, overlay]) {
    for (const [first, second] of [[parent, nested], [nested, parent]]) {
      const { h } = harness({ objs: [parent, child], spans: [text], items: [overlay] });
      h.toggleSelection(first); h.toggleSelection(second);
      assert.equal(h.multi.size, 0);
      assert.equal(h.cur || h.curObj || h.curSpan, second);
    }
  }
});

test('mixed movement preserves transforms and line direction and promotes text only on movement', () => {
  const line = { type: 'line', x: 80, y: 70, x2: 20, y2: 30 };
  const object = { x0: 20, y0: 30, x1: 80, y1: 70, edit: { sx: -2, sy: 1.5, tx: 200, ty: 15, rot: 15, fill: '#abcdef' } };
  const span = { index: 2, text: 'Original', x: 40, y: 80, width: 48, size: 12, font: 'CustomEmbedded', color: '#000000' };
  const { h, snapshots, saved, queued } = harness({ items: [line], objs: [object], spans: [span], multi: new Set([line, object, span]) });
  h.moveSelection(0, 0);
  assert.equal(h.items.length, 1);
  h.moveSelection(10, -5);
  assert.deepEqual(line, { type: 'line', x: 90, y: 65, x2: 30, y2: 25 });
  assert.deepEqual(JSON.parse(JSON.stringify(object.edit)), { sx: -2, sy: 1.5, tx: 210, ty: 10, rot: 15, fill: '#abcdef' });
  assert.equal(h.items[1].orig, 2);
  assert.equal(h.items[1].originalFont, 'CustomEmbedded');
  assert.equal(h.items[1].x, 50);
  assert.equal(h.items[1].y, 80 - .95 * 12 - 5);
  assert.equal(h.multi.has(span), false);
  assert.equal(h.multi.has(h.items[1]), true);
  assert.equal(snapshots.length, 1);
  assert.equal(JSON.parse(snapshots[0]).items.length, 1);
  assert.equal(saved[0], object);
  assert.equal(queued(), 1);
});

test('all six page alignments move union bounds without collapsing spacing or resizing', () => {
  for (const [mode, x, y] of [['left', 0, 30], ['center', 220, 30], ['right', 440, 30], ['top', 20, 0], ['middle', 20, 340], ['bottom', 20, 680]]) {
    const a = { type: 'rect', x: 20, y: 30, w: 40, h: 20 }, b = { type: 'ellipse', x: 140, y: 120, w: 40, h: 30 };
    const { h, snapshots } = harness({ items: [a, b], multi: new Set([a, b]) });
    h.alignTo([a, b], mode);
    assert.deepEqual(JSON.parse(JSON.stringify(h.selectionBox())), { x, y, w: 160, h: 120 }, mode);
    assert.equal(b.x - a.x, 120);
    assert.equal(b.y - a.y, 90);
    assert.equal(snapshots.length, 1);
  }
});

test('group drag batches movement, saves only after release and records one undo snapshot', () => {
  const a = { type: 'rect', x: 10, y: 20, w: 40, h: 30 }, b = { x0: 60, y0: 30, x1: 90, y1: 80 };
  const { h, snapshots, saved, queued, down, emit, frame } = harness({ items: [a], objs: [b], multi: new Set([a, b]) });
  down(a);
  emit('pointermove', 60, 60); emit('pointermove', 80, 80);
  assert.equal(a.x, 10);
  frame();
  assert.equal(a.x, 40);
  assert.equal(b.edit.tx, 30);
  emit('pointermove', 100, 100); frame();
  assert.equal(saved.length + queued(), 0);
  emit('pointerup', 120, 120);
  assert.equal(a.x, 60);
  assert.equal(a.y, 60);
  assert.equal(b.edit.tx, 50);
  assert.equal(b.edit.ty, 40);
  assert.equal(snapshots.length, 1);
  assert.equal(saved.length, 1);
  assert.ok(queued() > 0);
});

test('Shift-click removes a member only on release and Shift-drag preserves the group and fixed axis', () => {
  const a = { type: 'rect', x: 10, y: 20, w: 40, h: 30 }, b = { type: 'rect', x: 80, y: 90, w: 10, h: 10 };
  const { h, snapshots, down, emit } = harness({ items: [a, b], multi: new Set([a, b]) });
  down(a, { shiftKey: true });
  assert.equal(h.multi.size, 2);
  emit('pointerup', 20, 40, { shiftKey: true });
  assert.equal(h.multi.size, 0);
  assert.equal(h.cur, b);
  assert.equal(snapshots.length, 0);
  h.toggleSelection(a);
  h.grid.snap = true;
  down(a, { shiftKey: true });
  emit('pointerup', 50, 44, { shiftKey: true });
  assert.equal(h.multi.size, 2);
  assert.equal(a.x, 24);
  assert.equal(a.y, 20);
  assert.equal(b.x - a.x, 70);
  assert.equal(b.y - a.y, 70);
  assert.equal(snapshots.length, 1);
});

test('clicking a selected group member is selection-only and locked groups do not move', () => {
  const a = { type: 'rect', x: 10, y: 20, w: 40, h: 30 }, b = { type: 'rect', x: 80, y: 90, w: 10, h: 10 };
  const { h, snapshots, saved, queued, down, emit } = harness({ items: [a, b], multi: new Set([a, b]) });
  down(a); emit('pointerup', 20, 40);
  assert.equal(h.multi.size, 2);
  h.locked = true; h.moveSelection(5, 6); h.alignTo([a, b], 'center'); down(a); emit('pointermove', 80, 90);
  assert.equal(a.x, 10);
  assert.equal(snapshots.length + saved.length + queued(), 0);
});

test('unsupported whole-group/child combinations fail before any mutation or save', () => {
  const parent = { kind: 'form', formRef: 8 }, otherChild = { kind: 'path', inForm: true, parentForm: 9 };
  const { h, snapshots, saved, queued, messages, down } = harness({ objs: [parent, otherChild], multi: new Set([parent, otherChild]) });
  h.moveSelection(10, 10); h.alignTo([parent, otherChild], 'center'); down(parent);
  assert.equal(parent.edit, undefined);
  assert.equal(otherChild.edit, undefined);
  assert.equal(snapshots.length + saved.length + queued(), 0);
  assert.equal(messages.length, 3);
});
