# GitHub artifact names are discovery keys, never a trust boundary.
function Get-CatalogApiPages([string] $Endpoint, [string] $Collection, [string] $Failure) {
    $json = & gh api --paginate --slurp $Endpoint
    if ($LASTEXITCODE -ne 0) { throw $Failure }
    try { $pages = ConvertFrom-Json -InputObject ($json -join "`n") -NoEnumerate } catch { throw $Failure }
    if ($pages -isnot [array] -or $pages.Count -eq 0) { throw $Failure }
    foreach ($page in $pages) {
        if ($page.$Collection -isnot [array]) { throw $Failure }
    }
    return $pages
}

function Get-TrustedCatalogArtifact([string] $ArtifactPrefix) {
    $workflowPrefix = "$env:GITHUB_REPOSITORY/"
    $workflowSuffix = "@$env:GITHUB_REF"
    if ($env:GITHUB_EVENT_NAME -ne 'workflow_dispatch' -or !$env:CATALOG_DEFAULT_BRANCH -or
        $env:GITHUB_REF -cne "refs/heads/$env:CATALOG_DEFAULT_BRANCH" -or
        !$env:GITHUB_WORKFLOW_REF.StartsWith($workflowPrefix, [StringComparison]::Ordinal) -or
        !$env:GITHUB_WORKFLOW_REF.EndsWith($workflowSuffix, [StringComparison]::Ordinal)) {
        throw 'Catalog recovery requires an identified caller workflow dispatched from a trusted branch.'
    }
    $workflowPath = $env:GITHUB_WORKFLOW_REF.Substring($workflowPrefix.Length,
        $env:GITHUB_WORKFLOW_REF.Length - $workflowPrefix.Length - $workflowSuffix.Length)
    $branch = $env:GITHUB_REF.Substring('refs/heads/'.Length)
    # Scope discovery to the caller workflow. Repository-wide artifact listing can fail
    # on busy repositories and needlessly scans artifacts from unrelated workflows.
    # Do not filter runs on the server: GitHub caps filtered run searches at 1,000 results.
    $workflowName = [Uri]::EscapeDataString($workflowPath.Split('/')[-1])
    $pages = Get-CatalogApiPages "repos/$env:GITHUB_REPOSITORY/actions/workflows/$workflowName/runs?per_page=100" 'workflow_runs' 'Cannot inspect previous catalog workflow runs.'
    $candidates = [Collections.Generic.List[object]]::new()
    foreach ($producer in @($pages | ForEach-Object { $_.workflow_runs })) {
        $runId = $producer.id
        if ("$runId" -notmatch '^[1-9][0-9]*$') { throw 'Catalog artifact producer is unavailable. Reconcile before continuing.' }
        $runJson = & gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$runId"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the catalog artifact producer. Reconcile before continuing.' }
        $run = $runJson | ConvertFrom-Json
        if ($run.id -ne $runId -or !$run.event -or !$run.path -or !$run.head_repository.full_name -or !$run.head_branch) {
            throw 'Incomplete catalog artifact provenance. Reconcile before continuing.'
        }
        if ($run.event -ne 'workflow_dispatch' -or $run.path -cne $workflowPath -or
            $run.head_repository.full_name -cne $env:GITHUB_REPOSITORY -or $run.head_branch -cne $branch) { continue }
        $artifactPages = Get-CatalogApiPages "repos/$env:GITHUB_REPOSITORY/actions/runs/$runId/artifacts?per_page=100" 'artifacts' 'Cannot inspect previous catalog receipts.'
        foreach ($artifact in @($artifactPages | ForEach-Object { $_.artifacts })) {
            if (!$artifact.name -or !$artifact.name.StartsWith($ArtifactPrefix, [StringComparison]::Ordinal)) { continue }
            if ($artifact.workflow_run.id -ne $runId -or "$($artifact.id)" -notmatch '^[1-9][0-9]*$' -or $artifact.expired -isnot [bool]) {
                throw 'Incomplete catalog artifact producer metadata. Reconcile before continuing.'
            }
            $candidates.Add($artifact)
        }
    }
    foreach ($artifact in @($candidates | Sort-Object id -Descending)) {
        if ($artifact.expired) { throw 'Previous catalog receipt expired. Reconcile remote submissions before continuing.' }
        return $artifact
    }
    return $null
}

function Confirm-CatalogHistory([string] $OutputPath) {
    $historyPath = Join-Path $OutputPath 'workflow-history.json'
    $confirmed = $false
    if (Test-Path -LiteralPath $historyPath) {
        $history = Get-Content -LiteralPath $historyPath -Raw | ConvertFrom-Json
        if ($history.SchemaVersion -ne 1 -or $history.Confirmed -isnot [bool]) { throw 'Invalid catalog workflow history.' }
        $confirmed = $history.Confirmed
    }
    if (!$confirmed -and $env:CATALOG_EXECUTE -eq 'true') {
        if ($env:CATALOG_CONFIRM_NO_PRIOR_SUBMISSION -ne 'true') {
            throw 'No confirmed submission history. Check both remote catalogs and explicitly confirm no prior submission before executing.'
        }
        $confirmed = $true
    }
    @{ SchemaVersion = 1; Confirmed = $confirmed } | ConvertTo-Json | Set-Content -LiteralPath $historyPath -Encoding utf8
}
