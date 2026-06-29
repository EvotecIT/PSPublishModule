function Add-ManagedBenchmarkEngineRows {
    param(
        [Collections.Generic.List[object]] $Rows,
        [object] $Scenario,
        [string] $HostLabel,
        [string] $RunPath
    )

    $summaryPath = Join-Path $RunPath 'managed-module-summary.csv'
    if (-not (Test-Path -LiteralPath $summaryPath)) {
        return
    }

    foreach ($row in (Import-Csv -LiteralPath $summaryPath)) {
        $medianMs = ConvertTo-ManagedBenchmarkDouble -Value $row.MedianMs
        $outputBytes = ConvertTo-ManagedBenchmarkDouble -Value $row.MedianOutputBytes
        $outputFiles = ConvertTo-ManagedBenchmarkDouble -Value $row.MedianOutputFileCount
        $outputMbRaw = $outputBytes / 1MB
        $outputMb = [math]::Round($outputMbRaw, 2)
        $elapsedSeconds = if ($medianMs -gt 0) { $medianMs / 1000 } else { 0.0 }
        $outputMbPerSecond = if ($elapsedSeconds -gt 0 -and $outputMbRaw -gt 0) { [math]::Round($outputMbRaw / $elapsedSeconds, 2) } else { 0.0 }
        $outputFilesPerSecond = if ($elapsedSeconds -gt 0 -and $outputFiles -gt 0) { [math]::Round($outputFiles / $elapsedSeconds, 2) } else { 0.0 }

        $Rows.Add([pscustomobject]@{
            Suite = $Scenario.Suite
            Scenario = $Scenario.Name
            BenchmarkRole = $Scenario.BenchmarkRole
            ComparisonScope = $Scenario.ComparisonScope
            BenchmarkInterpretation = $Scenario.BenchmarkInterpretation
            ModuleName = $Scenario.ModuleName
            Engines = (Get-ScenarioEngines -Scenario $Scenario) -join ','
            Host = $HostLabel
            Operation = $row.Operation
            Engine = $row.Engine
            Runs = $row.Runs
            Succeeded = $row.Succeeded
            Failed = $row.Failed
            Skipped = $row.Skipped
            MedianMs = $row.MedianMs
            WarmRuns = $row.WarmRuns
            WarmMedianMs = $row.WarmMedianMs
            WarmMinMs = $row.WarmMinMs
            WarmMaxMs = $row.WarmMaxMs
            FirstIteration = $row.FirstIteration
            LastIteration = $row.LastIteration
            FirstMs = $row.FirstMs
            LastMs = $row.LastMs
            MinMs = $row.MinMs
            MaxMs = $row.MaxMs
            MedianOutputFileCount = $row.MedianOutputFileCount
            MedianOutputBytes = $row.MedianOutputBytes
            MedianOutputMB = $outputMb
            MedianOutputMBPerSecond = $outputMbPerSecond
            MedianOutputFilesPerSecond = $outputFilesPerSecond
            RunPath = $RunPath
        })
    }
}
