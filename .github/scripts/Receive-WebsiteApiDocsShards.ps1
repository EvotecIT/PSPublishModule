<#
.SYNOPSIS
Waits for the API docs shard artifacts of the current workflow run and extracts them.
.DESCRIPTION
The website-apidocs-shard jobs of powerforge-website-run.yml upload one archive each
(<prefix>-<shard>). This script polls the run's artifacts with the read-only job token
in GITHUB_TOKEN, downloads every archive for shards 2..ShardCount into
<Destination>/<shard>, and fails as soon as a shard reports a failure for this attempt
(<prefix>-<shard>-failed-<attempt>) or when the timeout expires.
#>
param(
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][long] $RunId,
    [Parameter(Mandatory)][int] $RunAttempt,
    [Parameter(Mandatory)][string] $ArtifactPrefix,
    [Parameter(Mandatory)][ValidateRange(2, 8)][int] $ShardCount,
    [Parameter(Mandatory)][string] $Destination,
    [int] $TimeoutMinutes = 120,
    [int] $PollSeconds = 20
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    throw 'GITHUB_TOKEN with actions: read is required to collect API docs shards.'
}
$headers = @{
    Authorization          = "Bearer $($env:GITHUB_TOKEN)"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

function Get-RunArtifacts {
    $artifacts = @()
    for ($page = 1; ; $page++) {
        $uri = "https://api.github.com/repos/$Repository/actions/runs/$RunId/artifacts?per_page=100&page=$page"
        $response = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 60
        $artifacts += @($response.artifacts)
        if (@($response.artifacts).Count -lt 100) { return $artifacts }
    }
}

$pending = [System.Collections.Generic.List[int]]::new()
2..$ShardCount | ForEach-Object { $pending.Add($_) }
$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
$started = [DateTime]::UtcNow
while ($pending.Count -gt 0) {
    try {
        $artifacts = @(Get-RunArtifacts | Where-Object { -not $_.expired })
    } catch {
        Write-Warning "Listing run artifacts failed; retrying: $($_.Exception.Message)"
        $artifacts = $null
    }

    foreach ($shard in @($pending)) {
        if ($null -eq $artifacts) { break }
        if ($artifacts | Where-Object { $_.name -ceq "$ArtifactPrefix-$shard-failed-$RunAttempt" }) {
            throw "API docs shard $shard failed; see the website-apidocs-shard ($shard) job."
        }

        $artifact = $artifacts | Where-Object { $_.name -ceq "$ArtifactPrefix-$shard" } |
            Sort-Object -Property created_at -Descending | Select-Object -First 1
        if (-not $artifact) { continue }

        $target = Join-Path $Destination $shard
        $zip = Join-Path $Destination "shard-$shard.zip"
        try {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            # The redirect goes to signed storage; PowerShell does not forward the Authorization header.
            Invoke-WebRequest -Uri "https://api.github.com/repos/$Repository/actions/artifacts/$($artifact.id)/zip" `
                -Headers $headers -OutFile $zip -TimeoutSec 600
            Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
            Remove-Item -LiteralPath $zip -Force
        } catch {
            # A rerun can replace an artifact while it downloads; list again and retry.
            Write-Warning "Downloading API docs shard $shard failed; retrying: $($_.Exception.Message)"
            Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
            continue
        }

        $elapsed = [int]([DateTime]::UtcNow - $started).TotalSeconds
        Write-Host ("Collected API docs shard {0} ({1:n1} MB) after waiting {2} s." -f $shard, ($artifact.size_in_bytes / 1MB), $elapsed)
        [void]$pending.Remove($shard)
    }

    if ($pending.Count -eq 0) { break }
    if ([DateTime]::UtcNow -gt $deadline) {
        throw "Timed out after $TimeoutMinutes minute(s) waiting for API docs shard(s): $($pending -join ', ')."
    }
    Start-Sleep -Seconds $PollSeconds
}
