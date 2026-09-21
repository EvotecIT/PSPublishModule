[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoRestore,
    [switch] $SkipTests,
    [switch] $IncludeCli,
    [switch] $IncludeLegacyWpf
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$avaloniaProject = Join-Path $repoRoot 'PowerForgeStudio.Avalonia\PowerForgeStudio.Avalonia.csproj'
$avaloniaTestsProject = Join-Path $repoRoot 'PowerForgeStudio.Avalonia.Tests\PowerForgeStudio.Avalonia.Tests.csproj'
$wpfProject = Join-Path $repoRoot 'PowerForgeStudio.Wpf\PowerForgeStudio.Wpf.csproj'
$cliProject = Join-Path $repoRoot 'PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj'
$wpfTestsProject = Join-Path $repoRoot 'PowerForgeStudio.Wpf.Tests\PowerForgeStudio.Wpf.Tests.csproj'
$studioTestsProject = Join-Path $repoRoot 'PowerForgeStudio.Tests\PowerForgeStudio.Tests.csproj'
$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

if ($IncludeLegacyWpf -and -not $runningOnWindows) {
    throw 'The legacy PowerForge Studio WPF host can only build on Windows.'
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

$commonArguments = @('-c', $Configuration)
if ($NoRestore) {
    $commonArguments += '--no-restore'
}

Invoke-DotNet -Arguments (@('build', $avaloniaProject) + $commonArguments)

if ($IncludeLegacyWpf) {
    Invoke-DotNet -Arguments (@('build', $wpfProject) + $commonArguments)
}

if ($IncludeCli) {
    Invoke-DotNet -Arguments (@('build', $cliProject) + $commonArguments)
}

if (-not $SkipTests) {
    Invoke-DotNet -Arguments (@('test', $avaloniaTestsProject) + $commonArguments)
    Invoke-DotNet -Arguments (@('test', $studioTestsProject) + $commonArguments)
    if ($IncludeLegacyWpf) {
        Invoke-DotNet -Arguments (@('test', $wpfTestsProject) + $commonArguments)
    }
}

Write-Host "PowerForge Studio build workflow completed." -ForegroundColor Green
