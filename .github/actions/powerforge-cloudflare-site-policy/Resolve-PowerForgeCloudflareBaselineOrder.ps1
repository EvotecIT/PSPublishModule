[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-PositiveInt64 {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][string] $Value)
    [long] $number = 0
    if (-not [long]::TryParse($Value, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref] $number) -or $number -le 0) {
        throw "$Name must be a positive integer."
    }
    $number
}

function Get-DateTimeOffset {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][AllowEmptyString()][string] $Value)
    [DateTimeOffset] $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref] $parsed)) {
        throw "$Name must be an ISO-8601 timestamp."
    }
    $parsed.ToUniversalTime()
}

function Write-Decision {
    param([Parameter(Mandatory)][bool] $Stale, [Parameter(Mandatory)][bool] $UsePrevious, [Parameter(Mandatory)][string] $Reason)
    "stale=$($Stale.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "use_previous=$($UsePrevious.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "reason=$Reason" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

function Invoke-GitHubGet {
    param([Parameter(Mandatory)][string] $Uri)
    $headers = @{ Accept = 'application/vnd.github+json'; Authorization = "Bearer $script:Token"; 'X-GitHub-Api-Version' = '2022-11-28' }
    try {
        Invoke-RestMethod -Method Get -Uri $Uri -Headers $headers
    } catch {
        # Public repositories expose deployment history without authentication. This
        # preserves existing callers that grant Actions read but not Deployments read.
        $statusCode = if ($null -ne $_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        if ($statusCode -eq 403 -and $script:ApiUrl -eq 'https://api.github.com' -and $Uri -like "$script:ApiUrl/repos/$script:Repository/deployments*") {
            $publicHeaders = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'PowerForge-Cloudflare-Site-Policy' }
            return Invoke-RestMethod -Method Get -Uri $Uri -Headers $publicHeaders
        }
        throw
    }
}

function Get-PagesDeployments {
    param([Parameter(Mandatory)][DateTimeOffset] $OldestRequired)
    $deployments = [System.Collections.Generic.List[object]]::new()
    $page = 1
    $historyComplete = $false
    do {
        $uri = "$script:ApiUrl/repos/$script:Repository/deployments?environment=github-pages&per_page=100&page=$page"
        $current = @(Invoke-GitHubGet -Uri $uri)
        foreach ($deployment in $current) {
            $deploymentId = Get-PositiveInt64 -Name 'deployment id' -Value ([string]$deployment.id)
            $statusesUri = "$script:ApiUrl/repos/$script:Repository/deployments/$deploymentId/statuses?per_page=100"
            $status = @(Invoke-GitHubGet -Uri $statusesUri) | Where-Object {
                [string]$_.state -eq 'success' -and [string]$_.environment_url -match '^https?://' -and [string]$_.log_url -match '/actions/runs/(?<run>[0-9]+)/job/(?<job>[0-9]+)'
            } | Sort-Object -Property @{ Expression = { [DateTimeOffset]$_.created_at }; Descending = $true } | Select-Object -First 1
            if ($null -ne $status) {
                [void]([string]$status.log_url -match '/actions/runs/(?<run>[0-9]+)/job/(?<job>[0-9]+)')
                $deployments.Add([pscustomobject]@{
                    DeploymentId = $deploymentId
                    RunId = Get-PositiveInt64 -Name 'deployment workflow run id' -Value $Matches.run
                    JobId = Get-PositiveInt64 -Name 'deployment workflow job id' -Value $Matches.job
                    DeployedAt = Get-DateTimeOffset -Name 'deployment status timestamp' -Value ([string]$status.created_at)
                })
            }
        }
        $oldestOnPage = $current | ForEach-Object { Get-DateTimeOffset -Name 'deployment creation timestamp' -Value ([string]$_.created_at) } |
            Sort-Object | Select-Object -First 1
        $historyComplete = $current.Count -lt 100 -or ($null -ne $oldestOnPage -and $oldestOnPage -lt $OldestRequired)
        $page++
    } while (-not $historyComplete -and $page -le 20)
    if (-not $historyComplete) {
        throw 'GitHub Pages deployment history exceeded the 2,000-deployment safety bound.'
    }
    @($deployments)
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) { throw 'GITHUB_OUTPUT is required.' }
$script:ApiUrl = ([string]$env:POWERFORGE_GITHUB_API_URL).TrimEnd('/')
$script:Repository = [string]$env:POWERFORGE_GITHUB_REPOSITORY
$script:Token = [string]$env:POWERFORGE_GITHUB_TOKEN
if (-not [Uri]::IsWellFormedUriString($script:ApiUrl, [UriKind]::Absolute)) { throw 'Deployment ordering requires an absolute GitHub API URL.' }
if ($script:Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Deployment ordering requires an owner/repository identifier.' }
if ([string]::IsNullOrWhiteSpace($script:Token)) { throw 'Deployment ordering requires a GitHub token with Actions read permission.' }

$deploymentRunId = Get-PositiveInt64 -Name 'deployment-run-id' -Value $env:POWERFORGE_DEPLOYMENT_RUN_ID
$deploymentRunAttempt = Get-PositiveInt64 -Name 'deployment-run-attempt' -Value $env:POWERFORGE_DEPLOYMENT_RUN_ATTEMPT
$deploymentJobId = Get-PositiveInt64 -Name 'deployment-job-id' -Value $env:POWERFORGE_DEPLOYMENT_JOB_ID
$previousManifest = [IO.Path]::GetFullPath($env:POWERFORGE_CLOUDFLARE_PREVIOUS_MANIFEST)
$baselineState = [IO.Path]::GetFullPath($env:POWERFORGE_CLOUDFLARE_BASELINE_STATE)
$baselineArtifactRunIdText = [string]$env:POWERFORGE_BASELINE_ARTIFACT_RUN_ID

$pagesDeployments = @(Get-PagesDeployments -OldestRequired ([DateTimeOffset]::UtcNow.AddDays(-8)))
$currentJobDeployments = @($pagesDeployments | Where-Object { $_.RunId -eq $deploymentRunId -and $_.JobId -eq $deploymentJobId })
$currentDeployment = $currentJobDeployments | Sort-Object -Property @(
    @{ Expression = { $_.DeployedAt }; Descending = $true },
    @{ Expression = { $_.DeploymentId }; Descending = $true }
) | Select-Object -First 1
if ($null -eq $currentDeployment) {
    throw "GitHub's deployment history does not yet identify Pages deployment run $deploymentRunId attempt $deploymentRunAttempt job $deploymentJobId."
}

$latestDeployment = $pagesDeployments | Sort-Object -Property @(
    @{ Expression = { $_.DeployedAt }; Descending = $true },
    @{ Expression = { $_.DeploymentId }; Descending = $true }
) | Select-Object -First 1
if ($null -ne $latestDeployment -and $latestDeployment.DeploymentId -ne $currentDeployment.DeploymentId -and
    ($latestDeployment.DeployedAt -gt $currentDeployment.DeployedAt -or ($latestDeployment.DeployedAt -eq $currentDeployment.DeployedAt -and $latestDeployment.DeploymentId -gt $currentDeployment.DeploymentId))) {
    Write-Warning "Skipping stale Cloudflare policy job for deployment run $deploymentRunId attempt $deploymentRunAttempt job $deploymentJobId because a newer GitHub Pages deployment is active."
    Write-Decision -Stale $true -UsePrevious $false -Reason 'a different GitHub Pages deployment is currently active'
    exit 0
}

if (-not (Test-Path -LiteralPath $previousManifest -PathType Leaf)) {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'no previous manifest is available'
    exit 0
}
if (-not (Test-Path -LiteralPath $baselineState -PathType Leaf) -or [string]::IsNullOrWhiteSpace($baselineArtifactRunIdText)) {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'the previous baseline has no deployment-order state'
    exit 0
}

try {
    $state = Get-Content -LiteralPath $baselineState -Raw | ConvertFrom-Json
    if ([int]$state.schemaVersion -ne 1) { throw "unsupported schema version '$($state.schemaVersion)'" }
    $baselineRunId = Get-PositiveInt64 -Name 'baseline deployment run id' -Value ([string]$state.deploymentRunId)
    $baselineRunAttempt = Get-PositiveInt64 -Name 'baseline deployment run attempt' -Value ([string]$state.deploymentRunAttempt)
    $baselineJobId = Get-PositiveInt64 -Name 'baseline deployment job id' -Value ([string]$state.deploymentJobId)
    $baselineArtifactRunId = Get-PositiveInt64 -Name 'baseline artifact run id' -Value $baselineArtifactRunIdText
} catch {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'the previous baseline deployment-order state is invalid'
    exit 0
}
if ($baselineArtifactRunId -ne $baselineRunId) {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'the previous baseline artifact does not match its deployment run'
    exit 0
}

$baselineJobDeployments = @($pagesDeployments | Where-Object { $_.RunId -eq $baselineRunId -and $_.JobId -eq $baselineJobId })
$baselineDeployment = $baselineJobDeployments | Sort-Object -Property @(
    @{ Expression = { $_.DeployedAt }; Descending = $true },
    @{ Expression = { $_.DeploymentId }; Descending = $true }
) | Select-Object -First 1
if ($null -eq $baselineDeployment) {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'the previous baseline Pages deployment is no longer verifiable'
    exit 0
}

$intervening = @($pagesDeployments | Where-Object {
    $afterBaseline = $_.DeployedAt -gt $baselineDeployment.DeployedAt -or ($_.DeployedAt -eq $baselineDeployment.DeployedAt -and $_.DeploymentId -gt $baselineDeployment.DeploymentId)
    $beforeCurrent = $_.DeployedAt -lt $currentDeployment.DeployedAt -or ($_.DeployedAt -eq $currentDeployment.DeployedAt -and $_.DeploymentId -lt $currentDeployment.DeploymentId)
    $belongsToCurrentJob = $currentJobDeployments.DeploymentId -contains $_.DeploymentId
    $afterBaseline -and $beforeCurrent -and -not $belongsToCurrentJob
})
if ($intervening.Count -gt 0) {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'an intervening GitHub Pages deployment has no successful purge baseline'
    exit 0
}

Write-Decision -Stale $false -UsePrevious $true -Reason 'the previous baseline is ordered for this deployment'
