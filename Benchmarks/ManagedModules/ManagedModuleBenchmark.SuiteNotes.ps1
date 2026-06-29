function Format-ManagedBenchmarkMarkdownValue {
    param(
        [object] $Value
    )

    if ($null -eq $Value) {
        return ''
    }

    $text = [string] $Value
    $text.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Get-ManagedBenchmarkProperty {
    param(
        [object] $InputObject,
        [string] $Name
    )

    if ($null -eq $InputObject) {
        return ''
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return ''
    }

    if ($property.Value -is [array]) {
        return ($property.Value -join ', ')
    }

    [string] $property.Value
}

function Add-ManagedBenchmarkMarkdownTable {
    param(
        [Collections.Generic.List[string]] $Lines,
        [object[]] $Rows,
        [string[]] $Columns
    )

    if (-not $Rows -or $Rows.Count -eq 0) {
        $Lines.Add('_No rows._')
        return
    }

    $Lines.Add('| ' + ($Columns -join ' | ') + ' |')
    $Lines.Add('| ' + (($Columns | ForEach-Object { '---' }) -join ' | ') + ' |')

    foreach ($row in $Rows) {
        $values = foreach ($column in $Columns) {
            Format-ManagedBenchmarkMarkdownValue (Get-ManagedBenchmarkProperty -InputObject $row -Name $column)
        }

        $Lines.Add('| ' + ($values -join ' | ') + ' |')
    }
}

function Write-ManagedBenchmarkSuiteNotes {
    param(
        [object[]] $Scenarios,
        [object[]] $SummaryRows,
        [object[]] $EngineRows,
        [object[]] $ScoreboardRows,
        [object[]] $OptimizationRows,
        [object[]] $HostComparisonRows,
        [object[]] $HostRows,
        [object[]] $GateViolations,
        [object[]] $HostGateViolations,
        [string] $Path,
        [datetime] $GeneratedAt = (Get-Date)
    )

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# Managed Module Benchmark Suite Notes')
    $lines.Add('')
    $lines.Add("Generated: $($GeneratedAt.ToString('u'))")
    $lines.Add('')
    $lines.Add('Read this first: `Scoreboard` rows are provider comparisons. `Diagnostic` rows are managed-only or managed-focused evidence and must not be read as provider races.')
    $lines.Add('')

    $scoreboards = @($Scenarios | Where-Object { (Get-ManagedBenchmarkProperty -InputObject $_ -Name 'BenchmarkRole') -eq 'Scoreboard' })
    $diagnostics = @($Scenarios | Where-Object { (Get-ManagedBenchmarkProperty -InputObject $_ -Name 'BenchmarkRole') -eq 'Diagnostic' })

    $lines.Add('## Scoreboards')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $scoreboards -Columns @('Suite', 'Name', 'ComparisonScope', 'Operations', 'Engines', 'BenchmarkInterpretation')
    $lines.Add('')

    $lines.Add('## Diagnostics')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $diagnostics -Columns @('Suite', 'Name', 'ComparisonScope', 'Operations', 'Engines', 'BenchmarkInterpretation')
    $lines.Add('')

    $lines.Add('## Results')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $SummaryRows -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Host', 'Operation', 'FastestEngine', 'FastestMs', 'ManagedMs', 'ManagedRank', 'ManagedVsFastest')
    $lines.Add('')

    if ($ScoreboardRows -and $ScoreboardRows.Count -gt 0) {
        $lines.Add('## Provider Scoreboard')
        $lines.Add('')
        $lines.Add('This table is the wide comparison view for README-ready evidence: managed first, then each comparison engine with its median time and ratio against the fastest successful engine for that scenario. `Skipped` means the engine had no equivalent operation or was unavailable on that host.')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $ScoreboardRows -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Host', 'Operation', 'Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet', 'FastestEngine', 'ManagedRank', 'ManagedVsFastest', 'BenchmarkInterpretation')
        $lines.Add('')
    }

    if ($EngineRows -and $EngineRows.Count -gt 0) {
        $lines.Add('## Engine Medians')
        $lines.Add('')
        $lines.Add('These rows show each participating engine median at suite level. Use this table for provider comparisons; use `Optimization Targets` for managed-only bottleneck work.')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $EngineRows -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Host', 'Operation', 'Engine', 'MedianMs', 'MedianOutputMB', 'MedianOutputMBPerSecond', 'MedianOutputFilesPerSecond', 'Runs', 'Succeeded', 'Failed', 'Skipped', 'FirstMs', 'LastMs')
        $lines.Add('')
    }

    if ($HostComparisonRows -and $HostComparisonRows.Count -gt 0) {
        $lines.Add('## Host Comparisons')
        $lines.Add('')
        $lines.Add('These rows compare the same managed scenario across PowerShell hosts. Median columns feed the host gate; first and last columns show cold-start and warm-cache behavior separately.')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $HostComparisonRows -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Operation', 'BaselineHost', 'BaselineMs', 'ComparisonHost', 'ComparisonMs', 'ComparisonVsBaseline', 'BaselineFirstMs', 'ComparisonFirstMs', 'FirstComparisonVsBaseline', 'BaselineLastMs', 'ComparisonLastMs', 'LastComparisonVsBaseline', 'BenchmarkInterpretation')
        $lines.Add('')
    }

    if ($OptimizationRows -and $OptimizationRows.Count -gt 0) {
        $lines.Add('## Optimization Targets')
        $lines.Add('')
        $lines.Add('Use these rows to decide where the next managed-engine optimization should start. `Diagnostic` rows identify managed cost centers; `Scoreboard` rows keep provider-race context.')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $OptimizationRows -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Host', 'Operation', 'ManagedMs', 'OutputMB', 'OutputMBPerSecond', 'OutputFilesPerSecond', 'Bottleneck', 'LastMs', 'LastBottleneck', 'LastBottleneckMs', 'LastWarmOptimizationLane', 'LastWarmOptimizationLaneMs', 'LastCriticalOptimizationLane', 'LastCriticalOptimizationLaneMs', 'LastCriticalRootBranch', 'LastCriticalRootBranchMs', 'LastCriticalRootBranchDominantPhase', 'LastRootDependencyUnattributedMs', 'LastCriticalDependencyBranch', 'LastCriticalDependencyBranchMs', 'LastCriticalDependencyBranchDominantPhase', 'LastCriticalMaterializationBranch', 'LastCriticalMaterializationBranchMs', 'LastCriticalMaterializationDominantPhase', 'LastMaterializationMs', 'LastMaterializationMBPerSecond', 'LastMaterializationDominantPhase', 'LastDependencyQueueWaitMs', 'LastDependencyMs', 'LastSlowestDependencyQueueWait', 'LastSlowestDependencyQueueWaitMs', 'LastSlowestDependencyPackage', 'LastSlowestDependencyPackageParent', 'LastSlowestDependencyPackageMs', 'LastCoalescedWaitMs', 'LastInstallLockWaitMs', 'ExtractionCacheLockWaitMs', 'LastExtractionCacheLockWaitMs', 'LastPromotionMoveMs', 'LastSlowestMaterializedPackage', 'LastSlowestMaterializedPackageMs', 'LastSlowestMaterializedPackageExtractionCacheLockWaitMs', 'LastSlowestMaterializedPackagePromotionMoveMs', 'LastCriticalOptimizationQuestion', 'LastWarmOptimizationQuestion')
        $lines.Add('')
    }

    $lines.Add('## Hosts')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $HostRows -Columns @('Host', 'Status', 'Executable', 'Reason')
    $lines.Add('')

    if ($GateViolations -and $GateViolations.Count -gt 0) {
        $lines.Add('## Performance Gate Violations')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $GateViolations -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Host', 'Operation', 'Reason', 'BenchmarkInterpretation')
        $lines.Add('')
    }

    if ($HostGateViolations -and $HostGateViolations.Count -gt 0) {
        $lines.Add('## Host Gate Violations')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $HostGateViolations -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Operation', 'Reason', 'BenchmarkInterpretation')
        $lines.Add('')
    }

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -Path $directory -ItemType Directory -Force | Out-Null
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}
