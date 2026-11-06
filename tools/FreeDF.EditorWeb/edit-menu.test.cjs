const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');

function historyHarness() {
  const saved = [], pageSteps = [], errors = [];
  const context = vm.createContext({
    reportError: e => errors.push(e), paint() {}, props() {}, status() {},
    widgetState: () => ({}), saveObj: async () => {}, setField: async () => {},
    pageHistoryStep: kind => pageSteps.push(kind), clearTimeout() {},
    request: async (url, options) => { saved.push(JSON.parse(options.body)); },
    document: { querySelectorAll: () => [] }, refreshDirty: async () => {},
    pendingUploads: new Map(), pruneAssetImages() {}, okey: () => '', updateBase() {},
  });
  const history = html.slice(html.indexOf('const historySnapshot ='), html.indexOf('function pageHistoryStep('));
  const flush = html.slice(html.indexOf('async function flush()'), html.indexOf('let propsTarget ='));
  vm.runInContext(`
    let items = [], objs = [], undo = [], redo = [], cur = null, curObj = null, curSpan = null;
    const multi = new Set(); let cropEdit = null;
    let fieldSave = Promise.resolve(), objectSave = Promise.resolve(), itemSave = Promise.resolve();
    const objectSaveErrors = new Map(), state = {pageCount:1}, clip = null;
    let saveT = 0, sel = 0, origKey = '', widgetStamp = 0;
    function queueSave() { saveT = 1; }
    ${history}
    ${flush}
    globalThis.push = pushUndo; globalThis.back = doUndo; globalThis.forward = doRedo; globalThis.flush = flush;
    globalThis.setItems = value => items = value;
    globalThis.getItems = () => JSON.parse(JSON.stringify(items));
    globalThis.stacks = () => [undo.length, redo.length];
  `, context);
  return { context, saved, pageSteps, errors };
}

test('object creation, deletion and geometry changes have matching undo and redo', async () => {
  const { context: h, errors } = historyHarness();
  h.push(); h.setItems([{ id: 'rect', x: 10 }]);
  h.push(); h.setItems([{ id: 'rect', x: 50 }]);
  h.push(); h.setItems([]);
  await h.back();
  assert.equal(h.getItems()[0].x, 50);
  await h.back();
  assert.equal(h.getItems()[0].x, 10);
  await h.forward();
  assert.equal(h.getItems()[0].x, 50);
  await h.forward();
  assert.equal(h.getItems().length, 0);
  assert.equal(errors.length, 0);
});

test('a new edit clears redo and repeated history commands remain ordered', async () => {
  const { context: h } = historyHarness();
  for (let x = 1; x <= 3; x++) { h.push(); h.setItems([{ x }]); }
  await Promise.all([h.back(), h.back(), h.forward()]);
  assert.equal(h.getItems()[0].x, 2);
  h.push(); h.setItems([{ x: 9 }]);
  assert.equal(h.stacks()[1], 0);
  await h.back();
  assert.equal(h.getItems()[0].x, 2);
});

test('empty object stacks fall back to existing page/form history', async () => {
  const { context: h, pageSteps } = historyHarness();
  await h.back(); await h.forward();
  assert.deepEqual(pageSteps, ['undo', 'redo']);
});

test('image bytes referenced only by redo are included in saved history assets', async () => {
  const { context: h, saved } = historyHarness();
  h.push(); h.setItems([{ type: 'image', asset: 'redo-only-image' }]);
  await h.back();
  assert.equal(h.getItems().length, 0);
  await h.flush();
  assert.deepEqual(saved[0].historyAssets, ['redo-only-image']);
  await h.forward();
  assert.equal(h.getItems()[0].asset, 'redo-only-image');
});

function menuHarness(values = {}) {
  const context = vm.createContext({
    state: { pageCount: 1 }, undo: [], redo: [], cur: null, curObj: null, curSpan: null,
    locked: false, clip: null, formUi: null, mod: 'Ctrl+', items: [], multi: new Set(),
    $: () => ({}), doUndo() {}, doRedo() {}, deselect() {}, openFind() {},
    ...values,
  });
  const menus = html.slice(html.indexOf('function pageAlignmentEntry('), html.indexOf('function menuFor('));
  vm.runInContext(`${menus}; globalThis.entries = editMenuEntries;`, context);
  return context;
}

test('Edit exposes history and disables unavailable document/selection commands', () => {
  const h = menuHarness({ state: { pageCount: 0 } });
  const entries = h.entries().filter(e => e !== '-');
  for (const label of ['Undo', 'Redo', 'Cut', 'Copy', 'Paste', 'Duplicate', 'Delete', 'Properties', 'Deselect', 'Find…'])
    assert.equal(entries.find(e => e.label === label).disabled, true, label);
});

test('overlay menus share contextual clipboard, ordering and locked alignment actions', () => {
  const item = { type: 'rect' }, h = menuHarness({ cur: item, items: [item], locked: true, clip: item, undo: ['snapshot'] });
  const entries = h.entries().filter(Boolean);
  assert.equal(entries.find(e => e.label === 'Undo').disabled, false);
  assert.equal(entries.find(e => e.label === 'Paste').disabled, false);
  assert.equal(entries.find(e => e.label === 'Align to page').disabled, true);
  assert.equal(entries.find(e => e.label === 'Arrange').sub[0].disabled, true);
  assert.equal(entries.find(e => e.label === 'Copy').disabled, undefined);
});

test('original text and objects expose only their supported editing actions', () => {
  const h = menuHarness({ curObj: { edit: null } });
  let entries = h.entries().filter(Boolean);
  assert.equal(entries.find(e => e.label === 'Reset object').disabled, true);
  assert.equal(entries.find(e => e.label === 'Align to page').sub.filter(e => e !== '-').length, 6);
  assert.equal(entries.some(e => e.label === 'Copy'), false);
  h.curObj = null; h.curSpan = {};
  entries = h.entries().filter(Boolean);
  assert.equal(entries.some(e => e.label === 'Edit text'), true);
  assert.equal(entries.some(e => e.label === 'Align to page'), true);
});

test('multi-selection exposes union alignment without misleading single-object actions', () => {
  const h = menuHarness({ multi: new Set([{}, {}]) }), entries = h.entries();
  for (const label of ['Cut', 'Copy', 'Duplicate', 'Delete']) assert.equal(entries.find(e => e?.label === label).disabled, true, label);
  assert.equal(entries.find(e => e?.label === 'Align to page').sub.filter(e => e !== '-').length, 6);
  assert.equal(entries.find(e => e?.label === 'Deselect').disabled, false);
});

function alignmentHarness(values = {}) {
  const layer = { children: [] }, snapshots = [], saved = [], messages = [];
  let queued = 0;
  const h = vm.createContext({
    locked: false, items: [], objs: [], spans: [], cur: null, curObj: null, curSpan: null,
    geom: { Lw: 1200, Lh: 1600 }, S: 2,
    $: () => layer, props() {}, status: message => messages.push(message),
    pushUndo: () => snapshots.push(JSON.stringify({ items: h.items, edits: h.objs.map(o => o.edit) })),
    queueSave: () => queued++, saveObj: o => saved.push(o),
    paint: () => { layer.children = h.items.filter(it => it.type === 'text').map(it => ({ _it: it, offsetWidth: Math.max(it.w, it.size * it.text.length * .5) * h.S, offsetHeight: it.size * 1.2 * h.S })); },
    fontFamily: () => 'Helvetica', crypto: { randomUUID: () => 'converted-text' },
    ...values,
  });
  const bounds = html.slice(html.indexOf('const bboxOf ='), html.indexOf('function commit('));
  const original = html.slice(html.indexOf('const oBox ='), html.indexOf('let objectSave ='));
  const span = html.slice(html.indexOf('function spanItem('), html.indexOf('// Only actual edits'));
  const align = html.slice(html.indexOf('function alignmentDelta('), html.indexOf('function paste('));
  const translate = html.slice(html.indexOf('function translateObject('), html.indexOf('function translateSelection('));
  vm.runInContext(`${bounds}\n${original}\n${span}\n${translate}\n${align}\nglobalThis.align = alignTo; globalThis.bounds = oBox;`, h);
  return { h, snapshots, saved, messages, queued: () => queued };
}

test('all six page alignments position overlays without resizing or moving the other axis', () => {
  for (const [mode, x, y] of [['left', 0, 40], ['center', 260, 40], ['right', 520, 40], ['top', 20, 0], ['middle', 20, 375], ['bottom', 20, 750]]) {
    const item = { type: 'rect', x: 20, y: 40, w: 80, h: 50 };
    const { h, snapshots, queued } = alignmentHarness({ items: [item] });
    h.align(item, mode);
    assert.deepEqual(item, { type: 'rect', x, y, w: 80, h: 50 }, mode);
    assert.equal(snapshots.length, 1, mode);
    assert.equal(queued(), 1, mode);
  }
});

test('page alignment translates both endpoints of reversed lines', () => {
  const line = { type: 'line', x: 180, y: 90, x2: 80, y2: 40 };
  const { h } = alignmentHarness({ items: [line] });
  h.align(line, 'right');
  assert.deepEqual(line, { type: 'line', x: 600, y: 90, x2: 500, y2: 40 });
});

test('all six original-object alignments preserve scaling, mirroring and appearance', () => {
  for (const [mode, x, y] of [['left', 0, 85], ['center', 260, 85], ['right', 520, 85], ['top', 80, 0], ['middle', 80, 370], ['bottom', 80, 740]]) {
    const object = { x0: 20, y0: 30, x1: 60, y1: 50, edit: { sx: -2, sy: 3, tx: 200, ty: -5, fill: '#123456' } };
    const { h, snapshots, saved, queued } = alignmentHarness({ objs: [object] });
    h.align(object, mode);
    assert.deepEqual([...h.bounds(object)], [x, y, x + 80, y + 60], mode);
    assert.equal(object.edit.sx, -2);
    assert.equal(object.edit.sy, 3);
    assert.equal(object.edit.fill, '#123456');
    assert.equal(snapshots.length, 1);
    assert.equal(saved[0], object);
    assert.equal(queued(), 0);
  }
});

test('aligning original text promotes it once, retaining its original font and undo snapshot', () => {
  const span = { index: 7, text: 'Original text', font: 'EmbeddedFont', x: 20, y: 100, width: 120, size: 16, color: '#123456' };
  const { h, snapshots, queued } = alignmentHarness({ spans: [span], curSpan: span });
  h.align(span, 'center');
  assert.equal(h.items.length, 1);
  assert.equal(h.items[0].x, 240);
  assert.equal(h.items[0].y, 84.8);
  assert.equal(h.items[0].orig, 7);
  assert.equal(h.items[0].originalFont, 'EmbeddedFont');
  assert.equal(h.cur, h.items[0]);
  assert.equal(h.curSpan, null);
  assert.equal(snapshots.length, 1);
  assert.deepEqual(JSON.parse(snapshots[0]).items, []);
  assert.equal(queued(), 1);
});

test('locked alignment leaves original text and history untouched', () => {
  const span = { index: 7 };
  const { h, snapshots, saved, queued, messages } = alignmentHarness({ spans: [span], locked: true });
  h.align(span, 'center');
  assert.equal(h.items.length, 0);
  assert.equal(snapshots.length, 0);
  assert.equal(saved.length, 0);
  assert.equal(queued(), 0);
  assert.deepEqual(messages, ['Positions and sizes are locked.']);
});

test('recovery validates optional redo while retaining compatibility with older records', () => {
  const source = readFileSync(join(__dirname, '..', 'FreeDF.EditorWasm', 'wwwroot', 'recovery.js'), 'utf8');
  const context = vm.createContext({});
  vm.runInContext(source.slice(source.indexOf('export function validateUi'), source.indexOf('export async function createRecovery')).replace('export ', '') + '; globalThis.validate = validateUi;', context);
  const ui = { sel: 0, zoom: 1, preview: false, fitMode: 'page', undo: [], clip: null };
  context.validate(ui);
  context.validate({ ...ui, redo: [JSON.stringify({ items: [], ed: [] })] });
  assert.throws(() => context.validate({ ...ui, redo: 'bad' }), /corrupt/);
  assert.throws(() => context.validate({ ...ui, redo: [JSON.stringify({ items: [] })] }), /corrupt/);
});
