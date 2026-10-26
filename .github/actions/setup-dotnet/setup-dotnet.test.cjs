'use strict';
const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {versionRequest, selectVersion, sdksFromMetadata, parseGlobalJson, globalSdkRequest} = require('./sdk-versions.cjs');
const {getMetadata, requestedVersions, inspectInstallation, installerArguments, downloadInstaller, resolveInstallRoot, discoverInstallations, installationTargets, expose} = require('./setup-dotnet.cjs');

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

test('global.json owns primary and additional SDK policy without workflow version overrides', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-global-test-'));
  const file = path.join(directory, 'global.json');
  const previousVersion = process.env['INPUT_DOTNET-VERSION'];
  const previousGlobal = process.env['INPUT_GLOBAL-JSON-FILE'];
  const document = '{ // SDK policy\n "sdk": { "version": "10.0.300", "rollForward": "latestFeature" /* CI policy */ }, "powerforge": { "sdks": [{"version":"8.0.400", "rollForward":"latestPatch"}] }, "note": "https://example.com/a/*b*/", "quote": "a\\\"//b" }';
  fs.writeFileSync(file, document);
  try {
    process.env['INPUT_DOTNET-VERSION'] = '8.0.x\n10.0.x';
    process.env['INPUT_GLOBAL-JSON-FILE'] = file;
    assert.deepEqual(requestedVersions().map(r => r.value), ['10.0.300', '8.0.400']);
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

test('global.json rollForward selects available feature bands and required servicing floors', () => {
  const versions = ['10.0.100', '10.0.302', '10.0.303', '10.0.403', '10.0.404-preview.1', '11.0.100'];
  assert.equal(selectVersion(versions, globalSdkRequest({version: '10.0.300', rollForward: 'latestFeature'})), '10.0.403');
  assert.equal(selectVersion(versions, globalSdkRequest({version: '10.0.300', rollForward: 'latestPatch'})), '10.0.303');
  assert.equal(selectVersion(versions, globalSdkRequest({version: '10.0.300'})), '10.0.303');
  assert.equal(selectVersion(['10.0.302'], globalSdkRequest({version: '10.0.303', rollForward: 'latestPatch'})), undefined);
  assert.equal(selectVersion(versions, globalSdkRequest({version: '10.0.303', rollForward: 'disable'})), '10.0.303');
  assert.throws(() => globalSdkRequest({version: '10.0.300', rollForward: 'unknown'}), /rollForward/);
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
  const existing = discoverInstallations([stock, user], 'arm64', inspect);
  assert.equal(selectVersion(existing[0].installed, versionRequest('10.0.x')), '10.0.401');
  assert.deepEqual(installationTargets(existing, '', writable), [{root: user, installed: ['10.0.401']}]);
  assert.deepEqual(installationTargets([existing[0]], '', writable), []);
  // An explicit directory remains the caller's choice, including read-only reuse.
  assert.deepEqual(installationTargets([existing[0]], stock, writable), [{root: stock, installed: ['10.0.401']}]);
  assert.equal(resolveInstallRoot('', [], '/toolcache', 'arm64', 'darwin', path.join(os.tmpdir(), 'mac-user')), path.resolve(os.tmpdir(), 'mac-user', '.dotnet'));
});

test('a reused read-only root does not force later writes and earlier SDKs survive a root change', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-sequence-test-'));
  const keys = ['GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_OUTPUT', 'INPUT_DOTNET-VERSION', 'INPUT_GLOBAL-JSON-FILE', 'EVOTEC_DOTNET_SDKS_X64', 'EVOTEC_DOTNET_SDKS_ARM64'];
  const previous = Object.fromEntries(keys.map(key => [key, process.env[key]]));
  try {
    process.env.GITHUB_ENV = path.join(directory, 'env');
    delete process.env.GITHUB_PATH;
    delete process.env.GITHUB_OUTPUT;
    expose('/read-only/sdk', ['8.0.426'], 'x64');
    const exported = fs.readFileSync(process.env.GITHUB_ENV, 'utf8');
    assert.equal(exported.includes('DOTNET_INSTALL_DIR='), false);
    for (const line of exported.trim().split(/\r?\n/)) {
      const split = line.indexOf('=');
      if (line.startsWith('EVOTEC_DOTNET_SDKS_')) process.env[line.slice(0, split)] = line.slice(split + 1);
    }
    process.env['INPUT_DOTNET-VERSION'] = '10.0.403';
    const globalFile = path.join(directory, 'global.json');
    fs.writeFileSync(globalFile, JSON.stringify({sdk: {version: '10.0.300', rollForward: 'latestFeature'}}));
    process.env['INPUT_GLOBAL-JSON-FILE'] = globalFile;
    delete process.env.EVOTEC_DOTNET_SDKS_ARM64;
    assert.deepEqual(requestedVersions('x64').map(r => r.value), ['10.0.300', '8.0.426']);
    assert.deepEqual(requestedVersions('arm64').map(r => r.value), ['10.0.300']);
    assert.equal(selectVersion(['10.0.403'], requestedVersions('x64')[1]), undefined);
  } finally {
    for (const [key, value] of Object.entries(previous)) {
      if (value === undefined) delete process.env[key]; else process.env[key] = value;
    }
    fs.rmSync(directory, {recursive: true, force: true});
  }
});

test('architecture children remain discoverable for explicit and macOS roots on later jobs', () => {
  for (const parent of [path.resolve('explicit-sdk'), path.resolve('mac-user', '.dotnet')]) {
    const child = path.join(parent, 'x86');
    const inspect = (root, architecture) => root === child && architecture === 'x86'
      ? {root, installed: ['8.0.426']} : null;
    const existing = discoverInstallations([parent], 'x86', inspect);
    assert.deepEqual(existing, [{root: child, installed: ['8.0.426']}]);
    assert.equal(selectVersion(existing[0].installed, versionRequest('8.0.426')), '8.0.426');
    assert.deepEqual(discoverInstallations([parent], 'arm64', inspect), []);
  }
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
