// Publish-time bundling of the static editor shell (published output only; sources keep their module layout).
//   editor.css  <- editor.css + theme.css + surface.css (link order)
//   editor.js   <- editor.js + ui.js (classic scripts, document order)
//   host.js     <- host/pool/scheduler/sw-client/recovery/annotations/form-fields/samples as one module whose
//                  members are evaluated on first use (same laziness as the separate files had)
//   index.html  <- favicons inlined as data URIs; dynamic import(new URL('x.js', document.baseURI)) calls resolve
//                  through window.__editorImport against the bundle. worker.js, replica-worker.js, sw.js stay separate.
// Anything unexpected (unknown import/export forms, unreadable assets) fails the publish instead of shipping half a bundle.
import { readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';

const [site, ...flags] = process.argv.slice(2);
const minifying = !flags.includes('--no-minify');
let esbuild = null;
if (minifying) {
  try { esbuild = await import('esbuild'); }
  catch { throw new Error('Bundle: esbuild is not installed. Run "npm ci" in tools/ (or pass --no-minify).'); }
}
if (!site) throw new Error('Usage: node bundle-editor-site.mjs <published-wwwroot>');
const read = name => readFileSync(join(site, name), 'utf8');
const write = (name, text) => writeFileSync(join(site, name), text);
const fail = message => { throw new Error('Bundle: ' + message); };

let html = read('index.html');

// CSS, in the order the page linked it.
const cssLinks = [...html.matchAll(/<link rel="stylesheet" href="([^"]+\.css)">\n?/g)];
if (cssLinks.length < 2) fail('expected the editor stylesheets to be linked from index.html.');
const css = cssLinks.map(match => match[1]);
if (css[0] !== 'editor.css') fail('editor.css must be the first stylesheet.');
const cssText = css.map(name => {
  const text = read(name);
  if (/@import|url\(\s*(?!['"]?(?:data:|#|%23))/i.test(text)) fail(`${name} has @import or url() references that a merged stylesheet may not resolve.`);
  return text;
}).join('\n');
write('editor.css', cssText);
for (const name of css.slice(1)) rmSync(join(site, name));
html = html.replace(cssLinks[0][0], cssLinks[0][0]);
for (const match of cssLinks.slice(1)) html = html.replace(match[0], '');

// Classic scripts, in document order.
const classic = [...html.matchAll(/<script src="([^"]+\.js)" defer><\/script>\n?/g)];
if (classic.map(match => match[1]).join() !== 'editor.js,ui.js') fail('expected deferred classic scripts editor.js then ui.js, found ' + classic.map(match => match[1]).join());
const dynamicImport = /import\(new URL\('([A-Za-z0-9._-]+\.m?js)', document\.baseURI\)\)/g;
const siblingImport = /import\('\.\/([A-Za-z0-9._-]+\.m?js)'\)/g;
const viaRegistry = text => text.replace(dynamicImport, (_, name) => `__editorImport('${name}')`)
  .replace(siblingImport, (_, name) => `__editorImport('${name}')`);
const editorJs = viaRegistry(read('editor.js')) + '\n;\n' + viaRegistry(read('ui.js'));
rmSync(join(site, 'ui.js'));
write('editor.js', editorJs);
html = html.replace(classic[1][0], '');

// Module members: everything reachable from the page's dynamic imports and host.js through static or dynamic imports.
// Discovery (not a fixed list) means a new lazily imported module is bundled automatically instead of being dropped.
const NAME = '[A-Za-z0-9._-]+\\.m?js';
const dynamicRef = new RegExp(`import\\(new URL\\('(${NAME})', document\\.baseURI\\)\\)`, 'g');
const staticImport = new RegExp(`^import\\s+(?:(\\*\\s+as\\s+[A-Za-z_$][\\w$]*)|\\{([^}]*)\\})\\s+from\\s+'\\./(${NAME})';?[ \\t]*$`, 'gm');
const siblingDynamic = new RegExp(`import\\('\\./(${NAME})'\\)`, 'g');
const members = [];
const registryRef = new RegExp(`__editorImport\\('(${NAME})'\\)`, 'g');
const queue = ['host.js', ...[html, editorJs].flatMap(text => [...text.matchAll(dynamicRef), ...text.matchAll(siblingDynamic), ...text.matchAll(registryRef)].map(match => match[1]))];
while (queue.length) {
  const name = queue.shift();
  if (members.includes(name)) continue;
  if (!existsSync(join(site, name))) fail(`imported module ${name} is not part of the published site.`);
  members.push(name);
  const source = read(name);
  for (const match of source.matchAll(staticImport)) queue.push(match[3]);
  for (const match of source.matchAll(siblingDynamic)) queue.push(match[1]);
  for (const match of source.matchAll(dynamicRef)) queue.push(match[1]);
}
const transform = (name, source) => {
  const exported = [];
  let text = source.replace(/^export\s+(async\s+function|function|class|const|let)\s+([A-Za-z_$][\w$]*)/gm, (_, kind, id) => { exported.push(id); return `${kind} ${id}`; });
  text = text.replace(/^export\s+\{([^}]*)\};?[ \t]*$/gm, (_, list) => {
    for (const item of list.split(',').map(part => part.trim()).filter(Boolean)) {
      const [local, alias = local] = item.split(/\s+as\s+/);
      exported.push(local === alias ? local : `${alias}: ${local}`);
    }
    return '';
  });
  text = text.replace(staticImport, (_, namespace, names, from) => {
    if (!members.includes(from)) fail(`${name} imports ${from}, which is not part of the bundle.`);
    return namespace ? `const ${namespace.replace(/\*\s+as\s+/, '')} = __require('${from}');` : `const {${names.replace(/\s+as\s+/g, ': ')}} = __require('${from}');`;
  });
  text = text.replace(siblingDynamic, (_, from) => `__import('${from}')`);
  text = text.replace(dynamicRef, (_, from) => `__import('${from}')`);
  if (/^(import|export)\b/m.test(text)) fail(`${name} still contains an unhandled import/export statement (default and re-exports are not supported).`);
  if (/^(await|for await)\b/m.test(text)) fail(`${name} uses top-level await, which cannot be wrapped.`);
  return { exported, text };
};
const parts = members.map(name => ({ name, ...transform(name, read(name)) }));
const bundle = `// Bundled editor modules (generated at publish time).
const __defs = {}, __cache = {};
const __require = name => {
  if (!(name in __cache)) {
    if (!__defs[name]) throw new Error('Missing bundled module ' + name);
    __cache[name] = null;
    __cache[name] = __defs[name]();
  }
  if (__cache[name] === null) throw new Error('Circular bundled module ' + name);
  return __cache[name];
};
const __import = name => { try { return Promise.resolve(__require(name)); } catch (error) { return Promise.reject(error); } };
${parts.map(part => `__defs['${part.name}'] = () => {\n${part.text}\nreturn { ${part.exported.join(', ')} };\n};`).join('\n')}
window.__editorModules = __require;
window.__editorModulesReady();
`;
for (const name of members) rmSync(join(site, name));
write('host.js', bundle);

html = viaRegistry(html);
// Minify (identifier, syntax and whitespace); classic scripts keep their top-level names because other scripts use them.
if (esbuild) {
  const min = (code, loader, extra = {}) => esbuild.transformSync(code, { loader, minify: true, legalComments: 'none', target: 'es2022', ...extra }).code;
  write('host.js', min(read('host.js'), 'js', { format: 'esm' }));
  write('editor.js', min(read('editor.js'), 'js'));
  write('editor.css', min(read('editor.css'), 'css'));
  html = html.replace(/<script>([\s\S]*?)<\/script>/g, (whole, code) => code.trim() ? `<script>${min(code, 'js')}</script>` : whole);
}

// Syntax gate: the generated files must parse before they ship.
const scratch = join(tmpdir(), 'editor-bundle-check-' + process.pid);
for (const [name, file] of [['host.js', 'host.mjs'], ['editor.js', 'editor.cjs']]) {
  writeFileSync(scratch + file, read(name));
  try { execFileSync(process.execPath, ['--check', scratch + file], { stdio: 'pipe' }); }
  catch (error) { fail(`${name} does not parse: ${error.stderr}`); }
  finally { rmSync(scratch + file, { force: true }); }
}

// HTML: registry before any consumer, inline dynamic imports, bundle script, inlined favicons.
const icon = (file, type) => `data:${type};base64,${readFileSync(join(site, file)).toString('base64')}`;
const inline = [['favicon.svg', 'image/svg+xml'], ['favicon-32.png', 'image/png'], ['favicon.ico', 'image/x-icon']];
// One 512px icon serves every size (the OS scales it): the manifest declares it for 192 and 512, and the 180px
// apple-touch copy and the 192px file are dropped (two fewer startup files).
if (!html.includes('href="apple-touch-icon.png"')) fail('index.html does not link apple-touch-icon.png.');
html = html.replace('href="apple-touch-icon.png"', 'href="icon-512.png"');
rmSync(join(site, 'apple-touch-icon.png'));
const manifest = JSON.parse(read('site.webmanifest'));
const big = manifest.icons?.find(entry => entry.src === 'icon-512.png');
if (!big || !manifest.icons.some(entry => entry.src === 'icon-192.png')) fail('site.webmanifest must list icon-192.png and icon-512.png.');
big.sizes = '192x192 512x512';
// favicon.svg is inlined into the page, so the manifest must not reference a file that is no longer published.
manifest.icons = manifest.icons.filter(entry => entry.src === 'icon-512.png');
write('site.webmanifest', JSON.stringify(manifest));
rmSync(join(site, 'icon-192.png'));
for (const [file, type] of inline) {
  if (!html.includes(`href="${file}"`)) fail(`index.html does not link ${file}.`);
  html = html.replace(`href="${file}"`, `href="${icon(file, type)}"`);
  rmSync(join(site, file));
}
html = viaRegistry(html);
if (/import\(new URL\(/.test(html + editorJs)) fail('a dynamic import of a bundled module was not rewritten.');
const registry = '<script>{let ok,no;window.__editorModulesLoaded=new Promise((a,b)=>{ok=a;no=b});window.__editorModulesReady=ok;window.__editorModulesFailed=no;window.__editorImport=name=>window.__editorModulesLoaded.then(()=>window.__editorModules(name))}</script>\n'
  + '<script type="module" src="host.js" onerror="__editorModulesFailed(new TypeError(\'Failed to load host.js\'))"></script>\n';
const anchor = '<script src="editor.js" defer></script>';
if (!html.includes(anchor)) fail('editor.js script tag not found.');
html = html.replace(anchor, registry + anchor);
write('index.html', html);

const left = ['theme.css', 'surface.css', 'ui.js', ...members.filter(name => name !== 'host.js'), ...inline.map(entry => entry[0])].filter(name => existsSync(join(site, name)));
if (left.length) fail('bundled files still published: ' + left.join(', '));
console.log(`Bundled shell: editor.css (${css.length} sheets), editor.js (2 scripts), host.js (${members.length} modules), ${inline.length} inlined icons, icon-192/apple-touch dropped in favour of icon-512.png.`);
