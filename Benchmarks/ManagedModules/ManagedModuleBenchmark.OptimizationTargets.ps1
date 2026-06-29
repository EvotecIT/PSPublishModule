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

function Get-ManagedPhaseQuestion {
    param([string] $Name)

    switch ($Name) {
        'RootDependency' { 'Can dependency scheduling, installed-version reuse, or repository lookup fan-out shrink the root operation?' }
        'Download' { 'Can package download throughput, source selection, caching, or request coalescing improve this lane?' }
        'Extraction' { 'Can archive extraction, path creation, or file writes be reduced safely?' }
        'Promotion' { 'Can final move, overwrite, or receipt writes be reduced without weakening rollback?' }
        'HarnessOverhead' { 'Is the benchmark dominated by child-host startup, module import, or measurement wrapper work?' }
        default { 'Add managed detail instrumentation before optimizing this row.' }
    }
}

function Get-ManagedBenchmarkBottleneck {
    param(
        [double] $ManagedMilliseconds,
        [object[]] $Phases
    )

    $bottleneck = @($Phases | Sort-Object Milliseconds -Descending | Select-Object -First 1)
    $bottleneckMs = if ($bottleneck.Count) { [double] $bottleneck[0].Milliseconds } else { 0.0 }
    if ($bottleneckMs -le 0) {
        return [pscustomobject]@{
            Name = 'Uninstrumented'
            Milliseconds = 0.0
            Share = ''
            ShareRaw = 0.0
            Note = ''
            Question = Get-ManagedPhaseQuestion -Name ''
        }
    }

    $share = Get-ManagedBottleneckShare -ManagedMilliseconds $ManagedMilliseconds -BottleneckMilliseconds $bottleneckMs
    [pscustomobject]@{
        Name = [string] $bottleneck[0].Name
        Milliseconds = $bottleneckMs
        Share = [string] $share.Text
        ShareRaw = [double] $share.Raw
        Note = [string] $share.Note
        Question = Get-ManagedPhaseQuestion -Name ([string] $bottleneck[0].Name)
    }
}

function New-ManagedOptimizationTarget {
    param([object[]] $Rows)

    foreach ($row in @($Rows | Where-Object { $_.ManagedMs -and (ConvertTo-ManagedBenchmarkDouble -Value $_.ManagedMs) -gt 0 })) {
        $managedMs = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedMs
        $phases = @(
            [pscustomobject]@{ Name = 'RootDependency'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRootDependencyMs }
            [pscustomobject]@{ Name = 'Download'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedDownloadMs }
            [pscustomobject]@{ Name = 'Extraction'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedExtractionMs }
            [pscustomobject]@{ Name = 'Promotion'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPromotionMs }
            [pscustomobject]@{ Name = 'HarnessOverhead'; Milliseconds = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedHarnessOverheadMs }
        )
        $bottleneck = Get-ManagedBenchmarkBottleneck -ManagedMilliseconds $managedMs -Phases $phases
        $lastMs = if ($row.PSObject.Properties['ManagedLastMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastMs } else { 0.0 }
        $lastPhases = @(
            [pscustomobject]@{ Name = 'RootDependency'; Milliseconds = if ($row.PSObject.Properties['ManagedLastRootDependencyMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastRootDependencyMs } else { 0.0 } }
            [pscustomobject]@{ Name = 'Download'; Milliseconds = if ($row.PSObject.Properties['ManagedLastDownloadMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastDownloadMs } else { 0.0 } }
            [pscustomobject]@{ Name = 'Extraction'; Milliseconds = if ($row.PSObject.Properties['ManagedLastExtractionMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastExtractionMs } else { 0.0 } }
            [pscustomobject]@{ Name = 'Promotion'; Milliseconds = if ($row.PSObject.Properties['ManagedLastPromotionMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastPromotionMs } else { 0.0 } }
        )
        $lastBottleneck = Get-ManagedBenchmarkBottleneck -ManagedMilliseconds $lastMs -Phases $lastPhases
        $rootElapsedMs = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRootElapsedMs
        $repositoryRequests = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedRepositoryRequests
        $packageRequests = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPackageRepositoryRequests
        $packageRedirects = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPackageRepositoryRedirects
        $downloadBytes = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedDownloadBytes
        $downloadMb = [math]::Round($downloadBytes / 1MB, 2)
        $firstDownloadBytes = if ($row.PSObject.Properties['ManagedFirstDownloadBytes']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedFirstDownloadBytes } else { 0.0 }
        $lastDownloadBytes = if ($row.PSObject.Properties['ManagedLastDownloadBytes']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastDownloadBytes } else { 0.0 }

        [pscustomobject]@{
            Suite = if ($row.PSObject.Properties['Suite']) { [string] $row.Suite } else { '' }
            Scenario = [string] $row.Scenario
            BenchmarkRole = if ($row.PSObject.Properties['BenchmarkRole']) { [string] $row.BenchmarkRole } else { '' }
            ComparisonScope = if ($row.PSObject.Properties['ComparisonScope']) { [string] $row.ComparisonScope } else { '' }
            BenchmarkInterpretation = if ($row.PSObject.Properties['BenchmarkInterpretation']) { [string] $row.BenchmarkInterpretation } else { '' }
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
            FirstDownloadMB = [math]::Round($firstDownloadBytes / 1MB, 2)
            LastDownloadMB = [math]::Round($lastDownloadBytes / 1MB, 2)
            PackageCount = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedPackageCount
            UniquePackageCount = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedUniquePackageCount
            CacheHits = ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedCacheHits
            ExtractionCacheHits = if ($row.PSObject.Properties['ManagedExtractionCacheHits']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedExtractionCacheHits } else { 0.0 }
            FirstMs = if ($row.PSObject.Properties['ManagedFirstMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedFirstMs } else { 0.0 }
            LastMs = if ($row.PSObject.Properties['ManagedLastMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastMs } else { 0.0 }
            FirstRepositoryRequests = if ($row.PSObject.Properties['ManagedFirstRepositoryRequests']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedFirstRepositoryRequests } else { 0.0 }
            LastRepositoryRequests = if ($row.PSObject.Properties['ManagedLastRepositoryRequests']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastRepositoryRequests } else { 0.0 }
            FirstPackageRepositoryRequests = if ($row.PSObject.Properties['ManagedFirstPackageRepositoryRequests']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedFirstPackageRepositoryRequests } else { 0.0 }
            LastPackageRepositoryRequests = if ($row.PSObject.Properties['ManagedLastPackageRepositoryRequests']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastPackageRepositoryRequests } else { 0.0 }
            FirstCacheHits = if ($row.PSObject.Properties['ManagedFirstCacheHits']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedFirstCacheHits } else { 0.0 }
            LastCacheHits = if ($row.PSObject.Properties['ManagedLastCacheHits']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastCacheHits } else { 0.0 }
            FirstExtractionCacheHits = if ($row.PSObject.Properties['ManagedFirstExtractionCacheHits']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedFirstExtractionCacheHits } else { 0.0 }
            LastExtractionCacheHits = if ($row.PSObject.Properties['ManagedLastExtractionCacheHits']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastExtractionCacheHits } else { 0.0 }
            CoalescedWaitMs = if ($row.PSObject.Properties['ManagedCoalescedWaitMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedCoalescedWaitMs } else { 0.0 }
            LastCoalescedWaitMs = if ($row.PSObject.Properties['ManagedLastCoalescedWaitMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastCoalescedWaitMs } else { 0.0 }
            LastSlowestCoalescedWait = if ($row.PSObject.Properties['ManagedLastSlowestCoalescedWaitName']) { [string] $row.ManagedLastSlowestCoalescedWaitName } else { '' }
            LastSlowestCoalescedWaitMs = if ($row.PSObject.Properties['ManagedLastSlowestCoalescedWaitMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastSlowestCoalescedWaitMs } else { 0.0 }
            SlowestMaterializedPackageMs = if ($row.PSObject.Properties['ManagedSlowestMaterializedPackageMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedSlowestMaterializedPackageMs } else { 0.0 }
            LastSlowestMaterializedPackage = if ($row.PSObject.Properties['ManagedLastSlowestMaterializedPackageName']) { [string] $row.ManagedLastSlowestMaterializedPackageName } else { '' }
            LastSlowestMaterializedPackageMs = if ($row.PSObject.Properties['ManagedLastSlowestMaterializedPackageMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastSlowestMaterializedPackageMs } else { 0.0 }
            LastSlowestMaterializedPackageExtractionMs = if ($row.PSObject.Properties['ManagedLastSlowestMaterializedPackageExtractionMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastSlowestMaterializedPackageExtractionMs } else { 0.0 }
            LastSlowestMaterializedPackagePromotionMs = if ($row.PSObject.Properties['ManagedLastSlowestMaterializedPackagePromotionMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastSlowestMaterializedPackagePromotionMs } else { 0.0 }
            LastRootDependencyMs = if ($row.PSObject.Properties['ManagedLastRootDependencyMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastRootDependencyMs } else { 0.0 }
            LastDownloadMs = if ($row.PSObject.Properties['ManagedLastDownloadMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastDownloadMs } else { 0.0 }
            LastExtractionMs = if ($row.PSObject.Properties['ManagedLastExtractionMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastExtractionMs } else { 0.0 }
            LastPromotionMs = if ($row.PSObject.Properties['ManagedLastPromotionMs']) { ConvertTo-ManagedBenchmarkDouble -Value $row.ManagedLastPromotionMs } else { 0.0 }
            Bottleneck = [string] $bottleneck.Name
            BottleneckMs = [math]::Round([double] $bottleneck.Milliseconds, 2)
            BottleneckShare = [string] $bottleneck.Share
            BottleneckShareRaw = [math]::Round([double] $bottleneck.ShareRaw, 1)
            TimingNote = [string] $bottleneck.Note
            NextQuestion = [string] $bottleneck.Question
            LastBottleneck = [string] $lastBottleneck.Name
            LastBottleneckMs = [math]::Round([double] $lastBottleneck.Milliseconds, 2)
            LastBottleneckShare = [string] $lastBottleneck.Share
            LastBottleneckShareRaw = [math]::Round([double] $lastBottleneck.ShareRaw, 1)
            LastTimingNote = [string] $lastBottleneck.Note
            LastNextQuestion = [string] $lastBottleneck.Question
        }
    }
}
