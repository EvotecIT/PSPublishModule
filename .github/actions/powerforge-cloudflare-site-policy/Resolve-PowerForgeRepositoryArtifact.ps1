$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-GitHubOutput {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [AllowEmptyString()]
        [string] $Value
    )

    "$Name=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

$artifactName = [string] $env:POWERFORGE_ARTIFACT_NAME
$apiUrl = ([string] $env:POWERFORGE_GITHUB_API_URL).TrimEnd('/')
$repository = [string] $env:POWERFORGE_GITHUB_REPOSITORY
$token = [string] $env:POWERFORGE_GITHUB_TOKEN
$referenceRunIdText = [string] $env:POWERFORGE_REFERENCE_RUN_ID
$excludeRunIdText = [string] $env:POWERFORGE_EXCLUDE_RUN_ID

if ([string]::IsNullOrWhiteSpace($artifactName)) {
    throw 'Repository artifact lookup requires an artifact name.'
}
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Repository artifact lookup requires a GitHub token with Actions read permission.'
}
if ($repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'Repository artifact lookup requires an owner/repository identifier.'
}
if (-not [Uri]::IsWellFormedUriString($apiUrl, [UriKind]::Absolute)) {
    throw 'Repository artifact lookup requires an absolute GitHub API URL.'
}

[long] $referenceRunId = 0
if (-not [string]::IsNullOrWhiteSpace($referenceRunIdText) -and
    (-not [long]::TryParse($referenceRunIdText, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref] $referenceRunId) -or $referenceRunId -le 0)) {
    throw 'Repository artifact lookup received an invalid reference workflow run id.'
}

[long] $excludeRunId = 0
if (-not [string]::IsNullOrWhiteSpace($excludeRunIdText) -and
    (-not [long]::TryParse($excludeRunIdText, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref] $excludeRunId) -or $excludeRunId -le 0)) {
    throw 'Repository artifact lookup received an invalid excluded workflow run id.'
}

$headers = @{
    Accept = 'application/vnd.github+json'
    Authorization = "Bearer $token"
    'X-GitHub-Api-Version' = '2022-11-28'
}
$encodedName = [Uri]::EscapeDataString($artifactName)
$artifacts = [System.Collections.Generic.List[object]]::new()
$page = 1

do {
    $uri = "$apiUrl/repos/$repository/actions/artifacts?name=$encodedName&per_page=100&page=$page"
    $response = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
    $current = @($response.artifacts)
    foreach ($artifact in $current) {
        if ([string]$artifact.name -ceq $artifactName -and -not [bool]$artifact.expired) {
            $artifacts.Add($artifact)
        }
    }

    $page++
} while ($current.Count -eq 100 -and (($page - 1) * 100) -lt [int64]$response.total_count)

$latest = $artifacts |
    Where-Object { $null -ne $_.workflow_run -and [int64]$_.workflow_run.id -gt 0 } |
    Sort-Object -Property @(
        @{ Expression = { [DateTimeOffset]$_.created_at }; Descending = $true },
        @{ Expression = { [int64]$_.id }; Descending = $true }
    ) |
    Select-Object -First 1

$gap = $false
if ($referenceRunId -gt 0) {
    $reference = $artifacts |
        Where-Object { $null -ne $_.workflow_run -and [int64]$_.workflow_run.id -eq $referenceRunId } |
        Sort-Object -Property @(
            @{ Expression = { [DateTimeOffset]$_.created_at }; Descending = $true },
            @{ Expression = { [int64]$_.id }; Descending = $true }
        ) |
        Select-Object -First 1

    if ($null -eq $reference) {
        $gap = $true
    } else {
        $referenceCreatedAt = ([DateTimeOffset]$reference.created_at).ToUniversalTime()
        $referenceArtifactId = [int64]$reference.id
        $gap = @($artifacts | Where-Object {
            if ($null -eq $_.workflow_run -or [int64]$_.workflow_run.id -le 0 -or [int64]$_.workflow_run.id -eq $excludeRunId) {
                return $false
            }
            $createdAt = ([DateTimeOffset]$_.created_at).ToUniversalTime()
            $createdAt -gt $referenceCreatedAt -or ($createdAt -eq $referenceCreatedAt -and [int64]$_.id -gt $referenceArtifactId)
        }).Count -gt 0
    }
}

if ($null -eq $latest) {
    Write-GitHubOutput -Name 'found' -Value 'false'
    Write-GitHubOutput -Name 'run_id' -Value ''
    Write-GitHubOutput -Name 'artifact_id' -Value ''
    Write-GitHubOutput -Name 'created_at' -Value ''
    Write-GitHubOutput -Name 'gap' -Value ($gap.ToString().ToLowerInvariant())
    exit 0
}

Write-GitHubOutput -Name 'found' -Value 'true'
Write-GitHubOutput -Name 'run_id' -Value ([string][int64]$latest.workflow_run.id)
Write-GitHubOutput -Name 'artifact_id' -Value ([string][int64]$latest.id)
Write-GitHubOutput -Name 'created_at' -Value (([DateTimeOffset]$latest.created_at).ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture))
Write-GitHubOutput -Name 'gap' -Value ($gap.ToString().ToLowerInvariant())
