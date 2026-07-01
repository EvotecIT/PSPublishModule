#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultPath,

    [string] $ReadmePath = (Join-Path $PSScriptRoot '..\..\README.MD'),

    [ValidateSet('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')]
    [string[]] $Engine = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-BenchmarkDouble {
    param([object] $Value)

    if ($null -eq $Value) {
        return 0
    }

    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return 0
    }

    if ($text.Contains(',') -and -not $text.Contains('.')) {
        $text = $text.Replace(',', '.')
    }

    [double]::Parse($text, [Globalization.CultureInfo]::InvariantCulture)
}

function Format-BenchmarkCell {
    param(
        [object] $Row,
        [double] $ManagedSeconds
    )

    if ($null -eq $Row) {
        return 'Not run'
    }

    if ($Row.Status -ne 'Succeeded') {
        return $Row.Status
    }

    $seconds = ConvertTo-BenchmarkDouble -Value $Row.Seconds
    if ($ManagedSeconds -le 0) {
        return ('{0}s' -f $seconds.ToString('N2', [Globalization.CultureInfo]::InvariantCulture))
    }

    '{0}x ({1}s)' -f
        ($seconds / $ManagedSeconds).ToString('N2', [Globalization.CultureInfo]::InvariantCulture),
        $seconds.ToString('N2', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-ManagedResultText {
    param(
        [object[]] $Rows,
        [object] $ManagedRow
    )

    if ($null -eq $ManagedRow -or $ManagedRow.Status -ne 'Succeeded') {
        return 'Managed did not complete'
    }

    $successful = @($Rows | Where-Object { $_.Status -eq 'Succeeded' })
    if ($successful.Count -eq 1) {
        return 'Managed only successful'
    }

    $fastest = $successful | Sort-Object { ConvertTo-BenchmarkDouble -Value $_.Seconds } | Select-Object -First 1
    if ($fastest.Engine -eq 'Managed') {
        return 'Managed fastest'
    }

    'Managed slower than {0}' -f $fastest.Engine
}

function Get-BenchmarkMedian {
    param([double[]] $Values)

    if ($Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) {
        return $sorted[$middle]
    }

    ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function ConvertTo-BenchmarkSummaryRows {
    param([object[]] $Rows)

    foreach ($group in ($Rows | Group-Object ScenarioLabel, Host, Operation, Engine)) {
        $items = @($group.Group)
        $first = $items | Select-Object -First 1
        $successful = @($items | Where-Object { $_.Status -eq 'Succeeded' })
        if ($successful.Count -gt 0) {
            $seconds = @($successful | ForEach-Object { ConvertTo-BenchmarkDouble -Value $_.Seconds })
            $median = Get-BenchmarkMedian -Values $seconds
            $milliseconds = [Math]::Round($median * 1000, 2)
            $status = 'Succeeded'
            $reason = ''
        } else {
            $statusGroup = $items |
                Group-Object Status |
                Sort-Object Count -Descending |
                Select-Object -First 1
            $representative = $items |
                Where-Object { $_.Status -eq $statusGroup.Name } |
                Select-Object -First 1
            $median = 0
            $milliseconds = 0
            $status = $statusGroup.Name
            $reason = $representative.Reason
        }

        [pscustomobject]@{
            TimestampUtc = $first.TimestampUtc
            Host = $first.Host
            Scenario = $first.Scenario
            ScenarioLabel = $first.ScenarioLabel
            ModuleName = $first.ModuleName
            Version = $first.Version
            Operation = $first.Operation
            Engine = $first.Engine
            Iteration = 'median'
            Status = $status
            Milliseconds = $milliseconds.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
            Seconds = $median.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)
            Reason = $reason
        }
    }
}

function ConvertTo-BenchmarkMarkdown {
    param(
        [object[]] $Rows,
        [string[]] $SelectedEngine
    )

    $Rows = @(ConvertTo-BenchmarkSummaryRows -Rows $Rows)
    $knownEngines = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
    $presentEngines = @($Rows | ForEach-Object { $_.Engine } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $engines = if ($SelectedEngine.Count -gt 0) {
        @($SelectedEngine)
    } else {
        @($knownEngines | Where-Object { $presentEngines -contains $_ })
    }

    $extraEngines = @($presentEngines | Where-Object { $engines -notcontains $_ } | Sort-Object)
    if ($extraEngines.Count -gt 0) {
        $engines += $extraEngines
    }

    if ($engines.Count -eq 0) {
        throw 'No benchmark engines were found in the result CSV.'
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add(('| Scenario | Host | Operation | {0} | Result |' -f ($engines -join ' | '))) | Out-Null
    $lines.Add(('| --- | --- | --- | {0} | --- |' -f (@($engines | ForEach-Object { '---' }) -join ' | '))) | Out-Null

    $groups = $Rows |
        Group-Object ScenarioLabel, Host, Operation |
        Sort-Object {
            $parts = $_.Name -split ', '
            '{0}|{1}|{2}' -f $parts[0], $parts[1], $parts[2]
        }

    foreach ($group in $groups) {
        $items = @($group.Group)
        $managed = $items | Where-Object { $_.Engine -eq 'Managed' } | Select-Object -First 1
        $managedSeconds = if ($managed -and $managed.Status -eq 'Succeeded') { ConvertTo-BenchmarkDouble -Value $managed.Seconds } else { 0 }
        $first = $items | Select-Object -First 1
        $cells = foreach ($engine in $engines) {
            $row = $items | Where-Object { $_.Engine -eq $engine } | Select-Object -First 1
            Format-BenchmarkCell -Row $row -ManagedSeconds $managedSeconds
        }
        $result = Get-ManagedResultText -Rows $items -ManagedRow $managed
        $lines.Add(('| {0} | {1} | {2} | {3} | {4} |' -f
            $first.ScenarioLabel,
            $first.Host,
            $first.Operation,
            ($cells -join ' | '),
            $result)) | Out-Null
    }

    $lines -join "`n"
}

$rows = @(Import-Csv -LiteralPath $ResultPath)
if ($rows.Count -eq 0) {
    throw "No benchmark rows were found in '$ResultPath'."
}

$table = ConvertTo-BenchmarkMarkdown -Rows $rows -SelectedEngine $Engine
$readme = Get-Content -LiteralPath $ReadmePath -Raw
$start = '<!-- managed-module-benchmark-table:start -->'
$end = '<!-- managed-module-benchmark-table:end -->'
$pattern = [regex]::Escape($start) + '(?s).*?' + [regex]::Escape($end)
$replacement = $start + "`n" + $table + "`n" + $end

if ($readme -notmatch $pattern) {
    throw "README marker block was not found in '$ReadmePath'."
}

[System.IO.File]::WriteAllText(
    (Resolve-Path -LiteralPath $ReadmePath).Path,
    [regex]::Replace($readme, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement }),
    [System.Text.UTF8Encoding]::new($false))

$table
