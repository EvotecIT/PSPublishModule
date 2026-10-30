const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const {spawnSync} = require('node:child_process');
const {buildCandidate} = require('./snapshot.cjs');

const VERSION = '8.0.6';
const ASSETS = Object.freeze({
  linux: {name: 'component-detection-linux-x64', sha256: 'ce5a5bbfa7c7e7e58eb3caaf99d50cebcd8ff38331c2af6a545108f8aeb97443'}
});

async function downloadScanner(directory, platform = process.platform, request = fetch) {
  const asset = ASSETS[platform];
  if (!asset) throw new Error('Scanning requires Linux x64.');
  const response = await request(`https://github.com/microsoft/component-detection/releases/download/v${VERSION}/${asset.name}`, {signal: AbortSignal.timeout(120000)});
  if (!response.ok) throw new Error(`Scanner download failed: HTTP ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  if (crypto.createHash('sha256').update(bytes).digest('hex') !== asset.sha256) throw new Error('Scanner SHA-256 verification failed.');
  const executable = path.join(directory, asset.name);
  fs.writeFileSync(executable, bytes, {flag: 'wx', mode: 0o700});
  return executable;
}

function scannerEnvironment(environment) {
  // Pass only runtime discovery and temporary-directory settings to the scanner.
  return Object.fromEntries(Object.entries(environment).filter(([key]) =>
    /^(PATH|SYSTEMROOT|WINDIR|COMSPEC|TEMP|TMP|TMPDIR|HOME|USERPROFILE|DOTNET_ROOT(?:_X64)?|DOTNET_CLI_HOME|NUGET_PACKAGES)$/i.test(key)));
}

async function scan(root, indexFile, outputFile) {
  if (process.platform !== 'linux' || process.arch !== 'x64') throw new Error('Scanning requires a Linux x64 runner.');
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-component-scan-'));
  try {
    const executable = await downloadScanner(temporary);
    const resultFile = path.join(temporary, 'components.json');
    const result = spawnSync(executable, ['scan', '--SourceDirectory', root, '--ManifestFile', resultFile,
      '--DetectorCategories', 'NuGet', '--Verbosity', 'Quiet', '--Output', temporary], {
      stdio: 'inherit', windowsHide: true, timeout: 8 * 60 * 1000,
      env: {...scannerEnvironment(process.env), TEMP: temporary, TMP: temporary, TMPDIR: temporary,
        DOTNET_BUNDLE_EXTRACT_BASE_DIR: path.join(temporary, 'bundle')}
    });
    if (result.status !== 0) throw new Error(`Component Detection failed: ${result.error || result.status}`);
    const index = JSON.parse(fs.readFileSync(indexFile, 'utf8').replace(/^\uFEFF/, ''));
    const candidate = buildCandidate(JSON.parse(fs.readFileSync(resultFile, 'utf8')), root, index.assets.map(item => item.project));
    fs.writeFileSync(outputFile, JSON.stringify(candidate));
    console.log(`Prepared ${Object.keys(candidate.manifests).length} NuGet manifests with scanner ${VERSION}.`);
  } finally {
    fs.rmSync(temporary, {recursive: true, force: true});
  }
}

if (require.main === module) scan(...process.argv.slice(2)).catch(error => {console.error(error.message); process.exitCode = 1;});
module.exports = {VERSION, ASSETS, downloadScanner, scannerEnvironment, scan};
