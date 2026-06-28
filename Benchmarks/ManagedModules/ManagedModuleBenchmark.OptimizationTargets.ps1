function Get-ManagedBottleneckShare {
    param(
        [double] $ManagedMilliseconds,
        [double] $BottleneckMilliseconds
    )

    if ($ManagedMilliseconds -le 0 -or $BottleneckMilliseconds -le 0) {
        return [pscustomobject]@{
            Text = ''
            Raw = 0.0
            Note = ''
        }
    }

    $rawShare = [math]::Round(($BottleneckMilliseconds / $ManagedMilliseconds) * 100, 1)
    if ($rawShare -gt 100) {
        return [pscustomobject]@{
            Text = '>100%'
            Raw = $rawShare
            Note = 'Summed package and dependency phase timings overlap; use the raw share as parallel work evidence, not wall-clock percentage.'
        }
    }

    [pscustomobject]@{
        Text = ('{0}%' -f $rawShare.ToString('0.#', [Globalization.CultureInfo]::InvariantCulture))
        Raw = $rawShare
        Note = ''
    }
}

function New-ManagedOptimizationTarget {
    param([object[]] $Rows)

    foreach ($row in @($Rows | Where-Object { $_.ManagedMs -and (ConvertTo-ManagedBenchmarkDouble -Value $_.ManagedMs) -gt 0 })) {
        $managedMs = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedMs
        $phases = @(
            [pscustomobject]@{ Name = 'RootDependency'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRootDependencyMs; Question = 'Can dependency scheduling, installed-version reuse, or repository lookup fan-out shrink the root operation?' }
            [pscustomobject]@{ Name = 'Download'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedDownloadMs; Question = 'Can package download throughput, source selection, caching, or request coalescing improve this lane?' }
            [pscustomobject]@{ Name = 'Extraction'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedExtractionMs; Question = 'Can archive extraction, path creation, or file writes be reduced safely?' }
            [pscustomobject]@{ Name = 'Promotion'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPromotionMs; Question = 'Can final move, overwrite, or receipt writes be reduced without weakening rollback?' }
            [pscustomobject]@{ Name = 'HarnessOverhead'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedHarnessOverheadMs; Question = 'Is the benchmark dominated by child-host startup, module import, or measurement wrapper work?' }
        )
        $bottleneck = @($phases | Sort-Object Milliseconds -Descending | Select-Object -First 1)
        $bottleneckMs = if ($bottleneck.Count) { [double] $bottleneck[0].Milliseconds } else { 0.0 }
        $rootElapsedMs = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRootElapsedMs
        $repositoryRequests = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRepositoryRequests
        $packageRequests = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPackageRepositoryRequests
        $packageRedirects = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPackageRepositoryRedirects
        $downloadBytes = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedDownloadBytes
        $downloadMb = [math]::Round($downloadBytes / 1MB, 2)
        $bottleneckShare = Get-ManagedBottleneckShare -ManagedMilliseconds $managedMs -BottleneckMilliseconds $bottleneckMs

        [pscustomobject]@{
            Suite = if ($row.PSObject.Properties['Suite']) { [string] $row.Suite } else { '' }
            Scenario = [string] $row.Scenario
            ModuleName = if ($row.PSObject.Properties['ModuleName']) { [string] $row.ModuleName } else { '' }
            Host = if ($row.PSObject.Properties['Host']) { [string] $row.Host } else { '' }
            Operation = [string] $row.Operation
            ManagedMs = [math]::Round($managedMs, 2)
            ManagedRank = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRank
            ManagedVsFastest = if ($row.PSObject.Properties['ManagedVsFastest']) { [string] $row.ManagedVsFastest } else { '' }
            RootElapsedMs = [math]::Round($rootElapsedMs, 2)
            HarnessOverheadMs = [math]::Round((ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedHarnessOverheadMs), 2)
            RootDependencyMs = [math]::Round((ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRootDependencyMs), 2)
            DownloadMs = [math]::Round((ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedDownloadMs), 2)
            ExtractionMs = [math]::Round((ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedExtractionMs), 2)
            PromotionMs = [math]::Round((ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPromotionMs), 2)
            RepositoryRequests = [math]::Round($repositoryRequests, 2)
            PackageRepositoryRequests = [math]::Round($packageRequests, 2)
            PackageRepositoryRedirects = [math]::Round($packageRedirects, 2)
            DownloadMB = $downloadMb
            PackageCount = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPackageCount
            UniquePackageCount = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedUniquePackageCount
            CacheHits = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedCacheHits
            Bottleneck = if ($bottleneckMs -gt 0) { [string] $bottleneck[0].Name } else { 'Uninstrumented' }
            BottleneckMs = [math]::Round($bottleneckMs, 2)
            BottleneckShare = [string] $bottleneckShare.Text
            BottleneckShareRaw = [math]::Round([double] $bottleneckShare.Raw, 1)
            TimingNote = [string] $bottleneckShare.Note
            NextQuestion = if ($bottleneckMs -gt 0) { [string] $bottleneck[0].Question } else { 'Add managed detail instrumentation before optimizing this row.' }
        }
    }
}
