[CmdletBinding()] param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'release.json'),
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [switch] $SkipModule,
    [switch] $NoDotnetBuild,
    [string] $ModuleVersion,
    [string] $PreReleaseTag,
    [switch] $NoSign,
    [switch] $SignModule,
    [switch] $Plan,
    [switch] $Validate,
    [switch] $PackagesOnly,
    [switch] $ToolsOnly,
    [switch] $PublishNuget,
    [switch] $PublishGitHub,
    [switch] $PublishToolGitHub,
    [string[]] $Target,
    [string[]] $Runtime,
    [string[]] $Framework,
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string[]] $Flavor
)

$shouldRunModule = -not $SkipModule -and -not $Plan -and -not $Validate -and -not $PackagesOnly -and -not $ToolsOnly
$configDeclaresNativeModule = $false
if (Test-Path -LiteralPath $ConfigPath) {
    try {
        $releaseConfig = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
        $configDeclaresNativeModule = $null -ne $releaseConfig.Module
    } catch {
        Write-Verbose "Could not inspect release config for native module support: $($_.Exception.Message)"
    }
}

if ($configDeclaresNativeModule) {
    $shouldRunModule = $false
}

if ($shouldRunModule) {
    $moduleScript = Join-Path $PSScriptRoot '..\Module\Build\Build-Module.ps1'
    $moduleParams = @{
        Configuration = $Configuration
    }

    if ($NoDotnetBuild) { $moduleParams.NoDotnetBuild = $true }
    if ($PSBoundParameters.ContainsKey('ModuleVersion')) { $moduleParams.ModuleVersion = $ModuleVersion }
    if ($PSBoundParameters.ContainsKey('PreReleaseTag')) { $moduleParams.PreReleaseTag = $PreReleaseTag }
    if ($PSBoundParameters.ContainsKey('NoSign')) { $moduleParams.NoSign = $NoSign.IsPresent }
    if ($PSBoundParameters.ContainsKey('SignModule')) { $moduleParams.SignModule = $SignModule.IsPresent }

    & $moduleScript @moduleParams
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} elseif (-not $SkipModule) {
    if ($configDeclaresNativeModule) {
        Write-Verbose 'Skipping standalone module stage because release.json declares native Module support.'
    } else {
        Write-Verbose 'Skipping module stage because this wrapper is running in plan/validate or scoped packages/tools mode.'
    }
}

$releaseParams = @{
    ConfigPath = $ConfigPath
    Configuration = $Configuration
}

if ($Plan) { $releaseParams.Plan = $true }
if ($Validate) { $releaseParams.Validate = $true }
if ($NoDotnetBuild) { $releaseParams.ModuleNoDotnetBuild = $true }
if ($PSBoundParameters.ContainsKey('ModuleVersion')) { $releaseParams.ModuleVersion = $ModuleVersion }
if ($PSBoundParameters.ContainsKey('PreReleaseTag')) { $releaseParams.ModulePreReleaseTag = $PreReleaseTag }
if ($PSBoundParameters.ContainsKey('NoSign')) { $releaseParams.ModuleNoSign = $NoSign.IsPresent }
if ($PSBoundParameters.ContainsKey('SignModule')) { $releaseParams.ModuleSignModule = $SignModule.IsPresent }
if ($PackagesOnly) { $releaseParams.PackagesOnly = $true }
if ($ToolsOnly) { $releaseParams.ToolsOnly = $true }
if ($PublishNuget) { $releaseParams.PublishNuget = $true }
if ($PublishGitHub) { $releaseParams.PublishGitHub = $true }
if ($PublishToolGitHub) { $releaseParams.PublishToolGitHub = $true }
if ($Target) { $releaseParams.Target = $Target }
if ($Runtime) { $releaseParams.Runtime = $Runtime }
if ($Framework) { $releaseParams.Framework = $Framework }
if ($Flavor) { $releaseParams.Flavor = $Flavor }

& (Join-Path $PSScriptRoot 'Build-Project.ps1') @releaseParams
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
