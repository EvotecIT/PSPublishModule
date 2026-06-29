param(
    [string[]] $ScoreboardPath,

    [string] $BenchmarkRoot = $(if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { Join-Path (Get-Location).Path 'Ignore\Benchmarks\MM' } else { Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\MM' }),

    [string[]] $Suite,

    [string[]] $ScenarioName,

    [string[]] $HostName,

    [string[]] $Operation,

    [switch] $LatestPerScenario,

    [string] $OutputPath,

    [string] $ReadmePath,

    [string] $MarkerName = 'managed-module-benchmark-table'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ReadmeBenchmarkProperty {
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

function ConvertTo-ReadmeBenchmarkDouble {
    param([object] $Value)

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

function Format-ReadmeBenchmarkSeconds {
    param([double] $Milliseconds)

    if ($Milliseconds -le 0) {
        return ''
    }

    ($Milliseconds / 1000.0).ToString('0.00s', [Globalization.CultureInfo]::InvariantCulture)
}

function Format-ReadmeBenchmarkRatio {
    param([double] $Ratio)

    if ($Ratio -le 0) {
        return ''
    }

    $Ratio.ToString('0.00x', [Globalization.CultureInfo]::InvariantCulture)
}

function Format-ReadmeBenchmarkHost {
    param([string] $HostName)

    switch ($HostName) {
        'PowerShell7' { 'PowerShell 7'; break }
        'WindowsPowerShell' { 'Windows PowerShell 5.1'; break }
        default { $HostName }
    }
}

function Format-ReadmeBenchmarkCell {
    param(
        [object] $Row,
        [string] $Engine,
        [double] $ManagedMilliseconds
    )

    $status = Get-ReadmeBenchmarkProperty -InputObject $Row -Name "${Engine}Status"
    $operationName = Get-ReadmeBenchmarkProperty -InputObject $Row -Name 'Operation'
    $milliseconds = ConvertTo-ReadmeBenchmarkDouble -Value (Get-ReadmeBenchmarkProperty -InputObject $Row -Name "${Engine}Ms")

    if ($status -eq 'Succeeded' -and $milliseconds -gt 0) {
        $ratio = if ($Engine -eq 'Managed') { 1.0 } elseif ($ManagedMilliseconds -gt 0) { $milliseconds / $ManagedMilliseconds } else { 0.0 }
        return '{0} ({1})' -f (Format-ReadmeBenchmarkRatio -Ratio $ratio), (Format-ReadmeBenchmarkSeconds -Milliseconds $milliseconds)
    }

    if ($status -eq 'Failed') {
        return 'Failed'
    }

    if ($status -eq 'Skipped') {
        $notEquivalent =
            ($Engine -eq 'ModuleFast' -and $operationName -ne 'Install') -or
            ($Engine -eq 'PSResourceGet' -and $operationName -eq 'SaveForce')
        if ($notEquivalent) {
            return 'Not equivalent'
        }

        return 'Skipped'
    }

    'Not in this gate'
}

function Resolve-ReadmeBenchmarkScoreboardPaths {
    param([string[]] $InputPath)

    $paths = [Collections.Generic.List[string]]::new()
    foreach ($path in @($InputPath)) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        if (Test-Path -LiteralPath $path -PathType Container) {
            $candidate = Join-Path $path 'suite-scoreboard.csv'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $paths.Add((Resolve-Path -LiteralPath $candidate).Path)
            }
            continue
        }

        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $paths.Add((Resolve-Path -LiteralPath $path).Path)
            continue
        }

        throw "Benchmark scoreboard path '$path' does not exist."
    }

    if ($paths.Count -eq 0) {
        if (-not (Test-Path -LiteralPath $BenchmarkRoot -PathType Container)) {
            throw "Benchmark root '$BenchmarkRoot' does not exist. Pass -ScoreboardPath with one or more suite-scoreboard.csv files."
        }

        foreach ($file in Get-ChildItem -LiteralPath $BenchmarkRoot -Filter 'suite-scoreboard.csv' -Recurse -File) {
            $paths.Add($file.FullName)
        }
    }

    , $paths.ToArray()
}

function Test-ReadmeBenchmarkFilter {
    param(
        [object] $Row,
        [string[]] $Values,
        [string] $PropertyName
    )

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return $true
    }

    $value = Get-ReadmeBenchmarkProperty -InputObject $Row -Name $PropertyName
    foreach ($candidate in $Values) {
        if ($candidate -eq $value) {
            return $true
        }
    }

    $false
}

function Select-LatestReadmeBenchmarkRows {
    param([object[]] $Rows)

    $groups = @($Rows | Group-Object -Property Suite, Scenario, Host, Operation)
    foreach ($group in $groups) {
        @($group.Group | Sort-Object ArtifactLastWriteTime -Descending | Select-Object -First 1)
    }
}

function ConvertTo-ReadmeBenchmarkTable {
    param([object[]] $Rows)

    $output = [Collections.Generic.List[string]]::new()
    $output.Add('| Scenario | Host | Operation | Managed | ModuleFast | PSResourceGet | PowerShellGet | Scope |')
    $output.Add('| --- | --- | --- | ---: | ---: | ---: | ---: | --- |')

    foreach ($row in @($Rows | Sort-Object Suite, Scenario, Host, Operation)) {
        $managedMs = ConvertTo-ReadmeBenchmarkDouble -Value (Get-ReadmeBenchmarkProperty -InputObject $row -Name 'ManagedMs')
        $scenario = Get-ReadmeBenchmarkProperty -InputObject $row -Name 'Scenario'
        $hostName = Format-ReadmeBenchmarkHost -HostName (Get-ReadmeBenchmarkProperty -InputObject $row -Name 'Host')
        $operationName = Get-ReadmeBenchmarkProperty -InputObject $row -Name 'Operation'
        $scope = Get-ReadmeBenchmarkProperty -InputObject $row -Name 'ComparisonScope'
        $cells = @(
            "| ``$scenario``",
            $hostName,
            $operationName,
            (Format-ReadmeBenchmarkCell -Row $row -Engine 'Managed' -ManagedMilliseconds $managedMs),
            (Format-ReadmeBenchmarkCell -Row $row -Engine 'ModuleFast' -ManagedMilliseconds $managedMs),
            (Format-ReadmeBenchmarkCell -Row $row -Engine 'PSResourceGet' -ManagedMilliseconds $managedMs),
            (Format-ReadmeBenchmarkCell -Row $row -Engine 'PowerShellGet' -ManagedMilliseconds $managedMs),
            "$scope |"
        )
        $output.Add(($cells -join ' | '))
    }

    $output -join "`n"
}

$paths = Resolve-ReadmeBenchmarkScoreboardPaths -InputPath $ScoreboardPath
$rows = foreach ($path in $paths) {
    $artifact = Get-Item -LiteralPath $path
    foreach ($row in Import-Csv -LiteralPath $path) {
        if ((Get-ReadmeBenchmarkProperty -InputObject $row -Name 'BenchmarkRole') -ne 'Scoreboard') {
            continue
        }

        if (-not (Test-ReadmeBenchmarkFilter -Row $row -Values $Suite -PropertyName 'Suite')) {
            continue
        }

        if (-not (Test-ReadmeBenchmarkFilter -Row $row -Values $ScenarioName -PropertyName 'Scenario')) {
            continue
        }

        if (-not (Test-ReadmeBenchmarkFilter -Row $row -Values $HostName -PropertyName 'Host')) {
            continue
        }

        if (-not (Test-ReadmeBenchmarkFilter -Row $row -Values $Operation -PropertyName 'Operation')) {
            continue
        }

        $row | Add-Member -NotePropertyName ArtifactPath -NotePropertyValue $path -Force -PassThru |
            Add-Member -NotePropertyName ArtifactLastWriteTime -NotePropertyValue $artifact.LastWriteTimeUtc -Force -PassThru
    }
}

$selectedRows = @($rows)
if ($LatestPerScenario) {
    $selectedRows = @(Select-LatestReadmeBenchmarkRows -Rows $selectedRows)
}

$table = ConvertTo-ReadmeBenchmarkTable -Rows $selectedRows

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $directory = Split-Path -Path $OutputPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    [IO.File]::WriteAllText($OutputPath, $table + "`n", [Text.UTF8Encoding]::new($false))
}

if (-not [string]::IsNullOrWhiteSpace($ReadmePath)) {
    $start = "<!-- ${MarkerName}:start -->"
    $end = "<!-- ${MarkerName}:end -->"
    $readme = Get-Content -LiteralPath $ReadmePath -Raw
    $pattern = '(?s)' + [regex]::Escape($start) + '.*?' + [regex]::Escape($end)
    if ($readme -notmatch $pattern) {
        throw "README marker block '$start' ... '$end' was not found."
    }

    $replacement = $start + "`n" + $table + "`n" + $end
    $updated = [regex]::Replace($readme, $pattern, [System.Text.RegularExpressions.MatchEvaluator] { param($match) $replacement })
    [IO.File]::WriteAllText((Resolve-Path -LiteralPath $ReadmePath).Path, $updated, [Text.UTF8Encoding]::new($false))
}

$table
