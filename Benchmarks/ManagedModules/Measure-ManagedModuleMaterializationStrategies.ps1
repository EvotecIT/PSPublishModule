param(
    [Parameter(Mandatory)]
    [string] $ModuleName,

    [Parameter(Mandatory)]
    [string] $Version,

    [string] $Repository = 'PSGallery',

    [string] $RepositoryName = 'PSGallery',

    [int] $RepeatCount = 3,

    [string] $OutputDirectory = '',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [switch] $AcceptLicense,

    [switch] $KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $invariantCulture
[Threading.Thread]::CurrentThread.CurrentUICulture = $invariantCulture

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'Ignore\Benchmarks\MM-Materialization'
}
$runStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runRoot = Join-Path $OutputDirectory ('Run-{0}-{1}' -f $runStamp, $PID)
$tempRoot = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    Join-Path ([IO.Path]::GetPathRoot($repoRoot)) 'Temp\PFMM-Materialization'
} else {
    Join-Path ([IO.Path]::GetTempPath()) 'pfmm-materialization'
}
$workRoot = Join-Path $tempRoot ('Run-{0}-{1}' -f $runStamp, $PID)
$packageCacheRoot = Join-Path $workRoot 'PackageCache'
$saveRoot = Join-Path $workRoot 'SaveRoots'

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Artifacts.ps1')

function Import-LocalManagedModule {
    if (-not $SkipBuild.IsPresent) {
        $projectPath = Join-Path $repoRoot 'PSPublishModule\PSPublishModule.csproj'
        & dotnet build $projectPath -c $Configuration --nologo --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for PSPublishModule ($Configuration)."
        }
    }

    $moduleBinary = Join-Path $repoRoot "PSPublishModule\bin\$Configuration\net8.0\PSPublishModule.dll"
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        $moduleBinary = Join-Path $repoRoot "PSPublishModule\bin\$Configuration\net472\PSPublishModule.dll"
    }

    if (-not (Test-Path -LiteralPath $moduleBinary)) {
        throw "Built module binary was not found: $moduleBinary"
    }

    Import-Module -Name $moduleBinary -Force
    $moduleBinary
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)][string] $Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join ''
        } finally {
            $sha.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Get-CachedPackagePath {
    param(
        [Parameter(Mandatory)][string] $PackageCacheDirectory,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $PackageVersion
    )

    $fileName = ('{0}.{1}.nupkg' -f $Name.Trim().ToLowerInvariant(), $PackageVersion.Trim().ToLowerInvariant())
    $path = Join-Path $PackageCacheDirectory $fileName
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Cached package was not found: $path"
    }

    $path
}

function Get-ExtractedCacheRoot {
    param(
        [Parameter(Mandatory)][string] $PackageCacheDirectory,
        [Parameter(Mandatory)][string] $PackageSha256
    )

    Join-Path $PackageCacheDirectory (Join-Path '.x' (Join-Path '1' $PackageSha256.Substring(0, 32)))
}

function Remove-ExtractedCacheRoot {
    param([Parameter(Mandatory)][string] $CacheRoot)

    if (-not (Test-Path -LiteralPath $CacheRoot)) {
        return
    }

    Remove-Item -LiteralPath $CacheRoot -Recurse -Force
}

function Invoke-MaterializationSave {
    param(
        [Parameter(Mandatory)][string] $Destination,
        [string] $Detail = ''
    )

    $parameters = @{
        Name = $ModuleName
        Version = $Version
        Repository = $Repository
        RepositoryName = $RepositoryName
        Path = $Destination
        PackageCacheDirectory = $packageCacheRoot
        Force = $true
        SkipDependencyCheck = $true
    }
    if ($AcceptLicense.IsPresent) {
        $parameters.AcceptLicense = $true
    }

    $result = Save-ManagedModule @parameters
    if (-not [string]::IsNullOrWhiteSpace($Detail)) {
        Write-ManagedBenchmarkJson -InputObject $result -Path $Detail -Depth 8
    }

    $result
}

function New-MaterializationRow {
    param(
        [Parameter(Mandatory)][string] $Strategy,
        [Parameter(Mandatory)][int] $Iteration,
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)][TimeSpan] $Elapsed,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][string] $DetailPath
    )

    [pscustomobject]@{
        Strategy = $Strategy
        Iteration = $Iteration
        ModuleName = [string] $Result.Name
        Version = [string] $Result.Version
        Status = [string] $Result.Status
        Host = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'WindowsPowerShell' } else { 'PowerShell7' }
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        ElapsedMilliseconds = [math]::Round($Elapsed.TotalMilliseconds, 2)
        EngineElapsedMilliseconds = [math]::Round(([TimeSpan] $Result.Elapsed).TotalMilliseconds, 2)
        DownloadMilliseconds = [math]::Round(([TimeSpan] $Result.DownloadElapsed).TotalMilliseconds, 2)
        ExtractionMilliseconds = [math]::Round(([TimeSpan] $Result.ExtractionElapsed).TotalMilliseconds, 2)
        PromotionMilliseconds = [math]::Round(([TimeSpan] $Result.PromotionElapsed).TotalMilliseconds, 2)
        PromotionDirectMaterializationMilliseconds = [math]::Round(([TimeSpan] $Result.PromotionDirectMaterializationElapsed).TotalMilliseconds, 2)
        PromotionMaterializedDirectly = [bool] $Result.PromotionMaterializedDirectly
        ExtractionFromCache = [bool] $Result.ExtractionFromCache
        DownloadFromCache = if ($null -ne $Result.Download) { [bool] $Result.Download.FromCache } else { $false }
        RepositoryRequests = [long] $Result.RepositoryRequestCount
        PackageRepositoryRequests = [long] $Result.PackageRepositoryRequestCount
        DownloadBytes = if ($null -ne $Result.Download) { [long] $Result.Download.BytesWritten } else { 0L }
        FileCount = [int] $Result.FileCount
        ExtractedBytes = [long] $Result.ExtractedBytes
        OutputRoot = $Destination
        DetailPath = $DetailPath
    }
}

function Get-HostOsDescription {
    try {
        [Runtime.InteropServices.RuntimeInformation]::OSDescription
    } catch {
        [Environment]::OSVersion.VersionString
    }
}

function Get-HostProcessArchitecture {
    try {
        [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    } catch {
        [Environment]::GetEnvironmentVariable('PROCESSOR_ARCHITECTURE')
    }
}

$moduleBinary = Import-LocalManagedModule
New-Item -Path $runRoot, $workRoot, $packageCacheRoot, $saveRoot -ItemType Directory -Force | Out-Null

$rows = [Collections.Generic.List[object]]::new()
try {
    $seedDestination = Join-Path $saveRoot 'seed'
    Invoke-MaterializationSave -Destination $seedDestination | Out-Null
    $packagePath = Get-CachedPackagePath -PackageCacheDirectory $packageCacheRoot -Name $ModuleName -PackageVersion $Version
    $packageSha256 = Get-Sha256Hex -Path $packagePath
    $extractedCacheRoot = Get-ExtractedCacheRoot -PackageCacheDirectory $packageCacheRoot -PackageSha256 $packageSha256
    if (-not (Test-Path -LiteralPath $extractedCacheRoot)) {
        throw "Extracted package cache was not found after seed save: $extractedCacheRoot"
    }

    foreach ($iteration in 1..$RepeatCount) {
        foreach ($strategy in @('ExtractedPayload', 'PackageArchive')) {
            if ($strategy -eq 'PackageArchive') {
                Remove-ExtractedCacheRoot -CacheRoot $extractedCacheRoot
            }

            $destination = Join-Path $saveRoot ('{0}-{1}' -f $strategy, $iteration)
            $detailPath = Join-Path $runRoot ('{0}-{1}.json' -f $strategy, $iteration)
            $stopwatch = [Diagnostics.Stopwatch]::StartNew()
            $result = Invoke-MaterializationSave -Destination $destination -Detail $detailPath
            $stopwatch.Stop()
            $rows.Add((New-MaterializationRow -Strategy $strategy -Iteration $iteration -Result $result -Elapsed $stopwatch.Elapsed -Destination $destination -DetailPath $detailPath))

            if ($strategy -eq 'PackageArchive' -and -not (Test-Path -LiteralPath $extractedCacheRoot)) {
                throw "PackageArchive run did not repopulate extracted package cache: $extractedCacheRoot"
            }
        }
    }

    $summary = @(
        $rows |
            Group-Object Strategy |
            ForEach-Object {
                $ordered = @($_.Group | Sort-Object {[double] $_.ElapsedMilliseconds})
                $medianIndex = [int] [math]::Floor(($ordered.Count - 1) / 2)
                [pscustomobject]@{
                    Strategy = $_.Name
                    Runs = $_.Count
                    MedianMilliseconds = [double] $ordered[$medianIndex].ElapsedMilliseconds
                    MinMilliseconds = [double] ($_.Group | Measure-Object ElapsedMilliseconds -Minimum).Minimum
                    MaxMilliseconds = [double] ($_.Group | Measure-Object ElapsedMilliseconds -Maximum).Maximum
                    MedianExtractionMilliseconds = [double] (@($_.Group | Sort-Object {[double] $_.ExtractionMilliseconds})[$medianIndex].ExtractionMilliseconds)
                    ExtractionFromCache = [bool] $_.Group[0].ExtractionFromCache
                }
            }
    )

    $metadata = [ordered]@{
        ModuleName = $ModuleName
        Version = $Version
        Repository = $Repository
        RepositoryName = $RepositoryName
        RepeatCount = $RepeatCount
        ModuleBinary = $moduleBinary
        PackagePath = $packagePath
        PackageSha256 = $packageSha256
        ExtractedCacheRoot = $extractedCacheRoot
        OutputDirectory = $runRoot
        WorkRoot = $workRoot
        KeepArtifacts = $KeepArtifacts.IsPresent
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        PSEdition = $PSVersionTable.PSEdition
        OS = Get-HostOsDescription
        ProcessArchitecture = Get-HostProcessArchitecture
    }

    Write-ManagedBenchmarkCsv -InputObject @($rows) -Path (Join-Path $runRoot 'materialization-results.csv')
    Write-ManagedBenchmarkCsv -InputObject @($summary) -Path (Join-Path $runRoot 'materialization-summary.csv')
    Write-ManagedBenchmarkJson -InputObject @($rows) -Path (Join-Path $runRoot 'materialization-results.json') -Depth 8
    Write-ManagedBenchmarkJson -InputObject $metadata -Path (Join-Path $runRoot 'metadata.json') -Depth 8

    $summary
    Write-Host "Materialization benchmark output: $runRoot"
} finally {
    if (-not $KeepArtifacts.IsPresent -and (Test-Path -LiteralPath $workRoot)) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
