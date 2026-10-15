const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const repository = path.resolve(__dirname, '../..');
const lines = fs.readFileSync(path.join(repository, '.github/workflows/BuildModule.yml'), 'utf8').split(/\r?\n/);
const start = lines.findIndex(line => line.trim() === '- name: Qualify complete public modules and script workflow');
const run = lines.findIndex((line, index) => index > start && line.trim() === 'run: |');
const end = lines.findIndex((line, index) => index > run && line.trim().startsWith('$env:PATH ='));
const creation = lines.slice(run + 1, end).map(line => line.trimStart()).join('\n');

test('CI whole-workflow creation admits long runner temp and preserves its fresh short ownership',
  { skip: process.platform !== 'win32' }, () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-ci-admission-'));
    const runner = path.join(root, 'actions-runner', '_work', '_temp');
    const gate = path.join(runner, 'powerforge-compiler-gate');
    let workspace;
    fs.mkdirSync(gate, { recursive: true });
    try {
      const script = `${creation}
$env:DOTNET_CLI_HOME=Join-Path $workflowRoot 'home'
$env:TEMP=Join-Path $workflowRoot 'temp'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
$message=$null
$repeatMessage=$null
try { New-WholeWorkflowWorkspace -RunnerTemp $env:RUNNER_TEMP | Out-Null }
catch { $repeatMessage=$_.Exception.Message }
try { & ./Build/Invoke-PowerShellWholeWorkflowGate.ps1 -WorkspacePath $workflowRoot -EvidencePath (Join-Path $gateRoot 'evidence') -CliAssemblyPath (Join-Path $gateRoot 'missing-cli.dll') }
catch { $message=$_.Exception.Message }
@{workspace=$workflowRoot;message=$message;repeatMessage=$repeatMessage;exists=Test-Path -LiteralPath $workflowRoot}|ConvertTo-Json -Compress
`;
      const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', script], {
        cwd: repository, env: { ...process.env, RUNNER_TEMP: runner, NUGET_SCRATCH: path.join(root, 'unused-shared-locks') },
        encoding: 'utf8', timeout: 30_000
      });
      assert.ifError(result.error);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      const actual = JSON.parse(result.stdout.trim());
      workspace = actual.workspace;
      assert.ok(runner.length > 25, 'The regression must use a genuinely long runner temp.');
      assert.ok(workspace.length <= 25, `CI created ${workspace.length} characters: ${workspace}`);
      assert.equal(actual.exists, true);
      assert.match(actual.repeatMessage, /Prior whole-workflow ownership must be preserved/);
      assert.match(actual.message, /^Build the source CLI first:/, 'The real gate must admit the root before checking its CLI.');
      const owner = JSON.parse(fs.readFileSync(path.join(workspace, '.powerforge-whole-workflow-owner.json'), 'utf8'));
      assert.equal(owner.runnerTemp, runner);
      assert.equal(owner.workspacePath, workspace);
      assert.equal(fs.readFileSync(path.join(gate, 'whole-workflow-workspace.json'), 'utf8'), fs.readFileSync(path.join(workspace, '.powerforge-whole-workflow-owner.json'), 'utf8'));
    } finally {
      if (workspace && fs.existsSync(workspace) && !workspace.startsWith(root + path.sep)) {
        const owner = JSON.parse(fs.readFileSync(path.join(workspace, '.powerforge-whole-workflow-owner.json'), 'utf8'));
        assert.equal(owner.runnerTemp, runner);
        assert.equal(owner.workspacePath, workspace);
        fs.rmSync(workspace, { recursive: true });
      }
      fs.rmSync(root, { recursive: true });
    }
  });
