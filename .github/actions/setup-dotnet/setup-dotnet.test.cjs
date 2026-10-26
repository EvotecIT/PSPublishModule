'use strict';
const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {versionRequest, selectVersion, sdksFromMetadata, parseGlobalJson} = require('./sdk-versions.cjs');
const {getMetadata, requestedVersions, inspectInstallation, installerArguments, downloadInstaller, resolveInstallRoot, discoverInstallations} = require('./setup-dotnet.cjs');

test('SDK requests preserve pins and feature bands while excluding previews by default', () => {
  const versions = ['10.0.303', '10.0.402', '10.0.403', '10.0.404-preview.1', '11.0.100'];
  for (const request of ['10', '10.x', '10.0', '10.0.x']) assert.equal(selectVersion(versions, versionRequest(request)), '10.0.403');
  assert.equal(selectVersion(versions, versionRequest('10.0.3xx')), '10.0.303');
  assert.equal(selectVersion(versions, versionRequest('10.0.402')), '10.0.402');
  assert.equal(selectVersion(versions, versionRequest('10.0.x'), true), '10.0.404-preview.1');
  assert.equal(selectVersion(versions, versionRequest('8.0.x')), undefined);
});

test('metadata includes every servicing SDK', () => {
  const sdks = sdksFromMetadata({releases: [{sdk: {version: '10.0.403'}, sdks: [
    {version: '10.0.403'}, {version: '10.0.303'}, {version: '10.0.113'}
  ]}]});
  assert.deepEqual([...sdks.keys()], ['10.0.403', '10.0.303', '10.0.113']);
});

test('commented global.json pins are additive and remain unchanged', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-global-test-'));
  const file = path.join(directory, 'global.json');
  const previousVersion = process.env['INPUT_DOTNET-VERSION'];
  const previousGlobal = process.env['INPUT_GLOBAL-JSON-FILE'];
  const document = '{ // SDK policy\n "sdk": { "version": "10.0.303" /* CI pin */ }, "note": "https://example.com/a/*b*/", "quote": "a\\\"//b" }';
  fs.writeFileSync(file, document);
  try {
    process.env['INPUT_DOTNET-VERSION'] = '8.0.x\n10.0.x';
    process.env['INPUT_GLOBAL-JSON-FILE'] = file;
    assert.deepEqual(requestedVersions().map(r => r.value), ['8.0.x', '10.0.x', '10.0.303']);
    assert.equal(fs.readFileSync(file, 'utf8'), document);
    assert.equal(parseGlobalJson(document).note, 'https://example.com/a/*b*/');
    assert.equal(parseGlobalJson(document).quote, 'a"//b');
  } finally {
    if (previousVersion === undefined) delete process.env['INPUT_DOTNET-VERSION'];
    else process.env['INPUT_DOTNET-VERSION'] = previousVersion;
    if (previousGlobal === undefined) delete process.env['INPUT_GLOBAL-JSON-FILE'];
    else process.env['INPUT_GLOBAL-JSON-FILE'] = previousGlobal;
    fs.rmSync(directory, {recursive: true, force: true});
  }
});

test('installation discovery checks architecture and retains runtime-only roots', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-architecture-test-'));
  fs.writeFileSync(path.join(directory, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'), 'fixture');
  const execute = (file, args) => ({status: 0, stdout: args[0] === '--info' ? 'Host:\n  Architecture: x64\n' : ''});
  try {
    assert.equal(inspectInstallation(directory, 'arm64', execute), null);
    assert.deepEqual(inspectInstallation(directory, 'x64', execute), {root: directory, installed: []});
  } finally { fs.rmSync(directory, {recursive: true, force: true}); }
});

test('Windows and POSIX installers receive exact SDKs and only the official CDN', () => {
  for (const windows of [false, true]) {
    const args = installerArguments('installer', 'root with spaces', '10.0.403', 'x64', windows);
    assert.equal(args[args.indexOf(windows ? '-Version' : '--version') + 1], '10.0.403');
    assert.equal(args[args.indexOf(windows ? '-InstallDir' : '--install-dir') + 1], 'root with spaces');
    for (const flag of windows ? ['-AzureFeed', '-UncachedFeed'] : ['--azure-feed', '--uncached-feed']) {
      assert.equal(args[args.indexOf(flag) + 1], 'https://builds.dotnet.microsoft.com/dotnet');
    }
    assert.equal(args.some(a => /channel|runtime|aka\.ms|dotnetorigin/i.test(a)), false);
  }
  assert.throws(() => installerArguments('script', 'root', '10.0.x', 'x64'), /exact SDK version/);
});

test('existing SDK directories cannot satisfy a request with the wrong architecture', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-root-test-'));
  const sdk = path.join(directory, 'sdk', '8.0.426');
  fs.mkdirSync(sdk, {recursive: true});
  fs.writeFileSync(path.join(directory, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'), 'x64 installation');
  try {
    // Architecture discovery rejected the old root. The official installer
    // must not see its sdk/<version> marker and incorrectly return success.
    const isolated = resolveInstallRoot(directory, [], directory, 'x86');
    assert.equal(isolated, path.join(directory, 'x86'));
    assert.equal(fs.existsSync(path.join(isolated, 'sdk', '8.0.426')), false);
    assert.equal(resolveInstallRoot(directory, [{root: directory, installed: ['8.0.426']}], directory, 'x64'), directory);
    assert.equal(fs.existsSync(sdk), true);
    const macHome = path.join(directory, 'mac-user');
    const macRoot = path.join(macHome, '.dotnet');
    fs.mkdirSync(path.join(macRoot, 'sdk', '8.0.426'), {recursive: true});
    fs.writeFileSync(path.join(macRoot, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'), 'arm64 installation');
    assert.equal(resolveInstallRoot('', [], directory, 'x64', 'darwin', macHome), path.join(macRoot, 'x64'));
    assert.equal(fs.existsSync(path.join(macRoot, 'sdk', '8.0.426')), true);
  } finally { fs.rmSync(directory, {recursive: true, force: true}); }
});

test('read-only system SDKs do not become implicit installation targets', () => {
  const stock = '/usr/local/share/dotnet';
  const user = '/Users/runner/.dotnet';
  const inspect = root => ({root, installed: ['10.0.401']});
  const writable = root => root === user;
  assert.deepEqual(discoverInstallations([stock, user], 'arm64', '', inspect, writable), [{root: user, installed: ['10.0.401']}]);
  assert.deepEqual(discoverInstallations([stock], 'arm64', '', inspect, writable), []);
  // An explicit directory remains the caller's choice, including read-only reuse.
  assert.deepEqual(discoverInstallations([stock], 'arm64', stock, inspect, writable), [{root: stock, installed: ['10.0.401']}]);
  assert.equal(resolveInstallRoot('', [], '/toolcache', 'arm64', 'darwin', path.join(os.tmpdir(), 'mac-user')), path.resolve(os.tmpdir(), 'mac-user', '.dotnet'));
});

test('metadata refuses origin and aka.ms before network access and rejects redirects', async () => {
  const previousFetch = global.fetch;
  const calls = [];
  global.fetch = async (url, options) => { calls.push({url, options}); return new Response('{}', {status: 200}); };
  try {
    await assert.rejects(getMetadata('https://dotnetorigin.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'), /outside the official CDN/);
    await assert.rejects(getMetadata('https://aka.ms/dotnet/10.0/releases.json'), /outside the official CDN/);
    assert.equal(calls.length, 0);
    await getMetadata('https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json');
    assert.equal(calls[0].options.redirect, 'error');
  } finally { global.fetch = previousFetch; }
});

test('an altered Microsoft installer is rejected before execution', async () => {
  const previousFetch = global.fetch;
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-installer-test-'));
  global.fetch = async () => new Response('altered installer', {status: 200});
  try {
    await assert.rejects(downloadInstaller(directory), /SHA-256 verification failed/);
    assert.deepEqual(fs.readdirSync(directory), []);
  } finally {
    global.fetch = previousFetch;
    fs.rmSync(directory, {recursive: true, force: true});
  }
});
