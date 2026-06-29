#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultPath,

    [string] $ReadmePath = (Join-Path $PSScriptRoot '..\..\README.MD')
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

function ConvertTo-BenchmarkMarkdown {
    param([object[]] $Rows)

    $engines = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('| Scenario | Host | Operation | Managed | ModuleFast | PSResourceGet | PowerShellGet | Result |') | Out-Null
    $lines.Add('| --- | --- | --- | --- | --- | --- | --- | --- |') | Out-Null

    $groups = $Rows |
        Group-Object ScenarioLabel, Host, Operation |
        Sort-Object {
            $parts = $_.Name -split ', '
            '{0}|{1}|{2}' -f $parts[0], $parts[1], $parts[2]
        }

    foreach ($group in $groups) {
        $items = @($group.Group)
        $managed = $items | Where-Object { $_.Engine -eq 'Managed' } | Sort-Object Iteration | Select-Object -First 1
        $managedSeconds = if ($managed -and $managed.Status -eq 'Succeeded') { ConvertTo-BenchmarkDouble -Value $managed.Seconds } else { 0 }
        $first = $items | Select-Object -First 1
        $cells = foreach ($engine in $engines) {
            $row = $items | Where-Object { $_.Engine -eq $engine } | Sort-Object Iteration | Select-Object -First 1
            Format-BenchmarkCell -Row $row -ManagedSeconds $managedSeconds
        }
        $result = Get-ManagedResultText -Rows $items -ManagedRow $managed
        $lines.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f
            $first.ScenarioLabel,
            $first.Host,
            $first.Operation,
            $cells[0],
            $cells[1],
            $cells[2],
            $cells[3],
            $result)) | Out-Null
    }

    $lines -join [Environment]::NewLine
}

$rows = @(Import-Csv -LiteralPath $ResultPath)
if ($rows.Count -eq 0) {
    throw "No benchmark rows were found in '$ResultPath'."
}

$table = ConvertTo-BenchmarkMarkdown -Rows $rows
$readme = Get-Content -LiteralPath $ReadmePath -Raw
$start = '<!-- managed-module-benchmark-table:start -->'
$end = '<!-- managed-module-benchmark-table:end -->'
$pattern = [regex]::Escape($start) + '(?s).*?' + [regex]::Escape($end)
$replacement = $start + [Environment]::NewLine + $table + [Environment]::NewLine + $end

if ($readme -notmatch $pattern) {
    throw "README marker block was not found in '$ReadmePath'."
}

[System.IO.File]::WriteAllText(
    (Resolve-Path -LiteralPath $ReadmePath).Path,
    [regex]::Replace($readme, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement }),
    [System.Text.UTF8Encoding]::new($false))

$table
