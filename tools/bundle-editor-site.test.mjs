import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import vm from 'node:vm';

const script = new URL('./bundle-editor-site.mjs', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const fixtures = [];
test.afterEach(() => {
  for (const dir of fixtures.splice(0)) rmSync(dir, { recursive: true, force: true });
});

function fixture(extra = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'bundle-test-'));
  fixtures.push(dir);
  const files = {
    'index.html': '<!doctype html><html><head>\n<link rel="icon" href="favicon.svg" type="image/svg+xml">\n<link rel="icon" href="favicon-32.png" type="image/png">\n<link rel="alternate icon" href="favicon.ico">\n<link rel="apple-touch-icon" href="apple-touch-icon.png">\n<link rel="manifest" href="site.webmanifest">\n<link rel="stylesheet" href="editor.css">\n<link rel="stylesheet" href="theme.css">\n<script src="editor.js" defer></script>\n<script src="ui.js" defer></script>\n</head><body><script>\nasync function later() { const m = await import(new URL(\'panel.mjs\', document.baseURI)); return m.render(); }\n</script></body></html>',
    'editor.css': 'body{margin:0}', 'theme.css': ':root{--a:1}',
    'editor.js': "async function startEditor() { const { start } = await import(new URL('host.js', document.baseURI)); return start; }",
    'ui.js': 'function ui() {}',
    'host.js': "export async function start() { const { stamp } = await import('./sw-client.js'); return stamp; }",
    'sw-client.js': 'export const stamp = 7;',
    'panel.mjs': "import { project } from './map.mjs';\nexport function render() { return project(2); }",
    'map.mjs': 'export const project = n => n * 21;\nconst hidden = 1;\nexport { hidden as visible };',
    'favicon.svg': '<svg/>', 'favicon-32.png': 'png', 'favicon.ico': 'ico', 'apple-touch-icon.png': 'png',
    'icon-192.png': 'png', 'icon-512.png': 'png',
    'site.webmanifest': JSON.stringify({ icons: [{ src: 'icon-192.png' }, { src: 'icon-512.png' }, { src: 'favicon.svg' }] }),
    ...extra,
  };
  for (const [name, text] of Object.entries(files)) writeFileSync(join(dir, name), text);
  return dir;
}
const bundle = dir => execFileSync(process.execPath, [script, dir, '--no-minify'], { stdio: 'pipe' }).toString();

test('discovers lazily imported modules (including .mjs with static imports) and resolves them through the registry', async () => {
  const dir = fixture();
  assert.match(bundle(dir), /host\.js \(4 modules\)/);
  for (const gone of ['panel.mjs', 'map.mjs', 'sw-client.js', 'ui.js', 'theme.css', 'apple-touch-icon.png', 'icon-192.png', 'favicon.ico'])
    assert.equal(existsSync(join(dir, gone)), false, gone + ' should not be published separately');
  const window = {};
  let ready = false;
  window.__editorModulesReady = () => { ready = true; };
  vm.runInNewContext(readFileSync(join(dir, 'host.js'), 'utf8'), { window, Promise, Error });
  assert.ok(ready);
  assert.equal(window.__editorModules('panel.mjs').render(), 42);
  assert.equal(window.__editorModules('map.mjs').visible, 1);
  assert.equal(await window.__editorModules('host.js').start(), 7);
  const html = readFileSync(join(dir, 'index.html'), 'utf8');
  assert.match(html, /__editorImport\('panel\.mjs'\)/);
  assert.doesNotMatch(html, /import\(new URL/);
  assert.match(html, /<script type="module" src="host\.js"/);
  assert.deepEqual(JSON.parse(readFileSync(join(dir, 'site.webmanifest'), 'utf8')).icons, [{ src: 'icon-512.png', sizes: '192x192 512x512' }]);
});

test('fails the publish instead of shipping a partial bundle', () => {
  assert.throws(() => bundle(fixture({ 'map.mjs': 'export default 1;' })), /unhandled import\/export/);
  assert.throws(() => bundle(fixture({ 'map.mjs': 'await Promise.resolve();\nexport const project = 1;' })), /top-level await/);
  assert.throws(() => bundle(fixture({ 'theme.css': "a{background:url('x.png')}" })), /url\(\)/);
  assert.throws(() => bundle(fixture({ 'panel.mjs': "import { project } from './missing.mjs';\nexport const render = project;" })), /not part of the published site/);
});

test('discovers document-relative dynamic imports inside a bundled member', async () => {
  const dir = fixture({
    'map.mjs': "export async function project(n) { const m = await import(new URL('nested.mjs', document.baseURI)); return m.multiply(n); }",
    'nested.mjs': 'export const multiply = n => n * 21;',
  });
  assert.match(bundle(dir), /host\.js \(5 modules\)/);
  assert.equal(existsSync(join(dir, 'nested.mjs')), false);
  const window = { __editorModulesReady() {} };
  vm.runInNewContext(readFileSync(join(dir, 'host.js'), 'utf8'), { window, Promise, Error });
  assert.equal(await window.__editorModules('panel.mjs').render(), 42);
});

test('bundles sibling dynamic imports from inline page scripts', () => {
  const dir = fixture();
  writeFileSync(join(dir, 'index.html'), readFileSync(join(dir, 'index.html'), 'utf8')
    .replace("import(new URL('panel.mjs', document.baseURI))", "import('./panel.mjs')"));
  assert.match(bundle(dir), /host\.js \(4 modules\)/);
  assert.equal(existsSync(join(dir, 'panel.mjs')), false);
  assert.equal(existsSync(join(dir, 'map.mjs')), false);
  const html = readFileSync(join(dir, 'index.html'), 'utf8');
  assert.match(html, /__editorImport\('panel\.mjs'\)/);
  assert.doesNotMatch(html, /import\('\.\//);
});
