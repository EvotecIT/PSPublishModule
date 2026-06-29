function Get-BenchmarkComparisonScope {
    param(
        [string] $SuiteName,
        [string] $Name,
        [string[]] $Operations,
        [string[]] $Engines
    )

    if ($SuiteName -eq 'HeavySaveCacheGate') {
        return 'ManagedOnlySaveCache'
    }

    if ($SuiteName -eq 'RepairGate') {
        return 'ManagedOnlyRepairPlan'
    }

    if ($SuiteName -eq 'PublishGate') {
        return 'PublishCapableProviders'
    }

    if ($SuiteName -eq 'SecurityGate') {
        return 'AuthenticodeCapableProviders'
    }

    if ($Name -like '*.ProviderMatrix') {
        return 'InstallProviderMatrix'
    }

    if ($Name -like '*.SameSource') {
        return 'InstallSameSource'
    }

    $hasInstall = @($Operations | Where-Object { $_ -like 'Install*' }).Count -gt 0
    $hasSave = @($Operations | Where-Object { $_ -like 'Save*' }).Count -gt 0
    if ($hasInstall -and $hasSave) {
        return 'MixedLifecycle'
    }

    if ($hasInstall) {
        if (@($Engines | Where-Object { $_ -eq 'ModuleFast' }).Count -gt 0) {
            return 'InstallWithModuleFast'
        }

        return 'InstallCapableProviders'
    }

    if ($hasSave) {
        return 'SaveCapableProviders'
    }

    'ProviderComparison'
}

function Get-BenchmarkInterpretation {
    param(
        [string] $ComparisonScope
    )

    switch ($ComparisonScope) {
        'InstallSameSource' { 'Strict install scoreboard: managed and ModuleFast use the same source URL.'; break }
        'InstallProviderMatrix' { 'Install scoreboard: provider-default source behavior is compared across available engines.'; break }
        'InstallWithModuleFast' { 'Install lifecycle scoreboard: ModuleFast participates only where it has an equivalent install operation.'; break }
        'InstallCapableProviders' { 'Install scoreboard: compare engines that can perform this install operation.'; break }
        'SaveCapableProviders' { 'Save scoreboard: compare save-capable providers only; ModuleFast has no equivalent save command.'; break }
        'ManagedOnlySaveCache' { 'Diagnostic only: managed warm-cache save isolates package cache, extraction, and output materialization cost; do not rank it against providers or install rows.'; break }
        'ManagedOnlyRepairPlan' { 'Diagnostic only: managed repair planning evidence; competitor rows are explicit skips when no equivalent planner exists.'; break }
        'PublishCapableProviders' { 'Publish scoreboard: compare engines that can publish to the prepared feed.'; break }
        'AuthenticodeCapableProviders' { 'Authenticode compatibility scoreboard: compare install/save providers that expose signature validation for signable module files.'; break }
        'MixedLifecycle' { 'Mixed lifecycle scoreboard: compare the selected install and save operations separately before drawing a combined conclusion.'; break }
        default { 'Provider comparison: read with the operation and engine set before treating it as a speed scoreboard.' }
    }
}

function New-BenchmarkScenario {
    param(
        [string] $SuiteName,
        [string] $Name,
        [string] $ModuleName,
        [string] $Version = '',
        [string] $UpdateBaselineVersion = '',
        [bool] $AcceptLicense = $false,
        [string[]] $Operations = $Operation,
        [string[]] $RepairScenarios = @(),
        [string[]] $Engines = $Engine,
        [string] $Repository = '',
        [string] $RepositoryName = '',
        [string] $ScenarioModuleFastSource = '',
        [int] $ScenarioManagedMaxRank = 0,
        [double] $ScenarioManagedMaxVsFastest = 0,
        [int] $ScenarioManagedMinAuthenticodeCheckedFiles = 0,
        [int] $ScenarioManagedMinAuthenticodeCatalogFiles = 0,
        [bool] $ScenarioAuthenticodeCheck = $false,
        [ValidateSet('', 'Default', 'Cold', 'Warm')]
        [string] $ScenarioCacheMode = '',
        [int] $ScenarioRepeatCount = 0
    )

    $benchmarkRole = if ($SuiteName -eq 'HeavySaveCacheGate' -or $SuiteName -eq 'RepairGate') {
        'Diagnostic'
    } else {
        'Scoreboard'
    }
    $comparisonScope = Get-BenchmarkComparisonScope -SuiteName $SuiteName -Name $Name -Operations $Operations -Engines $Engines
    $benchmarkInterpretation = Get-BenchmarkInterpretation -ComparisonScope $comparisonScope

    [pscustomobject]@{
        Suite = $SuiteName
        Name = $Name
        BenchmarkRole = $benchmarkRole
        ComparisonScope = $comparisonScope
        BenchmarkInterpretation = $benchmarkInterpretation
        ModuleName = $ModuleName
        Version = $Version
        UpdateBaselineVersion = $UpdateBaselineVersion
        AcceptLicense = $AcceptLicense
        AuthenticodeCheck = $ScenarioAuthenticodeCheck
        Operations = $Operations
        RepairScenarios = $RepairScenarios
        Engines = $Engines
        Repository = $Repository
        RepositoryName = $RepositoryName
        ModuleFastSource = $ScenarioModuleFastSource
        ManagedMaxRank = $ScenarioManagedMaxRank
        ManagedMaxVsFastest = $ScenarioManagedMaxVsFastest
        ManagedMinAuthenticodeCheckedFiles = $ScenarioManagedMinAuthenticodeCheckedFiles
        ManagedMinAuthenticodeCatalogFiles = $ScenarioManagedMinAuthenticodeCatalogFiles
        CacheMode = $ScenarioCacheMode
        RepeatCount = $ScenarioRepeatCount
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
        New-BenchmarkScenario -SuiteName 'LifecycleGate' -Name 'ThreadJob.InstallSave.NoOpForce' -ModuleName 'ThreadJob' -Version '2.1.0' -Operations @('InstallNoOp', 'InstallForce', 'SaveNoOp', 'SaveForce') -ScenarioManagedMaxVsFastest 1.25
        New-BenchmarkScenario -SuiteName 'LifecycleGate' -Name 'Graph.Authentication.InstallExact.NoOpForce' -ModuleName 'Microsoft.Graph.Authentication' -Version '2.38.0' -AcceptLicense $true -Operations @('InstallNoOp', 'InstallForce') -Engines @('Managed', 'ModuleFast', 'PSResourceGet') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'LifecycleGate' -Name 'Graph.Authentication.SaveExact.NoOpForce' -ModuleName 'Microsoft.Graph.Authentication' -Version '2.38.0' -AcceptLicense $true -Operations @('SaveNoOp', 'SaveForce') -Engines @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'LifecycleGate' -Name 'Az.Accounts.InstallExact.NoOpForce' -ModuleName 'Az.Accounts' -Version '5.5.0' -AcceptLicense $true -Operations @('InstallNoOp', 'InstallForce') -Engines @('Managed', 'ModuleFast', 'PSResourceGet') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'LifecycleGate' -Name 'Az.Accounts.SaveExact.NoOpForce' -ModuleName 'Az.Accounts' -Version '5.5.0' -AcceptLicense $true -Operations @('SaveNoOp', 'SaveForce') -Engines @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'HeavyLifecycleGate' -Name 'Graph.Full.InstallExact.NoOpForce' -ModuleName 'Microsoft.Graph' -Version '2.38.0' -AcceptLicense $true -Operations @('InstallNoOp', 'InstallForce') -Engines @('Managed', 'ModuleFast', 'PSResourceGet') -Repository 'https://pwsh.gallery/index.json' -RepositoryName 'PWSHGallery' -ScenarioModuleFastSource 'https://pwsh.gallery/index.json' -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'HeavyLifecycleGate' -Name 'Az.Full.InstallExact.NoOpForce' -ModuleName 'Az' -Version '16.0.0' -AcceptLicense $true -Operations @('InstallNoOp', 'InstallForce') -Engines @('Managed', 'ModuleFast', 'PSResourceGet') -Repository 'https://pwsh.gallery/index.json' -RepositoryName 'PWSHGallery' -ScenarioModuleFastSource 'https://pwsh.gallery/index.json' -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'HeavySaveGate' -Name 'Graph.Full.Save' -ModuleName 'Microsoft.Graph' -Version '2.38.0' -AcceptLicense $true -Operations @('Save') -Engines @('Managed', 'PSResourceGet', 'PowerShellGet') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'HeavySaveGate' -Name 'Az.Full.Save' -ModuleName 'Az' -Version '16.0.0' -AcceptLicense $true -Operations @('Save') -Engines @('Managed', 'PSResourceGet', 'PowerShellGet') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'HeavySaveCacheGate' -Name 'Graph.Full.Save.ManagedWarmCache' -ModuleName 'Microsoft.Graph' -Version '2.38.0' -AcceptLicense $true -Operations @('Save') -Engines @('Managed') -ScenarioCacheMode 'Warm' -ScenarioRepeatCount 2
        New-BenchmarkScenario -SuiteName 'HeavySaveCacheGate' -Name 'Az.Full.Save.ManagedWarmCache' -ModuleName 'Az' -Version '16.0.0' -AcceptLicense $true -Operations @('Save') -Engines @('Managed') -ScenarioCacheMode 'Warm' -ScenarioRepeatCount 2
        New-BenchmarkScenario -SuiteName 'PublishGate' -Name 'Synthetic.Publish.LocalFeed' -ModuleName 'Company.ManagedPublishBenchmark' -Version '1.0.0' -Operations @('Publish') -Engines @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
        New-BenchmarkScenario -SuiteName 'SecurityGate' -Name 'ThreadJob.Authenticode.InstallSave' -ModuleName 'ThreadJob' -Version '2.1.0' -Operations @('Install', 'Save') -Engines @('Managed', 'PSResourceGet') -ScenarioManagedMaxRank 1 -ScenarioManagedMinAuthenticodeCheckedFiles 1 -ScenarioAuthenticodeCheck $true
        New-BenchmarkScenario -SuiteName 'RepairGate' -Name 'ThreadJob.Repair.StaleVersion' -ModuleName 'ThreadJob' -Version '2.1.0' -UpdateBaselineVersion '2.0.3' -Operations @('RepairPlan') -RepairScenarios @('StaleVersion') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'RepairGate' -Name 'ThreadJob.Repair.SourceDrift' -ModuleName 'ThreadJob' -Version '2.1.0' -Operations @('RepairPlan') -RepairScenarios @('SourceDrift') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'RepairGate' -Name 'ThreadJob.Repair.ScopeDrift' -ModuleName 'ThreadJob' -Version '2.1.0' -Operations @('RepairPlan') -RepairScenarios @('ScopeDrift') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'RepairGate' -Name 'Graph.Repair.FamilyCoherence' -ModuleName 'ThreadJob' -Operations @('RepairPlan') -RepairScenarios @('FamilyCoherence') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'RepairGate' -Name 'ThreadJob.Repair.LoadedModuleSafety' -ModuleName 'ThreadJob' -Operations @('RepairPlan') -RepairScenarios @('LoadedModuleSafety') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'RepairGate' -Name 'ThreadJob.Repair.CleanupPlanning' -ModuleName 'ThreadJob' -Operations @('RepairPlan') -RepairScenarios @('CleanupPlanning') -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'SpeedGate' -Name 'Graph.Full.SameSource' -ModuleName 'Microsoft.Graph' -Version '2.38.0' -AcceptLicense $true -Operations @('Install') -Engines @('Managed', 'ModuleFast') -Repository 'https://pwsh.gallery/index.json' -RepositoryName 'PWSHGallery' -ScenarioModuleFastSource 'https://pwsh.gallery/index.json' -ScenarioManagedMaxRank 1
        New-BenchmarkScenario -SuiteName 'SpeedGate' -Name 'Graph.Full.ProviderMatrix' -ModuleName 'Microsoft.Graph' -Version '2.38.0' -AcceptLicense $true -Operations @('Install') -Engines @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet') -ScenarioModuleFastSource $providerDefaultModuleFastSource
        New-BenchmarkScenario -SuiteName 'SpeedGate' -Name 'Az.Accounts.ProviderMatrix' -ModuleName 'Az.Accounts' -Version '5.5.0' -AcceptLicense $true -Operations @('Install') -Engines @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet') -ScenarioModuleFastSource $providerDefaultModuleFastSource
        New-BenchmarkScenario -SuiteName 'SpeedGate' -Name 'Az.Full.ProviderMatrix' -ModuleName 'Az' -Version '16.0.0' -AcceptLicense $true -Operations @('Install') -Engines @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet') -ScenarioModuleFastSource $providerDefaultModuleFastSource
        New-BenchmarkScenario -SuiteName 'SaveGate' -Name 'Graph.Authentication.Save' -ModuleName 'Microsoft.Graph.Authentication' -AcceptLicense $true -Operations @('Save') -Engines @('Managed', 'PSResourceGet') -ScenarioManagedMaxRank 1
    )
}
