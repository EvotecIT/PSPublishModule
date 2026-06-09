[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [ValidateSet('FrameworkDependent', 'SelfContained', 'Both')]
    [string] $Mode = 'Both',
    [string] $OutputRoot,
    [switch] $NoRestore,
    [switch] $SingleFile
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wpfProject = Join-Path $repoRoot 'PowerForgeStudio.Wpf\PowerForgeStudio.Wpf.csproj'
if (-not $PSBoundParameters.ContainsKey('OutputRoot') -or [string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'Artifacts\PowerForgeStudio'
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    Write-Host ("dotnet " + ($Arguments -join ' ')) -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$publishRoot = Join-Path $OutputRoot $Runtime
$frameworkDependentRoot = Join-Path $publishRoot 'framework-dependent'
$selfContainedRoot = Join-Path $publishRoot 'self-contained'

$baseArguments = @(
    'publish',
    $wpfProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '-p:UseAppHost=true'
)
if ($NoRestore) {
    $baseArguments += '--no-restore'
}

if ($Mode -in @('FrameworkDependent', 'Both')) {
    Invoke-DotNet -Arguments ($baseArguments + @(
            '--self-contained', 'false',
            '-o', $frameworkDependentRoot
        ))
    Write-Host "Framework-dependent publish ready: $frameworkDependentRoot\PowerForgeStudio.Wpf.exe" -ForegroundColor Green
}

if ($Mode -in @('SelfContained', 'Both')) {
    $publishSingleFile = if ($SingleFile.IsPresent) { 'true' } else { 'false' }
    $selfExtractNativeLibraries = if ($SingleFile.IsPresent) { 'true' } else { 'false' }
    $selfContainedArguments = $baseArguments + @(
        '--self-contained', 'true',
        "-p:PublishSingleFile=$publishSingleFile",
        "-p:IncludeNativeLibrariesForSelfExtract=$selfExtractNativeLibraries",
        '-o', $selfContainedRoot
    )
    Invoke-DotNet -Arguments $selfContainedArguments
    Write-Host "Self-contained publish ready: $selfContainedRoot\PowerForgeStudio.Wpf.exe" -ForegroundColor Green
}

Write-Host "PowerForge Studio publish workflow completed." -ForegroundColor Green
