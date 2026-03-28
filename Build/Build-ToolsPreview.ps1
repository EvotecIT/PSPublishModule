[CmdletBinding()] param(
    [ValidateSet('PowerForge', 'PowerForgeWeb', 'All')]
    [string[]] $Tool = @('All'),
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64', 'osx-x64', 'osx-arm64')]
    [string[]] $Runtime,
    [ValidateSet('net10.0', 'net8.0')]
    [string[]] $Framework = @('net10.0'),
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string[]] $Flavor = @('SingleContained'),
    [switch] $Plan,
    [switch] $Validate,
    [switch] $PublishGitHub,
    [string] $ConfigPath
)

if (-not $PSBoundParameters.ContainsKey('ConfigPath') -or [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'release.tools-preview.json'
}

if ($Tool -contains 'All') {
    $Tool = @('PowerForge', 'PowerForgeWeb')
}

$buildProjectParams = @{
    ConfigPath = $ConfigPath
    Configuration = $Configuration
    ToolsOnly = $true
    Target = $Tool
    Framework = $Framework
    Flavor = $Flavor
}

if ($Runtime) { $buildProjectParams.Runtime = $Runtime }
if ($Plan) { $buildProjectParams.Plan = $true }
if ($Validate) { $buildProjectParams.Validate = $true }
if ($PublishGitHub) { $buildProjectParams.PublishToolGitHub = $true }

& (Join-Path $PSScriptRoot 'Build-Project.ps1') @buildProjectParams
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
