$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
. (Join-Path $repoRoot '.github/actions/powerforge-linux-service-deploy/Invoke-PowerForgeSshTransport.ps1')

$taskRoot = Join-Path ([IO.Path]::GetTempPath()) "powerforge-ssh-diagnostic-$([Guid]::NewGuid().ToString('N'))"
$binRoot = Join-Path $taskRoot 'bin'
$transportPath = Join-Path $taskRoot 'deployment-transport.tar'
$originalPath = $env:PATH
try {
    New-Item -ItemType Directory -Path $binRoot | Out-Null
    @'
#!/usr/bin/env bash
set -Eeuo pipefail
cat >/dev/null
head -c 12000 /dev/zero | tr '\0' 'A' >&2
printf '\n::error::forged-control\001\n' >&2
printf 'remote stdout detail\n'
exit 17
'@ | Set-Content -LiteralPath (Join-Path $binRoot 'ssh') -Encoding utf8NoBOM
    chmod 0755 (Join-Path $binRoot 'ssh')
    if ($LASTEXITCODE -ne 0) { throw 'Unable to prepare the mock ssh executable.' }
    [IO.File]::WriteAllText($transportPath, 'fixture')
    $env:PATH = "$binRoot$([IO.Path]::PathSeparator)$originalPath"

    $failure = $null
    try {
        Invoke-PowerForgeSshTransport `
            -TransportPath $transportPath `
            -Target 'deploy@example.test' `
            -Port 22 `
            -SshOptions @('-o', 'BatchMode=yes') `
            -Service 'example'
    }
    catch {
        $failure = $_.Exception.Message
    }

    if ([string]::IsNullOrWhiteSpace($failure)) { throw 'The mock ssh failure was not surfaced.' }
    if ($failure -notmatch 'exit code 17') { throw "The ssh exit code was not preserved. Observed: $failure" }
    if ($failure -notmatch 'Remote stderr:' -or $failure -notmatch 'Remote stdout:') {
        throw 'Bounded remote diagnostics were not included.'
    }
    if ($failure -notmatch '\[earlier output truncated\]') { throw 'Truncated diagnostics were not marked.' }
    if ($failure -notmatch '(?m)^  \| ::error::forged-control\?') {
        throw 'Workflow-command text or control characters were not safely formatted.'
    }
    if ($failure -match '(?m)^::') { throw 'A remote workflow command reached the diagnostic output unprefixed.' }
    foreach ($suffix in @('.stdout', '.stderr')) {
        $capture = Get-Item -LiteralPath "$transportPath$suffix"
        if ($capture.Length -gt 8193) { throw "Diagnostic capture exceeded its byte bound: $($capture.FullName)" }
    }
}
finally {
    $env:PATH = $originalPath
    if (Test-Path -LiteralPath $taskRoot) {
        Remove-Item -LiteralPath $taskRoot -Recurse -Force
    }
}

Write-Host 'PowerForge SSH diagnostic fixture passed.'
