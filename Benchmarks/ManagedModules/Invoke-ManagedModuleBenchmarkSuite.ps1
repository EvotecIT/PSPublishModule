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

    [int] $SetupRetryCount = 2,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\MM'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [Alias('IncludeInstallManaged')]
    [switch] $IncludeInstall,

    [switch] $ValidateImport,

    [int] $ImportTimeoutSeconds = 120,

    [int] $ChildTimeoutSeconds = 1800,

    [switch] $RotateEngineOrder,

    [int] $ManagedMaxRank = 0,

    [double] $ManagedMaxVsFastest = 0,

    [int] $ManagedMinAuthenticodeCheckedFiles = 0,

    [int] $ManagedMinAuthenticodeCatalogFiles = 0,

    [double] $ManagedMaxWindowsPowerShellVsPowerShell7 = 0,

    [switch] $UseScenarioGates,

    [switch] $AuthenticodeCheck,

    [switch] $ListScenarios,

    [switch] $RemoveOutputRoots
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cacheModeWasBound = $PSBoundParameters.ContainsKey('CacheMode')
$repeatCountWasBound = $PSBoundParameters.ContainsKey('RepeatCount')
$providerDefaultModuleFastSource = 'ProviderDefault'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$compareScript = Join-Path $PSScriptRoot 'Compare-ManagedModuleEngines.ps1'
$suiteRoot = Join-Path $OutputDirectory ('S{0}-{1}' -f (Get-Date -Format 'yyyyMMddHHmmss'), $PID)
$validSuites = @('Smoke', 'Graph', 'Az', 'Enterprise', 'LifecycleGate', 'HeavyLifecycleGate', 'HeavySaveGate', 'HeavySaveCacheGate', 'PublishGate', 'SpeedGate', 'SaveGate', 'SecurityGate', 'RepairGate', 'All')
$validHosts = @('Current', 'PowerShell7', 'WindowsPowerShell')

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.PerformanceGate.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.HostComparison.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.OptimizationTargets.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Artifacts.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Process.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.EngineSummary.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Scoreboard.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Scenarios.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.SuiteNotes.ps1')

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

function Resolve-ScenarioList {
    $selectedSuites = if ($Suite -contains 'All') {
        @('Smoke', 'Graph', 'Az', 'Enterprise', 'LifecycleGate', 'HeavyLifecycleGate', 'HeavySaveGate', 'HeavySaveCacheGate', 'PublishGate', 'SpeedGate', 'SaveGate', 'SecurityGate', 'RepairGate')
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

function Get-ScenarioRepairScenarios {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['RepairScenarios'] -and $Scenario.RepairScenarios -and @($Scenario.RepairScenarios).Count -gt 0) {
        return @($Scenario.RepairScenarios)
    }

    @()
}

function Get-ScenarioEngines {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['Engines'] -and $Scenario.Engines -and @($Scenario.Engines).Count -gt 0) {
        return @($Scenario.Engines)
    }

    $Engine
}

function Get-ScenarioRepository {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['Repository'] -and -not [string]::IsNullOrWhiteSpace($Scenario.Repository)) {
        return [string]$Scenario.Repository
    }

    ''
}

function Get-ScenarioRepositoryName {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['RepositoryName'] -and -not [string]::IsNullOrWhiteSpace($Scenario.RepositoryName)) {
        return [string]$Scenario.RepositoryName
    }

    ''
}

function Get-ScenarioModuleFastSource {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['ModuleFastSource'] -and -not [string]::IsNullOrWhiteSpace($Scenario.ModuleFastSource)) {
        if ([string]::Equals([string]$Scenario.ModuleFastSource, $providerDefaultModuleFastSource, [StringComparison]::OrdinalIgnoreCase)) {
            return ''
        }

        return [string]$Scenario.ModuleFastSource
    }

    $ModuleFastSource
}

function Get-ScenarioModuleFastSourceLabel {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['ModuleFastSource'] -and -not [string]::IsNullOrWhiteSpace($Scenario.ModuleFastSource)) {
        return [string]$Scenario.ModuleFastSource
    }

    $ModuleFastSource
}

function Get-ScenarioManagedMaxRank {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['ManagedMaxRank']) {
        return [int] $Scenario.ManagedMaxRank
    }

    0
}

function Get-ScenarioManagedMaxVsFastest {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['ManagedMaxVsFastest']) {
        return [double] $Scenario.ManagedMaxVsFastest
    }

    0.0
}

function Get-ScenarioManagedMinAuthenticodeCheckedFiles {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['ManagedMinAuthenticodeCheckedFiles']) {
        return [int] $Scenario.ManagedMinAuthenticodeCheckedFiles
    }

    0
}

function Get-ScenarioManagedMinAuthenticodeCatalogFiles {
    param([object] $Scenario)

    if ($Scenario.PSObject.Properties['ManagedMinAuthenticodeCatalogFiles']) {
        return [int] $Scenario.ManagedMinAuthenticodeCatalogFiles
    }

    0
}

function Get-EffectiveManagedMaxRank {
    param([object] $Scenario)

    if ($ManagedMaxRank -gt 0) {
        return $ManagedMaxRank
    }
    if ($UseScenarioGates.IsPresent) {
        return Get-ScenarioManagedMaxRank -Scenario $Scenario
    }

    0
}

function Get-EffectiveManagedMaxVsFastest {
    param([object] $Scenario)

    if ($ManagedMaxVsFastest -gt 0) {
        return $ManagedMaxVsFastest
    }
    if ($UseScenarioGates.IsPresent) {
        return Get-ScenarioManagedMaxVsFastest -Scenario $Scenario
    }

    0.0
}

function Get-EffectiveManagedMinAuthenticodeCheckedFiles {
    param([object] $Scenario)

    if ($ManagedMinAuthenticodeCheckedFiles -gt 0) {
        return $ManagedMinAuthenticodeCheckedFiles
    }
    if ($UseScenarioGates.IsPresent) {
        return Get-ScenarioManagedMinAuthenticodeCheckedFiles -Scenario $Scenario
    }

    0
}

function Get-EffectiveManagedMinAuthenticodeCatalogFiles {
    param([object] $Scenario)

    if ($ManagedMinAuthenticodeCatalogFiles -gt 0) {
        return $ManagedMinAuthenticodeCatalogFiles
    }
    if ($UseScenarioGates.IsPresent) {
        return Get-ScenarioManagedMinAuthenticodeCatalogFiles -Scenario $Scenario
    }

    0
}

function Get-EffectiveCacheMode {
    param([object] $Scenario)

    if ($cacheModeWasBound) {
        return $CacheMode
    }
    if ($Scenario.PSObject.Properties['CacheMode'] -and -not [string]::IsNullOrWhiteSpace([string]$Scenario.CacheMode)) {
        return [string]$Scenario.CacheMode
    }

    $CacheMode
}

function Get-EffectiveRepeatCount {
    param([object] $Scenario)

    if ($repeatCountWasBound) {
        return $RepeatCount
    }
    if ($Scenario.PSObject.Properties['RepeatCount'] -and [int]$Scenario.RepeatCount -gt 0) {
        return [int]$Scenario.RepeatCount
    }

    $RepeatCount
}

function Get-HostOutputLabel {
    param([string] $HostLabel)

    switch ($HostLabel) {
        'PowerShell7' { 'ps7' }
        'WindowsPowerShell' { 'ps5' }
        'Current' { 'cur' }
        default { $HostLabel }
    }
}

function Get-ScenarioOutputLabel {
    param([object] $Scenario)

    $index = [array]::IndexOf(@($script:scenarios), $Scenario)
    if ($index -lt 0) {
        $index = 0
    }

    'sc{0:D2}' -f ($index + 1)
}

function Invoke-ScenarioHostRun {
    param(
        [object] $Scenario,
        [string] $HostLabel,
        [string] $Executable
    )

    $scenarioRoot = Join-Path $suiteRoot ('{0}\{1}' -f (Get-HostOutputLabel -HostLabel $HostLabel), (Get-ScenarioOutputLabel -Scenario $Scenario))
    New-Item -Path $scenarioRoot -ItemType Directory -Force | Out-Null
    $scenarioCacheMode = Get-EffectiveCacheMode -Scenario $Scenario
    $scenarioRepeatCount = Get-EffectiveRepeatCount -Scenario $Scenario

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
        ((Get-ScenarioEngines -Scenario $Scenario) -join ','),
        '-ModuleFastSource',
        (Get-ScenarioModuleFastSource -Scenario $Scenario),
        '-RepeatCount',
        ([string]$scenarioRepeatCount),
        '-SetupRetryCount',
        ([string]$SetupRetryCount),
        '-OutputDirectory',
        $scenarioRoot,
        '-Configuration',
        $Configuration,
        '-CacheMode',
        $scenarioCacheMode,
        '-SkipBuild'
    )
    $scenarioRepository = Get-ScenarioRepository -Scenario $Scenario
    if (-not [string]::IsNullOrWhiteSpace($scenarioRepository)) {
        $arguments += @('-Repository', $scenarioRepository)
    }
    $scenarioRepositoryName = Get-ScenarioRepositoryName -Scenario $Scenario
    if (-not [string]::IsNullOrWhiteSpace($scenarioRepositoryName)) {
        $arguments += @('-RepositoryName', $scenarioRepositoryName)
    }
    if ($RemoveOutputRoots.IsPresent) {
        $arguments += '-RemoveOutputRoots'
    }
    $repairScenarios = @(Get-ScenarioRepairScenarios -Scenario $Scenario)
    if ($repairScenarios.Count -gt 0) {
        $arguments += @('-RepairScenario', ($repairScenarios -join ','))
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
    $scenarioAuthenticodeCheck = $Scenario.PSObject.Properties['AuthenticodeCheck'] -and [bool]$Scenario.AuthenticodeCheck
    if ($AuthenticodeCheck.IsPresent -or $scenarioAuthenticodeCheck) {
        $arguments += '-AuthenticodeCheck'
    }
    if ($ValidateImport.IsPresent) {
        $arguments += '-ValidateImport'
        $arguments += @('-ImportTimeoutSeconds', ([string]$ImportTimeoutSeconds))
    }
    $arguments += @('-ChildTimeoutSeconds', ([string]$ChildTimeoutSeconds))
    if ($RotateEngineOrder.IsPresent) {
        $arguments += '-RotateEngineOrder'
    }

    Write-Host "Running $($Scenario.Name) on $HostLabel..."
    $processResult = Invoke-ManagedBenchmarkProcess `
        -FileName $Executable `
        -Arguments $arguments `
        -WorkingDirectory (Get-Location).Path `
        -TimeoutSeconds $ChildTimeoutSeconds `
        -TimeoutMessage "Benchmark scenario '$($Scenario.Name)' on '$HostLabel' exceeded $ChildTimeoutSeconds seconds."
    foreach ($line in @($processResult.StandardOutput -split "\r?\n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        Write-Host $line
    }
    foreach ($line in @($processResult.StandardError -split "\r?\n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        Write-Host $line
    }

    $run = Get-ChildItem -LiteralPath $scenarioRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($processResult.TimedOut) {
        throw $processResult.TimeoutMessage
    }

    if ($processResult.ExitCode -ne 0) {
        throw "Benchmark scenario '$($Scenario.Name)' failed on '$HostLabel' with exit code $($processResult.ExitCode)."
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
                BenchmarkRole = $Scenario.BenchmarkRole
                ComparisonScope = $Scenario.ComparisonScope
                BenchmarkInterpretation = $Scenario.BenchmarkInterpretation
                ModuleName = $Scenario.ModuleName
                Engines = (Get-ScenarioEngines -Scenario $Scenario) -join ','
                RepairScenarios = (Get-ScenarioRepairScenarios -Scenario $Scenario) -join ','
                AuthenticodeCheck = $Scenario.AuthenticodeCheck
                Repository = Get-ScenarioRepository -Scenario $Scenario
                RepositoryName = Get-ScenarioRepositoryName -Scenario $Scenario
                ModuleFastSource = Get-ScenarioModuleFastSourceLabel -Scenario $Scenario
                CacheMode = Get-EffectiveCacheMode -Scenario $Scenario
                RepeatCount = Get-EffectiveRepeatCount -Scenario $Scenario
                GateManagedMaxRank = Get-EffectiveManagedMaxRank -Scenario $Scenario
                GateManagedMaxVsFastest = Get-EffectiveManagedMaxVsFastest -Scenario $Scenario
                GateManagedMinAuthenticodeCheckedFiles = Get-EffectiveManagedMinAuthenticodeCheckedFiles -Scenario $Scenario
                GateManagedMinAuthenticodeCatalogFiles = Get-EffectiveManagedMinAuthenticodeCatalogFiles -Scenario $Scenario
                UpdateBaselineVersion = $resolvedBaseline
                UpdateTargetVersion = $resolvedTarget
                Host = $HostLabel
                Operation = $row.Operation
                FastestEngine = $row.FastestEngine
                FastestMs = $row.FastestMs
                ManagedMs = $row.ManagedMs
                ManagedRank = $row.ManagedRank
                ManagedVsFastest = $row.ManagedVsFastest
                ManagedFirstIteration = $row.ManagedFirstIteration
                ManagedLastIteration = $row.ManagedLastIteration
                ManagedFirstMs = $row.ManagedFirstMs
                ManagedLastMs = $row.ManagedLastMs
                ManagedOutputFileCount = $row.ManagedOutputFileCount
                ManagedOutputBytes = $row.ManagedOutputBytes
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
                ManagedPackageRepositoryRedirects = $row.ManagedPackageRepositoryRedirects
                ManagedDownloadBytes = $row.ManagedDownloadBytes
                ManagedCacheHits = $row.ManagedCacheHits
                ManagedExtractionCacheHits = $row.ManagedExtractionCacheHits
                ManagedCoalescedWaitCount = $row.ManagedCoalescedWaitCount
                ManagedCoalescedWaitMs = $row.ManagedCoalescedWaitMs
                ManagedSlowestCoalescedWaitMs = $row.ManagedSlowestCoalescedWaitMs
                ManagedInstallLockWaitCount = $row.ManagedInstallLockWaitCount
                ManagedInstallLockWaitMs = $row.ManagedInstallLockWaitMs
                ManagedSlowestInstallLockWaitMs = $row.ManagedSlowestInstallLockWaitMs
                ManagedSlowestDependencyPackageMs = $row.ManagedSlowestDependencyPackageMs
                ManagedSlowestDependencyQueueWaitMs = $row.ManagedSlowestDependencyQueueWaitMs
                ManagedSlowestMaterializedPackageMs = $row.ManagedSlowestMaterializedPackageMs
                ManagedCriticalDependencyBranchMs = $row.ManagedCriticalDependencyBranchMs
                ManagedCriticalRootBranchMs = $row.ManagedCriticalRootBranchMs
                ManagedCriticalMaterializationBranchMs = $row.ManagedCriticalMaterializationBranchMs
                ManagedAuthenticodeCheckedFiles = $row.ManagedAuthenticodeCheckedFiles
                ManagedAuthenticodeCatalogFiles = $row.ManagedAuthenticodeCatalogFiles
                ManagedFirstRepositoryRequests = $row.ManagedFirstRepositoryRequests
                ManagedLastRepositoryRequests = $row.ManagedLastRepositoryRequests
                ManagedFirstPackageRepositoryRequests = $row.ManagedFirstPackageRepositoryRequests
                ManagedLastPackageRepositoryRequests = $row.ManagedLastPackageRepositoryRequests
                ManagedFirstRootDependencyMs = $row.ManagedFirstRootDependencyMs
                ManagedLastRootDependencyMs = $row.ManagedLastRootDependencyMs
                ManagedFirstRootDependencyUnattributedMs = $row.ManagedFirstRootDependencyUnattributedMs
                ManagedLastRootDependencyUnattributedMs = $row.ManagedLastRootDependencyUnattributedMs
                ManagedFirstDependencyQueueWaitMs = $row.ManagedFirstDependencyQueueWaitMs
                ManagedLastDependencyQueueWaitMs = $row.ManagedLastDependencyQueueWaitMs
                ManagedFirstDependencyBranchElapsedMs = $row.ManagedFirstDependencyBranchElapsedMs
                ManagedLastDependencyBranchElapsedMs = $row.ManagedLastDependencyBranchElapsedMs
                ManagedFirstDownloadMs = $row.ManagedFirstDownloadMs
                ManagedLastDownloadMs = $row.ManagedLastDownloadMs
                ManagedFirstExtractionMs = $row.ManagedFirstExtractionMs
                ManagedLastExtractionMs = $row.ManagedLastExtractionMs
                ManagedFirstExtractionCacheLockWaitMs = $row.ManagedFirstExtractionCacheLockWaitMs
                ManagedLastExtractionCacheLockWaitMs = $row.ManagedLastExtractionCacheLockWaitMs
                ManagedFirstDependencyMs = $row.ManagedFirstDependencyMs
                ManagedLastDependencyMs = $row.ManagedLastDependencyMs
                ManagedFirstPromotionMs = $row.ManagedFirstPromotionMs
                ManagedLastPromotionMs = $row.ManagedLastPromotionMs
                ManagedFirstPromotionLockWaitMs = $row.ManagedFirstPromotionLockWaitMs
                ManagedLastPromotionLockWaitMs = $row.ManagedLastPromotionLockWaitMs
                ManagedFirstPromotionMoveMs = $row.ManagedFirstPromotionMoveMs
                ManagedLastPromotionMoveMs = $row.ManagedLastPromotionMoveMs
                ManagedFirstPromotionFinalMoveMs = $row.ManagedFirstPromotionFinalMoveMs
                ManagedLastPromotionFinalMoveMs = $row.ManagedLastPromotionFinalMoveMs
                ManagedFirstPromotionBackupMoveMs = $row.ManagedFirstPromotionBackupMoveMs
                ManagedLastPromotionBackupMoveMs = $row.ManagedLastPromotionBackupMoveMs
                ManagedFirstPromotionBackupCleanupMs = $row.ManagedFirstPromotionBackupCleanupMs
                ManagedLastPromotionBackupCleanupMs = $row.ManagedLastPromotionBackupCleanupMs
                ManagedFirstPromotionOverwriteCount = $row.ManagedFirstPromotionOverwriteCount
                ManagedLastPromotionOverwriteCount = $row.ManagedLastPromotionOverwriteCount
                ManagedFirstDirectMaterializationCount = $row.ManagedFirstDirectMaterializationCount
                ManagedLastDirectMaterializationCount = $row.ManagedLastDirectMaterializationCount
                ManagedFirstPromotionDirectMaterializationMs = $row.ManagedFirstPromotionDirectMaterializationMs
                ManagedLastPromotionDirectMaterializationMs = $row.ManagedLastPromotionDirectMaterializationMs
                ManagedFirstDownloadBytes = $row.ManagedFirstDownloadBytes
                ManagedLastDownloadBytes = $row.ManagedLastDownloadBytes
                ManagedFirstCacheHits = $row.ManagedFirstCacheHits
                ManagedLastCacheHits = $row.ManagedLastCacheHits
                ManagedFirstExtractionCacheHits = $row.ManagedFirstExtractionCacheHits
                ManagedLastExtractionCacheHits = $row.ManagedLastExtractionCacheHits
                ManagedFirstCoalescedWaitMs = $row.ManagedFirstCoalescedWaitMs
                ManagedLastCoalescedWaitMs = $row.ManagedLastCoalescedWaitMs
                ManagedLastSlowestCoalescedWaitName = $row.ManagedLastSlowestCoalescedWaitName
                ManagedLastSlowestCoalescedWaitMs = $row.ManagedLastSlowestCoalescedWaitMs
                ManagedFirstInstallLockWaitMs = $row.ManagedFirstInstallLockWaitMs
                ManagedLastInstallLockWaitMs = $row.ManagedLastInstallLockWaitMs
                ManagedLastSlowestInstallLockWaitName = $row.ManagedLastSlowestInstallLockWaitName
                ManagedLastSlowestInstallLockWaitMs = $row.ManagedLastSlowestInstallLockWaitMs
                ManagedLastSlowestDependencyPackageName = $row.ManagedLastSlowestDependencyPackageName
                ManagedLastSlowestDependencyPackageParent = $row.ManagedLastSlowestDependencyPackageParent
                ManagedLastSlowestDependencyPackageMs = $row.ManagedLastSlowestDependencyPackageMs
                ManagedLastSlowestDependencyQueueWaitName = $row.ManagedLastSlowestDependencyQueueWaitName
                ManagedLastSlowestDependencyQueueWaitMs = $row.ManagedLastSlowestDependencyQueueWaitMs
                ManagedLastSlowestMaterializedPackageName = $row.ManagedLastSlowestMaterializedPackageName
                ManagedLastSlowestMaterializedPackageMs = $row.ManagedLastSlowestMaterializedPackageMs
                ManagedLastSlowestMaterializedPackageExtractionMs = $row.ManagedLastSlowestMaterializedPackageExtractionMs
                ManagedLastSlowestMaterializedPackageExtractionCacheLockWaitMs = $row.ManagedLastSlowestMaterializedPackageExtractionCacheLockWaitMs
                ManagedLastSlowestMaterializedPackagePromotionMs = $row.ManagedLastSlowestMaterializedPackagePromotionMs
                ManagedLastSlowestMaterializedPackagePromotionLockWaitMs = $row.ManagedLastSlowestMaterializedPackagePromotionLockWaitMs
                ManagedLastSlowestMaterializedPackagePromotionMoveMs = $row.ManagedLastSlowestMaterializedPackagePromotionMoveMs
                ManagedLastSlowestMaterializedPackagePromotionFinalMoveMs = $row.ManagedLastSlowestMaterializedPackagePromotionFinalMoveMs
                ManagedLastSlowestMaterializedPackagePromotionBackupMoveMs = $row.ManagedLastSlowestMaterializedPackagePromotionBackupMoveMs
                ManagedLastSlowestMaterializedPackagePromotionBackupCleanupMs = $row.ManagedLastSlowestMaterializedPackagePromotionBackupCleanupMs
                ManagedLastSlowestMaterializedPackagePromotionHadExistingTarget = $row.ManagedLastSlowestMaterializedPackagePromotionHadExistingTarget
                ManagedLastSlowestMaterializedPackagePromotionMaterializedDirectly = $row.ManagedLastSlowestMaterializedPackagePromotionMaterializedDirectly
                ManagedLastSlowestMaterializedPackagePromotionDirectMaterializationMs = $row.ManagedLastSlowestMaterializedPackagePromotionDirectMaterializationMs
                ManagedLastCriticalDependencyBranchName = $row.ManagedLastCriticalDependencyBranchName
                ManagedLastCriticalDependencyBranchParent = $row.ManagedLastCriticalDependencyBranchParent
                ManagedLastCriticalDependencyBranchMs = $row.ManagedLastCriticalDependencyBranchMs
                ManagedLastCriticalDependencyBranchDominantPhase = $row.ManagedLastCriticalDependencyBranchDominantPhase
                ManagedLastCriticalDependencyBranchDominantPhaseMs = $row.ManagedLastCriticalDependencyBranchDominantPhaseMs
                ManagedLastCriticalRootBranchName = $row.ManagedLastCriticalRootBranchName
                ManagedLastCriticalRootBranchMs = $row.ManagedLastCriticalRootBranchMs
                ManagedLastCriticalRootBranchDominantPhase = $row.ManagedLastCriticalRootBranchDominantPhase
                ManagedLastCriticalRootBranchDominantPhaseMs = $row.ManagedLastCriticalRootBranchDominantPhaseMs
                ManagedLastCriticalMaterializationBranchName = $row.ManagedLastCriticalMaterializationBranchName
                ManagedLastCriticalMaterializationBranchMs = $row.ManagedLastCriticalMaterializationBranchMs
                ManagedLastCriticalMaterializationDominantPhase = $row.ManagedLastCriticalMaterializationDominantPhase
                ManagedLastCriticalMaterializationDominantPhaseMs = $row.ManagedLastCriticalMaterializationDominantPhaseMs
                ManagedMaintenanceActions = $row.ManagedMaintenanceActions
                ManagedMaintenanceFindings = $row.ManagedMaintenanceFindings
                ManagedRootDependencyMs = $row.ManagedRootDependencyMs
                ManagedRootDependencyUnattributedMs = $row.ManagedRootDependencyUnattributedMs
                ManagedDependencyQueueWaitMs = $row.ManagedDependencyQueueWaitMs
                ManagedDependencyBranchElapsedMs = $row.ManagedDependencyBranchElapsedMs
                ManagedDownloadMs = $row.ManagedDownloadMs
                ManagedExtractionMs = $row.ManagedExtractionMs
                ManagedExtractionCacheLockWaitMs = $row.ManagedExtractionCacheLockWaitMs
                ManagedDependencyMs = $row.ManagedDependencyMs
                ManagedPromotionMs = $row.ManagedPromotionMs
                ManagedPromotionLockWaitMs = $row.ManagedPromotionLockWaitMs
                ManagedPromotionMoveMs = $row.ManagedPromotionMoveMs
                RunPath = $RunPath
            })
        }
    }
}

$Suite = Resolve-TokenList -Value $Suite -Allowed $validSuites -Label 'suite'
$HostName = Resolve-TokenList -Value $HostName -Allowed $validHosts -Label 'host'
$scenarios = Resolve-ScenarioList
if ($ListScenarios.IsPresent) {
    $scenarios | Select-Object Suite, Name, BenchmarkRole, ComparisonScope, BenchmarkInterpretation, ModuleName, Version, UpdateBaselineVersion, AcceptLicense, AuthenticodeCheck, Operations, RepairScenarios, Engines, Repository, RepositoryName, ModuleFastSource, CacheMode, RepeatCount, ManagedMaxRank, ManagedMaxVsFastest, ManagedMinAuthenticodeCheckedFiles, ManagedMinAuthenticodeCatalogFiles
    return
}

New-Item -Path $suiteRoot -ItemType Directory -Force | Out-Null
Invoke-LocalBuild

$summaryRows = [Collections.Generic.List[object]]::new()
$engineRows = [Collections.Generic.List[object]]::new()
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
        Add-ManagedBenchmarkEngineRows -Rows $engineRows -Scenario $scenario -HostLabel $hostLabel -RunPath $run.FullName
    }
}

$summaryPath = Join-Path $suiteRoot 'suite-summary.csv'
$summaryJsonPath = Join-Path $suiteRoot 'suite-summary.json'
$engineSummaryPath = Join-Path $suiteRoot 'suite-engine-summary.csv'
$engineSummaryJsonPath = Join-Path $suiteRoot 'suite-engine-summary.json'
$hostComparisonPath = Join-Path $suiteRoot 'suite-host-comparison.csv'
$hostComparisonJsonPath = Join-Path $suiteRoot 'suite-host-comparison.json'
$scoreboardPath = Join-Path $suiteRoot 'suite-scoreboard.csv'
$scoreboardJsonPath = Join-Path $suiteRoot 'suite-scoreboard.json'
$optimizationTargetsPath = Join-Path $suiteRoot 'suite-optimization-targets.csv'
$optimizationTargetsJsonPath = Join-Path $suiteRoot 'suite-optimization-targets.json'
$hostsPath = Join-Path $suiteRoot 'suite-hosts.csv'
$gatePath = Join-Path $suiteRoot 'suite-gate.csv'
$hostGatePath = Join-Path $suiteRoot 'suite-host-gate.csv'
$metadataPath = Join-Path $suiteRoot 'metadata.json'
$notesPath = Join-Path $suiteRoot 'suite-notes.md'
$gateViolations = @(Get-ManagedPerformanceGateViolationForSuite -Rows @($summaryRows) -MaxRank $ManagedMaxRank -MaxVsFastest $ManagedMaxVsFastest -MinAuthenticodeCheckedFiles $ManagedMinAuthenticodeCheckedFiles -MinAuthenticodeCatalogFiles $ManagedMinAuthenticodeCatalogFiles -UseScenarioGates:$UseScenarioGates.IsPresent)
$hostComparisonRows = @(New-ManagedHostComparison -Rows @($summaryRows))
$scoreboardRows = @(New-ManagedBenchmarkScoreboard -EngineRows @($engineRows))
$hostGateViolations = @(Get-ManagedHostComparisonGateViolation -Rows @($hostComparisonRows) -MaxComparisonVsBaseline $ManagedMaxWindowsPowerShellVsPowerShell7)
$optimizationTargetRows = @(New-ManagedOptimizationTarget -Rows @($summaryRows))

Write-ManagedBenchmarkCsv -InputObject @($summaryRows) -Path $summaryPath
Write-ManagedBenchmarkJson -InputObject @($summaryRows) -Path $summaryJsonPath -Depth 8
Write-ManagedBenchmarkCsv -InputObject @($engineRows) -Path $engineSummaryPath
Write-ManagedBenchmarkJson -InputObject @($engineRows) -Path $engineSummaryJsonPath -Depth 8
Write-ManagedBenchmarkCsv -InputObject @($hostComparisonRows) -Path $hostComparisonPath
Write-ManagedBenchmarkJson -InputObject @($hostComparisonRows) -Path $hostComparisonJsonPath -Depth 8
Write-ManagedBenchmarkCsv -InputObject @($scoreboardRows) -Path $scoreboardPath
Write-ManagedBenchmarkJson -InputObject @($scoreboardRows) -Path $scoreboardJsonPath -Depth 8
Write-ManagedBenchmarkCsv -InputObject @($optimizationTargetRows) -Path $optimizationTargetsPath
Write-ManagedBenchmarkJson -InputObject @($optimizationTargetRows) -Path $optimizationTargetsJsonPath -Depth 8
Write-ManagedBenchmarkCsv -InputObject @($hostRows) -Path $hostsPath
if ($ManagedMaxRank -gt 0 -or
    $ManagedMaxVsFastest -gt 0 -or
    $ManagedMinAuthenticodeCheckedFiles -gt 0 -or
    $ManagedMinAuthenticodeCatalogFiles -gt 0 -or
    $UseScenarioGates.IsPresent) {
    Write-ManagedBenchmarkCsv -InputObject @($gateViolations) -Path $gatePath
}
if ($ManagedMaxWindowsPowerShellVsPowerShell7 -gt 0) {
    Write-ManagedBenchmarkCsv -InputObject @($hostGateViolations) -Path $hostGatePath
}
Write-ManagedBenchmarkSuiteNotes -Scenarios @($scenarios) -SummaryRows @($summaryRows) -EngineRows @($engineRows) -ScoreboardRows @($scoreboardRows) -OptimizationRows @($optimizationTargetRows) -HostComparisonRows @($hostComparisonRows) -HostRows @($hostRows) -GateViolations @($gateViolations) -HostGateViolations @($hostGateViolations) -Path $notesPath

$metadata = [ordered]@{
    Suites = $Suite
    ScenarioNames = $ScenarioName
    SelectedScenarios = @($scenarios | ForEach-Object {
            [ordered]@{
                Suite = $_.Suite
                Name = $_.Name
                BenchmarkRole = $_.BenchmarkRole
                ComparisonScope = $_.ComparisonScope
                BenchmarkInterpretation = $_.BenchmarkInterpretation
                ModuleName = $_.ModuleName
                Version = $_.Version
                UpdateBaselineVersion = $_.UpdateBaselineVersion
                AcceptLicense = $_.AcceptLicense
                AuthenticodeCheck = $_.AuthenticodeCheck
                Operations = @(Get-ScenarioOperations -Scenario $_)
                RepairScenarios = @(Get-ScenarioRepairScenarios -Scenario $_)
                Engines = @(Get-ScenarioEngines -Scenario $_)
                Repository = Get-ScenarioRepository -Scenario $_
                RepositoryName = Get-ScenarioRepositoryName -Scenario $_
                ModuleFastSource = Get-ScenarioModuleFastSourceLabel -Scenario $_
                CacheMode = Get-EffectiveCacheMode -Scenario $_
                RepeatCount = Get-EffectiveRepeatCount -Scenario $_
                ManagedMaxRank = Get-ScenarioManagedMaxRank -Scenario $_
                ManagedMaxVsFastest = Get-ScenarioManagedMaxVsFastest -Scenario $_
                ManagedMinAuthenticodeCheckedFiles = Get-ScenarioManagedMinAuthenticodeCheckedFiles -Scenario $_
                ManagedMinAuthenticodeCatalogFiles = Get-ScenarioManagedMinAuthenticodeCatalogFiles -Scenario $_
                EffectiveManagedMaxRank = Get-EffectiveManagedMaxRank -Scenario $_
                EffectiveManagedMaxVsFastest = Get-EffectiveManagedMaxVsFastest -Scenario $_
                EffectiveManagedMinAuthenticodeCheckedFiles = Get-EffectiveManagedMinAuthenticodeCheckedFiles -Scenario $_
                EffectiveManagedMinAuthenticodeCatalogFiles = Get-EffectiveManagedMinAuthenticodeCatalogFiles -Scenario $_
            }
        })
    Hosts = $HostName
    Engines = $Engine
    ModuleFastSource = $ModuleFastSource
    Operations = $Operation
    UpdateBaselineVersion = $UpdateBaselineVersion
    CacheMode = $CacheMode
    RepeatCount = $RepeatCount
    SetupRetryCount = $SetupRetryCount
    IncludeInstall = $IncludeInstall.IsPresent
    ValidateImport = $ValidateImport.IsPresent
    AuthenticodeCheck = $AuthenticodeCheck.IsPresent
    ImportTimeoutSeconds = $ImportTimeoutSeconds
    ChildTimeoutSeconds = $ChildTimeoutSeconds
    RotateEngineOrder = $RotateEngineOrder.IsPresent
    ManagedMaxRank = $ManagedMaxRank
    ManagedMaxVsFastest = $ManagedMaxVsFastest
    ManagedMinAuthenticodeCheckedFiles = $ManagedMinAuthenticodeCheckedFiles
    ManagedMinAuthenticodeCatalogFiles = $ManagedMinAuthenticodeCatalogFiles
    ManagedMaxWindowsPowerShellVsPowerShell7 = $ManagedMaxWindowsPowerShellVsPowerShell7
    UseScenarioGates = $UseScenarioGates.IsPresent
    ManagedPerformanceGatePassed = $gateViolations.Count -eq 0
    ManagedHostGatePassed = $hostGateViolations.Count -eq 0
    EngineSummaryPath = $engineSummaryPath
    HostComparisonPath = $hostComparisonPath
    ScoreboardPath = $scoreboardPath
    HostGatePath = $hostGatePath
    OptimizationTargetsPath = $optimizationTargetsPath
    NotesPath = $notesPath
    RemoveOutputRoots = $RemoveOutputRoots.IsPresent
    OutputDirectory = $suiteRoot
}
Write-ManagedBenchmarkJson -InputObject $metadata -Path $metadataPath -Depth 8

$summaryRows
Write-Host "Benchmark suite output: $suiteRoot"
if ($gateViolations.Count -gt 0) {
    throw "Managed performance gate failed for $($gateViolations.Count) suite row(s). See '$gatePath'."
}
if ($hostGateViolations.Count -gt 0) {
    $hostGateViolations | Format-Table Suite, Scenario, Operation, BaselineHost, BaselineMs, ComparisonHost, ComparisonMs, ComparisonVsBaseline, Reason -AutoSize | Out-String | Write-Host
    throw "Managed host comparison gate failed for $($hostGateViolations.Count) suite row(s). See '$hostGatePath'."
}
