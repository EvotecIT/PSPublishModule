[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [ValidateSet('auto', 'win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string] $Runtime = 'auto',

    [switch] $IncludeStudioSmoke
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedRuntime = if ($Runtime -ne 'auto') {
    $Runtime
} elseif ($IsWindows -or $PSVersionTable.PSEdition -eq 'Desktop') {
    'win-x64'
} elseif ($IsMacOS) {
    if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
} else {
    'linux-x64'
}
$artifactRoot = Join-Path $repoRoot 'Artefacts\ProductSmoke'
$webOutput = Join-Path $artifactRoot 'PowerForgeWeb'

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

Write-Host "Running unified no-publish product build ($resolvedRuntime)..."
$releaseOutput = & (Join-Path $PSScriptRoot 'Build-Project.ps1') `
    -Configuration $Configuration `
    -RunMode Build `
    -ModuleNoSign `
    -ModuleSkipInstall `
    -EnableSigning:$false `
    -Runtimes $resolvedRuntime `
    -Frameworks 'net10.0' `
    -Flavors 'SingleFx' `
    -Json
if ($LASTEXITCODE -ne 0) {
    throw "Unified no-publish product build failed with exit code $LASTEXITCODE.`n$($releaseOutput | Out-String)"
}

$release = $releaseOutput | Out-String | ConvertFrom-Json
if (-not $release.Success) {
    throw "Unified no-publish product build failed: $($release.ErrorMessage)"
}
if ($null -eq $release.Packages -or $null -eq $release.Module -or
    ($null -eq $release.Tools -and $null -eq $release.DotNetTools)) {
    throw 'Unified no-publish product build did not execute package, module, and tool lanes.'
}

Write-Host 'Building the checked-in PowerForge.Web sample through the real CLI...'
$webOutputText = & dotnet run `
    --project (Join-Path $repoRoot 'PowerForge.Web.Cli\PowerForge.Web.Cli.csproj') `
    --configuration $Configuration `
    --framework net10.0 `
    --no-restore `
    -- build `
    --config (Join-Path $repoRoot 'Samples\PowerForge.Web.Sample\site.json') `
    --out $webOutput `
    --clean `
    --json
if ($LASTEXITCODE -ne 0) {
    throw "PowerForge.Web sample build failed with exit code $LASTEXITCODE.`n$($webOutputText | Out-String)"
}

$webResult = $webOutputText | Out-String | ConvertFrom-Json
if (-not $webResult.Success -or -not (Test-Path -LiteralPath (Join-Path $webOutput 'index.html'))) {
    throw 'PowerForge.Web sample build did not produce a successful result and index.html.'
}

if ($IncludeStudioSmoke) {
    Write-Host 'Running the opt-in Studio build/checkpoint/sign/publish-lock smoke...'
    $originalSmoke = $env:POWERFORGE_RUN_PRODUCT_SMOKE
    $originalRoot = $env:RELEASE_OPS_STUDIO_SMOKE_ROOT
    try {
        $env:POWERFORGE_RUN_PRODUCT_SMOKE = 'true'
        $env:RELEASE_OPS_STUDIO_SMOKE_ROOT = $repoRoot
        & dotnet test (Join-Path $repoRoot 'PowerForgeStudio.Tests\PowerForgeStudio.Tests.csproj') `
            --configuration $Configuration `
            --no-restore `
            --filter 'FullyQualifiedName=PowerForgeStudio.Tests.PowerForgeStudioSmokeHarnessTests.LocalSmokePath_ExercisesBuildAndRetryStages'
        if ($LASTEXITCODE -ne 0) {
            throw "PowerForge Studio product smoke failed with exit code $LASTEXITCODE."
        }
    } finally {
        $env:POWERFORGE_RUN_PRODUCT_SMOKE = $originalSmoke
        $env:RELEASE_OPS_STUDIO_SMOKE_ROOT = $originalRoot
    }
}

[pscustomobject]@{
    Success     = $true
    Runtime     = $resolvedRuntime
    WebOutput   = $webOutput
    StudioSmoke = [bool] $IncludeStudioSmoke
}
