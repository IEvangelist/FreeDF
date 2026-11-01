const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const step = html.slice(html.indexOf('function pageHistoryStep('), html.indexOf('const oBox ='));
const undoFn = html.slice(html.indexOf('async function doUndo()'), html.indexOf('\n  const s = JSON.parse(undo.pop())'));

function harness(state) {
  const requests = [];
  const messages = [];
  const context = vm.createContext({
    status: message => messages.push(message), reportError: e => messages.push('error:' + e.message),
    post: async url => { requests.push(url); return { json: async () => ({ label: 'delete', undo: 0, redo: 1 }) }; },
    act: async (fn, onError) => { try { await fn(); } catch (e) { onError(e); } },
  });
  vm.runInContext(`let state = ${JSON.stringify(state)}, undo = [];
    ${step}\n${undoFn}\n}
    globalThis.pageHistoryStep = pageHistoryStep; globalThis.doUndo = doUndo;`, context);
  return { ...context, requests, messages };
}

test('page undo and redo post to the history endpoints and announce the result', async () => {
  const h = harness({ pageUndo: 2, pageRedo: 1 });
  await h.pageHistoryStep('undo');
  await h.pageHistoryStep('redo');
  assert.deepEqual(h.requests, ['/api/page-undo', '/api/page-redo']);
  assert.deepEqual(h.messages, ['Undid delete page change.', 'Redid delete page change.']);
});

test('empty page history announces instead of calling the server', async () => {
  const h = harness({ pageUndo: 0, pageRedo: 0 });
  await h.pageHistoryStep('undo');
  await h.pageHistoryStep('redo');
  assert.deepEqual(h.requests, []);
  assert.deepEqual(h.messages, ['Nothing to undo.', 'Nothing to redo.']);
});

test('item undo is exhausted before page undo is used', async () => {
  const h = harness({ pageUndo: 1, pageRedo: 0 });
  await h.doUndo();
  assert.deepEqual(h.requests, ['/api/page-undo']);
});
