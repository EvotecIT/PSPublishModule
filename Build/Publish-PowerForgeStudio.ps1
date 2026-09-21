[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [ValidateSet('FrameworkDependent', 'SelfContained', 'Both')]
    [string] $Mode = 'Both',
    [string] $OutputRoot,
    [switch] $NoRestore,
    [switch] $SingleFile,
    [switch] $LegacyWpf
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
if ($LegacyWpf -and (-not $runningOnWindows -or -not $Runtime.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase))) {
    throw 'The legacy PowerForge Studio WPF host can only publish for Windows from Windows.'
}
$project = if ($LegacyWpf) {
    Join-Path $repoRoot 'PowerForgeStudio.Wpf\PowerForgeStudio.Wpf.csproj'
} else {
    Join-Path $repoRoot 'PowerForgeStudio.Avalonia\PowerForgeStudio.Avalonia.csproj'
}
$executableBaseName = if ($LegacyWpf) { 'PowerForgeStudio.Wpf' } else { 'PowerForgeStudio' }
$executableName = if ($Runtime.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
    "$executableBaseName.exe"
} else {
    $executableBaseName
}
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
    $project,
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
    Write-Host "Framework-dependent publish ready: $(Join-Path $frameworkDependentRoot $executableName)" -ForegroundColor Green
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
    Write-Host "Self-contained publish ready: $(Join-Path $selfContainedRoot $executableName)" -ForegroundColor Green
}

Write-Host "PowerForge Studio publish workflow completed." -ForegroundColor Green
