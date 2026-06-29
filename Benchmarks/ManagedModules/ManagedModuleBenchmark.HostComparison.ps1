function ConvertTo-ManagedBenchmarkDouble {
    param([object] $Value)

    if ($null -eq $Value) {
        return 0.0
    }

    $text = ([string] $Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return 0.0
    }

    $parsed = 0.0
    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed)) {
        return $parsed
    }

    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::CurrentCulture, [ref] $parsed)) {
        return $parsed
    }

    0.0
}

function New-ManagedHostComparison {
    param(
        [object[]] $Rows,
        [string] $BaselineHost = 'PowerShell7',
        [string] $ComparisonHost = 'WindowsPowerShell',
        [string] $EngineName = 'Managed'
    )

    foreach ($group in @($Rows | Group-Object Suite, Scenario, ModuleName, Operation)) {
        $baseline = @($group.Group | Where-Object { $_.Host -eq $BaselineHost } | Select-Object -First 1)
        $comparison = @($group.Group | Where-Object { $_.Host -eq $ComparisonHost } | Select-Object -First 1)
        $baselineMs = if ($baseline.Count) { ConvertTo-ManagedBenchmarkDouble -Value $baseline[0].ManagedMs } else { 0.0 }
        $comparisonMs = if ($comparison.Count) { ConvertTo-ManagedBenchmarkDouble -Value $comparison[0].ManagedMs } else { 0.0 }
        $status = if ($baselineMs -le 0) {
            'MissingBaseline'
        } elseif ($comparisonMs -le 0) {
            'MissingComparison'
        } else {
            'Compared'
        }

        $ratio = if ($baselineMs -gt 0 -and $comparisonMs -gt 0) {
            [math]::Round($comparisonMs / $baselineMs, 2)
        } else {
            0.0
        }

        [pscustomobject]@{
            Suite = if ($group.Group[0].PSObject.Properties['Suite']) { [string] $group.Group[0].Suite } else { '' }
            Scenario = [string] $group.Group[0].Scenario
            BenchmarkRole = if ($group.Group[0].PSObject.Properties['BenchmarkRole']) { [string] $group.Group[0].BenchmarkRole } else { '' }
            ComparisonScope = if ($group.Group[0].PSObject.Properties['ComparisonScope']) { [string] $group.Group[0].ComparisonScope } else { '' }
            BenchmarkInterpretation = if ($group.Group[0].PSObject.Properties['BenchmarkInterpretation']) { [string] $group.Group[0].BenchmarkInterpretation } else { '' }
            ModuleName = if ($group.Group[0].PSObject.Properties['ModuleName']) { [string] $group.Group[0].ModuleName } else { '' }
            Operation = [string] $group.Group[0].Operation
            Engine = $EngineName
            Status = $status
            BaselineHost = $BaselineHost
            BaselineMs = [math]::Round($baselineMs, 2)
            ComparisonHost = $ComparisonHost
            ComparisonMs = [math]::Round($comparisonMs, 2)
            DeltaMs = if ($status -eq 'Compared') { [math]::Round($comparisonMs - $baselineMs, 2) } else { 0.0 }
            ComparisonVsBaseline = if ($status -eq 'Compared') { ('{0}x' -f $ratio.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)) } else { '' }
            FasterHost = if ($status -eq 'Compared') {
                if ($comparisonMs -lt $baselineMs) { $ComparisonHost } elseif ($baselineMs -lt $comparisonMs) { $BaselineHost } else { 'Tie' }
            } else {
                ''
            }
            BaselineRunPath = if ($baseline.Count -and $baseline[0].PSObject.Properties['RunPath']) { [string] $baseline[0].RunPath } else { '' }
            ComparisonRunPath = if ($comparison.Count -and $comparison[0].PSObject.Properties['RunPath']) { [string] $comparison[0].RunPath } else { '' }
        }
    }
}
