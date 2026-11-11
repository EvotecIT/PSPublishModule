const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawn, spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const repository = path.resolve(__dirname, '../..');
const scripts = process.env.POWERFORGE_RUNNER_SCRIPTS || path.join(repository, 'Build/RunnerMaintenance');
const shells = process.platform === 'win32' ? ['pwsh.exe', 'powershell.exe'] : [];

// Service/worker identity, registry values and the system root location are external
// boundaries. Directory creation and ACL enforcement are real and require an administrator.
const platformBoundary = `
function Join-Path {
    param($Path,$ChildPath)
    if ($Path -eq $env:SystemRoot -and $ChildPath -like 'SystemTemp*') { $Path=$env:PROBE_WINDOWS }
    Microsoft.PowerShell.Management\\Join-Path -Path $Path -ChildPath $ChildPath
}
function Get-ItemProperty {
    param($LiteralPath)
    if (($env:PROBE_INHERITED -eq 'machine' -and $LiteralPath -like '*Session Manager*') -or
        ($env:PROBE_INHERITED -eq 'system' -and $LiteralPath -like '*HKEY_USERS*')) {
        return [pscustomobject]@{ACTIONS_RUNNER_HOOK_JOB_COMPLETED='C:\\existing-hook.ps1'}
    }
    if ($env:PROBE_INHERITED -eq 'service' -and $LiteralPath -like '*Services*') {
        return [pscustomobject]@{Environment=@('ACTIONS_RUNNER_HOOK_JOB_STARTED=C:\\existing-service-hook.ps1')}
    }
    return [pscustomobject]@{}
}
function Get-CimInstance {
    param($ClassName,$Filter)
    if ($ClassName -eq 'Win32_Service') {
        return [pscustomobject]@{Name='fixture-service';StartName='LocalSystem';State='Stopped';
            PathName=('"'+(Join-Path $env:PROBE_RUNNER 'bin/RunnerService.exe')+'"')}
    }
    if ($env:PROBE_ACTIVE -eq 'true' -or ($Filter -and $env:PROBE_NO_WORKER -ne 'true')) {
        [pscustomobject]@{Name='Runner.Worker.exe';ExecutablePath=(Join-Path $env:PROBE_RUNNER 'bin/Runner.Worker.exe');ParentProcessId=0}
    }
}
`;

function fixture() {
  const root = fs.mkdtempSync(path.join(process.env.TEST_TMP_ROOT || path.join(process.env.SystemRoot, 'SystemTemp'), 'powerforge-runner-temp-'));
  const runner = path.join(root, 'runner');
  const windows = path.join(root, 'windows');
  const programFiles = path.join(root, 'program-files');
  const scratch = path.join(windows, 'SystemTemp', 'NuGetScratch');
  fs.mkdirSync(runner, { recursive: true });
  fs.mkdirSync(scratch, { recursive: true });
  fs.mkdirSync(programFiles);
  fs.writeFileSync(path.join(runner, '.runner'), JSON.stringify({ agentName: 'fixture-runner', gitHubUrl: 'https://github.com/EvotecIT' }));
  const original = Buffer.from('UNCHANGED=literal-value\nPATH=keep-this-path\n', 'utf8');
  fs.writeFileSync(path.join(runner, '.env'), original);
  return { root, runner, windows, programFiles, scratch, original,
    temp: path.join(windows, 'SystemTemp', 'PowerForge.RunnerJobs', 'fixture-runner'),
    installation: path.join(programFiles, 'PowerForge.RunnerMaintenance', 'fixture-runner') };
}

function invoke(shell, f, command, extraEnv = {}) {
  const text = `$ErrorActionPreference='Stop'\nforeach($m in @('Management','Security','Utility')) { Import-Module (Join-Path $PSHOME ('Modules/Microsoft.PowerShell.'+$m+'/Microsoft.PowerShell.'+$m+'.psd1')) -Force -ErrorAction Stop }\n$env:ProgramFiles=$env:PROBE_PROGRAM_FILES\n${platformBoundary}\ntry { ${command}; exit 0 } catch { [Console]::Error.WriteLine($_.Exception.Message); [Console]::Error.WriteLine($_.ScriptStackTrace); exit 1 }`;
  const result = spawnSync(shell, ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(text, 'utf16le').toString('base64')], {
    cwd: repository, encoding: 'utf8', windowsHide: true, timeout: 30_000,
    env: { ...process.env, PROBE_WINDOWS: f.windows, PROBE_PROGRAM_FILES: f.programFiles, PROBE_RUNNER: f.runner,
      PROBE_SCRATCH: f.scratch, PROBE_SCRIPTS: scripts, GITHUB_ACTIONS: 'true', GITHUB_RUN_ID: '12345', ...extraEnv }
  });
  assert.ifError(result.error);
  return result;
}

const install = (mode) => `& (Join-Path $env:PROBE_SCRIPTS 'Install-RunnerJobTempCleanup.ps1') -RunnerRoot $env:PROBE_RUNNER -NuGetScratchRoot $env:PROBE_SCRATCH -ExpectedOrganization EvotecIT -Mode ${mode} | ConvertTo-Json -Depth 4`;

for (const shell of shells) {
  for (const absent of [false, true]) {
    test(`${shell}: plan, install and restore preserve original runner environment (${absent ? 'absent' : 'existing'})`, () => {
      const f = fixture();
      try {
        if (absent) fs.unlinkSync(path.join(f.runner, '.env'));
        let result = invoke(shell, f, install('Plan'));
        assert.equal(result.status, 0, result.stdout + result.stderr);
        assert.equal(fs.existsSync(f.temp), false, 'Planning must not create temporary storage.');
        result = invoke(shell, f, install('Apply'));
        assert.equal(result.status, 0, result.stdout + result.stderr);
        const lines = fs.readFileSync(path.join(f.runner, '.env'), 'utf8');
        for (const name of ['TEMP', 'TMP', 'SystemTemp']) assert.ok(lines.includes(`${name}=${f.temp}\r\n`), name);
        assert.ok(lines.includes(`NUGET_SCRATCH=${f.scratch}\r\n`));
        assert.ok(lines.includes(`ACTIONS_RUNNER_HOOK_JOB_STARTED=${path.join(f.installation, 'Invoke-RunnerJobTempCleanup.ps1')}`));
        assert.ok(lines.includes(`ACTIONS_RUNNER_HOOK_JOB_COMPLETED=${path.join(f.installation, 'Invoke-RunnerJobTempCleanup.ps1')}`));
        if (!absent) assert.ok(lines.includes('UNCHANGED=literal-value\r\nPATH=keep-this-path'));
        result = invoke(shell, f, install('Restore'));
        assert.equal(result.status, 0, result.stdout + result.stderr);
        assert.equal(fs.existsSync(path.join(f.runner, '.env')), !absent);
        if (!absent) assert.deepEqual(fs.readFileSync(path.join(f.runner, '.env')), f.original);
        assert.ok(fs.existsSync(f.scratch), 'Shared NuGet locks must survive installation and rollback.');
      } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
    });
  }

  for (const scenario of ['active-runner', 'existing-hook', 'later-env-edit', 'redirected-temp']) {
    test(`${shell}: installation preserves ${scenario}`, () => {
      const f = fixture();
      const outside = path.join(f.root, 'outside');
      fs.mkdirSync(outside);
      fs.writeFileSync(path.join(outside, 'preserve.txt'), 'foreign');
      try {
        let result;
        if (scenario === 'active-runner') result = invoke(shell, f, install('Apply'), { PROBE_ACTIVE: 'true' });
        if (scenario === 'existing-hook') {
          fs.appendFileSync(path.join(f.runner, '.env'), 'ACTIONS_RUNNER_HOOK_JOB_COMPLETED=C:\\existing-hook.ps1\n');
          result = invoke(shell, f, install('Apply'));
        }
        if (scenario === 'redirected-temp') {
          fs.symlinkSync(outside, path.join(f.windows, 'SystemTemp', 'PowerForge.RunnerJobs'), 'junction');
          result = invoke(shell, f, install('Apply'));
        }
        if (scenario === 'later-env-edit') {
          result = invoke(shell, f, install('Apply'));
          assert.equal(result.status, 0, result.stdout + result.stderr);
          fs.appendFileSync(path.join(f.runner, '.env'), 'LATER=preserve\r\n');
          const later = fs.readFileSync(path.join(f.runner, '.env'));
          result = invoke(shell, f, install('Restore'));
          assert.deepEqual(fs.readFileSync(path.join(f.runner, '.env')), later);
        }
        assert.notEqual(result.status, 0, result.stdout + result.stderr);
        const reason = { 'active-runner': /Stop the idle runner/, 'existing-hook': /existing job hook must be preserved/,
          'later-env-edit': /automatic restore is held/, 'redirected-temp': /Redirected path is not supported/ }[scenario];
        assert.match(result.stdout + result.stderr, reason, 'Rejection must reach the intended boundary.');
        assert.equal(fs.readFileSync(path.join(outside, 'preserve.txt'), 'utf8'), 'foreign');
        if (scenario === 'active-runner') assert.deepEqual(fs.readFileSync(path.join(f.runner, '.env')), f.original);
      } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
    });
  }

  for (const location of ['machine', 'system', 'service']) {
    test(`${shell}: preserves a hook inherited from the ${location} environment`, () => {
      const f = fixture();
      try {
        const result = invoke(shell, f, install('Apply'), { PROBE_INHERITED: location });
        assert.notEqual(result.status, 0, result.stdout + result.stderr);
        assert.match(result.stdout + result.stderr, /existing inherited service hook must be preserved/);
        assert.deepEqual(fs.readFileSync(path.join(f.runner, '.env')), f.original);
        assert.equal(fs.existsSync(f.installation), false);
        assert.equal(fs.existsSync(f.temp), false);
      } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
    });
  }

  test(`${shell}: rejects an installation parent with unprivileged delete-child access`, () => {
    const f = fixture();
    try {
      const weakenFixtureOnly = `
$acl=Get-Acl -LiteralPath $env:ProgramFiles
$rule=[Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'),'DeleteSubdirectoriesAndFiles','Allow')
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $env:ProgramFiles -AclObject $acl
${install('Apply')}`;
      const result = invoke(shell, f, weakenFixtureOnly);
      assert.notEqual(result.status, 0, result.stdout + result.stderr);
      assert.match(result.stdout + result.stderr, /permissions permit an unprivileged hook replacement/);
      assert.deepEqual(fs.readFileSync(path.join(f.runner, '.env')), f.original);
      assert.equal(fs.existsSync(f.installation), false);
    } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
  });

  test(`${shell}: a missing script copy rolls back and allows a normal retry`, () => {
    const f = fixture();
    const bundle = path.join(f.root, 'incomplete-bundle');
    fs.mkdirSync(bundle);
    for (const name of ['Install-RunnerJobTempCleanup.ps1', 'Invoke-RunnerJobTempCleanup.ps1']) {
      fs.copyFileSync(path.join(scripts, name), path.join(bundle, name));
    }
    try {
      let result = invoke(shell, f, install('Apply'), { PROBE_SCRIPTS: bundle });
      assert.notEqual(result.status, 0, result.stdout + result.stderr);
      assert.match(result.stdout + result.stderr, /Clear-RunnerJobTemp\.ps1/);
      assert.deepEqual(fs.readFileSync(path.join(f.runner, '.env')), f.original);
      assert.equal(fs.existsSync(f.installation), false);
      assert.equal(fs.existsSync(f.temp), false);
      assert.equal(fs.existsSync(path.join(f.programFiles, 'PowerForge.RunnerMaintenance', 'fixture-runner.installation.json')), false);
      result = invoke(shell, f, install('Apply'));
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.ok(fs.existsSync(path.join(f.installation, 'configuration.json')));
    } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
  });

  test(`${shell}: abrupt termination after directory creation has a supported recovery`, () => {
    const f = fixture();
    const bundle = path.join(f.root, 'terminated-bundle');
    fs.mkdirSync(bundle);
    const installer = fs.readFileSync(path.join(scripts, 'Install-RunnerJobTempCleanup.ps1'), 'utf8');
    const interrupted = installer.replace(/^(\s*)New-PrivateDirectory \$directory\s*$/m,
      '$1New-PrivateDirectory $directory\n$1[Environment]::Exit(91)');
    assert.notEqual(interrupted, installer, 'Termination must occur immediately after the real directory mutation.');
    fs.writeFileSync(path.join(bundle, 'Install-RunnerJobTempCleanup.ps1'), interrupted);
    try {
      let result = invoke(shell, f, install('Apply'), { PROBE_SCRIPTS: bundle });
      assert.equal(result.status, 91, result.stdout + result.stderr);
      assert.deepEqual(fs.readFileSync(path.join(f.runner, '.env')), f.original);
      assert.ok(fs.existsSync(path.dirname(f.temp)), 'The process must terminate after creating the directory.');
      // Recovery must retain unexpected contents, then succeed once those contents are removed.
      const foreign = path.join(path.dirname(f.temp), 'unexpected.txt');
      fs.writeFileSync(foreign, 'preserve unexpected data');
      result = invoke(shell, f, install('Restore'));
      assert.notEqual(result.status, 0, result.stdout + result.stderr);
      assert.equal(fs.readFileSync(foreign, 'utf8'), 'preserve unexpected data');
      fs.unlinkSync(foreign);
      result = invoke(shell, f, install('Restore'));
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.equal(fs.existsSync(path.dirname(f.temp)), false);
      result = invoke(shell, f, install('Apply'));
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.ok(fs.existsSync(path.join(f.installation, 'configuration.json')));
    } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
  });

  test(`${shell}: assigned worker cleans private temp and preserves junctions, locked files and shared locks`, () => {
    const f = fixture();
    try {
      let result = invoke(shell, f, install('Apply'));
      assert.equal(result.status, 0, result.stdout + result.stderr);
      fs.writeFileSync(path.join(f.temp, 'completed.tmp'), 'completed output');
      fs.writeFileSync(path.join(f.temp, 'readonly.tmp'), 'completed read-only output');
      fs.chmodSync(path.join(f.temp, 'readonly.tmp'), 0o444);
      fs.writeFileSync(path.join(f.temp, 'locked.tmp'), 'live file');
      fs.writeFileSync(path.join(f.scratch, 'shared.lock'), 'preserve');
      fs.symlinkSync(f.scratch, path.join(f.temp, 'redirected'), 'junction');
      const cleanup = `
$config=Join-Path $env:ProgramFiles 'PowerForge.RunnerMaintenance/fixture-runner/configuration.json'
$root=Join-Path $env:SystemRoot 'SystemTemp/PowerForge.RunnerJobs/fixture-runner'
$env:TMP=$root
$env:TEMP=$root
$env:SystemTemp=$root
$handle=[IO.File]::Open((Join-Path $root 'locked.tmp'),'Open','Read','None')
try { & (Join-Path (Split-Path $config -Parent) 'Clear-RunnerJobTemp.ps1') -ConfigurationPath $config }
finally { $handle.Dispose() }
`;
      result = invoke(shell, f, cleanup);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.equal(fs.existsSync(path.join(f.temp, 'completed.tmp')), false);
      assert.equal(fs.existsSync(path.join(f.temp, 'readonly.tmp')), false);
      assert.equal(fs.readFileSync(path.join(f.temp, 'locked.tmp'), 'utf8'), 'live file');
      assert.equal(fs.readFileSync(path.join(f.scratch, 'shared.lock'), 'utf8'), 'preserve');
      assert.equal(fs.lstatSync(path.join(f.temp, 'redirected')).isSymbolicLink(), true);
      assert.ok(result.stdout.includes('removedEntries'));
    } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
  });

  test(`${shell}: a manual invocation cannot remove assigned-job temporary files`, () => {
    const f = fixture();
    try {
      let result = invoke(shell, f, install('Apply'));
      assert.equal(result.status, 0, result.stdout + result.stderr);
      const marker = path.join(f.temp, 'preserve.tmp');
      fs.writeFileSync(marker, 'assigned job');
      const cleanup = `& (Join-Path $env:ProgramFiles 'PowerForge.RunnerMaintenance/fixture-runner/Clear-RunnerJobTemp.ps1')`;
      result = invoke(shell, f, cleanup, { PROBE_NO_WORKER: 'true' });
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.ok((result.stdout + result.stderr).includes('held (worker;'), result.stdout + result.stderr);
      assert.equal(fs.readFileSync(marker, 'utf8'), 'assigned job');
    } finally { fs.rmSync(f.root, { recursive: true, force: true }); }
  });

  test(`${shell}: hook timeout stops its owned child and preserves an unrelated process`, () => {
    const f = fixture();
    const hookRoot = path.join(f.root, 'timeout-proof');
    const childRecord = path.join(hookRoot, 'child.json');
    const controlRecord = path.join(hookRoot, 'control.json');
    fs.mkdirSync(hookRoot);
    fs.copyFileSync(path.join(scripts, 'Invoke-RunnerJobTempCleanup.ps1'), path.join(hookRoot, 'Invoke-RunnerJobTempCleanup.ps1'));
    fs.writeFileSync(path.join(hookRoot, 'Clear-RunnerJobTemp.ps1'), `
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'child.json'), (@{pid=$PID;startTicks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString()} | ConvertTo-Json))
Start-Sleep -Seconds 120
`);
    const controlScript = `[IO.File]::WriteAllText($env:PROBE_CONTROL_RECORD, (@{pid=$PID;startTicks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString()} | ConvertTo-Json)); Start-Sleep -Seconds 120`;
    const control = spawn(shell, ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(controlScript, 'utf16le').toString('base64')], {
      windowsHide: true, stdio: 'ignore', env: { ...process.env, PROBE_CONTROL_RECORD: controlRecord }
    });
    try {
      const started = Date.now();
      const result = spawnSync(shell, ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', path.join(hookRoot, 'Invoke-RunnerJobTempCleanup.ps1')], {
        encoding: 'utf8', windowsHide: true, timeout: 60_000
      });
      assert.ifError(result.error);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.match(result.stdout + result.stderr, /cleanup timed out/);
      assert.ok(Date.now() - started < 60_000);
      assert.ok(fs.existsSync(childRecord));
      assert.ok(fs.existsSync(controlRecord));
      const check = `
$child=Get-Content -LiteralPath $env:PROBE_CHILD_RECORD -Raw | ConvertFrom-Json
$control=Get-Content -LiteralPath $env:PROBE_CONTROL_RECORD -Raw | ConvertFrom-Json
$p=Get-Process -Id $child.pid -ErrorAction SilentlyContinue
if($p -and $p.StartTime.ToUniversalTime().Ticks.ToString() -ceq $child.startTicks){throw 'Owned timeout child remains alive'}
$p=Get-Process -Id $control.pid -ErrorAction Stop
if($p.StartTime.ToUniversalTime().Ticks.ToString() -cne $control.startTicks){throw 'Control process identity changed'}
`;
      const verified = invoke(shell, f, check, { PROBE_CHILD_RECORD: childRecord, PROBE_CONTROL_RECORD: controlRecord });
      assert.equal(verified.status, 0, verified.stdout + verified.stderr);
    } finally {
      control.kill(); // This exact process was started by this test, with no children.
      if (fs.existsSync(childRecord)) {
        const stopOwned = `$r=Get-Content -LiteralPath $env:PROBE_CHILD_RECORD -Raw|ConvertFrom-Json; $p=Get-Process -Id $r.pid -ErrorAction SilentlyContinue; if($p -and $p.StartTime.ToUniversalTime().Ticks.ToString() -ceq $r.startTicks){Stop-Process -Id $p.Id -ErrorAction Stop}`;
        const stopped = invoke(shell, f, stopOwned, { PROBE_CHILD_RECORD: childRecord });
        assert.equal(stopped.status, 0, stopped.stdout + stopped.stderr);
      }
      fs.rmSync(f.root, { recursive: true, force: true });
    }
  });
}
