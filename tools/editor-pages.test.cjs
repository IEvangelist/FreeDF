const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const workflow = fs.readFileSync(path.join(__dirname, '..', '.github', 'workflows', 'editor-pages.yml'), 'utf8');
const jobs = Object.fromEntries([...workflow.matchAll(/^  ([\w-]+):\r?\n([\s\S]*?)(?=^  [\w-]+:\r?$|(?![\s\S]))/gm)]
  .map(([, name, body]) => [name, body]));
const needs = name => jobs[name].match(/^    needs: (.+)$/m)[1].replace(/[\[\]\r]/g, '').split(',').map(s => s.trim());
const condition = name => jobs[name].match(/^    if: (.+)$/m)[1].trim();
const allowed = (name, state, github, inputs) =>
  needs(name).every(dep => state[dep].result === 'success')
  && Function('needs', 'github', 'inputs', `return (${condition(name)});`)(state, github, inputs);

test('packaging and deployment depend on built bytes, never test results', () => {
  assert.deepEqual(needs('package'), ['plan', 'build']);
  assert.deepEqual(needs('deploy'), ['package']);
  const github = { event_name: 'push', ref_name: 'main', event: { repository: { default_branch: 'main' } } };
  for (const result of ['success', 'failure', 'cancelled', 'skipped', 'in_progress']) {
    const state = {
      plan: { result: 'success', outputs: { code: 'true' } }, build: { result: 'success' },
      unit: { result }, smoke: { result }, validated: { result }, package: { result: 'success' },
    };
    assert.equal(allowed('package', state, github, {}), true, result);
    assert.equal(allowed('deploy', state, github, {}), true, result);
  }
});

test('build failures, missing builds and failed packaging still block shipping', () => {
  const github = { event_name: 'push', ref_name: 'main', event: { repository: { default_branch: 'main' } } };
  for (const result of ['failure', 'cancelled', 'skipped']) {
    assert.equal(allowed('package', { plan: { result: 'success', outputs: { code: 'true' } }, build: { result } }, github, {}), false);
    assert.equal(allowed('deploy', { package: { result } }, github, {}), false);
  }
});

test('pull requests, partial runs and unrequested manual deployments never deploy', () => {
  const state = { plan: { result: 'success', outputs: { code: 'true' } }, build: { result: 'success' }, package: { result: 'success' } };
  const base = { event_name: 'workflow_dispatch', ref_name: 'main', event: { repository: { default_branch: 'main' } } };
  for (const inputs of [{ deploy: false, chunk: 'all' }, { deploy: true, chunk: 'forms' }]) {
    assert.equal(allowed('package', state, base, inputs), false);
    assert.equal(allowed('deploy', state, base, inputs), false);
  }
  assert.equal(allowed('package', state, { ...base, event_name: 'pull_request' }, {}), false);
  assert.equal(allowed('deploy', state, { ...base, event_name: 'pull_request' }, {}), false);
  assert.equal(allowed('deploy', state, { ...base, ref_name: 'feature' }, { deploy: true, chunk: 'all' }), false);
  assert.equal(allowed('deploy', state, base, { deploy: true, chunk: 'all' }), true);
});

test('validation still reports all planned failures instead of suppressing them', () => {
  assert.deepEqual(needs('validated'), ['plan', 'build', 'unit', 'smoke']);
  assert.equal(condition('validated'), 'always()');
  assert.match(jobs.validated, /ok "\$\{\{ needs\.unit\.result \}\}"/);
  assert.match(jobs.validated, /ok "\$\{\{ needs\.smoke\.result \}\}"/);
  assert.doesNotMatch(jobs.unit + jobs.smoke + jobs.validated, /continue-on-error:\s*true/);
});

test('published assets default to the deployment directory, not an obsolete repository name', () => {
  const publish = fs.readFileSync(path.join(__dirname, 'Publish-EditorPages.ps1'), 'utf8');
  assert.match(publish, /\[string\] \$BasePath = '\.\/'/);
  const chunks = fs.readFileSync(path.join(__dirname, 'Invoke-EditorChunk.ps1'), 'utf8');
  assert.match(chunks, /Smoke @\('--shell', \$Site,/);
});

test('public acceptance uses the current Pages URL and the exact build artifact', () => {
  const build = fs.readFileSync(path.join(__dirname, '..', '.github', 'workflows', 'editor-site.yml'), 'utf8');
  const acceptance = fs.readFileSync(path.join(__dirname, 'Test-EditorPublic.ps1'), 'utf8');
  assert.match(build, /name: editor-site\s/);
  assert.match(acceptance, /\$Url = 'https:\/\/ievangelist\.github\.io\/FreeDF\/'/);
  assert.match(acceptance, /\$Repository = 'IEvangelist\/FreeDF'/);
  assert.match(acceptance, /'--name', 'editor-site'/);
  assert.match(acceptance, /\$site = Join-Path \$artifact 'wwwroot'/);
  assert.match(acceptance, /Site-Manifest\.ps1.*editor-site\.sha256.*-Verify/);
});

test('non-default branches never package Pages or deploy, even with deploy=true', () => {
  const state = { plan: { result: 'success', outputs: { code: 'true' } }, build: { result: 'success' }, package: { result: 'success' } };
  for (const defaultBranch of ['main', 'release']) {
    for (const event_name of ['push', 'workflow_dispatch', 'pull_request']) {
      for (const ref_name of [defaultBranch, 'feature']) {
        const github = { event_name, ref_name, event: { repository: { default_branch: defaultBranch } } };
        const expected = ref_name === defaultBranch && event_name !== 'pull_request';
        assert.equal(allowed('package', state, github, { deploy: true, chunk: 'all' }), expected);
        assert.equal(allowed('deploy', state, github, { deploy: true, chunk: 'all' }), expected);
      }
    }
  }
});

test('feature runs cannot cancel default-branch deployment', () => {
  assert.match(workflow, /group: editor-pages-\$\{\{ github\.ref \}\}/);
});
