#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$installScript = Join-Path -Path $scriptRoot -ChildPath 'Install-Service.ps1'

if (-not (Test-Path -LiteralPath $installScript)) {
    throw "Install script not found: $installScript"
}

& $installScript -Start
