function Test-BenchmarkOperationUsesUpdateBaseline {
    param([string] $OperationName)

    $OperationName -eq 'Update' -or $OperationName -eq 'RepairPlan'
}

function New-SkippedRow {
    param(
        [string] $OperationName,
        [string] $ScenarioName = '',
        [string] $EngineName,
        [int] $Iteration,
        [string] $Reason
    )

    [pscustomobject]@{
        Operation = $OperationName
        Scenario = $ScenarioName
        Engine = $EngineName
        Iteration = $Iteration
        Status = 'Skipped'
        ModuleName = $ModuleName
        Version = $null
        UpdateBaselineVersion = if (Test-BenchmarkOperationUsesUpdateBaseline -OperationName $OperationName) { $script:ResolvedUpdateBaselineVersion } else { '' }
        UpdateTargetVersion = if (Test-BenchmarkOperationUsesUpdateBaseline -OperationName $OperationName) { $script:ResolvedUpdateTargetVersion } else { '' }
        ElapsedMilliseconds = 0
        OutputCount = 0
        OutputDirectoryCount = 0
        OutputFileCount = 0
        OutputBytes = 0
        OutputRoot = ''
        DetailPath = ''
        ManagedPackageCount = 0
        ManagedDependencyCount = 0
        ManagedUniquePackageCount = 0
        ManagedUniqueDependencyCount = 0
        ManagedInstalledPackageCount = 0
        ManagedAlreadyInstalledPackageCount = 0
        ManagedRootElapsedMilliseconds = 0
        ManagedHarnessOverheadMilliseconds = 0
        ManagedRootDependencyMilliseconds = 0
        ManagedRootDependencyUnattributedMilliseconds = 0
        ManagedRootDependencyCriticalPathGapMilliseconds = 0
        ManagedDependencyBranchParallelismRatio = 0
        ManagedTotalVersionSelectionWaitMilliseconds = 0
        ManagedTotalDependencyQueueWaitMilliseconds = 0
        ManagedTotalDependencyBranchElapsedMilliseconds = 0
        ManagedTotalDependencyBranchOverheadMilliseconds = 0
        ManagedTotalDownloadMilliseconds = 0
        ManagedTotalExtractionMilliseconds = 0
        ManagedTotalExtractionCacheLockWaitMilliseconds = 0
        ManagedTotalDependencyMilliseconds = 0
        ManagedTotalPromotionMilliseconds = 0
        ManagedTotalPromotionLockWaitMilliseconds = 0
        ManagedTotalPromotionMoveMilliseconds = 0
        ManagedTotalPromotionBackupMoveMilliseconds = 0
        ManagedTotalPromotionFinalMoveMilliseconds = 0
        ManagedTotalPromotionBackupCleanupMilliseconds = 0
        ManagedPromotionOverwriteCount = 0
        ManagedDirectMaterializationCount = 0
        ManagedTotalPromotionDirectMaterializationMilliseconds = 0
        ManagedRepositoryRequestCount = 0
        ManagedPackageRepositoryRequestCount = 0
        ManagedPackageRepositoryRedirectCount = 0
        ManagedDownloadBytes = 0
        ManagedCacheHitCount = 0
        ManagedExtractionCacheHitCount = 0
        ManagedCoalescedWaitCount = 0
        ManagedTotalCoalescedWaitMilliseconds = 0
        ManagedSlowestCoalescedWaitName = ''
        ManagedSlowestCoalescedWaitMilliseconds = 0
        ManagedInstallLockWaitCount = 0
        ManagedTotalInstallLockWaitMilliseconds = 0
        ManagedSlowestInstallLockWaitName = ''
        ManagedSlowestInstallLockWaitMilliseconds = 0
        ManagedSlowestDependencyPackageName = ''
        ManagedSlowestDependencyPackageParent = ''
        ManagedSlowestDependencyPackageMilliseconds = 0
        ManagedSlowestDependencyQueueWaitName = ''
        ManagedSlowestDependencyQueueWaitMilliseconds = 0
        ManagedSlowestVersionSelectionWaitName = ''
        ManagedSlowestVersionSelectionWaitMilliseconds = 0
        ManagedSlowestMaterializedPackageName = ''
        ManagedSlowestMaterializedPackageMilliseconds = 0
        ManagedSlowestMaterializedPackageExtractionMilliseconds = 0
        ManagedSlowestMaterializedPackageExtractionCacheLockWaitMilliseconds = 0
        ManagedSlowestMaterializedPackagePromotionMilliseconds = 0
        ManagedSlowestMaterializedPackagePromotionLockWaitMilliseconds = 0
        ManagedSlowestMaterializedPackagePromotionMoveMilliseconds = 0
        ManagedSlowestMaterializedPackagePromotionBackupMoveMilliseconds = 0
        ManagedSlowestMaterializedPackagePromotionFinalMoveMilliseconds = 0
        ManagedSlowestMaterializedPackagePromotionBackupCleanupMilliseconds = 0
        ManagedSlowestMaterializedPackagePromotionHadExistingTarget = $false
        ManagedSlowestMaterializedPackagePromotionMaterializedDirectly = $false
        ManagedSlowestMaterializedPackagePromotionDirectMaterializationMilliseconds = 0
        ManagedMaintenanceActionCount = 0
        ManagedMaintenanceFindingCount = 0
        ImportStatus = ''
        ImportVersion = ''
        ImportMilliseconds = 0
        ImportManifestPath = ''
        ImportError = ''
        Reason = $Reason
        Error = $Reason
    }
}

function New-FailedRow {
    param(
        [string] $OperationName,
        [string] $ScenarioName = '',
        [string] $EngineName,
        [int] $Iteration,
        [string] $Reason,
        [string] $OutputRoot = ''
    )

    $row = New-SkippedRow -OperationName $OperationName -ScenarioName $ScenarioName -EngineName $EngineName -Iteration $Iteration -Reason $Reason
    $row.Status = 'Failed'
    $row.OutputRoot = $OutputRoot
    $row
}
