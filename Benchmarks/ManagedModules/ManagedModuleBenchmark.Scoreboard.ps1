function ConvertTo-ManagedBenchmarkScoreboardDouble {
    param([object] $Value)

    if ($null -eq $Value) {
        return 0.0
    }

    $text = ([string] $Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return 0.0
    }

    $result = 0.0
    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref] $result)) {
        return $result
    }

    0.0
}

function Get-ManagedBenchmarkScoreboardProperty {
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

    [string] $property.Value
}

function Format-ManagedBenchmarkScoreboardNumber {
    param(
        [double] $Value,
        [int] $Digits = 2
    )

    [math]::Round($Value, $Digits).ToString("F$Digits", [Globalization.CultureInfo]::InvariantCulture)
}

function Format-ManagedBenchmarkScoreboardRatio {
    param([double] $Value)

    if ($Value -le 0) {
        return ''
    }

    if ([math]::Abs($Value - 1.0) -lt 0.005) {
        return '1x'
    }

    "$(Format-ManagedBenchmarkScoreboardNumber -Value $Value -Digits 2)x"
}

function Get-ManagedBenchmarkScoreboardStatus {
    param([object] $Row)

    if ($null -eq $Row) {
        return ''
    }

    $succeeded = [int](ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $Row -Name 'Succeeded'))
    $failed = [int](ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $Row -Name 'Failed'))
    $skipped = [int](ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $Row -Name 'Skipped'))
    if ($succeeded -gt 0) {
        return 'Succeeded'
    }
    if ($failed -gt 0) {
        return 'Failed'
    }
    if ($skipped -gt 0) {
        return 'Skipped'
    }

    ''
}

function Get-ManagedBenchmarkScoreboardEngineCell {
    param(
        [object] $Row,
        [double] $FastestMilliseconds
    )

    $status = Get-ManagedBenchmarkScoreboardStatus -Row $Row
    if ($status -eq 'Skipped' -or $status -eq 'Failed') {
        return $status
    }

    $median = if ($Row) {
        ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $Row -Name 'MedianMs')
    } else {
        0.0
    }

    if ($median -le 0 -or $FastestMilliseconds -le 0) {
        return ''
    }

    '{0} ms ({1})' -f (Format-ManagedBenchmarkScoreboardNumber -Value $median), (Format-ManagedBenchmarkScoreboardRatio -Value ($median / $FastestMilliseconds))
}

function New-ManagedBenchmarkScoreboard {
    param(
        [object[]] $EngineRows
    )

    $engines = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
    $groups = @($EngineRows | Group-Object -Property BenchmarkRole, Suite, Scenario, Host, Operation)
    foreach ($group in $groups) {
        $rows = @($group.Group)
        if ($rows.Count -eq 0) {
            continue
        }

        $first = $rows[0]
        $byEngine = @{}
        foreach ($row in $rows) {
            $engine = Get-ManagedBenchmarkScoreboardProperty -InputObject $row -Name 'Engine'
            if (-not [string]::IsNullOrWhiteSpace($engine) -and -not $byEngine.ContainsKey($engine)) {
                $byEngine[$engine] = $row
            }
        }

        $successful = @($rows | Where-Object {
                (Get-ManagedBenchmarkScoreboardStatus -Row $_) -eq 'Succeeded' -and
                (ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $_ -Name 'MedianMs')) -gt 0
            } | Sort-Object @{ Expression = { ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $_ -Name 'MedianMs') } })

        $fastestRow = if ($successful.Count -gt 0) { $successful[0] } else { $null }
        $fastestMs = if ($fastestRow) {
            ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $fastestRow -Name 'MedianMs')
        } else {
            0.0
        }
        $fastestEngine = if ($fastestRow) { Get-ManagedBenchmarkScoreboardProperty -InputObject $fastestRow -Name 'Engine' } else { '' }
        $managedRow = if ($byEngine.ContainsKey('Managed')) { $byEngine['Managed'] } else { $null }
        $managedMs = if ($managedRow) {
            ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $managedRow -Name 'MedianMs')
        } else {
            0.0
        }
        $managedRank = ''
        if ($managedRow -and $successful.Count -gt 0) {
            for ($index = 0; $index -lt $successful.Count; $index++) {
                if ([object]::ReferenceEquals($successful[$index], $managedRow)) {
                    $managedRank = [string]($index + 1)
                    break
                }
            }
        }

        $values = [ordered]@{
            BenchmarkRole = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'BenchmarkRole'
            Suite = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'Suite'
            Scenario = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'Scenario'
            ComparisonScope = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'ComparisonScope'
            BenchmarkInterpretation = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'BenchmarkInterpretation'
            ModuleName = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'ModuleName'
            Host = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'Host'
            Operation = Get-ManagedBenchmarkScoreboardProperty -InputObject $first -Name 'Operation'
            FastestEngine = $fastestEngine
            FastestMs = if ($fastestMs -gt 0) { Format-ManagedBenchmarkScoreboardNumber -Value $fastestMs } else { '' }
            ManagedRank = $managedRank
            ManagedVsFastest = if ($managedMs -gt 0 -and $fastestMs -gt 0) { Format-ManagedBenchmarkScoreboardRatio -Value ($managedMs / $fastestMs) } else { '' }
            SuccessfulEngines = (@($successful | ForEach-Object { Get-ManagedBenchmarkScoreboardProperty -InputObject $_ -Name 'Engine' }) -join ',')
            FailedEngines = (@($rows | Where-Object { (Get-ManagedBenchmarkScoreboardStatus -Row $_) -eq 'Failed' } | ForEach-Object { Get-ManagedBenchmarkScoreboardProperty -InputObject $_ -Name 'Engine' }) -join ',')
            SkippedEngines = (@($rows | Where-Object { (Get-ManagedBenchmarkScoreboardStatus -Row $_) -eq 'Skipped' } | ForEach-Object { Get-ManagedBenchmarkScoreboardProperty -InputObject $_ -Name 'Engine' }) -join ',')
        }

        foreach ($engine in $engines) {
            $row = if ($byEngine.ContainsKey($engine)) { $byEngine[$engine] } else { $null }
            $median = if ($row) {
                ConvertTo-ManagedBenchmarkScoreboardDouble -Value (Get-ManagedBenchmarkScoreboardProperty -InputObject $row -Name 'MedianMs')
            } else {
                0.0
            }
            $values["${engine}Status"] = Get-ManagedBenchmarkScoreboardStatus -Row $row
            $values["${engine}Ms"] = if ($median -gt 0) { Format-ManagedBenchmarkScoreboardNumber -Value $median } else { '' }
            $values["${engine}VsFastest"] = if ($median -gt 0 -and $fastestMs -gt 0) { Format-ManagedBenchmarkScoreboardRatio -Value ($median / $fastestMs) } else { '' }
            $values[$engine] = Get-ManagedBenchmarkScoreboardEngineCell -Row $row -FastestMilliseconds $fastestMs
        }

        [pscustomobject]$values
    }
}
