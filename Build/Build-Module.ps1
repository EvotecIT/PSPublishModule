[CmdletBinding()]
param(
    [ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto',
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [switch] $NoBuild,
    [Alias('JsonOnly')][switch] $Json,
    [string] $JsonPath
)

$script = Join-Path $PSScriptRoot '..\Module\Build\Build-ModuleSelf.ps1'
if (-not (Test-Path -LiteralPath $script)) {
    throw "Build script not found: $script"
}

if ($PSBoundParameters.ContainsKey('JsonPath')) {
    Write-Verbose "JsonPath is ignored in self-build mode."
}

$invoke = @{
    Framework      = $Framework
    Configuration  = $Configuration
}
if ($NoBuild) { $invoke.NoBuild = $true }
if ($Json) { $invoke.Json = $true }

& $script @invoke
