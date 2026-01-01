[CmdletBinding()]
param()

$script = Join-Path $PSScriptRoot 'Module\PSPublishModule.Tests.ps1'
if (-not (Test-Path -LiteralPath $script)) {
    throw "Test script not found: $script"
}

& $script
