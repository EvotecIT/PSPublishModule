[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('runner', 'caches', 'artifacts')]
    [string] $Mode
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $env:GITHUB_ACTION_PATH "../..")).Path
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

function Add-ApplyMode {
    param([System.Collections.Generic.List[string]] $Arguments)

    if ($env:INPUT_APPLY -eq 'true') {
        $null = $Arguments.Add('--apply')
    } else {
        $null = $Arguments.Add('--dry-run')
    }
}

function Add-OptionalPair {
    param(
        [System.Collections.Generic.List[string]] $Arguments,
        [string] $Option,
        [string] $Value
    )

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        $null = $Arguments.Add($Option)
        $null = $Arguments.Add($Value)
    }
}

function Resolve-Repository {
    if (-not [string]::IsNullOrWhiteSpace($env:INPUT_REPO)) {
        return $env:INPUT_REPO
    }

    return $env:GITHUB_REPOSITORY
}

function Resolve-Token {
    if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_GITHUB_TOKEN)) {
        return $env:POWERFORGE_GITHUB_TOKEN
    }

    throw 'GitHub token is required for remote GitHub housekeeping.'
}

function Write-EnvelopeSummary {
    param(
        [string] $CurrentMode,
        [pscustomobject] $Envelope
    )

    if (-not $Envelope.result) {
        return
    }

    $result = $Envelope.result
    switch ($CurrentMode) {
        'runner' {
            $lines = @(
                "### Runner housekeeping",
                "",
                "- Mode: $(if ($result.dryRun) { 'dry-run' } else { 'apply' })",
                "- Free before: $(Format-GiB ([long]$result.freeBytesBefore))",
                "- Free after: $(Format-GiB ([long]$result.freeBytesAfter))",
                "- Aggressive cleanup: $(if ($result.aggressiveApplied) { 'yes' } else { 'no' })",
                "- Success: $(if ($Envelope.success) { 'yes' } else { 'no' })"
            )

            if ($result.message) {
                $lines += "- Message: $($result.message)"
            }

            Write-Host ("Runner housekeeping: free {0} -> {1}; aggressive={2}" -f `
                (Format-GiB ([long]$result.freeBytesBefore)), `
                (Format-GiB ([long]$result.freeBytesAfter)), `
                $(if ($result.aggressiveApplied) { 'yes' } else { 'no' }))

            Write-MarkdownSummary -Lines ($lines + '')
        }
        'caches' {
            $usageLine = $null
            if ($result.usageBefore) {
                $usageLine = "- Usage before: $($result.usageBefore.activeCachesCount) caches, $(Format-GiB ([long]$result.usageBefore.activeCachesSizeInBytes))"
            }

            $lines = @(
                "### GitHub caches",
                "",
                "- Mode: $(if ($result.dryRun) { 'dry-run' } else { 'apply' })",
                "- Scanned: $($result.scannedCaches)",
                "- Matched: $($result.matchedCaches)",
                "- Planned deletes: $($result.plannedDeletes) ($(Format-GiB ([long]$result.plannedDeleteBytes)))",
                "- Deleted: $($result.deletedCaches) ($(Format-GiB ([long]$result.deletedBytes)))",
                "- Failed deletes: $($result.failedDeletes)",
                "- Success: $(if ($Envelope.success) { 'yes' } else { 'no' })"
            )

            if ($usageLine) {
                $lines = @($lines[0], $lines[1], $usageLine) + $lines[2..($lines.Length - 1)]
            }

            if ($result.message) {
                $lines += "- Message: $($result.message)"
            }

            Write-Host ("GitHub caches: scanned={0}, planned={1}, deleted={2}, failed={3}" -f `
                $result.scannedCaches, $result.plannedDeletes, $result.deletedCaches, $result.failedDeletes)

            Write-MarkdownSummary -Lines ($lines + '')
        }
        'artifacts' {
            $lines = @(
                "### GitHub artifacts",
                "",
                "- Mode: $(if ($result.dryRun) { 'dry-run' } else { 'apply' })",
                "- Scanned: $($result.scannedArtifacts)",
                "- Matched: $($result.matchedArtifacts)",
                "- Planned deletes: $($result.plannedDeletes) ($(Format-GiB ([long]$result.plannedDeleteBytes)))",
                "- Deleted: $($result.deletedArtifacts) ($(Format-GiB ([long]$result.deletedBytes)))",
                "- Failed deletes: $($result.failedDeletes)",
                "- Success: $(if ($Envelope.success) { 'yes' } else { 'no' })"
            )

            if ($result.message) {
                $lines += "- Message: $($result.message)"
            }

            Write-Host ("GitHub artifacts: scanned={0}, planned={1}, deleted={2}, failed={3}" -f `
                $result.scannedArtifacts, $result.plannedDeletes, $result.deletedArtifacts, $result.failedDeletes)

            Write-MarkdownSummary -Lines ($lines + '')
        }
    }
}

$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.AddRange(@('run', '--project', $project, '-c', 'Release', '--no-build', '--'))

switch ($Mode) {
    'runner' {
        $arguments.AddRange(@('github', 'runner', 'cleanup'))
        Add-ApplyMode -Arguments $arguments
        Add-OptionalPair -Arguments $arguments -Option '--min-free-gb' -Value $env:INPUT_MIN_FREE_GB
        Add-OptionalPair -Arguments $arguments -Option '--aggressive-threshold-gb' -Value $env:INPUT_RUNNER_AGGRESSIVE_THRESHOLD_GB

        if ($env:INPUT_ALLOW_SUDO -eq 'true') {
            $null = $arguments.Add('--allow-sudo')
        }
    }
    'caches' {
        $repository = Resolve-Repository
        $token = Resolve-Token

        $arguments.AddRange(@(
            'github', 'caches', 'prune',
            '--repo', $repository,
            '--token', $token,
            '--keep', $env:INPUT_CACHE_KEEP,
            '--max-age-days', $env:INPUT_CACHE_MAX_AGE_DAYS,
            '--max-delete', $env:INPUT_CACHE_MAX_DELETE
        ))

        Add-ApplyMode -Arguments $arguments
        Add-OptionalPair -Arguments $arguments -Option '--key' -Value $env:INPUT_CACHE_KEY
        Add-OptionalPair -Arguments $arguments -Option '--exclude' -Value $env:INPUT_CACHE_EXCLUDE
    }
    'artifacts' {
        $repository = Resolve-Repository
        $token = Resolve-Token

        $arguments.AddRange(@(
            'github', 'artifacts', 'prune',
            '--repo', $repository,
            '--token', $token,
            '--keep', $env:INPUT_ARTIFACT_KEEP,
            '--max-age-days', $env:INPUT_ARTIFACT_MAX_AGE_DAYS,
            '--max-delete', $env:INPUT_ARTIFACT_MAX_DELETE
        ))

        Add-ApplyMode -Arguments $arguments
        Add-OptionalPair -Arguments $arguments -Option '--name' -Value $env:INPUT_ARTIFACT_NAME
        Add-OptionalPair -Arguments $arguments -Option '--exclude' -Value $env:INPUT_ARTIFACT_EXCLUDE
    }
}

$null = $arguments.Add('--output')
$null = $arguments.Add('json')

$rawOutput = (& dotnet $arguments 2>&1 | Out-String).Trim()
$exitCode = $LASTEXITCODE

if ([string]::IsNullOrWhiteSpace($rawOutput)) {
    if ($exitCode -ne 0) {
        throw "PowerForge command failed for mode '$Mode' with exit code $exitCode and produced no output."
    }

    return
}

try {
    $envelope = $rawOutput | ConvertFrom-Json -Depth 20
} catch {
    Write-Host $rawOutput
    throw
}

Write-EnvelopeSummary -CurrentMode $Mode -Envelope $envelope

if (-not $envelope.success) {
    Write-Host $rawOutput
    if ($envelope.exitCode) {
        exit [int]$envelope.exitCode
    }

    exit 1
}
