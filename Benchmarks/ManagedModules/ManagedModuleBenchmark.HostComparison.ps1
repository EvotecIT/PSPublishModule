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

function Format-ManagedHostComparisonRatio {
    param(
        [double] $BaselineMs,
        [double] $ComparisonMs
    )

    if ($BaselineMs -le 0 -or $ComparisonMs -le 0) {
        return ''
    }

    $ratio = [math]::Round($ComparisonMs / $BaselineMs, 2)
    '{0}x' -f $ratio.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-ManagedHostComparisonWinner {
    param(
        [double] $BaselineMs,
        [double] $ComparisonMs,
        [string] $BaselineHost,
        [string] $ComparisonHost
    )

    if ($BaselineMs -le 0 -or $ComparisonMs -le 0) {
        return ''
    }

    if ($ComparisonMs -lt $BaselineMs) {
        return $ComparisonHost
    }

    if ($BaselineMs -lt $ComparisonMs) {
        return $BaselineHost
    }

    'Tie'
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
        $baselineFirstMs = if ($baseline.Count -and $baseline[0].PSObject.Properties['ManagedFirstMs']) { ConvertTo-ManagedBenchmarkDouble -Value $baseline[0].ManagedFirstMs } else { 0.0 }
        $comparisonFirstMs = if ($comparison.Count -and $comparison[0].PSObject.Properties['ManagedFirstMs']) { ConvertTo-ManagedBenchmarkDouble -Value $comparison[0].ManagedFirstMs } else { 0.0 }
        $baselineLastMs = if ($baseline.Count -and $baseline[0].PSObject.Properties['ManagedLastMs']) { ConvertTo-ManagedBenchmarkDouble -Value $baseline[0].ManagedLastMs } else { 0.0 }
        $comparisonLastMs = if ($comparison.Count -and $comparison[0].PSObject.Properties['ManagedLastMs']) { ConvertTo-ManagedBenchmarkDouble -Value $comparison[0].ManagedLastMs } else { 0.0 }
        $status = if ($baselineMs -le 0) {
            'MissingBaseline'
        } elseif ($comparisonMs -le 0) {
            'MissingComparison'
        } else {
            'Compared'
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
            ComparisonVsBaseline = if ($status -eq 'Compared') { Format-ManagedHostComparisonRatio -BaselineMs $baselineMs -ComparisonMs $comparisonMs } else { '' }
            FasterHost = if ($status -eq 'Compared') { Get-ManagedHostComparisonWinner -BaselineMs $baselineMs -ComparisonMs $comparisonMs -BaselineHost $BaselineHost -ComparisonHost $ComparisonHost } else { '' }
            BaselineFirstMs = [math]::Round($baselineFirstMs, 2)
            ComparisonFirstMs = [math]::Round($comparisonFirstMs, 2)
            FirstDeltaMs = if ($status -eq 'Compared' -and $baselineFirstMs -gt 0 -and $comparisonFirstMs -gt 0) { [math]::Round($comparisonFirstMs - $baselineFirstMs, 2) } else { 0.0 }
            FirstComparisonVsBaseline = if ($status -eq 'Compared') { Format-ManagedHostComparisonRatio -BaselineMs $baselineFirstMs -ComparisonMs $comparisonFirstMs } else { '' }
            FasterFirstHost = if ($status -eq 'Compared') { Get-ManagedHostComparisonWinner -BaselineMs $baselineFirstMs -ComparisonMs $comparisonFirstMs -BaselineHost $BaselineHost -ComparisonHost $ComparisonHost } else { '' }
            BaselineLastMs = [math]::Round($baselineLastMs, 2)
            ComparisonLastMs = [math]::Round($comparisonLastMs, 2)
            LastDeltaMs = if ($status -eq 'Compared' -and $baselineLastMs -gt 0 -and $comparisonLastMs -gt 0) { [math]::Round($comparisonLastMs - $baselineLastMs, 2) } else { 0.0 }
            LastComparisonVsBaseline = if ($status -eq 'Compared') { Format-ManagedHostComparisonRatio -BaselineMs $baselineLastMs -ComparisonMs $comparisonLastMs } else { '' }
            FasterLastHost = if ($status -eq 'Compared') { Get-ManagedHostComparisonWinner -BaselineMs $baselineLastMs -ComparisonMs $comparisonLastMs -BaselineHost $BaselineHost -ComparisonHost $ComparisonHost } else { '' }
            BaselineRunPath = if ($baseline.Count -and $baseline[0].PSObject.Properties['RunPath']) { [string] $baseline[0].RunPath } else { '' }
            ComparisonRunPath = if ($comparison.Count -and $comparison[0].PSObject.Properties['RunPath']) { [string] $comparison[0].RunPath } else { '' }
        }
    }
}

function ConvertFrom-ManagedHostComparisonRatio {
    param([object] $Value)

    if ($null -eq $Value) {
        return 0.0
    }

    $text = ([string] $Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return 0.0
    }

    if ($text.EndsWith('x', [StringComparison]::OrdinalIgnoreCase)) {
        $text = $text.Substring(0, $text.Length - 1)
    }

    ConvertTo-ManagedBenchmarkDouble -Value $text
}

function Get-ManagedHostComparisonGateViolation {
    param(
        [object[]] $Rows,
        [double] $MaxComparisonVsBaseline = 0
    )

    if ($MaxComparisonVsBaseline -le 0) {
        return @()
    }

    foreach ($row in @($Rows)) {
        $status = if ($row.PSObject.Properties['Status']) { [string] $row.Status } else { '' }
        $ratio = ConvertFrom-ManagedHostComparisonRatio -Value $row.ComparisonVsBaseline
        $missingHost = -not [string]::Equals($status, 'Compared', [StringComparison]::OrdinalIgnoreCase)
        $ratioFailed = -not $missingHost -and $ratio -gt $MaxComparisonVsBaseline

        if (-not ($missingHost -or $ratioFailed)) {
            continue
        }

        [pscustomobject]@{
            Suite = if ($row.PSObject.Properties['Suite']) { [string] $row.Suite } else { '' }
            Scenario = if ($row.PSObject.Properties['Scenario']) { [string] $row.Scenario } else { '' }
            BenchmarkRole = if ($row.PSObject.Properties['BenchmarkRole']) { [string] $row.BenchmarkRole } else { '' }
            ComparisonScope = if ($row.PSObject.Properties['ComparisonScope']) { [string] $row.ComparisonScope } else { '' }
            BenchmarkInterpretation = if ($row.PSObject.Properties['BenchmarkInterpretation']) { [string] $row.BenchmarkInterpretation } else { '' }
            ModuleName = if ($row.PSObject.Properties['ModuleName']) { [string] $row.ModuleName } else { '' }
            Operation = if ($row.PSObject.Properties['Operation']) { [string] $row.Operation } else { '' }
            Engine = if ($row.PSObject.Properties['Engine']) { [string] $row.Engine } else { '' }
            Status = $status
            BaselineHost = if ($row.PSObject.Properties['BaselineHost']) { [string] $row.BaselineHost } else { '' }
            BaselineMs = if ($row.PSObject.Properties['BaselineMs']) { [double] $row.BaselineMs } else { 0.0 }
            ComparisonHost = if ($row.PSObject.Properties['ComparisonHost']) { [string] $row.ComparisonHost } else { '' }
            ComparisonMs = if ($row.PSObject.Properties['ComparisonMs']) { [double] $row.ComparisonMs } else { 0.0 }
            ComparisonVsBaseline = if ($row.PSObject.Properties['ComparisonVsBaseline']) { [string] $row.ComparisonVsBaseline } else { '' }
            MaxComparisonVsBaseline = $MaxComparisonVsBaseline
            Reason = if ($missingHost) {
                "host comparison status is $status"
            } else {
                ("comparison ratio {0}x exceeds allowed ratio {1}x" -f $ratio.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture), $MaxComparisonVsBaseline.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture))
            }
        }
    }
}
