[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoRestore,
    [switch] $SkipTests,
    [switch] $IncludeCli,
    [switch] $DesktopOnlyTests
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$avaloniaProject = Join-Path $repoRoot 'PowerForgeStudio.Avalonia\PowerForgeStudio.Avalonia.csproj'
$avaloniaTestsProject = Join-Path $repoRoot 'PowerForgeStudio.Avalonia.Tests\PowerForgeStudio.Avalonia.Tests.csproj'
$cliProject = Join-Path $repoRoot 'PowerForgeStudio.Cli\PowerForgeStudio.Cli.csproj'
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

Invoke-DotNet -Arguments (@('build', $avaloniaProject) + $commonArguments)

if ($IncludeCli) {
    Invoke-DotNet -Arguments (@('build', $cliProject) + $commonArguments)
}

if (-not $SkipTests) {
    Invoke-DotNet -Arguments (@('test', $avaloniaTestsProject) + $commonArguments)
    $studioTestArguments = @('test', $studioTestsProject) + $commonArguments
    if ($DesktopOnlyTests) {
        $studioTestArguments += @(
            '--filter',
            'FullyQualifiedName!~ResolveExactAppleSourceCommit&FullyQualifiedName!~PowerForgeStudioReleaseStationProjectionServiceTests.BuildSnapshots_ProjectsSigningPublishAndVerificationStations'
        )
    }
    Invoke-DotNet -Arguments $studioTestArguments
}

Write-Host "PowerForge Studio build workflow completed." -ForegroundColor Green
