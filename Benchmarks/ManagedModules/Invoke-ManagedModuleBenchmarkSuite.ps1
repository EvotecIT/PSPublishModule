param(
    [string[]] $Suite = @('Smoke'),

    [string[]] $ScenarioName,

    [string[]] $HostName = @('Current'),

    [string[]] $Engine = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet'),

    [string] $ModuleFastSource = 'https://pwsh.gallery/index.json',

    [string[]] $Operation = @('Find', 'Save'),

    [string] $UpdateBaselineVersion = '',

    [ValidateSet('Default', 'Cold', 'Warm')]
    [string] $CacheMode = 'Default',

    [int] $RepeatCount = 1,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\ManagedModules\Suites'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [Alias('IncludeInstallManaged')]
    [switch] $IncludeInstall,

    [switch] $ValidateImport,

    [int] $ImportTimeoutSeconds = 120,

    [switch] $RotateEngineOrder,

    [int] $ManagedMaxRank = 0,

    [double] $ManagedMaxVsFastest = 0,

    [switch] $ListScenarios,

    [switch] $RemoveOutputRoots
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$compareScript = Join-Path $PSScriptRoot 'Compare-ManagedModuleEngines.ps1'
$suiteRoot = Join-Path $OutputDirectory ('Suite-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
$validSuites = @('Smoke', 'Graph', 'Az', 'Enterprise', 'All')
$validHosts = @('Current', 'PowerShell7', 'WindowsPowerShell')

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.PerformanceGate.ps1')

function Resolve-TokenList {
    param(
        [string[]] $Value,
        [string[]] $Allowed,
        [string] $Label
    )

    $resolved = [Collections.Generic.List[string]]::new()
    foreach ($item in @($Value)) {
        foreach ($token in ($item -split ',')) {
            $name = $token.Trim()
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            $match = @($Allowed | Where-Object { $_ -eq $name })
            if ($match.Count -eq 0) {
                throw "Unknown $Label '$name'. Valid values: $($Allowed -join ', ')."
            }

            if (-not $resolved.Contains($match[0])) {
                $resolved.Add($match[0])
            }
        }
    }

    , $resolved.ToArray()
}

function New-BenchmarkScenario {
    param(
        [string] $SuiteName,
        [string] $Name,
        [string] $ModuleName,
        [string] $Version = '',
        [string] $UpdateBaselineVersion = '',
        [bool] $AcceptLicense = $false,
        [string[]] $Operations = $Operation
    )

    [pscustomobject]@{
        Suite = $SuiteName
        Name = $Name
        ModuleName = $ModuleName
        Version = $Version
        UpdateBaselineVersion = $UpdateBaselineVersion
        AcceptLicense = $AcceptLicense
        Operations = $Operations
    }
}

function Get-ScenarioCatalog {
    @(
        New-BenchmarkScenario -SuiteName 'Smoke' -Name 'ThreadJob' -ModuleName 'ThreadJob' -Version '2.1.0' -UpdateBaselineVersion '2.0.3'
        New-BenchmarkScenario -SuiteName 'Graph' -Name 'Graph.Authentication' -ModuleName 'Microsoft.Graph.Authentication' -AcceptLicense $true
        New-BenchmarkScenario -SuiteName 'Graph' -Name 'Graph.Full' -ModuleName 'Microsoft.Graph' -AcceptLicense $true
        New-BenchmarkScenario -SuiteName 'Graph' -Name 'Graph.Beta.Full' -ModuleName 'Microsoft.Graph.Beta' -AcceptLicense $true
        New-BenchmarkScenario -SuiteName 'Az' -Name 'Az.Accounts' -ModuleName 'Az.Accounts'
        New-BenchmarkScenario -SuiteName 'Az' -Name 'Az.Resources' -ModuleName 'Az.Resources'
        New-BenchmarkScenario -SuiteName 'Az' -Name 'Az.Full' -ModuleName 'Az' -AcceptLicense $true
        New-BenchmarkScenario -SuiteName 'Enterprise' -Name 'Teams' -ModuleName 'MicrosoftTeams'
        New-BenchmarkScenario -SuiteName 'Enterprise' -Name 'ExchangeOnlineManagement' -ModuleName 'ExchangeOnlineManagement'
    )
}

function Resolve-ScenarioList {
    $selectedSuites = if ($Suite -contains 'All') {
        @('Smoke', 'Graph', 'Az', 'Enterprise')
    } else {
        $Suite
    }

    $scenarios = @(Get-ScenarioCatalog | Where-Object { $selectedSuites -contains $_.Suite })

    if ($ScenarioName -and $ScenarioName.Count -gt 0) {
        $selectedNames = @($ScenarioName | ForEach-Object {
                foreach ($token in ($_ -split ',')) {
                    $token.Trim()
                }
            } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $scenarios = @($scenarios | Where-Object { $selectedNames -contains $_.Name -or $selectedNames -contains $_.ModuleName })
        if ($scenarios.Count -eq 0) {
            throw "No benchmark scenarios matched '$($ScenarioName -join ', ')'. Use -ListScenarios to inspect available names."
        }
    }

    $scenarios
}

function Resolve-HostExecutable {
    param([string] $Name)

    switch ($Name) {
        'Current' {
            return (Get-Process -Id $PID).Path
        }
        'PowerShell7' {
            $command = Get-Command pwsh -ErrorAction SilentlyContinue
            if ($command) {
                return $command.Source
            }
            return $null
        }
        'WindowsPowerShell' {
            if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
                    [System.Runtime.InteropServices.OSPlatform]::Windows)) {
                return $null
            }

            $path = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
            if (Test-Path -LiteralPath $path) {
                return $path
            }
            return $null
        }
    }
}

function Invoke-LocalBuild {
    if ($SkipBuild.IsPresent) {
        return
    }

    $projectPath = Join-Path $repoRoot 'PSPublishModule\PSPublishModule.csproj'
    Write-Host "Building PSPublishModule ($Configuration) before suite..."
    & dotnet build $projectPath -c $Configuration --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for PSPublishModule ($Configuration)."
    }
}

function Get-ScenarioOperations {
    param([object] $Scenario)

    $operations = @($Scenario.Operations)
    if ($IncludeInstall.IsPresent -and -not ($operations -contains 'Install')) {
        $operations += 'Install'
    }

    $operations
}

function Invoke-ScenarioHostRun {
    param(
        [object] $Scenario,
        [string] $HostLabel,
        [string] $Executable
    )

    $scenarioRoot = Join-Path $suiteRoot ('{0}\{1}' -f $HostLabel, $Scenario.Name)
    New-Item -Path $scenarioRoot -ItemType Directory -Force | Out-Null

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $compareScript,
        '-ModuleName',
        $Scenario.ModuleName,
        '-Operation',
        ((Get-ScenarioOperations -Scenario $Scenario) -join ','),
        '-Engine',
        ($Engine -join ','),
        '-ModuleFastSource',
        $ModuleFastSource,
        '-RepeatCount',
        ([string]$RepeatCount),
        '-OutputDirectory',
        $scenarioRoot,
        '-Configuration',
        $Configuration,
        '-CacheMode',
        $CacheMode,
        '-SkipBuild'
    )
    if ($RemoveOutputRoots.IsPresent) {
        $arguments += '-RemoveOutputRoots'
    }

    if (-not [string]::IsNullOrWhiteSpace($Scenario.Version)) {
        $arguments += @('-Version', $Scenario.Version)
    }
    $baselineVersion = if (-not [string]::IsNullOrWhiteSpace($UpdateBaselineVersion)) {
        $UpdateBaselineVersion
    } else {
        [string] $Scenario.UpdateBaselineVersion
    }
    if (-not [string]::IsNullOrWhiteSpace($baselineVersion)) {
        $arguments += @('-UpdateBaselineVersion', $baselineVersion)
    }
    if ($Scenario.AcceptLicense) {
        $arguments += '-AcceptLicense'
    }
    if ($ValidateImport.IsPresent) {
        $arguments += '-ValidateImport'
        $arguments += @('-ImportTimeoutSeconds', ([string]$ImportTimeoutSeconds))
    }
    if ($RotateEngineOrder.IsPresent) {
        $arguments += '-RotateEngineOrder'
    }

    Write-Host "Running $($Scenario.Name) on $HostLabel..."
    $processOutput = @(& $Executable @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($line in $processOutput) {
        Write-Host $line
    }

    $run = Get-ChildItem -LiteralPath $scenarioRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($exitCode -ne 0) {
        throw "Benchmark scenario '$($Scenario.Name)' failed on '$HostLabel' with exit code $exitCode."
    }

    if (-not $run) {
        throw "Benchmark scenario '$($Scenario.Name)' on '$HostLabel' did not produce an output run directory."
    }

    return $run
}

function Add-SummaryRows {
    param(
        [Collections.Generic.List[object]] $Rows,
        [object] $Scenario,
        [string] $HostLabel,
        [string] $RunPath
    )

    $comparisonPath = Join-Path $RunPath 'managed-module-comparison.csv'
    $metadataPath = Join-Path $RunPath 'metadata.json'
    $runMetadata = if (Test-Path -LiteralPath $metadataPath) {
        Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    } else {
        $null
    }
    $resolvedBaseline = if ($runMetadata -and $runMetadata.PSObject.Properties['ResolvedUpdateBaselineVersion']) {
        [string]$runMetadata.ResolvedUpdateBaselineVersion
    } else {
        [string]$Scenario.UpdateBaselineVersion
    }
    $resolvedTarget = if ($runMetadata -and $runMetadata.PSObject.Properties['ResolvedUpdateTargetVersion']) {
        [string]$runMetadata.ResolvedUpdateTargetVersion
    } else {
        [string]$Scenario.Version
    }

    if (Test-Path -LiteralPath $comparisonPath) {
        foreach ($row in (Import-Csv -LiteralPath $comparisonPath)) {
            $Rows.Add([pscustomobject]@{
                Suite = $Scenario.Suite
                Scenario = $Scenario.Name
                ModuleName = $Scenario.ModuleName
                UpdateBaselineVersion = $resolvedBaseline
                UpdateTargetVersion = $resolvedTarget
                Host = $HostLabel
                Operation = $row.Operation
                FastestEngine = $row.FastestEngine
                FastestMs = $row.FastestMs
                ManagedMs = $row.ManagedMs
                ManagedRank = $row.ManagedRank
                ManagedVsFastest = $row.ManagedVsFastest
                ManagedPackageCount = $row.ManagedPackageCount
                ManagedDependencyCount = $row.ManagedDependencyCount
                ManagedUniquePackageCount = $row.ManagedUniquePackageCount
                ManagedUniqueDependencyCount = $row.ManagedUniqueDependencyCount
                ManagedInstalledPackageCount = $row.ManagedInstalledPackageCount
                ManagedAlreadyInstalledPackageCount = $row.ManagedAlreadyInstalledPackageCount
                ManagedRootElapsedMs = $row.ManagedRootElapsedMs
                ManagedHarnessOverheadMs = $row.ManagedHarnessOverheadMs
                ManagedRepositoryRequests = $row.ManagedRepositoryRequests
                ManagedPackageRepositoryRequests = $row.ManagedPackageRepositoryRequests
                ManagedDownloadBytes = $row.ManagedDownloadBytes
                ManagedCacheHits = $row.ManagedCacheHits
                ManagedMaintenanceActions = $row.ManagedMaintenanceActions
                ManagedMaintenanceFindings = $row.ManagedMaintenanceFindings
                ManagedRootDependencyMs = $row.ManagedRootDependencyMs
                ManagedDownloadMs = $row.ManagedDownloadMs
                ManagedExtractionMs = $row.ManagedExtractionMs
                ManagedPromotionMs = $row.ManagedPromotionMs
                RunPath = $RunPath
            })
        }
    }
}

$Suite = Resolve-TokenList -Value $Suite -Allowed $validSuites -Label 'suite'
$HostName = Resolve-TokenList -Value $HostName -Allowed $validHosts -Label 'host'
$scenarios = Resolve-ScenarioList
if ($ListScenarios.IsPresent) {
    $scenarios | Select-Object Suite, Name, ModuleName, Version, UpdateBaselineVersion, AcceptLicense, Operations
    return
}

New-Item -Path $suiteRoot -ItemType Directory -Force | Out-Null
Invoke-LocalBuild

$summaryRows = [Collections.Generic.List[object]]::new()
$hostRows = [Collections.Generic.List[object]]::new()

foreach ($hostLabel in $HostName) {
    $executable = Resolve-HostExecutable -Name $hostLabel
    if (-not $executable) {
        $hostRows.Add([pscustomobject]@{
            Host = $hostLabel
            Status = 'Skipped'
            Executable = ''
            Reason = 'Host executable was not found on this machine.'
        })
        continue
    }

    $hostRows.Add([pscustomobject]@{
        Host = $hostLabel
        Status = 'Available'
        Executable = $executable
        Reason = ''
    })

    foreach ($scenario in $scenarios) {
        $run = Invoke-ScenarioHostRun -Scenario $scenario -HostLabel $hostLabel -Executable $executable
        Add-SummaryRows -Rows $summaryRows -Scenario $scenario -HostLabel $hostLabel -RunPath $run.FullName
    }
}

$summaryPath = Join-Path $suiteRoot 'suite-summary.csv'
$summaryJsonPath = Join-Path $suiteRoot 'suite-summary.json'
$hostsPath = Join-Path $suiteRoot 'suite-hosts.csv'
$gatePath = Join-Path $suiteRoot 'suite-gate.csv'
$metadataPath = Join-Path $suiteRoot 'metadata.json'
$gateViolations = @(Get-ManagedPerformanceGateViolation -Rows @($summaryRows) -MaxRank $ManagedMaxRank -MaxVsFastest $ManagedMaxVsFastest)

$summaryRows | Export-Csv -LiteralPath $summaryPath -NoTypeInformation
$summaryRows | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8
$hostRows | Export-Csv -LiteralPath $hostsPath -NoTypeInformation
if ($ManagedMaxRank -gt 0 -or $ManagedMaxVsFastest -gt 0) {
    $gateViolations | Export-Csv -LiteralPath $gatePath -NoTypeInformation
}

$metadata = [ordered]@{
    Suites = $Suite
    ScenarioNames = $ScenarioName
    Hosts = $HostName
    Engines = $Engine
    ModuleFastSource = $ModuleFastSource
    Operations = $Operation
    UpdateBaselineVersion = $UpdateBaselineVersion
    CacheMode = $CacheMode
    RepeatCount = $RepeatCount
    IncludeInstall = $IncludeInstall.IsPresent
    ValidateImport = $ValidateImport.IsPresent
    ImportTimeoutSeconds = $ImportTimeoutSeconds
    RotateEngineOrder = $RotateEngineOrder.IsPresent
    ManagedMaxRank = $ManagedMaxRank
    ManagedMaxVsFastest = $ManagedMaxVsFastest
    ManagedPerformanceGatePassed = $gateViolations.Count -eq 0
    RemoveOutputRoots = $RemoveOutputRoots.IsPresent
    OutputDirectory = $suiteRoot
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

$summaryRows
Write-Host "Benchmark suite output: $suiteRoot"
if ($gateViolations.Count -gt 0) {
    throw "Managed performance gate failed for $($gateViolations.Count) suite row(s). See '$gatePath'."
}
