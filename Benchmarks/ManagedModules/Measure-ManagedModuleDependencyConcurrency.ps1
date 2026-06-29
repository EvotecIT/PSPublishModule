param(
    [Parameter(Mandatory)]
    [string] $ModuleName,

    [Parameter(Mandatory)]
    [string] $Version,

    [string] $Repository = 'PSGallery',

    [string] $RepositoryName = 'PSGallery',

    [ValidateSet('Save', 'Install')]
    [string] $Operation = 'Save',

    [string[]] $DependencyConcurrency = @('1', '2', '4', '8', '16', '32'),

    [ValidateSet('Managed', 'ManagedAndInstallProviders')]
    [string] $ComparisonMode = 'Managed',

    [ValidateSet('Default', 'Cold', 'Warm')]
    [string] $CacheMode = 'Warm',

    [int] $RepeatCount = 2,

    [int] $SetupRetryCount = 2,

    [string] $OutputDirectory = '',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [switch] $AcceptLicense,

    [switch] $RotateEngineOrder,

    [switch] $RemoveOutputRoots
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $invariantCulture
[Threading.Thread]::CurrentThread.CurrentUICulture = $invariantCulture

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'Ignore\Benchmarks\MMDC'
}

$runStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runRoot = Join-Path $OutputDirectory ('Run-{0}-{1}' -f $runStamp, $PID)
$compareScript = Join-Path $PSScriptRoot 'Compare-ManagedModuleEngines.ps1'

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Artifacts.ps1')

function Resolve-DependencyConcurrencyValues {
    $values = [Collections.Generic.List[int]]::new()
    foreach ($entry in @($DependencyConcurrency)) {
        foreach ($token in ([string]$entry -split ',')) {
            $trimmed = $token.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmed)) {
                continue
            }

            $value = 0
            if (-not [int]::TryParse($trimmed, [Globalization.NumberStyles]::Integer, $invariantCulture, [ref]$value)) {
                throw "DependencyConcurrency value '$trimmed' is not a valid integer."
            }
            if ($value -le 0) {
                throw "DependencyConcurrency value '$trimmed' must be greater than zero."
            }
            if (-not $values.Contains($value)) {
                $values.Add($value)
            }
        }
    }

    , @($values.ToArray() | Sort-Object)
}

function Invoke-DependencyConcurrencyRun {
    param([int] $Concurrency)

    $childOutput = Join-Path $runRoot ('C{0}' -f $Concurrency)
    $engineList = if ($ComparisonMode -eq 'ManagedAndInstallProviders' -and $Operation -eq 'Install') {
        @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
    } else {
        @('Managed')
    }

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $compareScript,
        '-ModuleName',
        $ModuleName,
        '-Version',
        $Version,
        '-Repository',
        $Repository,
        '-RepositoryName',
        $RepositoryName,
        '-Operation',
        $Operation,
        '-Engine',
        ($engineList -join ','),
        '-RepeatCount',
        ([string]$RepeatCount),
        '-SetupRetryCount',
        ([string]$SetupRetryCount),
        '-ManagedDependencyConcurrency',
        ([string]$Concurrency),
        '-CacheMode',
        $CacheMode,
        '-OutputDirectory',
        $childOutput,
        '-Configuration',
        $Configuration,
        '-ChildTimeoutSeconds',
        '1800'
    )
    if ($SkipBuild.IsPresent) {
        $arguments += '-SkipBuild'
    }
    if ($AcceptLicense.IsPresent) {
        $arguments += '-AcceptLicense'
    }
    if ($RotateEngineOrder.IsPresent) {
        $arguments += '-RotateEngineOrder'
    }
    if ($RemoveOutputRoots.IsPresent) {
        $arguments += '-RemoveOutputRoots'
    }

    Write-Host "Running $ModuleName $Version $Operation with DependencyConcurrency=$Concurrency..."
    $process = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $arguments -NoNewWindow -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "Dependency concurrency benchmark failed for value $Concurrency with exit code $($process.ExitCode)."
    }

    $run = Get-ChildItem -LiteralPath $childOutput -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $run) {
        throw "Dependency concurrency benchmark for value $Concurrency did not produce an output run directory."
    }

    $summaryPath = Join-Path $run.FullName 'managed-module-summary.csv'
    if (-not (Test-Path -LiteralPath $summaryPath)) {
        throw "Dependency concurrency benchmark for value $Concurrency did not produce '$summaryPath'."
    }

    foreach ($row in @(Import-Csv -LiteralPath $summaryPath)) {
        $row | Add-Member -NotePropertyName DependencyConcurrency -NotePropertyValue $Concurrency -Force
        $row | Add-Member -NotePropertyName SourceRunPath -NotePropertyValue $run.FullName -Force
        $row
    }
}

New-Item -Path $runRoot -ItemType Directory -Force | Out-Null
$resolvedDependencyConcurrency = Resolve-DependencyConcurrencyValues
$rows = foreach ($value in $resolvedDependencyConcurrency) {
    Invoke-DependencyConcurrencyRun -Concurrency $value
}

$summaryPath = Join-Path $runRoot 'dependency-concurrency-summary.csv'
$summaryJsonPath = Join-Path $runRoot 'dependency-concurrency-summary.json'
$metadataPath = Join-Path $runRoot 'metadata.json'

Write-ManagedBenchmarkCsv -InputObject @($rows) -Path $summaryPath
Write-ManagedBenchmarkJson -InputObject @($rows) -Path $summaryJsonPath -Depth 8
Write-ManagedBenchmarkJson -InputObject ([ordered]@{
        ModuleName = $ModuleName
        Version = $Version
        Repository = $Repository
        RepositoryName = $RepositoryName
        Operation = $Operation
        DependencyConcurrency = @($resolvedDependencyConcurrency)
        ComparisonMode = $ComparisonMode
        CacheMode = $CacheMode
        RepeatCount = $RepeatCount
        SetupRetryCount = $SetupRetryCount
        AcceptLicense = $AcceptLicense.IsPresent
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        PSEdition = $PSVersionTable.PSEdition
        OS = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        ProcessArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        OutputDirectory = $runRoot
        SummaryPath = $summaryPath
    }) -Path $metadataPath -Depth 8

$rows
Write-Host "Dependency concurrency benchmark output: $runRoot"
