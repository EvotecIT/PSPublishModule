# Refresh measured module counts without replacing reviewed Strict performance budgets.
function Update-PublicCorpusBaseline {
    param(
        [object] $Baseline,
        [object[]] $Modules,
        [string] $RuntimeIdentifier,
        [string] $Path
    )

    $counts = [ordered]@{
        runtimeIdentifier = $RuntimeIdentifier
        modulesPassed = $Modules.Count
        modulesTotal = $Modules.Count
        scenarioFamilies = @($Modules.scenarioFamily | Sort-Object -Unique).Count
        cleanImportedCommands = [int](($Modules | ForEach-Object { $_.exportedCommands } | Measure-Object -Sum).Sum)
    }
    foreach ($metric in @('analyzedUnits', 'boundUnits', 'emittedClrUnits', 'promotedTypedRegions',
            'exportedCmdletUnits', 'hostedRegions', 'retainedSourceUnits', 'semanticFallbackUnits',
            'shapingFallbackUnits', 'runtimeRoutedUnits', 'omittedUnits', 'rejectedUnits')) {
        $counts[$metric] = [int](($Modules | ForEach-Object { $_.counts.$metric } | Measure-Object -Sum).Sum)
    }
    $counts.cleanTargetCompleteProgramPassPercentage = 100.0
    $counts.emittedClrUnitPercentage = if ($counts.analyzedUnits) {
        [Math]::Round(100.0 * $counts.emittedClrUnits / $counts.analyzedUnits, 2)
    } else { 0.0 }
    $Baseline.recordedOn = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-dd')
    $Baseline.hybrid = $counts
    $stagingPath = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Write-Utf8Json -Path $stagingPath -Value $Baseline
        [IO.File]::Move($stagingPath, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $stagingPath) { Remove-Item -LiteralPath $stagingPath }
    }
}
