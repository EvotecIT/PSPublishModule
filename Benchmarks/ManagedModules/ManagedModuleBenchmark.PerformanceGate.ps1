function ConvertFrom-ManagedVsFastestRatio {
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

    $parsed = 0.0
    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed)) {
        return $parsed
    }

    0.0
}

function Get-ManagedPerformanceGateViolation {
    param(
        [object[]] $Rows,
        [int] $MaxRank,
        [double] $MaxVsFastest
    )

    if (($MaxRank -le 0) -and ($MaxVsFastest -le 0)) {
        return @()
    }

    foreach ($row in @($Rows)) {
        $managedRank = [int] $row.ManagedRank
        if ($managedRank -le 0) {
            [pscustomobject]@{
                Suite = if ($row.PSObject.Properties['Suite']) { [string] $row.Suite } else { '' }
                Scenario = if ($row.PSObject.Properties['Scenario']) { [string] $row.Scenario } else { '' }
                Host = if ($row.PSObject.Properties['Host']) { [string] $row.Host } else { '' }
                BenchmarkRole = if ($row.PSObject.Properties['BenchmarkRole']) { [string] $row.BenchmarkRole } else { '' }
                ComparisonScope = if ($row.PSObject.Properties['ComparisonScope']) { [string] $row.ComparisonScope } else { '' }
                BenchmarkInterpretation = if ($row.PSObject.Properties['BenchmarkInterpretation']) { [string] $row.BenchmarkInterpretation } else { '' }
                Operation = [string] $row.Operation
                FastestEngine = [string] $row.FastestEngine
                FastestMs = [double] $row.FastestMs
                ManagedMs = [double] $row.ManagedMs
                ManagedRank = $managedRank
                ManagedVsFastest = [string] $row.ManagedVsFastest
                Reason = 'managed did not produce a successful benchmark result'
            }
            continue
        }

        $ratio = ConvertFrom-ManagedVsFastestRatio -Value $row.ManagedVsFastest
        $rankFailed = $MaxRank -gt 0 -and $managedRank -gt $MaxRank
        $ratioFailed = $MaxVsFastest -gt 0 -and $ratio -gt $MaxVsFastest
        if (-not ($rankFailed -or $ratioFailed)) {
            continue
        }

        $reasonParts = @()
        if ($rankFailed) {
            $reasonParts += "managed rank $managedRank exceeds allowed rank $MaxRank"
        }
        if ($ratioFailed) {
            $reasonParts += ("managed ratio {0}x exceeds allowed ratio {1}x" -f $ratio.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture), $MaxVsFastest.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture))
        }

        [pscustomobject]@{
            Suite = if ($row.PSObject.Properties['Suite']) { [string] $row.Suite } else { '' }
            Scenario = if ($row.PSObject.Properties['Scenario']) { [string] $row.Scenario } else { '' }
            Host = if ($row.PSObject.Properties['Host']) { [string] $row.Host } else { '' }
            BenchmarkRole = if ($row.PSObject.Properties['BenchmarkRole']) { [string] $row.BenchmarkRole } else { '' }
            ComparisonScope = if ($row.PSObject.Properties['ComparisonScope']) { [string] $row.ComparisonScope } else { '' }
            BenchmarkInterpretation = if ($row.PSObject.Properties['BenchmarkInterpretation']) { [string] $row.BenchmarkInterpretation } else { '' }
            Operation = [string] $row.Operation
            FastestEngine = [string] $row.FastestEngine
            FastestMs = [double] $row.FastestMs
            ManagedMs = [double] $row.ManagedMs
            ManagedRank = $managedRank
            ManagedVsFastest = [string] $row.ManagedVsFastest
            Reason = $reasonParts -join '; '
        }
    }
}

function Get-ManagedPerformanceGateViolationForSuite {
    param(
        [object[]] $Rows,
        [int] $MaxRank,
        [double] $MaxVsFastest,
        [switch] $UseScenarioGates
    )

    if ($MaxRank -gt 0 -or $MaxVsFastest -gt 0) {
        return @(Get-ManagedPerformanceGateViolation -Rows @($Rows) -MaxRank $MaxRank -MaxVsFastest $MaxVsFastest)
    }

    if (-not $UseScenarioGates.IsPresent) {
        return @()
    }

    foreach ($row in @($Rows)) {
        $scenarioMaxRank = 0
        if ($row.PSObject.Properties['GateManagedMaxRank']) {
            $scenarioMaxRank = [int] $row.GateManagedMaxRank
        }

        $scenarioMaxVsFastest = 0.0
        if ($row.PSObject.Properties['GateManagedMaxVsFastest']) {
            $scenarioMaxVsFastest = [double] $row.GateManagedMaxVsFastest
        }

        Get-ManagedPerformanceGateViolation -Rows @($row) -MaxRank $scenarioMaxRank -MaxVsFastest $scenarioMaxVsFastest
    }
}
