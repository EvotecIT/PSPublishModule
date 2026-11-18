[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Start', 'Stop')][string] $Operation,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath
)
& (Join-Path $PSScriptRoot 'Invoke-InstallerFixture.ps1') @PSBoundParameters -Installer powershell
