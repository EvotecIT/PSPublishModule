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

if ($null -eq $latest) {
    Write-GitHubOutput -Name 'found' -Value 'false'
    Write-GitHubOutput -Name 'run_id' -Value ''
    exit 0
}

Write-GitHubOutput -Name 'found' -Value 'true'
Write-GitHubOutput -Name 'run_id' -Value ([string][int64]$latest.workflow_run.id)
