function Get-Median {
    param([double[]] $Values)

    if (-not $Values -or $Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) {
        return [math]::Round($sorted[$middle], 2)
    }

    [math]::Round(($sorted[$middle - 1] + $sorted[$middle]) / 2, 2)
}

function Get-MedianProperty {
    param(
        [object[]] $Rows,
        [string] $Name
    )

    Get-Median -Values @($Rows | ForEach-Object { [double] $_.$Name })
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

function Get-OrderedSucceededRows {
    param([object[]] $Rows)

    @($Rows | Sort-Object @{ Expression = { Get-IterationValue -Row $_ } }, @{ Expression = { [double]$_.ElapsedMilliseconds } })
}

function New-Summary {
    param([object[]] $Rows)

    foreach ($group in ($Rows | Group-Object Operation, Scenario, Engine)) {
        $passed = @($group.Group | Where-Object Status -eq 'Succeeded')
        $orderedPassed = @(Get-OrderedSucceededRows -Rows $passed)
        $firstPassed = if ($orderedPassed.Count) { $orderedPassed[0] } else { $null }
        $lastPassed = if ($orderedPassed.Count) { $orderedPassed[$orderedPassed.Count - 1] } else { $null }
        [pscustomobject]@{
            Operation = [string]$group.Group[0].Operation
            Scenario = [string]$group.Group[0].Scenario
            Engine = [string]$group.Group[0].Engine
            Runs = $group.Count
            Succeeded = $passed.Count
            Failed = @($group.Group | Where-Object Status -eq 'Failed').Count
            Skipped = @($group.Group | Where-Object Status -eq 'Skipped').Count
            MedianMs = Get-Median -Values @($passed | ForEach-Object { [double]$_.ElapsedMilliseconds })
            FirstIteration = if ($firstPassed) { Get-IterationValue -Row $firstPassed } else { 0 }
            LastIteration = if ($lastPassed) { Get-IterationValue -Row $lastPassed } else { 0 }
            FirstMs = if ($firstPassed) { [math]::Round([double]$firstPassed.ElapsedMilliseconds, 2) } else { 0 }
            LastMs = if ($lastPassed) { [math]::Round([double]$lastPassed.ElapsedMilliseconds, 2) } else { 0 }
            MinMs = if ($passed.Count) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Minimum).Minimum, 2) } else { 0 }
            MaxMs = if ($passed.Count) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Maximum).Maximum, 2) } else { 0 }
            MedianOutputFileCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'OutputFileCount' } else { 0 }
            MedianOutputBytes = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'OutputBytes' } else { 0 }
            MedianManagedPackageCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedPackageCount' } else { 0 }
            MedianManagedDependencyCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedDependencyCount' } else { 0 }
            MedianManagedUniquePackageCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedUniquePackageCount' } else { 0 }
            MedianManagedUniqueDependencyCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedUniqueDependencyCount' } else { 0 }
            MedianManagedInstalledPackageCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedInstalledPackageCount' } else { 0 }
            MedianManagedAlreadyInstalledPackageCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedAlreadyInstalledPackageCount' } else { 0 }
            MedianManagedRootElapsedMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedRootElapsedMilliseconds' } else { 0 }
            MedianManagedHarnessOverheadMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedHarnessOverheadMilliseconds' } else { 0 }
            MedianManagedRootDependencyMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedRootDependencyMilliseconds' } else { 0 }
            MedianManagedDownloadMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalDownloadMilliseconds' } else { 0 }
            MedianManagedExtractionMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalExtractionMilliseconds' } else { 0 }
            MedianManagedPromotionMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionMilliseconds' } else { 0 }
            MedianManagedRepositoryRequests = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedRepositoryRequestCount' } else { 0 }
            MedianManagedPackageRepositoryRequests = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedPackageRepositoryRequestCount' } else { 0 }
            MedianManagedPackageRepositoryRedirects = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedPackageRepositoryRedirectCount' } else { 0 }
            MedianManagedDownloadBytes = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedDownloadBytes' } else { 0 }
            MedianManagedCacheHits = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedCacheHitCount' } else { 0 }
            MedianManagedExtractionCacheHits = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedExtractionCacheHitCount' } else { 0 }
            MedianManagedAuthenticodeCheckedFiles = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedAuthenticodeCheckedFileCount' } else { 0 }
            FirstManagedRepositoryRequests = if ($firstPassed) { [double]$firstPassed.ManagedRepositoryRequestCount } else { 0 }
            LastManagedRepositoryRequests = if ($lastPassed) { [double]$lastPassed.ManagedRepositoryRequestCount } else { 0 }
            FirstManagedPackageRepositoryRequests = if ($firstPassed) { [double]$firstPassed.ManagedPackageRepositoryRequestCount } else { 0 }
            LastManagedPackageRepositoryRequests = if ($lastPassed) { [double]$lastPassed.ManagedPackageRepositoryRequestCount } else { 0 }
            FirstManagedDownloadBytes = if ($firstPassed) { [double]$firstPassed.ManagedDownloadBytes } else { 0 }
            LastManagedDownloadBytes = if ($lastPassed) { [double]$lastPassed.ManagedDownloadBytes } else { 0 }
            FirstManagedCacheHits = if ($firstPassed) { [double]$firstPassed.ManagedCacheHitCount } else { 0 }
            LastManagedCacheHits = if ($lastPassed) { [double]$lastPassed.ManagedCacheHitCount } else { 0 }
            FirstManagedExtractionCacheHits = if ($firstPassed) { [double]$firstPassed.ManagedExtractionCacheHitCount } else { 0 }
            LastManagedExtractionCacheHits = if ($lastPassed) { [double]$lastPassed.ManagedExtractionCacheHitCount } else { 0 }
            MedianManagedMaintenanceActions = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedMaintenanceActionCount' } else { 0 }
            MedianManagedMaintenanceFindings = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedMaintenanceFindingCount' } else { 0 }
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
            ManagedMs = if ($managed.Count) { [double]$managed[0].MedianMs } else { 0 }
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
            ManagedAuthenticodeCheckedFiles = if ($managed.Count) { [double] $managed[0].MedianManagedAuthenticodeCheckedFiles } else { 0 }
            ManagedFirstRepositoryRequests = if ($managed.Count) { [double] $managed[0].FirstManagedRepositoryRequests } else { 0 }
            ManagedLastRepositoryRequests = if ($managed.Count) { [double] $managed[0].LastManagedRepositoryRequests } else { 0 }
            ManagedFirstPackageRepositoryRequests = if ($managed.Count) { [double] $managed[0].FirstManagedPackageRepositoryRequests } else { 0 }
            ManagedLastPackageRepositoryRequests = if ($managed.Count) { [double] $managed[0].LastManagedPackageRepositoryRequests } else { 0 }
            ManagedFirstDownloadBytes = if ($managed.Count) { [double] $managed[0].FirstManagedDownloadBytes } else { 0 }
            ManagedLastDownloadBytes = if ($managed.Count) { [double] $managed[0].LastManagedDownloadBytes } else { 0 }
            ManagedFirstCacheHits = if ($managed.Count) { [double] $managed[0].FirstManagedCacheHits } else { 0 }
            ManagedLastCacheHits = if ($managed.Count) { [double] $managed[0].LastManagedCacheHits } else { 0 }
            ManagedFirstExtractionCacheHits = if ($managed.Count) { [double] $managed[0].FirstManagedExtractionCacheHits } else { 0 }
            ManagedLastExtractionCacheHits = if ($managed.Count) { [double] $managed[0].LastManagedExtractionCacheHits } else { 0 }
            ManagedMaintenanceActions = if ($managed.Count) { [double] $managed[0].MedianManagedMaintenanceActions } else { 0 }
            ManagedMaintenanceFindings = if ($managed.Count) { [double] $managed[0].MedianManagedMaintenanceFindings } else { 0 }
            ManagedRootDependencyMs = if ($managed.Count) { [double] $managed[0].MedianManagedRootDependencyMs } else { 0 }
            ManagedDownloadMs = if ($managed.Count) { [double] $managed[0].MedianManagedDownloadMs } else { 0 }
            ManagedExtractionMs = if ($managed.Count) { [double] $managed[0].MedianManagedExtractionMs } else { 0 }
            ManagedPromotionMs = if ($managed.Count) { [double] $managed[0].MedianManagedPromotionMs } else { 0 }
        }
    }
}
