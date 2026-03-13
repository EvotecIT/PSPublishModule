[CmdletBinding()] param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64', 'osx-x64', 'osx-arm64')]
    [string[]] $Runtime = @('win-x64'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateSet('net10.0', 'net8.0')]
    [string] $Framework = 'net10.0',
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string] $Flavor = 'SingleContained',
    [string] $OutDir,
    [switch] $ClearOut,
    [switch] $Zip,
    [switch] $UseStaging = $true,
    [switch] $KeepSymbols,
    [switch] $KeepDocs,
    [switch] $PublishGitHub,
    [string] $GitHubUsername = 'EvotecIT',
    [string] $GitHubRepositoryName = 'PSPublishModule',
    [string] $GitHubAccessToken,
    [string] $GitHubAccessTokenFilePath,
    [string] $GitHubAccessTokenEnvName = 'GITHUB_TOKEN',
    [string] $GitHubTagName,
    [string] $GitHubReleaseName,
    [switch] $GenerateReleaseNotes = $true,
    [switch] $IsPreRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'Build-PowerForge.ps1'
$invokeParams = @{
    Tool = 'PowerForgeWeb'
    Runtime = $Runtime
    Configuration = $Configuration
    Framework = $Framework
    Flavor = $Flavor
    UseStaging = $UseStaging
    GitHubUsername = $GitHubUsername
    GitHubRepositoryName = $GitHubRepositoryName
    GitHubAccessTokenEnvName = $GitHubAccessTokenEnvName
    GenerateReleaseNotes = $GenerateReleaseNotes
    IsPreRelease = $IsPreRelease
}

if ($PSBoundParameters.ContainsKey('OutDir')) { $invokeParams.OutDir = $OutDir }
if ($ClearOut) { $invokeParams.ClearOut = $true }
if ($Zip) { $invokeParams.Zip = $true }
if ($KeepSymbols) { $invokeParams.KeepSymbols = $true }
if ($KeepDocs) { $invokeParams.KeepDocs = $true }
if ($PublishGitHub) { $invokeParams.PublishGitHub = $true }
if ($PSBoundParameters.ContainsKey('GitHubAccessToken')) { $invokeParams.GitHubAccessToken = $GitHubAccessToken }
if ($PSBoundParameters.ContainsKey('GitHubAccessTokenFilePath')) { $invokeParams.GitHubAccessTokenFilePath = $GitHubAccessTokenFilePath }
if ($PSBoundParameters.ContainsKey('GitHubTagName')) { $invokeParams.GitHubTagName = $GitHubTagName }
if ($PSBoundParameters.ContainsKey('GitHubReleaseName')) { $invokeParams.GitHubReleaseName = $GitHubReleaseName }

& $scriptPath @invokeParams
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
