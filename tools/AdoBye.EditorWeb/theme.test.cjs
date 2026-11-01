const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const html = fs.readFileSync(path.join(__dirname, 'wwwroot', 'index.html'), 'utf8');
const ui = fs.readFileSync(path.join(__dirname, 'wwwroot', 'ui.js'), 'utf8');
const css = fs.readFileSync(path.join(__dirname, 'wwwroot', 'surface.css'), 'utf8');
const head = html.match(/<script>(try\{var d=document.documentElement.*?)<\/script>/s)[1];
const colors = ui.slice(ui.indexOf('  const COLORS ='), ui.indexOf('  // Focus handling'));

function boot(saved = {}) {
  const values = new Map(Object.entries(saved)), doc = { dataset: {} }, meta = { content: '#be185d' };
  const localStorage = { getItem: k => values.get(k) ?? null, setItem: (k, v) => values.set(k, v), removeItem: k => values.delete(k) };
  const context = vm.createContext({
    doc, document: { documentElement: doc, querySelector: () => meta }, localStorage,
    q: () => meta, store: { get: localStorage.getItem, set: (k, v) => v == null ? localStorage.removeItem(k) : localStorage.setItem(k, v) },
    window: {}, toast() {}, say() {}, setTheme() {}, setMotion() {}, setFullMotion() {},
  });
  vm.runInContext(head, context);
  const before = { ...doc.dataset, meta: meta.content };
  vm.runInContext(colors, context);
  return { api: context.window.editorUi, doc, meta, values, before };
}

test('primary and accent shade persist, reset and apply before paint', () => {
  let state = boot();
  assert.equal(state.api.primary(), 'pink');
  assert.equal(state.api.accent(), 'medium');
  state.api.setPrimary('magenta');
  state.api.setAccent('dark');
  assert.equal(state.api.primary(), 'magenta');
  state = boot(Object.fromEntries(state.values));
  assert.deepEqual(state.before, { primary: 'magenta', accent: 'dark', meta: '#a21caf' });
  state.api.setPrimary('cyan');
  assert.equal(state.api.accent(), 'dark');
  state.api.setPrimary('pink');
  state.api.setAccent('medium');
  assert.equal(state.values.has('editor.primary'), false);
  assert.equal(state.values.has('editor.accent'), false);
  assert.equal(state.meta.content, '#be185d');
});

test('legacy accent keeps the primary and migrates to Medium without overriding later choices', () => {
  let state = boot({ 'editor.accent': 'blue' });
  assert.deepEqual(state.before, { primary: 'blue', meta: '#2563eb' });
  assert.equal(state.api.primary(), 'blue');
  state.api.setPrimary('magenta');
  state = boot(Object.fromEntries(state.values));
  assert.equal(state.api.primary(), 'magenta');
  assert.equal(state.api.accent(), 'medium');
  state = boot({ 'editor.accent': 'blue', 'editor.primary': 'magenta' });
  assert.equal(state.before.primary, 'magenta');
  assert.equal(state.api.primary(), 'magenta');
});

test('invalid stored color names never become CSS or metadata values', () => {
  for (const value of ['invalid', '__proto__', 'constructor']) {
    const state = boot({ 'editor.accent': value, 'editor.primary': value });
    assert.deepEqual(state.before, { meta: '#be185d' });
    assert.equal(state.api.primary(), 'pink');
    assert.equal(state.api.accent(), 'medium');
    assert.equal(state.meta.content, '#be185d');
  }
});

function luminance(hex) {
  const rgb = hex.match(/[0-9a-f]{2}/gi).map(x => parseInt(x, 16) / 255);
  return rgb.map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4)
    .reduce((sum, x, i) => sum + x * [.2126, .7152, .0722][i], 0);
}

test('every primary and hover preset supports white text at WCAG AA', () => {
  for (const [, name, hex] of css.matchAll(/--(primary(?:-hover)?):\s*(#[0-9a-f]{6})/g))
    assert.ok(1.05 / (luminance(hex) + .05) >= 4.5, `${name}: ${hex}`);
});
