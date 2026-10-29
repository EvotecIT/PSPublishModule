const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {spawnSync} = require('node:child_process');

const script = path.join(__dirname, 'Restore-DependencyInputs.ps1');
const stageScript = path.join(__dirname, 'Stage-DependencyGraphs.ps1');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge dependency inputs '));
  t.after(() => fs.rmSync(root, {recursive: true, force: true}));
  fs.writeFileSync(path.join(root, 'Example.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>');
  return root;
}

function run(root, projects, workloads = '') {
  return spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-File', script, '-ProjectsJson', JSON.stringify(projects), '-Workloads', workloads], {
    cwd: root, encoding: 'utf8', timeout: 180000,
    env: {...process.env, MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_USE_MSBUILD_SERVER: '0'}
  });
}

test('explicit portable project restores a real NuGet asset graph', t => {
  const root = fixture(t);
  const result = run(root, ['Example.csproj']);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const assets = JSON.parse(fs.readFileSync(path.join(root, 'obj', 'project.assets.json'), 'utf8'));
  assert.ok(assets.targets['net8.0']);
  assert.equal(path.resolve(assets.project.restore.projectPath), path.join(root, 'Example.csproj'));
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
  original.project.restore.outputPath = '/Users/runner/work/Product/Product/obj/';
  fs.writeFileSync(file, JSON.stringify(original));
  result = stage('Import', exported, imported);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const graph = JSON.parse(fs.readFileSync(path.join(imported, 'obj', 'project.assets.json'), 'utf8'));
  assert.equal(graph.project.restore.projectPath, path.join(imported, 'Example.csproj'));
  assert.equal(graph.project.restore.outputPath, path.join(imported, 'obj') + path.sep);
  assert.deepEqual(graph.targets, original.targets);
  assert.deepEqual(graph.libraries, original.libraries);
  assert.deepEqual(graph.project.frameworks, original.project.frameworks);
});

test('transport rejects traversal, duplicate graphs and stale output', t => {
  const root = fixture(t);
  const output = root + '-import';
  t.after(() => fs.rmSync(output, {recursive: true, force: true}));
  fs.mkdirSync(path.join(root, 'obj'));
  fs.writeFileSync(path.join(root, 'obj', 'project.assets.json'), '{}');
  const entry = {file: 'obj/project.assets.json', project: 'Example.csproj'};
  for (const assets of [[{...entry, project: '../outside.csproj'}], [{...entry, file: '../project.assets.json'}], [entry, entry]]) {
    fs.writeFileSync(path.join(root, 'dependency-graphs.json'), JSON.stringify({version: 1, assets}));
    assert.notEqual(stage('Import', root, output).status, 0);
    assert.equal(fs.existsSync(output), false);
  }
  fs.mkdirSync(output);
  assert.notEqual(stage('Import', root, output).status, 0);
});
