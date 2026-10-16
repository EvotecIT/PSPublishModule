const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');

test('whole-workflow gate requires the real child exit and persisted qualification, retaining inherited guards and literal arguments',
  { skip: process.platform !== 'win32' }, (context) => {
    const version = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', '$PSVersionTable.PSVersion.ToString()'], { encoding: 'utf8', timeout: 30_000 });
    assert.ifError(version.error);
    if (!version.stdout.trim().startsWith('7.6.')) { context.skip('The whole-workflow host prerequisite is PowerShell7.6.'); return; }
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-whole-process-'));
    // A tiny task-owned sibling supplies the real wrapper's documented short Desktop layout.
    const workspace = path.join(path.parse(root).root, 'pfg-' + crypto.randomBytes(4).toString('hex'));
    const repository = path.join(root, 'repository with spaces');
    const build = path.join(repository, 'Build');
    const corpus = path.join(repository, 'Benchmarks', 'PowerShellCompilation', 'Corpus');
    const cli = path.join(root, 'immutable CLI', 'PowerForge.Cli.dll');
    const payload = path.join(root, 'payload.nupkg');
    try {
      fs.mkdirSync(build, { recursive: true });
      fs.mkdirSync(corpus, { recursive: true });
      fs.mkdirSync(path.dirname(cli), { recursive: true });
      fs.writeFileSync(cli, 'test-only immutable closure; never executed');
      fs.copyFileSync(path.resolve(__dirname, '../Invoke-PowerShellWholeWorkflowGate.ps1'), path.join(build, 'Invoke-PowerShellWholeWorkflowGate.ps1'));
      const commonSource = path.resolve(__dirname, '../../Benchmarks/PowerShellCompilation/Corpus/Corpus.Runner.Common.ps1');
      const networkBoundary = `
function Get-VerifiedCorpusPayload {
  param($Uri,$Sha256,$CacheRoot,$CacheExtension,$Label,$AllowedUrlPattern,[switch]$OfflineMode)
  return $env:POWERFORGE_GATE_BOUNDARY_PAYLOAD
}
`;
      fs.writeFileSync(path.join(corpus, 'Corpus.Runner.Common.ps1'), fs.readFileSync(commonSource, 'utf8') + networkBoundary);
      for (const [name, prefix, publicName, version] of [['Yaml', 'yaml', 'powershell-yaml', '0.4.12'], ['PlatyPS', 'platyps', 'platyPS', '0.14.2']]) {
        const directory = path.join(corpus, 'Workflows', name);
        fs.mkdirSync(directory, { recursive: true });
        for (const framework of ['net10.0', 'net472']) {
          const uri = `https://www.powershellgallery.com/api/v2/package/${publicName}/${version}`;
          const workload = { sourceInputs: [{ path: 'fixture.txt' }], origin: { packageUri: uri, packageSha256: 'a'.repeat(64) }, sourceProvenance: { packageUrl: uri, packageSha256: 'a'.repeat(64) } };
          fs.writeFileSync(path.join(directory, `${prefix}-whole-module.${framework}.json`), JSON.stringify({ schemaVersion: 1, workloads: [workload] }));
        }
      }
      const sitemap = path.join(corpus, 'Workflows', 'CompareWebSitemaps');
      fs.mkdirSync(sitemap, { recursive: true });
      fs.writeFileSync(path.join(sitemap, 'workflow.json'), JSON.stringify({ schemaVersion: 1, workloads: [{ id: 'sitemap-boundary-fixture' }] }));
      const qualifier = `
param($PacketPath,$SourceRoot,$CliAssemblyPath,$WorkspacePath,$EvidencePath,$DurableEvidencePath,[switch]$Offline,[switch]$AllowExternalExecution)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$summary=@{passed=1;total=1;wholeArtifactWorkflowsQualified=1;declaredWorkflowScenarios=3}
if($env:POWERFORGE_GATE_BOUNDARY_MODE -eq 'summary-failure'){$summary.passed=0;$summary.wholeArtifactWorkflowsQualified=0}
@{summary=$summary}|ConvertTo-Json|Set-Content -LiteralPath $EvidencePath
@{packet=$PacketPath;source=$SourceRoot;cli=$CliAssemblyPath;offline=[bool]$Offline;authorized=[bool]$AllowExternalExecution;home=$env:DOTNET_CLI_HOME;scratch=$env:NUGET_SCRATCH;certificates=$env:DOTNET_GENERATE_ASPNET_CERTIFICATE;globalTools=$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $DurableEvidencePath 'child-context.json')
if($env:POWERFORGE_GATE_BOUNDARY_MODE -eq 'exit-failure'){exit 23}
# Normal success deliberately does not invoke native code or set LASTEXITCODE.
`;
      fs.writeFileSync(path.join(corpus, 'Invoke-ExternalQualification.ps1'), qualifier);
      const env = { ...process.env,
        DOTNET_CLI_HOME: path.join(root, 'private SDK home'),
        TEMP: path.join(root, 'private temp'), TMP: path.join(root, 'private temp'),
        NUGET_SCRATCH: process.env.NUGET_SCRATCH || path.join(root, 'unused shared scratch'),
        DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: 'false',
        POWERFORGE_GATE_BOUNDARY_PAYLOAD: payload
      };
      fs.mkdirSync(env.DOTNET_CLI_HOME); fs.mkdirSync(env.TEMP);
      const createArchive = `$archive=[IO.Compression.ZipFile]::Open($env:POWERFORGE_GATE_BOUNDARY_PAYLOAD,[IO.Compression.ZipArchiveMode]::Create);try{$writer=[IO.StreamWriter]::new($archive.CreateEntry('fixture.txt').Open());try{$writer.Write('network comparison fixture')}finally{$writer.Dispose()}}finally{$archive.Dispose()}`;
      const setup = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', createArchive], { encoding: 'utf8', env, timeout: 30_000 });
      assert.ifError(setup.error); assert.equal(setup.status, 0, setup.stderr);
      for (const [mode, offline] of [['success', false], ['success-offline', true], ['exit-failure', false], ['summary-failure', false]]) {
        const evidence = path.join(root, 'evidence-' + mode);
        const argv = ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', path.join(build, 'Invoke-PowerShellWholeWorkflowGate.ps1'), '-WorkspacePath', workspace, '-EvidencePath', evidence, '-CliAssemblyPath', cli];
        if (offline) argv.push('-Offline');
        const run = spawnSync('pwsh', argv, { encoding: 'utf8', env: { ...env, POWERFORGE_GATE_BOUNDARY_MODE: mode }, timeout: 60_000 });
        assert.ifError(run.error);
        const succeeded = mode.startsWith('success');
        assert.equal(run.status === 0, succeeded, run.stderr + run.stdout);
        const summary = JSON.parse(fs.readFileSync(path.join(evidence, 'whole-workflow-gate.json'), 'utf8'));
        assert.equal(summary.passed, succeeded);
        assert.equal(summary.compilerClosureUnchanged, true);
        assert.equal(summary.results.length, succeeded ? 5 : 0);
        const lane = path.join(evidence, 'yaml-net10.0');
        const processReceipt = JSON.parse(fs.readFileSync(path.join(lane, 'qualifier.process.json'), 'utf8'));
        assert.equal(processReceipt.ExitCode, mode === 'exit-failure' ? 23 : 0);
        const child = JSON.parse(fs.readFileSync(path.join(lane, 'child-context.json'), 'utf8'));
        assert.equal(child.cli, cli);
        assert.equal(child.source, path.join(workspace, 'Yaml'));
        assert.equal(child.offline, offline);
        assert.equal(child.authorized, true);
        assert.equal(child.home, env.DOTNET_CLI_HOME);
        assert.equal(child.scratch, env.NUGET_SCRATCH);
        assert.equal(child.certificates, 'false');
        assert.equal(child.globalTools, 'false');
      }
    } finally {
      if (fs.existsSync(workspace)) fs.rmSync(workspace, { recursive: true });
      fs.rmSync(root, { recursive: true });
    }
  });
