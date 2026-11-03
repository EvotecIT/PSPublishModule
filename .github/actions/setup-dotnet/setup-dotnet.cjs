'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const {spawnSync} = require('node:child_process');
const {compareVersions, versionRequest, selectVersion, sdksFromMetadata, parseGlobalJson, globalSdkRequest} = require('./sdk-versions.cjs');

const CDN = 'https://builds.dotnet.microsoft.com/dotnet';
const INSTALLER_COMMIT = 'da3ce11ba63f3dbb0fb835d41bda2665d5c48e84'; // dotnet/install-scripts v2026.07.21
const INSTALLER_HASHES = {
  sh: '082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e',
  ps1: '3bb07bc8025211836c1e4f9d3f6a044e55b1fb6eec518a6c78851d04e210442b'
};
const input = name => process.env[`INPUT_${name.toUpperCase()}`]?.trim() || '';

async function fetchChecked(url) {
  for (let attempt = 1; ; attempt++) {
    try {
      const response = await fetch(url, {redirect: 'error', signal: AbortSignal.timeout(60000)});
      if (!response.ok) {
        await response.body?.cancel();
        throw new Error(`HTTP ${response.status}: ${url}`);
      }
      return response;
    } catch (error) {
      if (attempt === 3) throw error;
      await new Promise(resolve => setTimeout(resolve, attempt * 1000));
    }
  }
}

async function getMetadata(url) {
  const parsed = new URL(url);
  if (parsed.origin !== new URL(CDN).origin || !parsed.pathname.startsWith('/dotnet/release-metadata/')) {
    throw new Error(`Refusing metadata outside the official CDN: ${url}`);
  }
  return (await fetchChecked(url)).json();
}

function inspectInstallation(root, architecture, execute = spawnSync) {
  if (!root) return null;
  const executable = path.join(root, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
  if (!fs.existsSync(executable)) return null;
  const info = execute(executable, ['--info'], {encoding: 'utf8', timeout: 30000});
  if (info.status !== 0 || !new RegExp(`^\\s*Architecture:\\s+${architecture}\\s*$`, 'mi').test(info.stdout)) return null;
  const result = execute(executable, ['--list-sdks'], {encoding: 'utf8', timeout: 30000});
  if (result.status !== 0) return null;
  const installed = result.stdout.split(/\r?\n/).map(line => /^(\S+)\s+\[/.exec(line)?.[1]).filter(Boolean);
  return {root, installed}; // A runtime-only installation is also a valid target.
}

function installationRoots(architecture) {
  const explicit = process.env.DOTNET_INSTALL_DIR;
  if (explicit) return [path.resolve(explicit)];
  const roots = new Set([process.env.DOTNET_ROOT]);
  if (process.platform === 'darwin') roots.add(path.join(os.homedir(), '.dotnet'));
  const cacheBase = process.env.RUNNER_TOOL_CACHE || process.env.RUNNER_TEMP;
  if (cacheBase) roots.add(path.join(cacheBase, 'evotec-dotnet', architecture));
  for (const directory of (process.env.PATH || '').split(path.delimiter)) {
    const executable = path.join(directory, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
    if (fs.existsSync(executable)) roots.add(path.dirname(fs.realpathSync(executable)));
  }
  return [...roots].filter(Boolean);
}

function writableInstallation(root) {
  try {
    fs.accessSync(root, fs.constants.W_OK);
    return true;
  } catch { return false; }
}

function discoverInstallations(roots, architecture, inspect = inspectInstallation) {
  // Mixed-architecture roots use a child for installation. Discover that same
  // child on the next job before deciding that a download is necessary.
  const candidates = [...new Set(roots.flatMap(root => [root, path.join(root, architecture)]))];
  return candidates.map(root => inspect(root, architecture)).filter(Boolean);
}

function installationTargets(existing, explicit, writable = writableInstallation) {
  return existing.filter(candidate => explicit || writable(candidate.root));
}

function requestedVersions(architecture) {
  const globalFile = input('global-json-file') || 'global.json';
  const global = parseGlobalJson(fs.readFileSync(globalFile, 'utf8'));
  if (!global.sdk?.version) throw new Error(`No sdk.version in ${globalFile}`);
  const additional = global.powerforge?.sdks || [];
  if (!Array.isArray(additional)) throw new Error('global.json powerforge.sdks must be an array of SDK declarations.');
  const requests = [global.sdk, ...additional].map(globalSdkRequest);
  const extraVersions = input('additional-sdk-versions').split(/\s+/).filter(Boolean);
  requests.push(...extraVersions.map(versionRequest));
  // A later action may need a writable root instead of a reused system root.
  // Carry earlier requested SDKs into that installation without changing it.
  if (architecture) requests.push(...(process.env[`EVOTEC_DOTNET_SDKS_${architecture.toUpperCase()}`] || '').split(',').filter(Boolean).map(versionRequest));
  return requests;
}

function expose(root, versions, architecture) {
  const variables = {DOTNET_ROOT: root, [`DOTNET_ROOT_${architecture.toUpperCase()}`]: root,
    [`EVOTEC_DOTNET_SDKS_${architecture.toUpperCase()}`]: versions.join(',')};
  for (const [name, value] of Object.entries(variables)) {
    if (/[\r\n]/.test(value)) throw new Error(`Invalid .NET directory for ${name}`);
    if (process.env.GITHUB_ENV) fs.appendFileSync(process.env.GITHUB_ENV, `${name}=${value}${os.EOL}`);
  }
  if (process.env.GITHUB_PATH) fs.appendFileSync(process.env.GITHUB_PATH, `${root}${os.EOL}`);
  if (process.env.GITHUB_OUTPUT) {
    const highest = [...versions].sort(compareVersions).at(-1);
    fs.appendFileSync(process.env.GITHUB_OUTPUT, `dotnet-version=${highest}${os.EOL}dotnet-root=${root}${os.EOL}`);
  }
  console.log(`.NET SDKs ${versions.join(', ')} ready in ${root}`);
}

function installerArguments(script, root, version, architecture, windows = process.platform === 'win32') {
  // Exact versions bypass aka.ms. Both feeds are explicit; the SDK includes its
  // runtime, so there is no separate floating LTS runtime installation.
  if (!versionRequest(version).exact) throw new Error('The installer requires an exact SDK version.');
  return windows
    ? ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script,
      '-Version', version, '-Architecture', architecture, '-InstallDir', root,
      '-AzureFeed', CDN, '-UncachedFeed', CDN, '-NoPath', '-SkipNonVersionedFiles']
    : [script, '--version', version, '--architecture', architecture, '--install-dir', root,
      '--azure-feed', CDN, '--uncached-feed', CDN, '--no-path', '--skip-non-versioned-files'];
}

function resolveInstallRoot(explicit, existing, cacheBase, architecture, platform = process.platform, home = os.homedir()) {
  const cached = path.join(cacheBase, 'evotec-dotnet', architecture);
  const macDefault = path.join(home, '.dotnet');
  const fallback = platform === 'darwin' && (!fs.existsSync(macDefault) || writableInstallation(macDefault)) ? macDefault : cached;
  let root = path.resolve(explicit || existing[0]?.root || fallback);
  const executable = path.join(root, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
  if (fs.existsSync(executable) && !existing.some(candidate => path.resolve(candidate.root) === root)) {
    // Both an explicit directory and the normal macOS fallback may already
    // contain another architecture. Keep it and isolate this request.
    root = path.join(root, architecture);
  }
  return root;
}

async function downloadInstaller(directory, windows = process.platform === 'win32') {
  const extension = windows ? 'ps1' : 'sh';
  const url = `https://raw.githubusercontent.com/dotnet/install-scripts/${INSTALLER_COMMIT}/src/dotnet-install.${extension}`;
  const contents = Buffer.from(await (await fetchChecked(url)).arrayBuffer());
  const hash = crypto.createHash('sha256').update(contents).digest('hex');
  if (hash !== INSTALLER_HASHES[extension]) throw new Error('Microsoft dotnet-install script SHA-256 verification failed.');
  const script = path.join(directory, `dotnet-install.${extension}`);
  fs.writeFileSync(script, contents, {flag: 'wx'});
  return script;
}

async function main() {
  const requestedArchitecture = input('architecture') || process.arch;
  const architecture = ({amd64: 'x64', ia32: 'x86'})[requestedArchitecture] || requestedArchitecture;
  if (!['x64', 'x86', 'arm64', 'arm'].includes(architecture)) throw new Error(`Unsupported SDK architecture: ${architecture}`);
  const requests = requestedVersions(architecture);
  const existing = discoverInstallations(installationRoots(architecture), architecture);
  for (const {root, installed} of existing) {
    const selected = requests.map(request => selectVersion(installed, request, request.allowPreview));
    if (selected.every(Boolean)) return expose(root, selected, architecture);
  }
  const metadata = new Map();
  const selected = [];
  for (const request of requests) {
    if (request.exact) {
      selected.push(request.value);
      continue;
    }
    if (!metadata.has(request.channel)) {
      metadata.set(request.channel, sdksFromMetadata(await getMetadata(`${CDN}/release-metadata/${request.channel}/releases.json`)));
    }
    const version = selectVersion([...metadata.get(request.channel).keys()], request, request.allowPreview);
    if (!version) throw new Error(`No published SDK matches ${request.value} in the official release metadata.`);
    selected.push(version);
  }
  const versions = [...new Set(selected)].sort(compareVersions);
  const cacheBase = process.env.RUNNER_TOOL_CACHE || process.env.RUNNER_TEMP;
  if (!cacheBase) throw new Error('RUNNER_TOOL_CACHE or RUNNER_TEMP is required.');
  const targets = installationTargets(existing, process.env.DOTNET_INSTALL_DIR);
  const root = resolveInstallRoot(process.env.DOTNET_INSTALL_DIR, targets, cacheBase, architecture);
  const temporary = fs.mkdtempSync(path.join(process.env.RUNNER_TEMP || os.tmpdir(), 'evotec-dotnet-install-'));
  try {
    const script = await downloadInstaller(temporary);
    for (const version of versions) {
      const command = process.platform === 'win32' ? 'powershell.exe' : 'bash';
      console.log(`Installing .NET SDK ${version} from ${CDN}`);
      const result = spawnSync(command, installerArguments(script, root, version, architecture), {stdio: 'inherit', timeout: 600000});
      if (result.status !== 0) throw new Error(`dotnet-install failed for ${version}: ${result.error || result.status}`);
    }
    const nativeArchitecture = ({ia32: 'x86'})[process.arch] || process.arch;
    if (architecture === nativeArchitecture) {
      const installed = inspectInstallation(root, architecture)?.installed || [];
      if (!versions.every(v => installed.includes(v))) throw new Error('dotnet-install did not install every requested SDK.');
    }
    expose(root, versions, architecture);
  } finally {
    fs.rmSync(temporary, {recursive: true, force: true});
  }
}

if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
module.exports = {main, getMetadata, requestedVersions, inspectInstallation, installerArguments, downloadInstaller, resolveInstallRoot, discoverInstallations, installationTargets, expose};
