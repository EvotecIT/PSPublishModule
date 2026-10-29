const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {buildCandidate, validateCandidate} = require('./snapshot.cjs');
const {downloadScanner, scannerEnvironment} = require('./scan.cjs');
const {snapshotRequest, submit} = require('./submit.cjs');

function fixture(root = '/repo with spaces') {
  const ids = ['OpenXml', 'Framework', 'Packaging'].map(name => `${name} 1.0.0 - NuGet`);
  return {resultCode: 'Success', componentsFound: ids.map((id, i) => ({
    component: {id, type: 'NuGet', name: ['OpenXml', 'Framework', 'Packaging'][i], version: '1.0.0'},
    // Global referrers deliberately flatten the intermediate dependency; never use them for edges.
    topLevelReferrers: [{id: ids[0]}]
  })), dependencyGraphs: {[`${root}/Main.csproj`]: {
    graph: {[ids[0]]: [ids[1]], [ids[1]]: [ids[2]], [ids[2]]: null},
    explicitlyReferencedComponentIds: [ids[0]], developmentDependencies: []
  }}};
}
const urls = ['openxml', 'framework', 'packaging'].map(name => `pkg:nuget/${name}@1.0.0`);
const candidate = () => buildCandidate(fixture(), '/repo with spaces', ['Main.csproj']);
const environment = () => ({POWERFORGE_SUBMIT: 'true', GITHUB_EVENT_NAME: 'push', GITHUB_REF: 'refs/heads/main',
  GITHUB_SHA: 'a'.repeat(40), GITHUB_REPOSITORY: 'Example/Library', GITHUB_RUN_ID: '123', POWERFORGE_SNAPSHOT_TOKEN: 'synthetic-token'});

test('snapshot preserves intermediate edges, direct references and per-manifest scopes', () => {
  const scan = fixture();
  scan.dependencyGraphs['/repo with spaces/Other.csproj'] = {
    graph: {'Packaging 1.0.0 - NuGet': null}, explicitlyReferencedComponentIds: ['Packaging 1.0.0 - NuGet'],
    developmentDependencies: ['Packaging 1.0.0 - NuGet']
  };
  const result = buildCandidate(scan, '/repo with spaces', ['Main.csproj', 'Other.csproj']);
  const main = result.manifests['Main.csproj'].resolved;
  assert.deepEqual(main[urls[0]].dependencies, [urls[1]]);
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

test('failed scans, missing projects, escaping manifests and dangling edges fail closed', () => {
  const scan = fixture();
  assert.throws(() => buildCandidate({...scan, resultCode: 'Failed'}, '/repo with spaces', ['Main.csproj']), /successful/);
  assert.throws(() => buildCandidate(scan, '/repo with spaces', ['Missing.csproj']), /coverage/);
  assert.throws(() => buildCandidate(scan, '/other', ['Main.csproj']), /outside/);
  scan.dependencyGraphs['/repo with spaces/Main.csproj'].graph['Framework 1.0.0 - NuGet'] = ['Unknown 1.0.0 - NuGet'];
  assert.throws(() => buildCandidate(scan, '/repo with spaces', ['Main.csproj']), /unknown component/);
  scan.dependencyGraphs['/repo with spaces/Main.csproj'].graph['Framework 1.0.0 - NuGet'] = ['Packaging 1.0.0 - NuGet'];
  delete scan.dependencyGraphs['/repo with spaces/Main.csproj'].graph['Packaging 1.0.0 - NuGet'];
  assert.throws(() => buildCandidate(scan, '/repo with spaces', ['Main.csproj']), /Missing graph node/);
});

test('scanner bytes must pass the pinned digest before an executable is written', async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge scanner digest '));
  t.after(() => fs.rmSync(root, {recursive: true, force: true}));
  await assert.rejects(downloadScanner(root, 'linux', async url => {
    assert.match(url, /\/v8\.0\.6\/component-detection-linux-x64$/);
    return {ok: true, arrayBuffer: async () => Buffer.from('corrupt download')};
  }), /SHA-256 verification failed/);
  assert.deepEqual(fs.readdirSync(root), []);
});

test('scanner child environment contains runtime paths without job credentials', () => {
  const result = scannerEnvironment({PATH: '/tools', HOME: '/home/runner', DOTNET_ROOT: '/dotnet',
    GITHUB_TOKEN: 'synthetic', INPUT_TOKEN: 'synthetic', POWERFORGE_SNAPSHOT_TOKEN: 'synthetic', ACTIONS_RUNTIME_TOKEN: 'synthetic'});
  assert.deepEqual(result, {PATH: '/tools', HOME: '/home/runner', DOTNET_ROOT: '/dotnet'});
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
    return {status: 201};
  };
  await submit(candidate(), environment(), {repository: {default_branch: 'main'}}, 'profile-linux', request);
  assert.equal(calls, 1);
  await assert.rejects(submit(candidate(), environment(), {repository: {default_branch: 'main'}}, 'profile-linux', async () => ({status: 403})), /HTTP 403/);
});

test('uploader rejects malformed artifact paths and dependency edges before HTTP', () => {
  const payload = candidate();
  payload.manifests['../outside.csproj'] = payload.manifests['Main.csproj'];
  assert.throws(() => validateCandidate(payload), /outside the scan root|manifest path/);
  delete payload.manifests['../outside.csproj'];
  payload.manifests['Main.csproj'].resolved[urls[1]].dependencies = ['toString'];
  assert.throws(() => validateCandidate(payload), /dependency or edge/);
});
