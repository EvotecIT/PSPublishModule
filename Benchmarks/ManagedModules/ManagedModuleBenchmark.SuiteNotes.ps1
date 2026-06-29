function Format-ManagedBenchmarkMarkdownValue {
    param(
        [object] $Value
    )

    if ($null -eq $Value) {
        return ''
    }

    $text = [string] $Value
    $text.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Get-ManagedBenchmarkProperty {
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

    if ($property.Value -is [array]) {
        return ($property.Value -join ', ')
    }

    [string] $property.Value
}

function Add-ManagedBenchmarkMarkdownTable {
    param(
        [Collections.Generic.List[string]] $Lines,
        [object[]] $Rows,
        [string[]] $Columns
    )

    if (-not $Rows -or $Rows.Count -eq 0) {
        $Lines.Add('_No rows._')
        return
    }

    $Lines.Add('| ' + ($Columns -join ' | ') + ' |')
    $Lines.Add('| ' + (($Columns | ForEach-Object { '---' }) -join ' | ') + ' |')

    foreach ($row in $Rows) {
        $values = foreach ($column in $Columns) {
            Format-ManagedBenchmarkMarkdownValue (Get-ManagedBenchmarkProperty -InputObject $row -Name $column)
        }

        $Lines.Add('| ' + ($values -join ' | ') + ' |')
    }
}

function Write-ManagedBenchmarkSuiteNotes {
    param(
        [object[]] $Scenarios,
        [object[]] $SummaryRows,
        [object[]] $HostRows,
        [object[]] $GateViolations,
        [object[]] $HostGateViolations,
        [string] $Path,
        [datetime] $GeneratedAt = (Get-Date)
    )

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# Managed Module Benchmark Suite Notes')
    $lines.Add('')
    $lines.Add("Generated: $($GeneratedAt.ToString('u'))")
    $lines.Add('')
    $lines.Add('Read this first: `Scoreboard` rows are provider comparisons. `Diagnostic` rows are managed-only or managed-focused evidence and must not be read as provider races.')
    $lines.Add('')

    $scoreboards = @($Scenarios | Where-Object { (Get-ManagedBenchmarkProperty -InputObject $_ -Name 'BenchmarkRole') -eq 'Scoreboard' })
    $diagnostics = @($Scenarios | Where-Object { (Get-ManagedBenchmarkProperty -InputObject $_ -Name 'BenchmarkRole') -eq 'Diagnostic' })

    $lines.Add('## Scoreboards')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $scoreboards -Columns @('Suite', 'Name', 'ComparisonScope', 'Operations', 'Engines', 'BenchmarkInterpretation')
    $lines.Add('')

    $lines.Add('## Diagnostics')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $diagnostics -Columns @('Suite', 'Name', 'ComparisonScope', 'Operations', 'Engines', 'BenchmarkInterpretation')
    $lines.Add('')

    $lines.Add('## Results')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $SummaryRows -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Host', 'Operation', 'FastestEngine', 'FastestMs', 'ManagedMs', 'ManagedRank', 'ManagedVsFastest')
    $lines.Add('')

    $lines.Add('## Hosts')
    $lines.Add('')
    Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $HostRows -Columns @('Host', 'Status', 'Executable', 'Reason')
    $lines.Add('')

    if ($GateViolations -and $GateViolations.Count -gt 0) {
        $lines.Add('## Performance Gate Violations')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $GateViolations -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Host', 'Operation', 'Reason', 'BenchmarkInterpretation')
        $lines.Add('')
    }

    if ($HostGateViolations -and $HostGateViolations.Count -gt 0) {
        $lines.Add('## Host Gate Violations')
        $lines.Add('')
        Add-ManagedBenchmarkMarkdownTable -Lines $lines -Rows $HostGateViolations -Columns @('BenchmarkRole', 'Suite', 'Scenario', 'Operation', 'Reason', 'BenchmarkInterpretation')
        $lines.Add('')
    }

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -Path $directory -ItemType Directory -Force | Out-Null
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}
