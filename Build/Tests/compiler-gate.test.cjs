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
    Set-Content -LiteralPath $env:POWERFORGE_GATE_TEST_MARKER -Value 'invoked'
    throw '${boundaryMessage}'
}
try {
    $gateArguments = @{ EvidencePath = $env:POWERFORGE_GATE_TEST_EVIDENCE; NoBuild = $true }
    if ($env:POWERFORGE_GATE_TEST_RID) { $gateArguments.RuntimeIdentifier = $env:POWERFORGE_GATE_TEST_RID }
    & $env:POWERFORGE_GATE_TEST_SCRIPT @gateArguments
    throw 'Compiler gate unexpectedly completed.'
} catch {
    $capabilitiesPath = Join-Path $env:POWERFORGE_GATE_TEST_EVIDENCE 'host-capabilities.json'
    [ordered]@{
        message = $_.Exception.Message
        dotnetInvoked = Test-Path -LiteralPath $env:POWERFORGE_GATE_TEST_MARKER
        evidenceCreated = Test-Path -LiteralPath $env:POWERFORGE_GATE_TEST_EVIDENCE
        hostPlatform = if ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } else { 'Other' }
        osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        capabilities = if (Test-Path -LiteralPath $capabilitiesPath) { Get-Content -LiteralPath $capabilitiesPath -Raw | ConvertFrom-Json } else { $null }
    } | ConvertTo-Json -Depth 8 -Compress
}
`;

function runGate(runtimeIdentifier) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'powerforge-compiler-gate-'));
  try {
    const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(probe, 'utf16le').toString('base64')], {
      encoding: 'utf8',
      timeout: 30_000,
      env: {
        ...process.env,
        POWERFORGE_GATE_TEST_SCRIPT: gatePath,
        POWERFORGE_GATE_TEST_EVIDENCE: path.join(root, 'evidence'),
        POWERFORGE_GATE_TEST_MARKER: path.join(root, 'dotnet-invoked'),
        POWERFORGE_GATE_TEST_RID: runtimeIdentifier || ''
      }
    });
    assert.ifError(result.error); // PowerShell 7.4+ is a prerequisite, as for the gate itself.
    assert.equal(result.status, 0, result.stderr);
    return JSON.parse(result.stdout.trim());
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
  if (result.hostPlatform === 'Windows') {
    assert.ok(result.capabilities.requiredFamilies.includes('project Windows launch path'));
  } else {
    assert.ok(result.capabilities.excludedRequirements.some(requirement => requirement.family === 'project Windows launch path'));
  }
});
