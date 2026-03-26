[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $env:GITHUB_ACTION_PATH "../../..")).Path
$project = Join-Path $repoRoot "PowerForge.Cli/PowerForge.Cli.csproj"

function Format-GiB {
    param([long] $Bytes)

    if ($Bytes -le 0) {
        return '0.0 GiB'
    }

    return ('{0:N1} GiB' -f ($Bytes / 1GB))
}

function Format-NullableGiB {
    param($Bytes)

    if ($null -eq $Bytes) {
        return '-'
    }

    return Format-GiB ([long]$Bytes)
}

function Format-NullableCount {
    param($Value)

    if ($null -eq $Value) {
        return '-'
    }

    return [string]$Value
}

function Format-NullableDate {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return '-'
    }

    return [string]$Value
}

function Escape-MarkdownCell {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return '-'
    }

    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", '<br/>')
}

function Write-MarkdownSummary {
    param([string[]] $Lines)

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        return
    }

    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($Lines -join [Environment]::NewLine)
}

function Write-GitHubOutput {
    param(
        [string] $Name,
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        return
    }

    "{0}={1}" -f $Name, $Value | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

function Resolve-WorkspacePath {
    param(
        [string] $ConfiguredPath,
        [string] $DefaultRelativePath
    )

    $path = $ConfiguredPath
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = $DefaultRelativePath
    }

    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
        throw 'GITHUB_WORKSPACE is not set.'
    }

    return [System.IO.Path]::GetFullPath((Join-Path $env:GITHUB_WORKSPACE $path))
}

function Resolve-ConfigPath {
    return Resolve-WorkspacePath -ConfiguredPath $env:INPUT_CONFIG_PATH -DefaultRelativePath '.powerforge/github-housekeeping.json'
}

function Add-SectionTable {
    param(
        [System.Collections.Generic.List[string]] $Lines,
        [object[]] $Rows
    )

    if ($Rows.Count -eq 0) {
        return
    }

    $Lines.Add('')
    $Lines.Add('## Storage Summary')
    $Lines.Add('')
    $Lines.Add('| Section | Status | Planned | Deleted | Failed | Before | After |')
    $Lines.Add('| --- | --- | ---: | ---: | ---: | --- | --- |')

    foreach ($row in $Rows) {
        $Lines.Add("| $($row.Section) | $($row.Status) | $($row.Planned) | $($row.Deleted) | $($row.Failed) | $($row.Before) | $($row.After) |")
    }
}

function Add-ItemDetails {
    param(
        [System.Collections.Generic.List[string]] $Lines,
        [string] $Title,
        [object[]] $Items,
        [string] $Type
    )

    if ($Items.Count -eq 0) {
        return
    }

    $Lines.Add('')
    $Lines.Add("<details>")
    $Lines.Add("<summary>$Title ($($Items.Count))</summary>")
    $Lines.Add('')

    if ($Type -eq 'artifacts') {
        $Lines.Add('| Name | Size | Created | Updated | Reason | Delete status |')
        $Lines.Add('| --- | ---: | --- | --- | --- | --- |')
        foreach ($item in $Items | Select-Object -First 20) {
            $deleteState = if ($null -ne $item.deleteError -and -not [string]::IsNullOrWhiteSpace([string]$item.deleteError)) {
                "failed ($([string]$item.deleteStatusCode))"
            } elseif ($null -ne $item.deleteStatusCode) {
                "deleted ($([string]$item.deleteStatusCode))"
            } else {
                'planned'
            }

            $Lines.Add("| $(Escape-MarkdownCell ([string]$item.name)) | $(Format-GiB ([long]$item.sizeInBytes)) | $(Format-NullableDate $item.createdAt) | $(Format-NullableDate $item.updatedAt) | $(Escape-MarkdownCell ([string]$item.reason)) | $(Escape-MarkdownCell $deleteState) |")
        }
    } elseif ($Type -eq 'caches') {
        $Lines.Add('| Key | Size | Created | Last accessed | Reason | Delete status |')
        $Lines.Add('| --- | ---: | --- | --- | --- | --- |')
        foreach ($item in $Items | Select-Object -First 20) {
            $deleteState = if ($null -ne $item.deleteError -and -not [string]::IsNullOrWhiteSpace([string]$item.deleteError)) {
                "failed ($([string]$item.deleteStatusCode))"
            } elseif ($null -ne $item.deleteStatusCode) {
                "deleted ($([string]$item.deleteStatusCode))"
            } else {
                'planned'
            }

            $Lines.Add("| $(Escape-MarkdownCell ([string]$item.key)) | $(Format-GiB ([long]$item.sizeInBytes)) | $(Format-NullableDate $item.createdAt) | $(Format-NullableDate $item.lastAccessedAt) | $(Escape-MarkdownCell ([string]$item.reason)) | $(Escape-MarkdownCell $deleteState) |")
        }
    }

    if ($Items.Count -gt 20) {
        $Lines.Add('')
        $Lines.Add('_Showing first 20 items._')
    }

    $Lines.Add('')
    $Lines.Add('</details>')
}

function New-HousekeepingSummaryLines {
    param([pscustomobject] $Envelope)

    if (-not $Envelope.result) {
        return @(
            '# PowerForge GitHub Housekeeping Report',
            '',
            '> ❌ **Housekeeping failed before section results were produced**',
            '',
            '| Field | Value |',
            '| --- | --- |',
            "| Success | $(if ($Envelope.success) { 'Yes' } else { 'No' }) |",
            "| Exit code | $(Format-NullableCount $Envelope.exitCode) |",
            "| Error | $(Escape-MarkdownCell ([string]$Envelope.error)) |"
        )
    }

    $result = $Envelope.result
    $repository = if ([string]::IsNullOrWhiteSpace([string]$result.repository)) { '(runner-only)' } else { [string]$result.repository }
    $statusIcon = if ($Envelope.success) { '✅' } else { '❌' }
    $mode = if ($result.dryRun) { 'dry-run' } else { 'apply' }
    $rows = [System.Collections.Generic.List[object]]::new()

    if ($result.artifacts) {
        $artifactStatus = if ($null -eq $result.artifacts.failedDeletes -or [int]$result.artifacts.failedDeletes -eq 0) { 'ok' } else { 'warnings' }
        $rows.Add([pscustomobject]@{
            Section = 'Artifacts'
            Status = $artifactStatus
            Planned = ("{0} ({1})" -f (Format-NullableCount $result.artifacts.plannedDeletes), (Format-NullableGiB $result.artifacts.plannedDeleteBytes))
            Deleted = ("{0} ({1})" -f (Format-NullableCount $result.artifacts.deletedArtifacts), (Format-NullableGiB $result.artifacts.deletedBytes))
            Failed = Format-NullableCount $result.artifacts.failedDeletes
            Before = '-'
            After = '-'
        })
    }

    if ($result.caches) {
        $cacheStatus = if ($null -eq $result.caches.failedDeletes -or [int]$result.caches.failedDeletes -eq 0) { 'ok' } else { 'warnings' }
        $cacheBefore = if ($result.caches.usageBefore) {
            ("{0} caches / {1}" -f (Format-NullableCount $result.caches.usageBefore.activeCachesCount), (Format-NullableGiB $result.caches.usageBefore.activeCachesSizeInBytes))
        } else {
            '-'
        }
        $cacheAfter = if ($result.caches.usageAfter) {
            ("{0} caches / {1}" -f (Format-NullableCount $result.caches.usageAfter.activeCachesCount), (Format-NullableGiB $result.caches.usageAfter.activeCachesSizeInBytes))
        } else {
            '-'
        }
        $rows.Add([pscustomobject]@{
            Section = 'Caches'
            Status = $cacheStatus
            Planned = ("{0} ({1})" -f (Format-NullableCount $result.caches.plannedDeletes), (Format-NullableGiB $result.caches.plannedDeleteBytes))
            Deleted = ("{0} ({1})" -f (Format-NullableCount $result.caches.deletedCaches), (Format-NullableGiB $result.caches.deletedBytes))
            Failed = Format-NullableCount $result.caches.failedDeletes
            Before = $cacheBefore
            After = $cacheAfter
        })
    }

    if ($result.runner) {
        $rows.Add([pscustomobject]@{
            Section = 'Runner'
            Status = if ($result.runner.success) { 'ok' } else { 'warnings' }
            Planned = '-'
            Deleted = '-'
            Failed = if ($result.runner.success) { '0' } else { '1' }
            Before = Format-NullableGiB $result.runner.freeBytesBefore
            After = Format-NullableGiB $result.runner.freeBytesAfter
        })
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# PowerForge GitHub Housekeeping Report')
    $lines.Add('')
    $lines.Add("> $statusIcon **$repository** ran in **$mode** mode")
    $lines.Add('')
    $lines.Add('| Field | Value |')
    $lines.Add('| --- | --- |')
    $lines.Add("| Success | $(if ($Envelope.success) { 'Yes' } else { 'No' }) |")
    $lines.Add("| Requested sections | $(Escape-MarkdownCell ((@($result.requestedSections) -join ', '))) |")
    $lines.Add("| Completed sections | $(Escape-MarkdownCell ((@($result.completedSections) -join ', '))) |")
    $lines.Add("| Failed sections | $(Escape-MarkdownCell ((@($result.failedSections) -join ', '))) |")

    if ($result.message) {
        $lines.Add("| Message | $(Escape-MarkdownCell ([string]$result.message)) |")
    }

    Add-SectionTable -Lines $lines -Rows $rows.ToArray()

    if ($result.artifacts -and $result.artifacts.items) {
        Add-ItemDetails -Lines $lines -Title 'Artifact selection details' -Items @($result.artifacts.items) -Type 'artifacts'
    }

    if ($result.caches -and $result.caches.items) {
        Add-ItemDetails -Lines $lines -Title 'Cache selection details' -Items @($result.caches.items) -Type 'caches'
    }

    return $lines.ToArray()
}

function Write-TextFile {
    param(
        [string] $Path,
        [string[]] $Lines
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Set-Content -LiteralPath $Path -Value ($Lines -join [Environment]::NewLine) -Encoding utf8
}

function Write-JsonFile {
    param(
        [string] $Path,
        [string] $RawJson
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Set-Content -LiteralPath $Path -Value $RawJson -Encoding utf8
}

function Write-HousekeepingSummary {
    param(
        [pscustomobject] $Envelope,
        [string] $ReportPath,
        [string] $SummaryPath,
        [string] $RawJson
    )

    $lines = New-HousekeepingSummaryLines -Envelope $Envelope

    if ($Envelope.result) {
        $result = $Envelope.result
        Write-Host ("GitHub housekeeping: requested={0}; completed={1}; failed={2}" -f `
            (@($result.requestedSections) -join ','), `
            (@($result.completedSections) -join ','), `
            (@($result.failedSections) -join ','))
    } else {
        Write-Host ("GitHub housekeeping failed before a detailed result was produced: {0}" -f ([string]$Envelope.error))
    }

    Write-MarkdownSummary -Lines ($lines + '')
    Write-TextFile -Path $SummaryPath -Lines ($lines + '')
    Write-JsonFile -Path $ReportPath -RawJson $RawJson
    Write-GitHubOutput -Name 'report-path' -Value $ReportPath
    Write-GitHubOutput -Name 'summary-path' -Value $SummaryPath
}

$configPath = Resolve-ConfigPath
$reportPath = Resolve-WorkspacePath -ConfiguredPath $env:INPUT_REPORT_PATH -DefaultRelativePath '.powerforge/_reports/github-housekeeping.json'
$summaryPath = Resolve-WorkspacePath -ConfiguredPath $env:INPUT_SUMMARY_PATH -DefaultRelativePath '.powerforge/_reports/github-housekeeping.md'

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "Housekeeping config not found: $configPath"
}

$arguments = [System.Collections.Generic.List[string]]::new()
foreach ($argument in @(
    'run', '--project', $project, '--framework', 'net10.0', '-c', 'Release', '--no-build', '--',
    'github', 'housekeeping',
    '--config', $configPath
)) {
    $null = $arguments.Add([string]$argument)
}

if ($env:INPUT_APPLY -eq 'true') {
    $null = $arguments.Add('--apply')
} else {
    $null = $arguments.Add('--dry-run')
}

if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_GITHUB_TOKEN)) {
    $null = $arguments.Add('--token-env')
    $null = $arguments.Add('POWERFORGE_GITHUB_TOKEN')
}

$null = $arguments.Add('--output')
$null = $arguments.Add('json')

$rawOutput = (& dotnet $arguments 2>&1 | Out-String).Trim()
$exitCode = $LASTEXITCODE

if ([string]::IsNullOrWhiteSpace($rawOutput)) {
    if ($exitCode -ne 0) {
        throw "PowerForge housekeeping failed with exit code $exitCode and produced no output."
    }

    return
}

try {
    $envelope = $rawOutput | ConvertFrom-Json -Depth 30
} catch {
    Write-Host $rawOutput
    throw
}

Write-HousekeepingSummary -Envelope $envelope -ReportPath $reportPath -SummaryPath $summaryPath -RawJson $rawOutput

if (-not $envelope.success) {
    Write-Host $rawOutput
    if ($envelope.exitCode) {
        exit [int]$envelope.exitCode
    }

    exit 1
}
