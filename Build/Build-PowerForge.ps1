[CmdletBinding()] param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64', 'osx-x64', 'osx-arm64')]
    [string] $Runtime = 'win-x64',
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
    [switch] $KeepDocs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t) { Write-Host "[OK] $t" -ForegroundColor Green }
function Write-Step($t) { Write-Host "[+] $t" -ForegroundColor Yellow }

$repoRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..')))).Path
$proj = Join-Path $repoRoot 'PowerForge.Cli/PowerForge.Cli.csproj'

if (-not (Test-Path -LiteralPath $proj)) { throw "Project not found: $proj" }

if (-not $OutDir) {
    $OutDir = Join-Path $repoRoot ("Artifacts/PowerForge/{0}/{1}/{2}" -f $Runtime, $Framework, $Flavor)
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$publishDir = $OutDir
$stagingDir = $null
if ($UseStaging) {
    $stagingDir = Join-Path $env:TEMP ("PowerForge.Cli.publish." + [guid]::NewGuid().ToString("N"))
    $publishDir = $stagingDir
    Write-Step "Using staging publish dir -> $publishDir"
    if (Test-Path $publishDir) {
        Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
}

# When using a staging publish dir, treat the output as an exact snapshot and clear it by default.
if ($UseStaging -and -not $PSBoundParameters.ContainsKey('ClearOut')) {
    $ClearOut = $true
}

$singleFile = $Flavor -in @('SingleContained', 'SingleFx')
$selfContained = $Flavor -in @('SingleContained', 'Portable')
$compress = $singleFile
$selfExtract = $Flavor -eq 'SingleContained'

Write-Header "Build PowerForge ($Flavor)"
Write-Step "Publishing -> $publishDir"

$publishArgs = @(
    'publish', $proj,
    '-c', $Configuration,
    '-f', $Framework,
    '-r', $Runtime,
    "--self-contained:$selfContained",
    "/p:PublishSingleFile=$singleFile",
    "/p:PublishReadyToRun=false",
    "/p:PublishTrimmed=false",
    "/p:IncludeAllContentForSelfExtract=$singleFile",
    "/p:IncludeNativeLibrariesForSelfExtract=$selfExtract",
    "/p:EnableCompressionInSingleFile=$compress",
    "/p:DebugType=None",
    "/p:DebugSymbols=false",
    "/p:GenerateDocumentationFile=false",
    "/p:CopyDocumentationFiles=false",
    "/p:ExcludeSymbolsFromSingleFile=true",
    "/p:ErrorOnDuplicatePublishOutputFiles=false",
    "/p:UseAppHost=true",
    "/p:PublishDir=$publishDir"
)

if ($ClearOut -and (Test-Path $OutDir) -and ($publishDir -eq $OutDir)) {
    Write-Step "Clearing $OutDir"
    Get-ChildItem -Path $OutDir -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet.exe @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }       

if (-not $KeepSymbols) {
    Write-Step "Removing symbols (*.pdb)"
    Get-ChildItem -Path $publishDir -Filter *.pdb -File -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

if (-not $KeepDocs) {
    Write-Step "Removing docs (*.xml, *.pdf)"
    Get-ChildItem -Path $publishDir -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.xml', '.pdf') } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

$cliExe = Join-Path $publishDir 'PowerForge.Cli.exe'
$ridIsWindows = $Runtime -like 'win-*'
$cliExeAlt = Join-Path $publishDir 'PowerForge.Cli'
$friendlyExe = Join-Path $publishDir ($ridIsWindows ? 'powerforge.exe' : 'powerforge')
foreach ($candidate in @($cliExe, $cliExeAlt)) {
    if (-not (Test-Path -LiteralPath $candidate)) { continue }

    # Keep a stable, user-friendly binary name without duplicating 50+ MB on disk.
    if (Test-Path -LiteralPath $friendlyExe) {
        Remove-Item -LiteralPath $friendlyExe -Force -ErrorAction SilentlyContinue
    }
    Move-Item -LiteralPath $candidate -Destination $friendlyExe -Force
    break
}

if ($ClearOut -and (Test-Path $OutDir) -and ($publishDir -ne $OutDir)) {
    Write-Step "Clearing $OutDir"
    Get-ChildItem -Path $OutDir -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

if ($publishDir -ne $OutDir) {
    Write-Step "Copying publish output -> $OutDir"
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $OutDir -Recurse -Force
}

if ($Zip) {
    $zipName = "PowerForge-" + $Framework + "-" + $Runtime + "-" + $Flavor + "-" + (Get-Date -Format 'yyyyMMdd-HHmm') + ".zip"
    $zipPath = Join-Path (Split-Path -Parent $OutDir) $zipName
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Write-Step ("Create zip -> {0}" -f $zipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($OutDir, $zipPath)
}

if ($stagingDir -and (Test-Path $stagingDir)) {
    Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Ok ("Built PowerForge -> {0}" -f $OutDir)
