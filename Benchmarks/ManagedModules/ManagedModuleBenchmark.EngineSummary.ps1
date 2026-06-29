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
            FirstIteration = $row.FirstIteration
            LastIteration = $row.LastIteration
            FirstMs = $row.FirstMs
            LastMs = $row.LastMs
            MinMs = $row.MinMs
            MaxMs = $row.MaxMs
            MedianOutputFileCount = $row.MedianOutputFileCount
            MedianOutputBytes = $row.MedianOutputBytes
            RunPath = $RunPath
        })
    }
}
