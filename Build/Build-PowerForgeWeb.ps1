[CmdletBinding()] param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64', 'osx-x64', 'osx-arm64')]
    [string[]] $Runtime = @('win-x64'),
    [ValidateSet('net10.0', 'net8.0')]
    [string] $Framework = 'net10.0',
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string] $Flavor = 'SingleContained',
    [switch] $Plan,
    [switch] $Validate,
    [switch] $PublishGitHub,
    [string] $ConfigPath
)

if (-not $PSBoundParameters.ContainsKey('ConfigPath') -or [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'release.json'
}

$invokeParams = @{
    Tool = @('PowerForgeWeb')
    ConfigPath = $ConfigPath
    Configuration = $Configuration
    Runtime = $Runtime
    Framework = $Framework
    Flavor = $Flavor
}
if ($Plan) { $invokeParams.Plan = $true }
if ($Validate) { $invokeParams.Validate = $true }
if ($PublishGitHub) { $invokeParams.PublishGitHub = $true }

& (Join-Path $PSScriptRoot 'Build-PowerForge.ps1') @invokeParams
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
