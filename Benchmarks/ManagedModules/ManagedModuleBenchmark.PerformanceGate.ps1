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

function ConvertFrom-ManagedGateInteger {
    param([object] $Value)

    if ($null -eq $Value) {
        return 0
    }

    $text = ([string] $Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return 0
    }

    $parsed = 0
    if ([int]::TryParse($text, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed)) {
        return $parsed
    }

    0
}

function Get-ManagedPerformanceGateViolation {
    param(
        [object[]] $Rows,
        [int] $MaxRank,
        [double] $MaxVsFastest,
        [int] $MinAuthenticodeCheckedFiles = 0
    )

    if (($MaxRank -le 0) -and ($MaxVsFastest -le 0) -and ($MinAuthenticodeCheckedFiles -le 0)) {
        return @()
    }

    foreach ($row in @($Rows)) {
        $managedRank = [int] $row.ManagedRank
        $checkedFiles = if ($row.PSObject.Properties['ManagedAuthenticodeCheckedFiles']) {
            ConvertFrom-ManagedGateInteger -Value $row.ManagedAuthenticodeCheckedFiles
        } else {
            0
        }

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
                ManagedAuthenticodeCheckedFiles = $checkedFiles
                Reason = 'managed did not produce a successful benchmark result'
            }
            continue
        }

        $ratio = ConvertFrom-ManagedVsFastestRatio -Value $row.ManagedVsFastest
        $rankFailed = $MaxRank -gt 0 -and $managedRank -gt $MaxRank
        $ratioFailed = $MaxVsFastest -gt 0 -and $ratio -gt $MaxVsFastest
        $authenticodeFilesFailed = $MinAuthenticodeCheckedFiles -gt 0 -and $checkedFiles -lt $MinAuthenticodeCheckedFiles
        if (-not ($rankFailed -or $ratioFailed -or $authenticodeFilesFailed)) {
            continue
        }

        $reasonParts = @()
        if ($rankFailed) {
            $reasonParts += "managed rank $managedRank exceeds allowed rank $MaxRank"
        }
        if ($ratioFailed) {
            $reasonParts += ("managed ratio {0}x exceeds allowed ratio {1}x" -f $ratio.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture), $MaxVsFastest.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture))
        }
        if ($authenticodeFilesFailed) {
            $reasonParts += "managed checked $checkedFiles Authenticode file(s), expected at least $MinAuthenticodeCheckedFiles"
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
            ManagedAuthenticodeCheckedFiles = $checkedFiles
            Reason = $reasonParts -join '; '
        }
    }
}

function Get-ManagedPerformanceGateViolationForSuite {
    param(
        [object[]] $Rows,
        [int] $MaxRank,
        [double] $MaxVsFastest,
        [int] $MinAuthenticodeCheckedFiles = 0,
        [switch] $UseScenarioGates
    )

    if ($MaxRank -gt 0 -or $MaxVsFastest -gt 0 -or $MinAuthenticodeCheckedFiles -gt 0) {
        return @(Get-ManagedPerformanceGateViolation -Rows @($Rows) -MaxRank $MaxRank -MaxVsFastest $MaxVsFastest -MinAuthenticodeCheckedFiles $MinAuthenticodeCheckedFiles)
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

        $scenarioMinAuthenticodeCheckedFiles = 0
        if ($row.PSObject.Properties['GateManagedMinAuthenticodeCheckedFiles']) {
            $scenarioMinAuthenticodeCheckedFiles = ConvertFrom-ManagedGateInteger -Value $row.GateManagedMinAuthenticodeCheckedFiles
        }

        Get-ManagedPerformanceGateViolation -Rows @($row) -MaxRank $scenarioMaxRank -MaxVsFastest $scenarioMaxVsFastest -MinAuthenticodeCheckedFiles $scenarioMinAuthenticodeCheckedFiles
    }
}
