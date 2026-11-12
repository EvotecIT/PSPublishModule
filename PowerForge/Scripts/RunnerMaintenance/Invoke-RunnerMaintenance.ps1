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
