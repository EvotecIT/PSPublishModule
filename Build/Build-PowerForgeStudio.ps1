[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoRestore,
    [switch] $SkipTests,
    [switch] $IncludeCli
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wpfProject = Join-Path $repoRoot 'PowerForgeStudio.Wpf\PowerForgeStudio.Wpf.csproj'
$cliProject = Join-Path $repoRoot 'PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj'
$wpfTestsProject = Join-Path $repoRoot 'PowerForgeStudio.Wpf.Tests\PowerForgeStudio.Wpf.Tests.csproj'
$studioTestsProject = Join-Path $repoRoot 'PowerForgeStudio.Tests\PowerForgeStudio.Tests.csproj'

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

Invoke-DotNet -Arguments (@('build', $wpfProject) + $commonArguments)

if ($IncludeCli) {
    Invoke-DotNet -Arguments (@('build', $cliProject) + $commonArguments)
}

if (-not $SkipTests) {
    Invoke-DotNet -Arguments (@('test', $wpfTestsProject) + $commonArguments)
    Invoke-DotNet -Arguments (@('test', $studioTestsProject) + $commonArguments)
}

Write-Host "PowerForge Studio build workflow completed." -ForegroundColor Green
