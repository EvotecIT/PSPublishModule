const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {spawnSync} = require('node:child_process');

const script = path.join(__dirname, 'Restore-DependencyInputs.ps1');
const stageScript = path.join(__dirname, 'Stage-DependencyGraphs.ps1');
const sdkScript = path.join(__dirname, 'Assert-DependencySdk.ps1');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge dependency inputs '));
  t.after(() => fs.rmSync(root, {recursive: true, force: true}));
  fs.writeFileSync(path.join(root, 'Example.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>');
  return root;
}

function run(root, projects, workloads = '', version = '') {
  return spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-File', script, '-ProjectsJson', JSON.stringify(projects), '-Workloads', workloads, '-WorkloadVersion', version], {
    cwd: root, encoding: 'utf8', timeout: 180000,
    env: {...process.env, MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_USE_MSBUILD_SERVER: '0'}
  });
}

function sdk(root, setup, hideDotnet = false) {
  const command = (hideDotnet ? "$env:PATH = ''; " : '') +
    (setup === undefined ? '& $env:TEST_SDK_SCRIPT -Restore' : '& $env:TEST_SDK_SCRIPT -SetupDotNet $env:TEST_SETUP_DOTNET -Restore');
  return spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-Command', command], {
    cwd: root, encoding: 'utf8', timeout: 30000,
    env: {...process.env, TEST_SDK_SCRIPT: sdkScript, TEST_SETUP_DOTNET: setup ?? ''}
  });
}

test('an existing SDK restores a real portable graph without a policy file', t => {
  const root = fixture(t);
  const selected = sdk(root, 'false');
  assert.equal(selected.status, 0, selected.stdout + selected.stderr);
  assert.match(selected.stdout, /Using existing \.NET SDK \d+\.\d+\.\d+/);
  const result = run(root, ['Example.csproj']);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.equal(fs.existsSync(path.join(root, 'global.json')), false);
  const assets = JSON.parse(fs.readFileSync(path.join(root, 'obj', 'project.assets.json'), 'utf8'));
  assert.ok(assets.targets['net8.0']);
  assert.equal(path.resolve(assets.project.restore.projectPath), path.join(root, 'Example.csproj'));
});

test('SDK preflight defers SDK selection to configured setup by default', t => {
  const root = fixture(t);
  for (const setup of [undefined, 'true']) {
    const result = sdk(root, setup, true);
    assert.equal(result.status, 0, result.stdout + result.stderr);
  }
});

test('SDK preflight rejects nonboolean setup selections', t => {
  const root = fixture(t);
  for (const setup of ['', 'TRUE', '1']) {
    const result = sdk(root, setup, true);
    assert.notEqual(result.status, 0);
    assert.match(result.stdout + result.stderr, /setup-dotnet must be 'true' or 'false'/);
  }
});

test('existing SDK selection fails when dotnet is absent from PATH', t => {
  const result = sdk(fixture(t), 'false', true);
  assert.notEqual(result.status, 0);
  assert.match(result.stdout + result.stderr, /dotnet is not available on PATH/);
});

test('empty project selection fails before restore', t => {
  const root = fixture(t);
  const result = run(root, []);
  assert.notEqual(result.status, 0);
  assert.match(result.stdout + result.stderr, /nonempty JSON array/);
  assert.equal(fs.existsSync(path.join(root, 'obj')), false);
});

test('absolute and escaping project paths fail before restore', t => {
  const root = fixture(t);
  for (const selected of [path.join(root, 'Example.csproj'), '../outside.csproj']) {
    const result = run(root, [selected]);
    assert.notEqual(result.status, 0);
    assert.equal(fs.existsSync(path.join(root, 'obj')), false);
  }
});

test('missing and unsupported inputs fail before restore', t => {
  const root = fixture(t);
  fs.writeFileSync(path.join(root, 'Example.txt'), 'example');
  for (const selected of ['missing.csproj', 'Example.txt']) {
    assert.notEqual(run(root, [selected]).status, 0);
    assert.equal(fs.existsSync(path.join(root, 'obj')), false);
  }
});

test('invalid workload identifiers fail before installation or restore', t => {
  const root = fixture(t);
  const result = run(root, ['Example.csproj'], 'wasm-tools;echo');
  assert.notEqual(result.status, 0);
  assert.match(result.stdout + result.stderr, /Workload identifiers/);
  assert.equal(fs.existsSync(path.join(root, 'obj')), false);
});

test('invalid or unselected workload-set versions fail before installation', t => {
  const root = fixture(t);
  for (const [workloads, version] of [['ios', '10.0.112;echo'], ['', '10.0.112']]) {
    const result = run(root, ['Example.csproj'], workloads, version);
    assert.notEqual(result.status, 0);
    assert.match(result.stdout + result.stderr, /workload-version requires/);
    assert.equal(fs.existsSync(path.join(root, 'obj')), false);
  }
});

test('workload installation selects the exact set or retains bundled manifests and stops on failure', t => {
  const root = fixture(t);
  const harness = path.join(root, 'capture.ps1');
  fs.writeFileSync(harness, `
function dotnet {
  ConvertTo-Json -InputObject @($args) -Compress | Add-Content -LiteralPath (Join-Path $env:TEST_ROOT 'calls.jsonl')
  $global:LASTEXITCODE = 0
  if ($args[0] -eq 'workload' -and $env:TEST_FAIL -eq '1') { $global:LASTEXITCODE = 9 }
  if ($args[0] -eq 'restore') {
    New-Item -ItemType Directory -Force -Path (Join-Path $env:TEST_ROOT 'obj') | Out-Null
    '{}' | Set-Content -LiteralPath (Join-Path $env:TEST_ROOT 'obj/project.assets.json')
  }
}
& $env:TEST_SCRIPT -ProjectsJson ' ["Example.csproj"]' -Workloads 'ios wasm-tools' -WorkloadVersion $env:TEST_VERSION
`);
  for (const [version, fail] of [['10.0.112', false], ['', false], ['10.0.112', true]]) {
    const callsFile = path.join(root, 'calls.jsonl');
    fs.rmSync(callsFile, {force: true});
    const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-File', harness], {
      cwd: root, encoding: 'utf8', timeout: 30000,
      env: {...process.env, TEST_ROOT: root, TEST_SCRIPT: script, TEST_VERSION: version, TEST_FAIL: fail ? '1' : '0'}
    });
    assert.equal(result.status === 0, !fail, result.stdout + result.stderr);
    const calls = fs.readFileSync(callsFile, 'utf8').trim().split(/\r?\n/).map(line => JSON.parse(line));
    assert.deepEqual(calls[0], ['workload', 'install', 'ios', 'wasm-tools', ...(version ? ['--version', version] : ['--skip-manifest-update'])]);
    assert.equal(calls.length, fail ? 1 : 2);
    if (!fail) assert.equal(calls[1][0], 'restore');
  }
});

test('a failed restore stops before later project graphs are produced', t => {
  const root = fixture(t);
  fs.writeFileSync(path.join(root, 'Broken.csproj'), '<Project');
  const result = run(root, ['Broken.csproj', 'Example.csproj']);
  assert.notEqual(result.status, 0);
  assert.equal(fs.existsSync(path.join(root, 'obj', 'project.assets.json')), false);
});

function stage(mode, source, output) {
  return spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-File', stageScript, '-Mode', mode, '-Source', source, '-OutputPath', output], {
    encoding: 'utf8', timeout: 30000
  });
}

test('transport rebases a real restored graph without changing package targets or edges', t => {
  const root = fixture(t);
  assert.equal(run(root, ['Example.csproj']).status, 0);
  const exported = root + '-export';
  const imported = root + '-import';
  t.after(() => { for (const dir of [exported, imported]) fs.rmSync(dir, {recursive: true, force: true}); });
  let result = stage('Export', root, exported);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const file = path.join(exported, 'obj', 'project.assets.json');
  const original = JSON.parse(fs.readFileSync(file, 'utf8'));
  // Simulate a graph restored on macOS and imported on another filesystem.
  original.project.restore.projectPath = '/Users/runner/work/Product/Product/Example.csproj';
  original.project.restore.projectUniqueName = original.project.restore.projectPath;
  original.project.restore.outputPath = '/Users/runner/work/Product/Product/obj/';
  fs.writeFileSync(file, JSON.stringify(original));
  const indexFile = path.join(exported, 'dependency-graphs.json');
  const index = JSON.parse(fs.readFileSync(indexFile, 'utf8'));
  index.sourceRoot = '/Users/runner/work/Product/Product';
  fs.writeFileSync(indexFile, JSON.stringify(index));
  result = stage('Import', exported, imported);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const graph = JSON.parse(fs.readFileSync(path.join(imported, 'obj', 'project.assets.json'), 'utf8'));
  assert.equal(graph.project.restore.projectPath, path.join(imported, 'Example.csproj'));
  assert.equal(graph.project.restore.outputPath, path.join(imported, 'obj') + path.sep);
  assert.deepEqual(graph.targets, original.targets);
  assert.deepEqual(graph.libraries, original.libraries);
  assert.deepEqual(graph.project.frameworks, original.project.frameworks);
});

test('transport rejects graph identities that disagree with the indexed source project', t => {
  const root = fixture(t);
  const output = root + '-import';
  t.after(() => fs.rmSync(output, {recursive: true, force: true}));
  fs.mkdirSync(path.join(root, 'obj'));
  const sourceRoot = '/Users/runner/work/Product/Product';
  const project = sourceRoot + '/Example.csproj';
  const index = {version: 1, sourceRoot, assets: [{file: 'obj/project.assets.json', project: 'Example.csproj'}]};
  fs.writeFileSync(path.join(root, 'dependency-graphs.json'), JSON.stringify(index));
  for (const metadata of [
    {projectPath: sourceRoot + '/Other.csproj', projectUniqueName: project},
    {projectPath: project, projectUniqueName: sourceRoot + '/Other.csproj'},
    {projectPath: '/different/root/Example.csproj', projectUniqueName: project}
  ]) {
    fs.writeFileSync(path.join(root, 'obj', 'project.assets.json'), JSON.stringify({project: {restore: metadata}}));
    const result = stage('Import', root, output);
    assert.notEqual(result.status, 0, 'A mismatched identity must not be rewritten into the indexed project');
    assert.match(result.stdout + result.stderr, /identity does not match/);
    assert.equal(fs.existsSync(output), false);
  }
});

test('transport rejects traversal, duplicate graphs and stale output', t => {
  const root = fixture(t);
  const output = root + '-import';
  t.after(() => fs.rmSync(output, {recursive: true, force: true}));
  fs.mkdirSync(path.join(root, 'obj'));
  fs.writeFileSync(path.join(root, 'obj', 'project.assets.json'), '{}');
  const entry = {file: 'obj/project.assets.json', project: 'Example.csproj'};
  for (const assets of [[{...entry, project: '../outside.csproj'}], [{...entry, file: '../project.assets.json'}], [entry, entry]]) {
    fs.writeFileSync(path.join(root, 'dependency-graphs.json'), JSON.stringify({version: 1, sourceRoot: root.replace(/\\/g, '/'), assets}));
    assert.notEqual(stage('Import', root, output).status, 0);
    assert.equal(fs.existsSync(output), false);
  }
  fs.mkdirSync(output);
  assert.notEqual(stage('Import', root, output).status, 0);
});
