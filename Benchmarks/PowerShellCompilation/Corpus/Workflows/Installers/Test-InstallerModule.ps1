[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ArtifactPath,
    [Parameter(Mandatory)][ValidateSet('Observe-PowerShellInstaller.ps1', 'Observe-ScoopInstaller.ps1')][string] $ObserverName,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath
)
$ErrorActionPreference = 'Stop'
if (-not $env:POWERFORGE_PWSH76_PATH) { throw 'Installer acceptance requires the configured pinned PowerShell 7.6.6 host.' }
$hostPath = [IO.Path]::GetFullPath($env:POWERFORGE_PWSH76_PATH)
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) { throw 'The pinned host was not found.' }
$instance = 'Test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
& $hostPath -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot $ObserverName) `
    -ImportPath $ArtifactPath -Surface Built -ScenarioRoot $ScenarioRoot -ResultPath $ResultPath -Instance $instance
if ($LASTEXITCODE -ne 0) { throw 'Pinned installer acceptance failed.' }
