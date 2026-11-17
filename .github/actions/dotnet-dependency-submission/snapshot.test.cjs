const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {buildCandidate, validateCandidate} = require('./snapshot.cjs');
const {scan} = require('./scan.cjs');
const {snapshotRequest, submit} = require('./submit.cjs');

function fixture(root = '/repo with spaces') {
  return [{scope: 'runtime', project: {restore: {projectPath: `${root}/Main.csproj`},
    frameworks: {'net8.0': {dependencies: {OpenXml: {target: 'Package'}}}}}, targets: {'net8.0': {
    'OpenXml/1.0.0': {type: 'package', dependencies: {Framework: '1.0.0', Packaging: '1.0.0'}, runtime: {'lib/net8.0/OpenXml.dll': {}}},
    'Framework/1.0.0': {type: 'package', dependencies: {Packaging: '1.0.0'}, runtime: {'lib/net8.0/Framework.dll': {}}},
    'Packaging/1.0.0': {type: 'package', runtime: {'lib/net8.0/Packaging.dll': {}}}
  }}}];
}
const urls = ['openxml', 'framework', 'packaging'].map(name => `pkg:nuget/${name}@1.0.0`);
const candidate = () => buildCandidate(fixture(), '/repo with spaces', ['Main.csproj']);
const environment = () => ({POWERFORGE_SUBMIT: 'true', GITHUB_EVENT_NAME: 'push', GITHUB_REF: 'refs/heads/main',
  GITHUB_SHA: 'a'.repeat(40), GITHUB_REPOSITORY: 'Example/Library', GITHUB_RUN_ID: '123', POWERFORGE_SNAPSHOT_TOKEN: 'synthetic-token'});

test('snapshot preserves converging edges, direct references and per-manifest scopes', () => {
  const graphs = fixture();
  graphs.push({scope: 'development', project: {restore: {projectPath: '/repo with spaces/Other.csproj'},
    frameworks: {'net8.0': {dependencies: {Packaging: {target: 'Package'}}}}},
    targets: {'net8.0': {'Packaging/1.0.0': {type: 'package', runtime: {'lib/net8.0/Packaging.dll': {}}}}}});
  const result = buildCandidate(graphs, '/repo with spaces', ['Main.csproj', 'Other.csproj']);
  const main = result.manifests['Main.csproj'].resolved;
  assert.deepEqual(main[urls[0]].dependencies, [urls[1], urls[2]]);
  assert.deepEqual(main[urls[1]].dependencies, [urls[2]]);
  assert.equal(main[urls[0]].relationship, 'direct');
  assert.equal(main[urls[2]].relationship, 'indirect');
  assert.equal(main[urls[2]].scope, 'runtime');
  const other = result.manifests['Other.csproj'].resolved;
  assert.deepEqual(Object.keys(other), [urls[2]]);
  assert.equal(other[urls[2]].relationship, 'direct');
  assert.equal(other[urls[2]].scope, 'development');
  validateCandidate(result);
});

test('snapshot paths support native Windows roots and spaces', () => {
  const scan = fixture('C:/repo with spaces');
  const result = buildCandidate(scan, 'C:\\repo with spaces', ['Main.csproj']);
  assert.equal(result.manifests['Main.csproj'].file.source_location, 'Main.csproj');
});

test('missing targets, projects, escaping manifests and dangling edges fail closed', () => {
  const graphs = fixture();
  assert.throws(() => buildCandidate([{...graphs[0], targets: {}}], '/repo with spaces', ['Main.csproj']), /Invalid restored/);
  assert.throws(() => buildCandidate(graphs, '/repo with spaces', ['Missing.csproj']), /coverage/);
  assert.throws(() => buildCandidate(graphs, '/other', ['Main.csproj']), /outside/);
  delete graphs[0].targets['net8.0']['Packaging/1.0.0'];
  assert.throws(() => buildCandidate(graphs, '/repo with spaces', ['Main.csproj']), /Missing restored dependency/);
});

test('native targets retain project closure, versions and verified runtime or development use', () => {
  const graphs = fixture();
  const graph = graphs[0];
  graph.developmentPackages = ['Tooling/1.0.0'];
  graph.project.frameworks['net8.0'].dependencies.Tooling = {target: 'Package'};
  graph.targets['net8.0']['Tooling/1.0.0'] = {type: 'package', dependencies: {ToolOnly: '1.0.0', Packaging: '1.0.0'}, runtime: {'lib/net8.0/tool.dll': {}}};
  graph.targets['net8.0']['ToolOnly/1.0.0'] = {type: 'package', runtime: {'lib/net8.0/tool-only.dll': {}}};
  graph.project.frameworks['net8.0'].dependencies.CompileTool = {target: 'Package', include: 'Compile, Build, Analyzers'};
  graph.targets['net8.0']['CompileTool/1.0.0'] = {type: 'package', dependencies: {ToolRuntime: '1.0.0'}, build: {'build/tool.props': {}}};
  graph.targets['net8.0']['ToolRuntime/1.0.0'] = {type: 'package', runtime: {'lib/net8.0/tool-runtime.dll': {}}};
  graph.project.frameworks.net48 = {dependencies: {OpenXml: {target: 'Package'}, Private: {target: 'Package'}}};
  graph.targets.net48 = {'OpenXml/1.0.0': {type: 'package', runtime: {'lib/net48/_._': {}}}};
  graph.targets['net8.0/win-x64'] = structuredClone(graph.targets['net8.0']);
  graph.targets['net8.0/win-x64']['ProjectReference/1.0.0'] = {type: 'project', dependencies: {Private: '2.0.0'}};
  graph.targets['net8.0/win-x64']['Private/2.0.0'] = {type: 'package', native: {'runtimes/win-x64/native/private.dll': {}}};
  graph.targets.net48['Private/1.0.0'] = {type: 'package', compile: {'ref/net48/private.dll': {}}};
  graph.project.frameworks.net48.downloadDependencies = [{name: 'RuntimePack', version: '[10.0.14, 10.0.14]'}];
  const resolved = buildCandidate(graphs, '/repo with spaces', ['Main.csproj']).manifests['Main.csproj'].resolved;
  assert.equal(resolved[urls[0]].scope, 'runtime');
  assert.equal(resolved['pkg:nuget/private@2.0.0'].scope, 'runtime');
  assert.equal(resolved['pkg:nuget/private@2.0.0'].relationship, 'indirect');
  assert.equal(resolved['pkg:nuget/private@1.0.0'].scope, 'development');
  assert.equal(resolved['pkg:nuget/private@1.0.0'].relationship, 'direct');
  assert.equal(resolved['pkg:nuget/runtimepack@10.0.14'].scope, 'development');
  assert.equal(resolved['pkg:nuget/runtimepack@10.0.14'].relationship, 'direct');
  assert.equal(resolved['pkg:nuget/tooling@1.0.0'].scope, 'development');
  assert.equal(resolved['pkg:nuget/toolonly@1.0.0'].scope, 'development');
  assert.equal(resolved['pkg:nuget/compiletool@1.0.0'].scope, 'development');
  assert.equal(resolved['pkg:nuget/toolruntime@1.0.0'].scope, 'development');
  assert.equal(resolved[urls[2]].scope, 'runtime');
  assert.equal(Object.keys(resolved).length, 10);
  graph.scope = 'development';
  const development = buildCandidate(graphs, '/repo with spaces', ['Main.csproj']).manifests['Main.csproj'].resolved;
  assert.equal(Object.keys(development).length, 10);
  assert.ok(Object.values(development).every(item => item.scope === 'development'));
});

test('runtime scopes follow permitted dependency closure and selected primary asset categories', () => {
  const graphs = fixture();
  const graph = graphs[0];
  Object.assign(graph.project.frameworks['net8.0'].dependencies, {
    Meta: {target: 'Package'}, NativeOnly: {target: 'Package', include: 'Native'},
    ContentOnly: {target: 'Package', include: 'ContentFiles'}, NativeMeta: {target: 'Package', include: 'Native'},
    ContentMeta: {target: 'Package', include: 'ContentFiles'}, NativeNoContribution: {target: 'Package', include: 'Native'},
    ContentNoContribution: {target: 'Package', include: 'ContentFiles'}, RuntimeNoNative: {target: 'Package', include: 'Runtime'},
    Resource: {target: 'Package', include: 'Runtime'}, RidNative: {target: 'Package', include: 'Native'},
    NoCopy: {target: 'Package', include: 'ContentFiles'}, Placeholder: {target: 'Package', include: 'Native'},
    CycleA: {target: 'Package'}, DevMeta: {target: 'Package'}
  });
  Object.assign(graph.targets['net8.0'], {
    'Meta/1.0.0': {type: 'package', dependencies: {Packaging: '1.0.0'}},
    'NativeOnly/1.0.0': {type: 'package', native: {'runtimes/win-x64/native/payload.dll': {}}},
    'ContentOnly/1.0.0': {type: 'package', contentFiles: {'contentFiles/any/any/payload.json': {copyToOutput: true}}},
    'NativeMeta/1.0.0': {type: 'package', dependencies: {NativeOnly: '1.0.0'}},
    'ContentMeta/1.0.0': {type: 'package', dependencies: {ContentOnly: '1.0.0'}},
    'NativeNoContribution/1.0.0': {type: 'package', dependencies: {Packaging: '1.0.0'}},
    'ContentNoContribution/1.0.0': {type: 'package', dependencies: {Packaging: '1.0.0'}},
    'RuntimeNoNative/1.0.0': {type: 'package', dependencies: {NativeOnly: '1.0.0'}},
    'Resource/1.0.0': {type: 'package', resource: {'lib/net8.0/fr/payload.resources.dll': {}}},
    'RidNative/1.0.0': {type: 'package', runtimeTargets: {'runtimes/win-x64/native/rid.dll': {assetType: 'native'}}},
    'NoCopy/1.0.0': {type: 'package', contentFiles: {'contentFiles/any/any/source.cs': {copyToOutput: false}}},
    'Placeholder/1.0.0': {type: 'package', native: {'runtimes/win-x64/native/_._': {}}},
    'CycleA/1.0.0': {type: 'package', dependencies: {CycleB: '1.0.0'}},
    'CycleB/1.0.0': {type: 'package', dependencies: {CycleA: '1.0.0', Packaging: '1.0.0'}},
    'DevMeta/1.0.0': {type: 'package', dependencies: {DevLeaf: '1.0.0', Packaging: '1.0.0'}},
    'DevLeaf/1.0.0': {type: 'package', runtime: {'lib/net8.0/tool-only.dll': {}}}
  });
  graph.developmentPackages = ['DevMeta/1.0.0'];
  const resolved = buildCandidate(graphs, '/repo with spaces', ['Main.csproj']).manifests['Main.csproj'].resolved;
  for (const name of ['meta', 'nativeonly', 'contentonly', 'nativemeta', 'contentmeta', 'resource', 'ridnative', 'cyclea', 'cycleb']) {
    assert.equal(resolved[`pkg:nuget/${name}@1.0.0`].scope, 'runtime', name);
  }
  for (const name of ['nativenocontribution', 'contentnocontribution', 'runtimenonative', 'nocopy', 'placeholder', 'devmeta', 'devleaf']) {
    assert.equal(resolved[`pkg:nuget/${name}@1.0.0`].scope, 'development', name);
  }
  assert.equal(resolved[urls[2]].scope, 'runtime');
  assert.deepEqual(resolved['pkg:nuget/meta@1.0.0'].dependencies, [urls[2]]);
  assert.equal(Object.keys(resolved).length, 19);
  graph.scope = 'development';
  const development = buildCandidate(graphs, '/repo with spaces', ['Main.csproj']).manifests['Main.csproj'].resolved;
  assert.equal(Object.keys(development).length, 19);
  assert.ok(Object.values(development).every(item => item.scope === 'development'));
});

test('native file conversion validates index identity and writes a complete snapshot', t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge native graph '));
  t.after(() => fs.rmSync(root, {recursive: true, force: true}));
  const indexPath = path.join(root, 'dependency-graphs.json');
  const assets = [{file: 'obj/project.assets.json', project: 'Main.csproj', scope: 'runtime', developmentPackages: []}];
  fs.mkdirSync(path.join(root, 'obj'));
  fs.writeFileSync(path.join(root, assets[0].file), JSON.stringify(fixture(root)[0]));
  fs.writeFileSync(indexPath, JSON.stringify({version: 1, assets}));
  const output = path.join(root, 'snapshot.json');
  scan(root, indexPath, output);
  const actual = JSON.parse(fs.readFileSync(output, 'utf8'));
  assert.deepEqual(actual.manifests['Main.csproj'].resolved[urls[0]].dependencies, [urls[1], urls[2]]);
  delete assets[0].scope;
  delete assets[0].developmentPackages;
  fs.writeFileSync(indexPath, JSON.stringify({version: 1, assets}));
  const legacyOutput = path.join(root, 'legacy-snapshot.json');
  scan(root, indexPath, legacyOutput);
  const legacy = JSON.parse(fs.readFileSync(legacyOutput, 'utf8'));
  assert.deepEqual(legacy.manifests['Main.csproj'].resolved, actual.manifests['Main.csproj'].resolved);
  assets[0].project = 'Other.csproj';
  fs.writeFileSync(indexPath, JSON.stringify({version: 1, assets}));
  assert.throws(() => scan(root, indexPath, path.join(root, 'bad.json')), /differs from its index/);
  assets[0].file = '../project.assets.json';
  fs.writeFileSync(indexPath, JSON.stringify({version: 1, assets}));
  assert.throws(() => scan(root, indexPath, path.join(root, 'bad.json')), /outside|Invalid native asset path/);
  assert.equal(fs.existsSync(path.join(root, 'bad.json')), false);
});

test('uploader binds snapshot identity to the trusted run and blocks PRs and other refs', () => {
  const payload = candidate();
  payload.sha = 'untrusted-artifact-sha';
  const expected = snapshotRequest(payload, environment(), {repository: {default_branch: 'main'}}, 'profile-linux');
  assert.equal(expected.sha, 'a'.repeat(40));
  assert.equal(expected.job.correlator, 'profile-linux');
  assert.equal(expected.ref, 'refs/heads/main');
  for (const changes of [{GITHUB_EVENT_NAME: 'pull_request'}, {GITHUB_EVENT_NAME: 'pull_request_target'},
    {GITHUB_REF: 'refs/heads/feature'}, {GITHUB_REF: 'refs/tags/v1'}, {POWERFORGE_SUBMIT: 'false'}]) {
    assert.throws(() => snapshotRequest(payload, {...environment(), ...changes}, {repository: {default_branch: 'main'}}, 'profile-linux'), /trusted default-branch/);
  }
});

test('uploader sends the complete edge-preserving payload and propagates API failures', async () => {
  let calls = 0;
  const request = async (url, options) => {
    calls++;
    assert.equal(url, 'https://api.github.com/repos/Example/Library/dependency-graph/snapshots');
    assert.equal(options.method, 'POST');
    assert.equal(options.headers.Authorization, 'Bearer synthetic-token');
    const body = JSON.parse(options.body);
    assert.deepEqual(body.manifests['Main.csproj'].resolved[urls[1]].dependencies, [urls[2]]);
    return {status: 201, json: async () => ({result: 'SUCCESS'})};
  };
  await submit(candidate(), environment(), {repository: {default_branch: 'main'}}, 'profile-linux', request);
  assert.equal(calls, 1);
  await assert.rejects(submit(candidate(), environment(), {repository: {default_branch: 'main'}}, 'profile-linux', async () => ({status: 403})), /HTTP 403/);
});

test('uploader rejects malformed artifact paths and dependency edges before HTTP', () => {
  for (const file of ['C:/absolute/Main.csproj', 'c:relative/Main.csproj']) {
    const changed = candidate();
    const manifest = changed.manifests['Main.csproj'];
    delete changed.manifests['Main.csproj'];
    changed.manifests[file] = {...manifest, name: file, file: {source_location: file}};
    assert.throws(() => validateCandidate(changed), /manifest path/);
  }
  const payload = candidate();
  payload.manifests['../outside.csproj'] = payload.manifests['Main.csproj'];
  assert.throws(() => validateCandidate(payload), /outside the scan root|manifest path/);
  delete payload.manifests['../outside.csproj'];
  payload.manifests['Main.csproj'].resolved[urls[1]].dependencies = ['toString'];
  assert.throws(() => validateCandidate(payload), /dependency or edge/);
});

test('HTTP 201 without an applied snapshot result is a failure', async () => {
  for (const result of ['ACCEPTED', 'INVALID', 'UNRECOGNIZED', undefined]) {
    await assert.rejects(submit(candidate(), environment(), {repository: {default_branch: 'main'}}, 'profile-linux',
      async () => ({status: 201, json: async () => ({result, message: 'Snapshot not applied'})})), /not applied/);
  }
});
