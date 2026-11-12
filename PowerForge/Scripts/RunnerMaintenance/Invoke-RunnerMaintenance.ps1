<#
.SYNOPSIS
Runs the prebuilt runner-maintenance payload with a bounded child lifetime.
.PARAMETER Configuration
Private lifecycle or scheduled configuration created by maintenance enrollment.
.EXAMPLE
./Invoke-RunnerMaintenance.ps1 -Configuration /var/lib/powerforge-runner-maintenance/scheduled.json
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Configuration)

$ErrorActionPreference = 'Stop'
$settings = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
$policy = Get-Content -LiteralPath $settings.policy -Raw | ConvertFrom-Json
if (Test-Path -LiteralPath (Join-Path $policy.stateRootPath 'installation.pending.json')) {
    throw 'Enrollment recovery is pending. Retry the installer while its services remain stopped.'
}
if ($settings.previousHook) {
    $env:POWERFORGE_RUNNER_JOB_ID = [Guid]::NewGuid().ToString('N')
    # Enter once before the existing hook. Its live worker then protects its work
    # until maintenance reaches its own ready marker. Existing hooks keep their
    # normal foreground lifetime and background descendants.
    $gate = $null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not $gate) {
        try {
            $gate = [IO.File]::Open((Join-Path $policy.stateRootPath 'admission.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
        } catch [IO.IOException] {
            if ($clock.Elapsed.TotalSeconds -ge $policy.admissionTimeoutSeconds) { throw 'Existing hook could not enter runner admission.' }
            Start-Sleep -Milliseconds 100
        }
    }
    $gate.Dispose()
    $previous = $settings.previousHook
    switch ([IO.Path]::GetExtension($previous).ToLowerInvariant()) {
        '.ps1' {
            $shell = Join-Path $PSHOME $(if ($env:OS -eq 'Windows_NT' -and $PSVersionTable.PSEdition -ne 'Core') { 'powershell.exe' } elseif ($env:OS -eq 'Windows_NT') { 'pwsh.exe' } else { 'pwsh' })
            $command = '$ErrorActionPreference = ''Stop''; . ''' + $previous.Replace("'", "''") + '''; if (Test-Path variable:LASTEXITCODE) { exit $LASTEXITCODE }'
            & $shell -NoLogo -NoProfile -NonInteractive -Command $command
        }
        '.sh' { & bash -e $previous }
        '.cmd' { & $env:ComSpec /d /s /c ('"' + $previous + '"') }
        default { throw 'Unsupported existing GitHub hook language.' }
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $settings.executable
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = Split-Path -Parent $Configuration
# The enrollment path is local administrator-owned and cannot contain a quote or newline.
if ($Configuration.IndexOfAny([char[]] @('"', "`r", "`n")) -ge 0) {
    throw 'Invalid maintenance configuration path.'
}
$start.Arguments = 'github runner maintenance --hook-config "' + $Configuration + '"'
$child = [Diagnostics.Process]::Start($start)
try {
    if (-not $child.WaitForExit(240000)) {
        throw 'Runner maintenance exceeded its four-minute process budget.'
    }
    exit $child.ExitCode
} finally {
    if (-not $child.HasExited) {
        if ($PSVersionTable.PSEdition -eq 'Core') {
            $child.Kill($true)
        } else {
            & "$env:SystemRoot/System32/taskkill.exe" /PID $child.Id /T /F | Out-Null
        }
        $child.WaitForExit()
    }
    $child.Dispose()
}
