[CmdletBinding()]
param(
    [switch] $JsonOnly,
    [string] $JsonPath = (Join-Path $PSScriptRoot '..\powerforge.json')
)

$script = Join-Path $PSScriptRoot '..\Module\Build\Build-Module.ps1'
if (-not (Test-Path -LiteralPath $script)) {
    throw "Build script not found: $script"
}

& $script @PSBoundParameters
