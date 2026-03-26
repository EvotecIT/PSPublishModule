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

function Write-MarkdownSummary {
    param([string[]] $Lines)

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        return
    }

    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($Lines -join [Environment]::NewLine)
}

function Resolve-ConfigPath {
    $configPath = $env:INPUT_CONFIG_PATH
    if ([string]::IsNullOrWhiteSpace($configPath)) {
        $configPath = '.powerforge/github-housekeeping.json'
    }

    if ([System.IO.Path]::IsPathRooted($configPath)) {
        return [System.IO.Path]::GetFullPath($configPath)
    }

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
        throw 'GITHUB_WORKSPACE is not set.'
    }

    return [System.IO.Path]::GetFullPath((Join-Path $env:GITHUB_WORKSPACE $configPath))
}

function Write-HousekeepingSummary {
    param([pscustomobject] $Envelope)

    if (-not $Envelope.result) {
        return
    }

    $result = $Envelope.result
    $lines = @(
        "### GitHub housekeeping",
        "",
        "- Mode: $(if ($result.dryRun) { 'dry-run' } else { 'apply' })",
        "- Requested sections: $((@($result.requestedSections) -join ', '))",
        "- Completed sections: $((@($result.completedSections) -join ', '))",
        "- Failed sections: $((@($result.failedSections) -join ', '))",
        "- Success: $(if ($Envelope.success) { 'yes' } else { 'no' })"
    )

    if ($result.message) {
        $lines += "- Message: $($result.message)"
    }

    if ($result.caches) {
        $lines += ''
        $lines += '#### Caches'
        if ($result.caches.usageBefore) {
            $lines += "- Usage before: $($result.caches.usageBefore.activeCachesCount) caches, $(Format-GiB ([long]$result.caches.usageBefore.activeCachesSizeInBytes))"
        }
        if ($result.caches.usageAfter) {
            $lines += "- Usage after: $($result.caches.usageAfter.activeCachesCount) caches, $(Format-GiB ([long]$result.caches.usageAfter.activeCachesSizeInBytes))"
        }
        $lines += "- Planned deletes: $($result.caches.plannedDeletes) ($(Format-GiB ([long]$result.caches.plannedDeleteBytes)))"
        $lines += "- Deleted: $($result.caches.deletedCaches) ($(Format-GiB ([long]$result.caches.deletedBytes)))"
        $lines += "- Failed deletes: $($result.caches.failedDeletes)"
    }

    if ($result.artifacts) {
        $lines += ''
        $lines += '#### Artifacts'
        $lines += "- Planned deletes: $($result.artifacts.plannedDeletes) ($(Format-GiB ([long]$result.artifacts.plannedDeleteBytes)))"
        $lines += "- Deleted: $($result.artifacts.deletedArtifacts) ($(Format-GiB ([long]$result.artifacts.deletedBytes)))"
        $lines += "- Failed deletes: $($result.artifacts.failedDeletes)"
    }

    if ($result.runner) {
        $lines += ''
        $lines += '#### Runner'
        $lines += "- Free before: $(Format-GiB ([long]$result.runner.freeBytesBefore))"
        $lines += "- Free after: $(Format-GiB ([long]$result.runner.freeBytesAfter))"
        $lines += "- Aggressive cleanup: $(if ($result.runner.aggressiveApplied) { 'yes' } else { 'no' })"
    }

    Write-Host ("GitHub housekeeping: requested={0}; completed={1}; failed={2}" -f `
        (@($result.requestedSections) -join ','), `
        (@($result.completedSections) -join ','), `
        (@($result.failedSections) -join ','))

    Write-MarkdownSummary -Lines ($lines + '')
}

$configPath = Resolve-ConfigPath
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

Write-HousekeepingSummary -Envelope $envelope

if (-not $envelope.success) {
    Write-Host $rawOutput
    if ($envelope.exitCode) {
        exit [int]$envelope.exitCode
    }

    exit 1
}
