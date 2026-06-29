function Get-Median {
    param([double[]] $Values)

    $items = [Collections.Generic.List[double]]::new()
    foreach ($value in @($Values)) {
        $items.Add([double]$value)
    }
    if ($items.Count -eq 0) {
        return 0
    }

    [double[]] $sorted = $items.ToArray()
    [Array]::Sort($sorted)
    $middle = [int][Math]::Floor($sorted.Length / 2)
    if ($sorted.Length % 2 -eq 1) {
        return [math]::Round($sorted[$middle], 2)
    }

    [math]::Round(($sorted[$middle - 1] + $sorted[$middle]) / 2, 2)
}

function Get-MedianProperty {
    param(
        [object[]] $Rows,
        [string] $Name
    )

    $values = [Collections.Generic.List[double]]::new()
    foreach ($row in @($Rows)) {
        $property = if ($null -ne $row) {
            $row.PSObject.Properties | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
        } else {
            $null
        }
        if ($null -ne $property) {
            $values.Add([double] $property.Value)
        } else {
            $values.Add(0.0)
        }
    }

    Get-Median -Values $values.ToArray()
}

function Get-IterationValue {
    param([object] $Row)

    if (-not $Row.PSObject.Properties['Iteration']) {
        return 0
    }

    $iteration = 0
    if ([int]::TryParse([string]$Row.Iteration, [ref]$iteration)) {
        return $iteration
    }

    0
}

function Get-TextProperty {
    param(
        [object] $Row,
        [string] $Name
    )

    if ($null -eq $Row -or -not $Row.PSObject.Properties[$Name]) {
        return ''
    }

    [string] $Row.PSObject.Properties[$Name].Value
}

function Get-DoubleProperty {
    param(
        [object] $Row,
        [string] $Name
    )

    if ($null -eq $Row -or -not $Row.PSObject.Properties[$Name]) {
        return 0.0
    }

    [double] $Row.PSObject.Properties[$Name].Value
}

function Get-OrderedSucceededRows {
    param([object[]] $Rows)

    @($Rows | Sort-Object @{ Expression = { Get-IterationValue -Row $_ } }, @{ Expression = { [double]$_.ElapsedMilliseconds } })
}

function Get-ItemCount {
    param([object] $Value)

    $count = 0
    foreach ($item in @($Value)) {
        if ($null -ne $item) {
            $count++
        }
    }

    $count
}

function New-Summary {
    param([object[]] $Rows)

    foreach ($group in ($Rows | Group-Object Operation, Scenario, Engine)) {
        [object[]] $groupRows = @($group.Group)
        [object[]] $passed = @($groupRows | Where-Object Status -eq 'Succeeded')
        [object[]] $failed = @($groupRows | Where-Object Status -eq 'Failed')
        [object[]] $skipped = @($groupRows | Where-Object Status -eq 'Skipped')
        [object[]] $orderedPassed = @(Get-OrderedSucceededRows -Rows $passed)
        $groupRowCount = Get-ItemCount -Value $groupRows
        $passedCount = Get-ItemCount -Value $passed
        $failedCount = Get-ItemCount -Value $failed
        $skippedCount = Get-ItemCount -Value $skipped
        $orderedPassedCount = Get-ItemCount -Value $orderedPassed
        $firstPassed = if ($orderedPassedCount -gt 0) { $orderedPassed[0] } else { $null }
        $lastPassed = if ($orderedPassedCount -gt 0) { $orderedPassed[$orderedPassedCount - 1] } else { $null }
        [object[]] $warmPassed = if ($orderedPassedCount -gt 1) { @($orderedPassed | Select-Object -Skip 1) } else { @() }
        $warmPassedCount = Get-ItemCount -Value $warmPassed
        [pscustomobject]@{
            Operation = [string]$groupRows[0].Operation
            Scenario = [string]$groupRows[0].Scenario
            Engine = [string]$groupRows[0].Engine
            Runs = $groupRowCount
            Succeeded = $passedCount
            Failed = $failedCount
            Skipped = $skippedCount
            MedianMs = (Get-MedianProperty -Rows $passed -Name 'ElapsedMilliseconds')
            WarmRuns = $warmPassedCount
            WarmMedianMs = (Get-MedianProperty -Rows $warmPassed -Name 'ElapsedMilliseconds')
            WarmMinMs = if ($warmPassedCount) { [math]::Round(($warmPassed | Measure-Object ElapsedMilliseconds -Minimum).Minimum, 2) } else { 0 }
            WarmMaxMs = if ($warmPassedCount) { [math]::Round(($warmPassed | Measure-Object ElapsedMilliseconds -Maximum).Maximum, 2) } else { 0 }
            FirstIteration = if ($null -ne $firstPassed) { Get-IterationValue -Row $firstPassed } else { 0 }
            LastIteration = if ($null -ne $lastPassed) { Get-IterationValue -Row $lastPassed } else { 0 }
            FirstMs = if ($null -ne $firstPassed) { [math]::Round([double]$firstPassed.ElapsedMilliseconds, 2) } else { 0 }
            LastMs = if ($null -ne $lastPassed) { [math]::Round([double]$lastPassed.ElapsedMilliseconds, 2) } else { 0 }
            MinMs = if ($passedCount) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Minimum).Minimum, 2) } else { 0 }
            MaxMs = if ($passedCount) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Maximum).Maximum, 2) } else { 0 }
            MedianOutputFileCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'OutputFileCount' } else { 0 }
            MedianOutputBytes = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'OutputBytes' } else { 0 }
            MedianManagedPackageCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedPackageCount' } else { 0 }
            MedianManagedDependencyCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedDependencyCount' } else { 0 }
            MedianManagedUniquePackageCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedUniquePackageCount' } else { 0 }
            MedianManagedUniqueDependencyCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedUniqueDependencyCount' } else { 0 }
            MedianManagedInstalledPackageCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedInstalledPackageCount' } else { 0 }
            MedianManagedAlreadyInstalledPackageCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedAlreadyInstalledPackageCount' } else { 0 }
            MedianManagedRootElapsedMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedRootElapsedMilliseconds' } else { 0 }
            MedianManagedHarnessOverheadMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedHarnessOverheadMilliseconds' } else { 0 }
            MedianManagedRootDependencyMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedRootDependencyMilliseconds' } else { 0 }
            MedianManagedRootDependencyUnattributedMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedRootDependencyUnattributedMilliseconds' } else { 0 }
            MedianManagedRootDependencyCriticalPathGapMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedRootDependencyCriticalPathGapMilliseconds' } else { 0 }
            MedianManagedDependencyBranchParallelismRatio = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedDependencyBranchParallelismRatio' } else { 0 }
            MedianManagedVersionSelectionWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalVersionSelectionWaitMilliseconds' } else { 0 }
            MedianManagedDependencyQueueWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalDependencyQueueWaitMilliseconds' } else { 0 }
            MedianManagedDependencyBranchElapsedMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalDependencyBranchElapsedMilliseconds' } else { 0 }
            MedianManagedDependencyBranchOverheadMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalDependencyBranchOverheadMilliseconds' } else { 0 }
            MedianManagedDownloadMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalDownloadMilliseconds' } else { 0 }
            MedianManagedExtractionMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalExtractionMilliseconds' } else { 0 }
            MedianManagedExtractionCacheLockWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalExtractionCacheLockWaitMilliseconds' } else { 0 }
            MedianManagedDependencyMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalDependencyMilliseconds' } else { 0 }
            MedianManagedPromotionMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionMilliseconds' } else { 0 }
            MedianManagedPromotionLockWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionLockWaitMilliseconds' } else { 0 }
            MedianManagedPromotionMoveMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionMoveMilliseconds' } else { 0 }
            MedianManagedPromotionFinalMoveMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionFinalMoveMilliseconds' } else { 0 }
            MedianManagedPromotionBackupMoveMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionBackupMoveMilliseconds' } else { 0 }
            MedianManagedPromotionBackupCleanupMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionBackupCleanupMilliseconds' } else { 0 }
            MedianManagedPromotionOverwriteCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedPromotionOverwriteCount' } else { 0 }
            MedianManagedDirectMaterializationCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedDirectMaterializationCount' } else { 0 }
            MedianManagedPromotionDirectMaterializationMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionDirectMaterializationMilliseconds' } else { 0 }
            MedianManagedRepositoryRequests = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedRepositoryRequestCount' } else { 0 }
            MedianManagedPackageRepositoryRequests = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedPackageRepositoryRequestCount' } else { 0 }
            MedianManagedPackageRepositoryRedirects = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedPackageRepositoryRedirectCount' } else { 0 }
            MedianManagedDownloadBytes = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedDownloadBytes' } else { 0 }
            MedianManagedCacheHits = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedCacheHitCount' } else { 0 }
            MedianManagedExtractionCacheHits = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedExtractionCacheHitCount' } else { 0 }
            MedianManagedCoalescedWaitCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedCoalescedWaitCount' } else { 0 }
            MedianManagedCoalescedWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalCoalescedWaitMilliseconds' } else { 0 }
            MedianManagedSlowestCoalescedWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestCoalescedWaitMilliseconds' } else { 0 }
            MedianManagedInstallLockWaitCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedInstallLockWaitCount' } else { 0 }
            MedianManagedInstallLockWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalInstallLockWaitMilliseconds' } else { 0 }
            MedianManagedSlowestInstallLockWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestInstallLockWaitMilliseconds' } else { 0 }
            MedianManagedSlowestDependencyPackageMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestDependencyPackageMilliseconds' } else { 0 }
            MedianManagedSlowestDependencyQueueWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestDependencyQueueWaitMilliseconds' } else { 0 }
            MedianManagedSlowestVersionSelectionWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestVersionSelectionWaitMilliseconds' } else { 0 }
            MedianManagedSlowestMaterializedPackageMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackageMilliseconds' } else { 0 }
            MedianManagedSlowestMaterializedPackageFileCount = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackageFileCount' } else { 0 }
            MedianManagedSlowestMaterializedPackageExtractedBytes = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackageExtractedBytes' } else { 0 }
            MedianManagedSlowestMaterializedPackageMBPerSecond = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackageMBPerSecond' } else { 0 }
            MedianManagedSlowestMaterializedPackageFilesPerSecond = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackageFilesPerSecond' } else { 0 }
            MedianManagedSlowestMaterializedPackageExtractionCacheLockWaitMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackageExtractionCacheLockWaitMilliseconds' } else { 0 }
            MedianManagedSlowestMaterializedPackagePromotionMoveMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackagePromotionMoveMilliseconds' } else { 0 }
            MedianManagedSlowestMaterializedPackagePromotionFinalMoveMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackagePromotionFinalMoveMilliseconds' } else { 0 }
            MedianManagedSlowestMaterializedPackagePromotionDirectMaterializationMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedSlowestMaterializedPackagePromotionDirectMaterializationMilliseconds' } else { 0 }
            MedianManagedCriticalDependencyBranchMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedCriticalDependencyBranchMilliseconds' } else { 0 }
            MedianManagedCriticalRootBranchMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedCriticalRootBranchMilliseconds' } else { 0 }
            MedianManagedCriticalMaterializationBranchMs = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedCriticalMaterializationBranchMilliseconds' } else { 0 }
            MedianManagedAuthenticodeCheckedFiles = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedAuthenticodeCheckedFileCount' } else { 0 }
            MedianManagedAuthenticodeCatalogFiles = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedAuthenticodeCatalogFileCount' } else { 0 }
            FirstManagedRepositoryRequests = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedRepositoryRequestCount } else { 0 }
            LastManagedRepositoryRequests = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedRepositoryRequestCount } else { 0 }
            FirstManagedPackageRepositoryRequests = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedPackageRepositoryRequestCount } else { 0 }
            LastManagedPackageRepositoryRequests = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedPackageRepositoryRequestCount } else { 0 }
            FirstManagedRootDependencyMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedRootDependencyMilliseconds } else { 0 }
            LastManagedRootDependencyMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedRootDependencyMilliseconds } else { 0 }
            FirstManagedRootDependencyUnattributedMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedRootDependencyUnattributedMilliseconds } else { 0 }
            LastManagedRootDependencyUnattributedMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedRootDependencyUnattributedMilliseconds } else { 0 }
            FirstManagedRootDependencyCriticalPathGapMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedRootDependencyCriticalPathGapMilliseconds } else { 0 }
            LastManagedRootDependencyCriticalPathGapMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedRootDependencyCriticalPathGapMilliseconds } else { 0 }
            FirstManagedDependencyBranchParallelismRatio = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedDependencyBranchParallelismRatio } else { 0 }
            LastManagedDependencyBranchParallelismRatio = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedDependencyBranchParallelismRatio } else { 0 }
            FirstManagedVersionSelectionWaitMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalVersionSelectionWaitMilliseconds } else { 0 }
            LastManagedVersionSelectionWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalVersionSelectionWaitMilliseconds } else { 0 }
            FirstManagedDependencyQueueWaitMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalDependencyQueueWaitMilliseconds } else { 0 }
            LastManagedDependencyQueueWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalDependencyQueueWaitMilliseconds } else { 0 }
            FirstManagedDependencyBranchElapsedMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalDependencyBranchElapsedMilliseconds } else { 0 }
            LastManagedDependencyBranchElapsedMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalDependencyBranchElapsedMilliseconds } else { 0 }
            FirstManagedDependencyBranchOverheadMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalDependencyBranchOverheadMilliseconds } else { 0 }
            LastManagedDependencyBranchOverheadMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalDependencyBranchOverheadMilliseconds } else { 0 }
            FirstManagedDownloadMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalDownloadMilliseconds } else { 0 }
            LastManagedDownloadMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalDownloadMilliseconds } else { 0 }
            FirstManagedExtractionMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalExtractionMilliseconds } else { 0 }
            LastManagedExtractionMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalExtractionMilliseconds } else { 0 }
            FirstManagedExtractionCacheLockWaitMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalExtractionCacheLockWaitMilliseconds } else { 0 }
            LastManagedExtractionCacheLockWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalExtractionCacheLockWaitMilliseconds } else { 0 }
            FirstManagedDependencyMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalDependencyMilliseconds } else { 0 }
            LastManagedDependencyMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalDependencyMilliseconds } else { 0 }
            FirstManagedPromotionMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalPromotionMilliseconds } else { 0 }
            LastManagedPromotionMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalPromotionMilliseconds } else { 0 }
            FirstManagedPromotionLockWaitMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalPromotionLockWaitMilliseconds } else { 0 }
            LastManagedPromotionLockWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalPromotionLockWaitMilliseconds } else { 0 }
            FirstManagedPromotionMoveMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalPromotionMoveMilliseconds } else { 0 }
            LastManagedPromotionMoveMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalPromotionMoveMilliseconds } else { 0 }
            FirstManagedPromotionFinalMoveMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalPromotionFinalMoveMilliseconds } else { 0 }
            LastManagedPromotionFinalMoveMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalPromotionFinalMoveMilliseconds } else { 0 }
            FirstManagedPromotionBackupMoveMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalPromotionBackupMoveMilliseconds } else { 0 }
            LastManagedPromotionBackupMoveMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalPromotionBackupMoveMilliseconds } else { 0 }
            FirstManagedPromotionBackupCleanupMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalPromotionBackupCleanupMilliseconds } else { 0 }
            LastManagedPromotionBackupCleanupMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalPromotionBackupCleanupMilliseconds } else { 0 }
            FirstManagedPromotionOverwriteCount = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedPromotionOverwriteCount } else { 0 }
            LastManagedPromotionOverwriteCount = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedPromotionOverwriteCount } else { 0 }
            FirstManagedDirectMaterializationCount = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedDirectMaterializationCount } else { 0 }
            LastManagedDirectMaterializationCount = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedDirectMaterializationCount } else { 0 }
            FirstManagedPromotionDirectMaterializationMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalPromotionDirectMaterializationMilliseconds } else { 0 }
            LastManagedPromotionDirectMaterializationMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalPromotionDirectMaterializationMilliseconds } else { 0 }
            FirstManagedDownloadBytes = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedDownloadBytes } else { 0 }
            LastManagedDownloadBytes = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedDownloadBytes } else { 0 }
            FirstManagedCacheHits = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedCacheHitCount } else { 0 }
            LastManagedCacheHits = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedCacheHitCount } else { 0 }
            FirstManagedExtractionCacheHits = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedExtractionCacheHitCount } else { 0 }
            LastManagedExtractionCacheHits = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedExtractionCacheHitCount } else { 0 }
            FirstManagedCoalescedWaitMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalCoalescedWaitMilliseconds } else { 0 }
            LastManagedCoalescedWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalCoalescedWaitMilliseconds } else { 0 }
            LastManagedSlowestCoalescedWaitName = Get-TextProperty -Row $lastPassed -Name 'ManagedSlowestCoalescedWaitName'
            LastManagedSlowestCoalescedWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestCoalescedWaitMilliseconds } else { 0 }
            FirstManagedInstallLockWaitMs = if ($null -ne $firstPassed) { [double]$firstPassed.ManagedTotalInstallLockWaitMilliseconds } else { 0 }
            LastManagedInstallLockWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedTotalInstallLockWaitMilliseconds } else { 0 }
            LastManagedSlowestInstallLockWaitName = Get-TextProperty -Row $lastPassed -Name 'ManagedSlowestInstallLockWaitName'
            LastManagedSlowestInstallLockWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestInstallLockWaitMilliseconds } else { 0 }
            LastManagedSlowestDependencyPackageName = Get-TextProperty -Row $lastPassed -Name 'ManagedSlowestDependencyPackageName'
            LastManagedSlowestDependencyPackageParent = Get-TextProperty -Row $lastPassed -Name 'ManagedSlowestDependencyPackageParent'
            LastManagedSlowestDependencyPackageMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestDependencyPackageMilliseconds } else { 0 }
            LastManagedSlowestDependencyQueueWaitName = Get-TextProperty -Row $lastPassed -Name 'ManagedSlowestDependencyQueueWaitName'
            LastManagedSlowestDependencyQueueWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestDependencyQueueWaitMilliseconds } else { 0 }
            LastManagedSlowestVersionSelectionWaitName = Get-TextProperty -Row $lastPassed -Name 'ManagedSlowestVersionSelectionWaitName'
            LastManagedSlowestVersionSelectionWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestVersionSelectionWaitMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackageName = Get-TextProperty -Row $lastPassed -Name 'ManagedSlowestMaterializedPackageName'
            LastManagedSlowestMaterializedPackageMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackageMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackageFileCount = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackageFileCount } else { 0 }
            LastManagedSlowestMaterializedPackageExtractedBytes = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackageExtractedBytes } else { 0 }
            LastManagedSlowestMaterializedPackageMBPerSecond = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackageMBPerSecond } else { 0 }
            LastManagedSlowestMaterializedPackageFilesPerSecond = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackageFilesPerSecond } else { 0 }
            LastManagedSlowestMaterializedPackageExtractionMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackageExtractionMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackageExtractionCacheLockWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackageExtractionCacheLockWaitMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackagePromotionMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackagePromotionMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackagePromotionLockWaitMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackagePromotionLockWaitMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackagePromotionMoveMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackagePromotionMoveMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackagePromotionFinalMoveMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackagePromotionFinalMoveMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackagePromotionBackupMoveMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackagePromotionBackupMoveMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackagePromotionBackupCleanupMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackagePromotionBackupCleanupMilliseconds } else { 0 }
            LastManagedSlowestMaterializedPackagePromotionHadExistingTarget = if ($null -ne $lastPassed) { [bool]$lastPassed.ManagedSlowestMaterializedPackagePromotionHadExistingTarget } else { $false }
            LastManagedSlowestMaterializedPackagePromotionMaterializedDirectly = if ($null -ne $lastPassed) { [bool]$lastPassed.ManagedSlowestMaterializedPackagePromotionMaterializedDirectly } else { $false }
            LastManagedSlowestMaterializedPackagePromotionDirectMaterializationMs = if ($null -ne $lastPassed) { [double]$lastPassed.ManagedSlowestMaterializedPackagePromotionDirectMaterializationMilliseconds } else { 0 }
            LastManagedCriticalDependencyBranchName = Get-TextProperty -Row $lastPassed -Name 'ManagedCriticalDependencyBranchName'
            LastManagedCriticalDependencyBranchParent = Get-TextProperty -Row $lastPassed -Name 'ManagedCriticalDependencyBranchParent'
            LastManagedCriticalDependencyBranchMs = Get-DoubleProperty -Row $lastPassed -Name 'ManagedCriticalDependencyBranchMilliseconds'
            LastManagedCriticalDependencyBranchDominantPhase = Get-TextProperty -Row $lastPassed -Name 'ManagedCriticalDependencyBranchDominantPhase'
            LastManagedCriticalDependencyBranchDominantPhaseMs = Get-DoubleProperty -Row $lastPassed -Name 'ManagedCriticalDependencyBranchDominantPhaseMilliseconds'
            LastManagedCriticalRootBranchName = Get-TextProperty -Row $lastPassed -Name 'ManagedCriticalRootBranchName'
            LastManagedCriticalRootBranchMs = Get-DoubleProperty -Row $lastPassed -Name 'ManagedCriticalRootBranchMilliseconds'
            LastManagedCriticalRootBranchDominantPhase = Get-TextProperty -Row $lastPassed -Name 'ManagedCriticalRootBranchDominantPhase'
            LastManagedCriticalRootBranchDominantPhaseMs = Get-DoubleProperty -Row $lastPassed -Name 'ManagedCriticalRootBranchDominantPhaseMilliseconds'
            LastManagedCriticalMaterializationBranchName = Get-TextProperty -Row $lastPassed -Name 'ManagedCriticalMaterializationBranchName'
            LastManagedCriticalMaterializationBranchMs = Get-DoubleProperty -Row $lastPassed -Name 'ManagedCriticalMaterializationBranchMilliseconds'
            LastManagedCriticalMaterializationDominantPhase = Get-TextProperty -Row $lastPassed -Name 'ManagedCriticalMaterializationDominantPhase'
            LastManagedCriticalMaterializationDominantPhaseMs = Get-DoubleProperty -Row $lastPassed -Name 'ManagedCriticalMaterializationDominantPhaseMilliseconds'
            MedianManagedMaintenanceActions = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedMaintenanceActionCount' } else { 0 }
            MedianManagedMaintenanceFindings = if ($passedCount) { Get-MedianProperty -Rows $passed -Name 'ManagedMaintenanceFindingCount' } else { 0 }
        }
    }
}

function New-Comparison {
    param([object[]] $SummaryRows)

    foreach ($operationGroup in ($SummaryRows | Group-Object Operation, Scenario)) {
        $successful = @($operationGroup.Group | Where-Object { $_.Succeeded -gt 0 -and $_.MedianMs -gt 0 } | Sort-Object MedianMs)
        $managed = @($successful | Where-Object Engine -eq 'Managed' | Select-Object -First 1)
        $fastest = @($successful | Select-Object -First 1)
        [pscustomobject]@{
            Operation = [string]$operationGroup.Group[0].Operation
            Scenario = [string]$operationGroup.Group[0].Scenario
            FastestEngine = if ($fastest.Count) { [string]$fastest[0].Engine } else { '' }
            FastestMs = if ($fastest.Count) { [double]$fastest[0].MedianMs } else { 0 }
            FastestWarmMedianMs = if ($fastest.Count) { [double]$fastest[0].WarmMedianMs } else { 0 }
            ManagedMs = if ($managed.Count) { [double]$managed[0].MedianMs } else { 0 }
            ManagedWarmRuns = if ($managed.Count) { [double] $managed[0].WarmRuns } else { 0 }
            ManagedWarmMedianMs = if ($managed.Count) { [double] $managed[0].WarmMedianMs } else { 0 }
            ManagedWarmMinMs = if ($managed.Count) { [double] $managed[0].WarmMinMs } else { 0 }
            ManagedWarmMaxMs = if ($managed.Count) { [double] $managed[0].WarmMaxMs } else { 0 }
            ManagedRank = if ($managed.Count -and $successful.Count) {
                1 + @($successful | Where-Object { $_.MedianMs -lt $managed[0].MedianMs }).Count
            } else {
                0
            }
            ManagedVsFastest = if ($managed.Count -and $fastest.Count -and $fastest[0].MedianMs -gt 0) {
                ('{0}x' -f ([math]::Round($managed[0].MedianMs / $fastest[0].MedianMs, 2)))
            } else {
                ''
            }
            ManagedFirstIteration = if ($managed.Count) { [double] $managed[0].FirstIteration } else { 0 }
            ManagedLastIteration = if ($managed.Count) { [double] $managed[0].LastIteration } else { 0 }
            ManagedFirstMs = if ($managed.Count) { [double] $managed[0].FirstMs } else { 0 }
            ManagedLastMs = if ($managed.Count) { [double] $managed[0].LastMs } else { 0 }
            ManagedOutputFileCount = if ($managed.Count) { [double] $managed[0].MedianOutputFileCount } else { 0 }
            ManagedOutputBytes = if ($managed.Count) { [double] $managed[0].MedianOutputBytes } else { 0 }
            ManagedPackageCount = if ($managed.Count) { [double] $managed[0].MedianManagedPackageCount } else { 0 }
            ManagedDependencyCount = if ($managed.Count) { [double] $managed[0].MedianManagedDependencyCount } else { 0 }
            ManagedUniquePackageCount = if ($managed.Count) { [double] $managed[0].MedianManagedUniquePackageCount } else { 0 }
            ManagedUniqueDependencyCount = if ($managed.Count) { [double] $managed[0].MedianManagedUniqueDependencyCount } else { 0 }
            ManagedInstalledPackageCount = if ($managed.Count) { [double] $managed[0].MedianManagedInstalledPackageCount } else { 0 }
            ManagedAlreadyInstalledPackageCount = if ($managed.Count) { [double] $managed[0].MedianManagedAlreadyInstalledPackageCount } else { 0 }
            ManagedRootElapsedMs = if ($managed.Count) { [double] $managed[0].MedianManagedRootElapsedMs } else { 0 }
            ManagedHarnessOverheadMs = if ($managed.Count) { [double] $managed[0].MedianManagedHarnessOverheadMs } else { 0 }
            ManagedRepositoryRequests = if ($managed.Count) { [double] $managed[0].MedianManagedRepositoryRequests } else { 0 }
            ManagedPackageRepositoryRequests = if ($managed.Count) { [double] $managed[0].MedianManagedPackageRepositoryRequests } else { 0 }
            ManagedPackageRepositoryRedirects = if ($managed.Count) { [double] $managed[0].MedianManagedPackageRepositoryRedirects } else { 0 }
            ManagedDownloadBytes = if ($managed.Count) { [double] $managed[0].MedianManagedDownloadBytes } else { 0 }
            ManagedCacheHits = if ($managed.Count) { [double] $managed[0].MedianManagedCacheHits } else { 0 }
            ManagedExtractionCacheHits = if ($managed.Count) { [double] $managed[0].MedianManagedExtractionCacheHits } else { 0 }
            ManagedCoalescedWaitCount = if ($managed.Count) { [double] $managed[0].MedianManagedCoalescedWaitCount } else { 0 }
            ManagedCoalescedWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedCoalescedWaitMs } else { 0 }
            ManagedSlowestCoalescedWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestCoalescedWaitMs } else { 0 }
            ManagedInstallLockWaitCount = if ($managed.Count) { [double] $managed[0].MedianManagedInstallLockWaitCount } else { 0 }
            ManagedInstallLockWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedInstallLockWaitMs } else { 0 }
            ManagedSlowestInstallLockWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestInstallLockWaitMs } else { 0 }
            ManagedSlowestDependencyPackageMs = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestDependencyPackageMs } else { 0 }
            ManagedSlowestDependencyQueueWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestDependencyQueueWaitMs } else { 0 }
            ManagedSlowestVersionSelectionWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestVersionSelectionWaitMs } else { 0 }
            ManagedSlowestMaterializedPackageMs = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestMaterializedPackageMs } else { 0 }
            ManagedSlowestMaterializedPackageFileCount = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestMaterializedPackageFileCount } else { 0 }
            ManagedSlowestMaterializedPackageExtractedBytes = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestMaterializedPackageExtractedBytes } else { 0 }
            ManagedSlowestMaterializedPackageMBPerSecond = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestMaterializedPackageMBPerSecond } else { 0 }
            ManagedSlowestMaterializedPackageFilesPerSecond = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestMaterializedPackageFilesPerSecond } else { 0 }
            ManagedSlowestMaterializedPackageExtractionCacheLockWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedSlowestMaterializedPackageExtractionCacheLockWaitMs } else { 0 }
            ManagedCriticalDependencyBranchMs = if ($managed.Count) { [double] $managed[0].MedianManagedCriticalDependencyBranchMs } else { 0 }
            ManagedCriticalRootBranchMs = if ($managed.Count) { [double] $managed[0].MedianManagedCriticalRootBranchMs } else { 0 }
            ManagedCriticalMaterializationBranchMs = if ($managed.Count) { [double] $managed[0].MedianManagedCriticalMaterializationBranchMs } else { 0 }
            ManagedAuthenticodeCheckedFiles = if ($managed.Count) { [double] $managed[0].MedianManagedAuthenticodeCheckedFiles } else { 0 }
            ManagedAuthenticodeCatalogFiles = if ($managed.Count) { [double] $managed[0].MedianManagedAuthenticodeCatalogFiles } else { 0 }
            ManagedFirstRepositoryRequests = if ($managed.Count) { [double] $managed[0].FirstManagedRepositoryRequests } else { 0 }
            ManagedLastRepositoryRequests = if ($managed.Count) { [double] $managed[0].LastManagedRepositoryRequests } else { 0 }
            ManagedFirstPackageRepositoryRequests = if ($managed.Count) { [double] $managed[0].FirstManagedPackageRepositoryRequests } else { 0 }
            ManagedLastPackageRepositoryRequests = if ($managed.Count) { [double] $managed[0].LastManagedPackageRepositoryRequests } else { 0 }
            ManagedFirstRootDependencyMs = if ($managed.Count) { [double] $managed[0].FirstManagedRootDependencyMs } else { 0 }
            ManagedLastRootDependencyMs = if ($managed.Count) { [double] $managed[0].LastManagedRootDependencyMs } else { 0 }
            ManagedFirstRootDependencyUnattributedMs = if ($managed.Count) { [double] $managed[0].FirstManagedRootDependencyUnattributedMs } else { 0 }
            ManagedLastRootDependencyUnattributedMs = if ($managed.Count) { [double] $managed[0].LastManagedRootDependencyUnattributedMs } else { 0 }
            ManagedFirstRootDependencyCriticalPathGapMs = if ($managed.Count) { [double] $managed[0].FirstManagedRootDependencyCriticalPathGapMs } else { 0 }
            ManagedLastRootDependencyCriticalPathGapMs = if ($managed.Count) { [double] $managed[0].LastManagedRootDependencyCriticalPathGapMs } else { 0 }
            ManagedFirstDependencyBranchParallelismRatio = if ($managed.Count) { [double] $managed[0].FirstManagedDependencyBranchParallelismRatio } else { 0 }
            ManagedLastDependencyBranchParallelismRatio = if ($managed.Count) { [double] $managed[0].LastManagedDependencyBranchParallelismRatio } else { 0 }
            ManagedFirstVersionSelectionWaitMs = if ($managed.Count) { [double] $managed[0].FirstManagedVersionSelectionWaitMs } else { 0 }
            ManagedLastVersionSelectionWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedVersionSelectionWaitMs } else { 0 }
            ManagedFirstDependencyQueueWaitMs = if ($managed.Count) { [double] $managed[0].FirstManagedDependencyQueueWaitMs } else { 0 }
            ManagedLastDependencyQueueWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedDependencyQueueWaitMs } else { 0 }
            ManagedFirstDependencyBranchElapsedMs = if ($managed.Count) { [double] $managed[0].FirstManagedDependencyBranchElapsedMs } else { 0 }
            ManagedLastDependencyBranchElapsedMs = if ($managed.Count) { [double] $managed[0].LastManagedDependencyBranchElapsedMs } else { 0 }
            ManagedFirstDependencyBranchOverheadMs = if ($managed.Count) { [double] $managed[0].FirstManagedDependencyBranchOverheadMs } else { 0 }
            ManagedLastDependencyBranchOverheadMs = if ($managed.Count) { [double] $managed[0].LastManagedDependencyBranchOverheadMs } else { 0 }
            ManagedFirstDownloadMs = if ($managed.Count) { [double] $managed[0].FirstManagedDownloadMs } else { 0 }
            ManagedLastDownloadMs = if ($managed.Count) { [double] $managed[0].LastManagedDownloadMs } else { 0 }
            ManagedFirstExtractionMs = if ($managed.Count) { [double] $managed[0].FirstManagedExtractionMs } else { 0 }
            ManagedLastExtractionMs = if ($managed.Count) { [double] $managed[0].LastManagedExtractionMs } else { 0 }
            ManagedFirstDependencyMs = if ($managed.Count) { [double] $managed[0].FirstManagedDependencyMs } else { 0 }
            ManagedLastDependencyMs = if ($managed.Count) { [double] $managed[0].LastManagedDependencyMs } else { 0 }
            ManagedFirstPromotionMs = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionMs } else { 0 }
            ManagedLastPromotionMs = if ($managed.Count) { [double] $managed[0].LastManagedPromotionMs } else { 0 }
            ManagedFirstDownloadBytes = if ($managed.Count) { [double] $managed[0].FirstManagedDownloadBytes } else { 0 }
            ManagedLastDownloadBytes = if ($managed.Count) { [double] $managed[0].LastManagedDownloadBytes } else { 0 }
            ManagedFirstCacheHits = if ($managed.Count) { [double] $managed[0].FirstManagedCacheHits } else { 0 }
            ManagedLastCacheHits = if ($managed.Count) { [double] $managed[0].LastManagedCacheHits } else { 0 }
            ManagedFirstExtractionCacheHits = if ($managed.Count) { [double] $managed[0].FirstManagedExtractionCacheHits } else { 0 }
            ManagedLastExtractionCacheHits = if ($managed.Count) { [double] $managed[0].LastManagedExtractionCacheHits } else { 0 }
            ManagedFirstExtractionCacheLockWaitMs = if ($managed.Count) { [double] $managed[0].FirstManagedExtractionCacheLockWaitMs } else { 0 }
            ManagedLastExtractionCacheLockWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedExtractionCacheLockWaitMs } else { 0 }
            ManagedFirstCoalescedWaitMs = if ($managed.Count) { [double] $managed[0].FirstManagedCoalescedWaitMs } else { 0 }
            ManagedLastCoalescedWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedCoalescedWaitMs } else { 0 }
            ManagedFirstPromotionLockWaitMs = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionLockWaitMs } else { 0 }
            ManagedLastPromotionLockWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedPromotionLockWaitMs } else { 0 }
            ManagedFirstPromotionMoveMs = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionMoveMs } else { 0 }
            ManagedLastPromotionMoveMs = if ($managed.Count) { [double] $managed[0].LastManagedPromotionMoveMs } else { 0 }
            ManagedFirstPromotionFinalMoveMs = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionFinalMoveMs } else { 0 }
            ManagedLastPromotionFinalMoveMs = if ($managed.Count) { [double] $managed[0].LastManagedPromotionFinalMoveMs } else { 0 }
            ManagedFirstPromotionBackupMoveMs = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionBackupMoveMs } else { 0 }
            ManagedLastPromotionBackupMoveMs = if ($managed.Count) { [double] $managed[0].LastManagedPromotionBackupMoveMs } else { 0 }
            ManagedFirstPromotionBackupCleanupMs = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionBackupCleanupMs } else { 0 }
            ManagedLastPromotionBackupCleanupMs = if ($managed.Count) { [double] $managed[0].LastManagedPromotionBackupCleanupMs } else { 0 }
            ManagedFirstPromotionOverwriteCount = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionOverwriteCount } else { 0 }
            ManagedLastPromotionOverwriteCount = if ($managed.Count) { [double] $managed[0].LastManagedPromotionOverwriteCount } else { 0 }
            ManagedFirstDirectMaterializationCount = if ($managed.Count) { [double] $managed[0].FirstManagedDirectMaterializationCount } else { 0 }
            ManagedLastDirectMaterializationCount = if ($managed.Count) { [double] $managed[0].LastManagedDirectMaterializationCount } else { 0 }
            ManagedFirstPromotionDirectMaterializationMs = if ($managed.Count) { [double] $managed[0].FirstManagedPromotionDirectMaterializationMs } else { 0 }
            ManagedLastPromotionDirectMaterializationMs = if ($managed.Count) { [double] $managed[0].LastManagedPromotionDirectMaterializationMs } else { 0 }
            ManagedLastSlowestCoalescedWaitName = if ($managed.Count) { [string] $managed[0].LastManagedSlowestCoalescedWaitName } else { '' }
            ManagedLastSlowestCoalescedWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestCoalescedWaitMs } else { 0 }
            ManagedFirstInstallLockWaitMs = if ($managed.Count) { [double] $managed[0].FirstManagedInstallLockWaitMs } else { 0 }
            ManagedLastInstallLockWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedInstallLockWaitMs } else { 0 }
            ManagedLastSlowestInstallLockWaitName = if ($managed.Count) { [string] $managed[0].LastManagedSlowestInstallLockWaitName } else { '' }
            ManagedLastSlowestInstallLockWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestInstallLockWaitMs } else { 0 }
            ManagedLastSlowestDependencyPackageName = if ($managed.Count) { [string] $managed[0].LastManagedSlowestDependencyPackageName } else { '' }
            ManagedLastSlowestDependencyPackageParent = if ($managed.Count) { [string] $managed[0].LastManagedSlowestDependencyPackageParent } else { '' }
            ManagedLastSlowestDependencyPackageMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestDependencyPackageMs } else { 0 }
            ManagedLastSlowestDependencyQueueWaitName = if ($managed.Count) { [string] $managed[0].LastManagedSlowestDependencyQueueWaitName } else { '' }
            ManagedLastSlowestDependencyQueueWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestDependencyQueueWaitMs } else { 0 }
            ManagedLastSlowestVersionSelectionWaitName = if ($managed.Count) { [string] $managed[0].LastManagedSlowestVersionSelectionWaitName } else { '' }
            ManagedLastSlowestVersionSelectionWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestVersionSelectionWaitMs } else { 0 }
            ManagedLastSlowestMaterializedPackageName = if ($managed.Count) { [string] $managed[0].LastManagedSlowestMaterializedPackageName } else { '' }
            ManagedLastSlowestMaterializedPackageMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackageMs } else { 0 }
            ManagedLastSlowestMaterializedPackageFileCount = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackageFileCount } else { 0 }
            ManagedLastSlowestMaterializedPackageExtractedBytes = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackageExtractedBytes } else { 0 }
            ManagedLastSlowestMaterializedPackageMBPerSecond = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackageMBPerSecond } else { 0 }
            ManagedLastSlowestMaterializedPackageFilesPerSecond = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackageFilesPerSecond } else { 0 }
            ManagedLastSlowestMaterializedPackageExtractionMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackageExtractionMs } else { 0 }
            ManagedLastSlowestMaterializedPackageExtractionCacheLockWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackageExtractionCacheLockWaitMs } else { 0 }
            ManagedLastSlowestMaterializedPackagePromotionMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackagePromotionMs } else { 0 }
            ManagedLastSlowestMaterializedPackagePromotionLockWaitMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackagePromotionLockWaitMs } else { 0 }
            ManagedLastSlowestMaterializedPackagePromotionMoveMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackagePromotionMoveMs } else { 0 }
            ManagedLastSlowestMaterializedPackagePromotionFinalMoveMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackagePromotionFinalMoveMs } else { 0 }
            ManagedLastSlowestMaterializedPackagePromotionBackupMoveMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackagePromotionBackupMoveMs } else { 0 }
            ManagedLastSlowestMaterializedPackagePromotionBackupCleanupMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackagePromotionBackupCleanupMs } else { 0 }
            ManagedLastSlowestMaterializedPackagePromotionHadExistingTarget = if ($managed.Count) { [bool] $managed[0].LastManagedSlowestMaterializedPackagePromotionHadExistingTarget } else { $false }
            ManagedLastSlowestMaterializedPackagePromotionMaterializedDirectly = if ($managed.Count) { [bool] $managed[0].LastManagedSlowestMaterializedPackagePromotionMaterializedDirectly } else { $false }
            ManagedLastSlowestMaterializedPackagePromotionDirectMaterializationMs = if ($managed.Count) { [double] $managed[0].LastManagedSlowestMaterializedPackagePromotionDirectMaterializationMs } else { 0 }
            ManagedLastCriticalDependencyBranchName = if ($managed.Count) { [string] $managed[0].LastManagedCriticalDependencyBranchName } else { '' }
            ManagedLastCriticalDependencyBranchParent = if ($managed.Count) { [string] $managed[0].LastManagedCriticalDependencyBranchParent } else { '' }
            ManagedLastCriticalDependencyBranchMs = if ($managed.Count) { [double] $managed[0].LastManagedCriticalDependencyBranchMs } else { 0 }
            ManagedLastCriticalDependencyBranchDominantPhase = if ($managed.Count) { [string] $managed[0].LastManagedCriticalDependencyBranchDominantPhase } else { '' }
            ManagedLastCriticalDependencyBranchDominantPhaseMs = if ($managed.Count) { [double] $managed[0].LastManagedCriticalDependencyBranchDominantPhaseMs } else { 0 }
            ManagedLastCriticalRootBranchName = if ($managed.Count) { [string] $managed[0].LastManagedCriticalRootBranchName } else { '' }
            ManagedLastCriticalRootBranchMs = if ($managed.Count) { [double] $managed[0].LastManagedCriticalRootBranchMs } else { 0 }
            ManagedLastCriticalRootBranchDominantPhase = if ($managed.Count) { [string] $managed[0].LastManagedCriticalRootBranchDominantPhase } else { '' }
            ManagedLastCriticalRootBranchDominantPhaseMs = if ($managed.Count) { [double] $managed[0].LastManagedCriticalRootBranchDominantPhaseMs } else { 0 }
            ManagedLastCriticalMaterializationBranchName = if ($managed.Count) { [string] $managed[0].LastManagedCriticalMaterializationBranchName } else { '' }
            ManagedLastCriticalMaterializationBranchMs = if ($managed.Count) { [double] $managed[0].LastManagedCriticalMaterializationBranchMs } else { 0 }
            ManagedLastCriticalMaterializationDominantPhase = if ($managed.Count) { [string] $managed[0].LastManagedCriticalMaterializationDominantPhase } else { '' }
            ManagedLastCriticalMaterializationDominantPhaseMs = if ($managed.Count) { [double] $managed[0].LastManagedCriticalMaterializationDominantPhaseMs } else { 0 }
            ManagedMaintenanceActions = if ($managed.Count) { [double] $managed[0].MedianManagedMaintenanceActions } else { 0 }
            ManagedMaintenanceFindings = if ($managed.Count) { [double] $managed[0].MedianManagedMaintenanceFindings } else { 0 }
            ManagedRootDependencyMs = if ($managed.Count) { [double] $managed[0].MedianManagedRootDependencyMs } else { 0 }
            ManagedRootDependencyUnattributedMs = if ($managed.Count) { [double] $managed[0].MedianManagedRootDependencyUnattributedMs } else { 0 }
            ManagedRootDependencyCriticalPathGapMs = if ($managed.Count) { [double] $managed[0].MedianManagedRootDependencyCriticalPathGapMs } else { 0 }
            ManagedDependencyBranchParallelismRatio = if ($managed.Count) { [double] $managed[0].MedianManagedDependencyBranchParallelismRatio } else { 0 }
            ManagedVersionSelectionWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedVersionSelectionWaitMs } else { 0 }
            ManagedDependencyQueueWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedDependencyQueueWaitMs } else { 0 }
            ManagedDependencyBranchElapsedMs = if ($managed.Count) { [double] $managed[0].MedianManagedDependencyBranchElapsedMs } else { 0 }
            ManagedDependencyBranchOverheadMs = if ($managed.Count) { [double] $managed[0].MedianManagedDependencyBranchOverheadMs } else { 0 }
            ManagedDownloadMs = if ($managed.Count) { [double] $managed[0].MedianManagedDownloadMs } else { 0 }
            ManagedExtractionMs = if ($managed.Count) { [double] $managed[0].MedianManagedExtractionMs } else { 0 }
            ManagedExtractionCacheLockWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedExtractionCacheLockWaitMs } else { 0 }
            ManagedDependencyMs = if ($managed.Count) { [double] $managed[0].MedianManagedDependencyMs } else { 0 }
            ManagedPromotionMs = if ($managed.Count) { [double] $managed[0].MedianManagedPromotionMs } else { 0 }
            ManagedPromotionLockWaitMs = if ($managed.Count) { [double] $managed[0].MedianManagedPromotionLockWaitMs } else { 0 }
            ManagedPromotionMoveMs = if ($managed.Count) { [double] $managed[0].MedianManagedPromotionMoveMs } else { 0 }
        }
    }
}
