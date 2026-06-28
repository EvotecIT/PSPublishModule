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

function New-Summary {
    param([object[]] $Rows)

    foreach ($group in ($Rows | Group-Object Operation, Scenario, Engine)) {
        $passed = @($group.Group | Where-Object Status -eq 'Succeeded')
        [pscustomobject]@{
            Operation = [string]$group.Group[0].Operation
            Scenario = [string]$group.Group[0].Scenario
            Engine = [string]$group.Group[0].Engine
            Runs = $group.Count
            Succeeded = $passed.Count
            Failed = @($group.Group | Where-Object Status -eq 'Failed').Count
            Skipped = @($group.Group | Where-Object Status -eq 'Skipped').Count
            MedianMs = Get-Median -Values @($passed | ForEach-Object { [double]$_.ElapsedMilliseconds })
            MinMs = if ($passed.Count) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Minimum).Minimum, 2) } else { 0 }
            MaxMs = if ($passed.Count) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Maximum).Maximum, 2) } else { 0 }
            MedianOutputFileCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'OutputFileCount' } else { 0 }
            MedianOutputBytes = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'OutputBytes' } else { 0 }
            MedianManagedPackageCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedPackageCount' } else { 0 }
            MedianManagedDependencyCount = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedDependencyCount' } else { 0 }
            MedianManagedRootDependencyMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedRootDependencyMilliseconds' } else { 0 }
            MedianManagedDownloadMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalDownloadMilliseconds' } else { 0 }
            MedianManagedExtractionMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalExtractionMilliseconds' } else { 0 }
            MedianManagedPromotionMs = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedTotalPromotionMilliseconds' } else { 0 }
            MedianManagedRepositoryRequests = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedRepositoryRequestCount' } else { 0 }
            MedianManagedDownloadBytes = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedDownloadBytes' } else { 0 }
            MedianManagedCacheHits = if ($passed.Count) { Get-MedianProperty -Rows $passed -Name 'ManagedCacheHitCount' } else { 0 }
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
            ManagedPackageCount = if ($managed.Count) { [double] $managed[0].MedianManagedPackageCount } else { 0 }
            ManagedDependencyCount = if ($managed.Count) { [double] $managed[0].MedianManagedDependencyCount } else { 0 }
            ManagedRepositoryRequests = if ($managed.Count) { [double] $managed[0].MedianManagedRepositoryRequests } else { 0 }
            ManagedDownloadBytes = if ($managed.Count) { [double] $managed[0].MedianManagedDownloadBytes } else { 0 }
            ManagedCacheHits = if ($managed.Count) { [double] $managed[0].MedianManagedCacheHits } else { 0 }
            ManagedRootDependencyMs = if ($managed.Count) { [double] $managed[0].MedianManagedRootDependencyMs } else { 0 }
            ManagedDownloadMs = if ($managed.Count) { [double] $managed[0].MedianManagedDownloadMs } else { 0 }
            ManagedExtractionMs = if ($managed.Count) { [double] $managed[0].MedianManagedExtractionMs } else { 0 }
            ManagedPromotionMs = if ($managed.Count) { [double] $managed[0].MedianManagedPromotionMs } else { 0 }
        }
    }
}
