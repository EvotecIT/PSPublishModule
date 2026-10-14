const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const gatePath = path.resolve(__dirname, '../Invoke-PowerShellCompilerGate.ps1');
const boundaryMessage = 'Compiler gate test reached the dotnet boundary.';
const probe = `
$ErrorActionPreference = 'Stop'
function dotnet {
    [ordered]@{arguments=@($args);selectedCli=$env:POWERFORGE_TEST_CLI_ASSEMBLY_PATH} | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $env:POWERFORGE_GATE_TEST_MARKER
    throw '${boundaryMessage}'
}
try {
    $gateArguments = @{ EvidencePath = $env:POWERFORGE_GATE_TEST_EVIDENCE; NoBuild = $true }
    if ($env:POWERFORGE_GATE_TEST_RID) { $gateArguments.RuntimeIdentifier = $env:POWERFORGE_GATE_TEST_RID }
    if ($env:POWERFORGE_GATE_TEST_LANE) { $gateArguments.Lane = $env:POWERFORGE_GATE_TEST_LANE }
    if ($env:POWERFORGE_GATE_TEST_ARTIFACTS) { $gateArguments.ArtifactsPath = $env:POWERFORGE_GATE_TEST_ARTIFACTS }
    & $env:POWERFORGE_GATE_TEST_SCRIPT @gateArguments
    throw 'Compiler gate unexpectedly completed.'
} catch {
    $capabilitiesPath = Join-Path $env:POWERFORGE_GATE_TEST_EVIDENCE 'host-capabilities.json'
    $invocation = if (Test-Path -LiteralPath $env:POWERFORGE_GATE_TEST_MARKER) { Get-Content -LiteralPath $env:POWERFORGE_GATE_TEST_MARKER -Raw | ConvertFrom-Json } else { $null }
    [ordered]@{
        message = $_.Exception.Message
        dotnetInvoked = Test-Path -LiteralPath $env:POWERFORGE_GATE_TEST_MARKER
        evidenceCreated = Test-Path -LiteralPath $env:POWERFORGE_GATE_TEST_EVIDENCE
        hostPlatform = if ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } else { 'Other' }
        osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        capabilities = if (Test-Path -LiteralPath $capabilitiesPath) { Get-Content -LiteralPath $capabilitiesPath -Raw | ConvertFrom-Json } else { $null }
        filter = if ($invocation) { $invocation.arguments[[Array]::IndexOf($invocation.arguments, '--filter') + 1] } else { $null }
        invocation = $invocation
        callerCliAfter = $env:POWERFORGE_TEST_CLI_ASSEMBLY_PATH
    } | ConvertTo-Json -Depth 8 -Compress
}
`;

function runGate(runtimeIdentifier, lane, artifactsMode) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-compiler-gate-'));
  try {
    const artifacts = artifactsMode ? path.join(root, 'candidate graph') : '';
    const selectedCli = artifactsMode ? path.join(artifacts, 'bin', 'PowerForge.Cli', 'release_net10.0', 'PowerForge.Cli.dll') : '';
    const callerCli = artifactsMode ? path.join(root, 'previous', 'PowerForge.Cli.dll') : '';
    if (artifactsMode) {
      fs.mkdirSync(path.dirname(callerCli), { recursive: true });
      fs.writeFileSync(callerCli, 'previous graph');
      if (artifactsMode === 'valid') {
        fs.mkdirSync(path.dirname(selectedCli), { recursive: true });
        fs.writeFileSync(selectedCli, 'selected graph');
      }
    }
    const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(probe, 'utf16le').toString('base64')], {
      encoding: 'utf8',
      timeout: 30_000,
      env: {
        ...process.env,
        POWERFORGE_GATE_TEST_SCRIPT: gatePath,
        POWERFORGE_GATE_TEST_EVIDENCE: path.join(root, 'evidence'),
        POWERFORGE_GATE_TEST_MARKER: path.join(root, 'dotnet-invoked'),
        POWERFORGE_GATE_TEST_RID: runtimeIdentifier || '',
        POWERFORGE_GATE_TEST_LANE: lane || '',
        POWERFORGE_GATE_TEST_ARTIFACTS: artifacts,
        POWERFORGE_TEST_CLI_ASSEMBLY_PATH: callerCli
      }
    });
    assert.ifError(result.error); // PowerShell 7.4+ is a prerequisite, as for the gate itself.
    assert.equal(result.status, 0, result.stderr);
    return { ...JSON.parse(result.stdout.trim()), artifacts, selectedCli, callerCli };
  } finally {
    fs.rmSync(root, { recursive: true });
  }
}

function supportedHost(result) {
  return ['Windows', 'Linux'].includes(result.hostPlatform) &&
    result.osArchitecture === 'X64' && result.processArchitecture === 'X64';
}

test('compiler gate rejects a foreign RID before evidence creation or validation commands', () => {
  const result = runGate(process.platform === 'win32' ? 'linux-x64' : 'win-x64');
  assert.match(result.message, supportedHost(result) ? /does not match the current host/ : /supports only Windows x64 and Linux x64/);
  assert.equal(result.dotnetInvoked, false);
  assert.equal(result.evidenceCreated, false);
  assert.equal(result.capabilities, null);
});

test('Full compiler qualification includes every required Fast contract even without a category trait', () => {
  const fast = runGate(undefined, 'Fast');
  const full = runGate(undefined, 'Full');
  if (!supportedHost(fast)) return;
  assert.equal(full.message, boundaryMessage);
  assert.match(full.filter, /Category=PowerShellCompilerGate/);
  for (const contract of fast.filter.split('|')) {
    assert.ok(full.filter.split('|').includes(contract), contract);
  }
});

test('compiler gate derives an omitted RID from a supported host before reaching validation', () => {
  const result = runGate();
  if (!supportedHost(result)) {
    assert.match(result.message, /supports only Windows x64 and Linux x64/);
    assert.equal(result.dotnetInvoked, false);
    assert.equal(result.evidenceCreated, false);
    return;
  }
  assert.equal(result.message, boundaryMessage);
  assert.equal(result.dotnetInvoked, true);
  assert.equal(result.capabilities.runtimeIdentifier, result.hostPlatform === 'Windows' ? 'win-x64' : 'linux-x64');
  assert.equal(result.capabilities.hostPlatform, result.hostPlatform);
  assert.equal(result.capabilities.performancePolicy, 'Enforce');
  for (const family of ['empty executable entry', 'corpus performance disposition', 'final emitted unit ownership', 'conditional declaration execution', 'retained executable console presentation']) {
    assert.ok(result.capabilities.requiredFamilies.includes(family), family);
  }
  for (const method of ['Build_PackageConsolePreservesFormattingStreamsAndAuthoredErrors']) {
    assert.ok(result.filter.split('|').includes('FullyQualifiedName~PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.' + method), method);
  }
  for (const method of ['Build_EmptyExecutableEntryPointExitsWithoutOutput', 'PublicCorpusPerformancePolicyRecordsOnlyKnownTimingBudgets']) {
    assert.ok(result.filter.split('|').includes('FullyQualifiedName~PowerForge.Tests.PowerShellCompilationArtifactHardeningTests.' + method), method);
  }
  if (result.hostPlatform === 'Windows') {
    for (const family of ['project Windows launch path', 'project declared test invocation', 'project offline recovery integrity', 'native owned declarations', 'project legacy payload path', 'native source load timing', 'native command region execution', 'native Hybrid executable console presentation']) {
      assert.ok(result.capabilities.requiredFamilies.includes(family), family);
    }
    assert.ok(result.filter.split('|').includes('FullyQualifiedName~PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_NativeHybridConsolePreservesFormattingStreamsAndAuthoredErrors'));
  } else {
    assert.ok(result.capabilities.excludedRequirements.some(requirement => requirement.family === 'project Windows launch path'));
    assert.ok(result.capabilities.excludedRequirements.some(requirement => requirement.family === 'native Hybrid executable console presentation'));
    assert.equal(result.filter.split('|').includes('FullyQualifiedName~PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_NativeHybridConsolePreservesFormattingStreamsAndAuthoredErrors'), false);
  }
});

test('NoBuild compiler gate uses the selected SDK graph and restores the caller CLI selector after failure', () => {
  const result = runGate(undefined, 'Fast', 'valid');
  if (!supportedHost(result)) return;
  assert.equal(result.message, boundaryMessage);
  const argv = result.invocation.arguments;
  assert.equal(argv[argv.indexOf('--artifacts-path') + 1], result.artifacts);
  assert.ok(argv.includes('--no-build'));
  assert.ok(argv.includes('--no-restore'));
  assert.equal(result.invocation.selectedCli, result.selectedCli);
  assert.equal(result.callerCliAfter, result.callerCli);

  const missing = runGate(undefined, 'Fast', 'missing');
  assert.match(missing.message, /selected prebuilt test CLI must be an absolute existing/);
  assert.equal(missing.dotnetInvoked, false);
  assert.equal(missing.evidenceCreated, false);
  assert.equal(missing.callerCliAfter, missing.callerCli);
});

test('whole-workflow gate refuses unsafe roots and preserves previous evidence before acquiring inputs', () => {
  const wholeGate = path.resolve(__dirname, '../Invoke-PowerShellWholeWorkflowGate.ps1');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-whole-gate-'));
  const evidence = path.join(root, 'evidence');
  fs.mkdirSync(evidence);
  const sentinel = path.join(evidence, 'retained.json');
  fs.writeFileSync(sentinel, 'retained receipt');
  try {
    const script = `
$ErrorActionPreference='Stop'
$cases=@(
  @{WorkspacePath=[IO.Path]::GetPathRoot($env:POWERFORGE_WHOLE_TEST_EVIDENCE);EvidencePath=($env:POWERFORGE_WHOLE_TEST_EVIDENCE+'.volume')},
  @{WorkspacePath=($env:POWERFORGE_WHOLE_TEST_EVIDENCE+'-too-long');EvidencePath=($env:POWERFORGE_WHOLE_TEST_EVIDENCE+'.long')},
  @{WorkspacePath=($env:POWERFORGE_WHOLE_TEST_EVIDENCE+'-unused');EvidencePath=$env:POWERFORGE_WHOLE_TEST_EVIDENCE}
)
$rows=@(foreach($case in $cases){
  try{& $env:POWERFORGE_WHOLE_TEST_SCRIPT @case; throw 'Unsafe gate unexpectedly proceeded'}
  catch{[ordered]@{message=$_.Exception.Message;evidenceExists=Test-Path -LiteralPath $case.EvidencePath}}
})
$rows | ConvertTo-Json -Compress
`;
    const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')], {
      encoding: 'utf8', timeout: 30_000,
      env: { ...process.env, POWERFORGE_WHOLE_TEST_SCRIPT: wholeGate, POWERFORGE_WHOLE_TEST_EVIDENCE: evidence }
    });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stderr);
    const rows = JSON.parse(result.stdout.trim());
    for (const row of rows) {
      assert.match(row.message, /requires Windows x64|below the volume root|at most 25 characters|empty evidence directory/);
    }
    assert.equal(rows[0].evidenceExists, false);
    assert.equal(rows[1].evidenceExists, false);
    assert.equal(fs.readFileSync(sentinel, 'utf8'), 'retained receipt');
  } finally {
    fs.rmSync(root, { recursive: true });
  }
});
