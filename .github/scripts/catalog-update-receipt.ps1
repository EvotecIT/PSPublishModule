# GitHub artifact names are discovery keys, never a trust boundary.
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
    $pages = & gh api --paginate --slurp "repos/$env:GITHUB_REPOSITORY/actions/artifacts?per_page=100"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect previous catalog receipts.' }
    $candidates = @($pages | ConvertFrom-Json | ForEach-Object { $_.artifacts } |
        Where-Object { $_.name.StartsWith($ArtifactPrefix, [StringComparison]::Ordinal) } | Sort-Object id -Descending)
    $runs = @{}
    foreach ($artifact in $candidates) {
        $runId = $artifact.workflow_run.id
        if (!$runId) { throw 'Catalog artifact producer is unavailable. Reconcile before continuing.' }
        if (!$runs.ContainsKey($runId)) {
            $runJson = & gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$runId"
            if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the catalog artifact producer. Reconcile before continuing.' }
            $runs[$runId] = $runJson | ConvertFrom-Json
        }
        $run = $runs[$runId]
        if ($run.id -ne $runId -or !$run.event -or !$run.path -or !$run.head_repository.full_name -or !$run.head_branch) {
            throw 'Incomplete catalog artifact provenance. Reconcile before continuing.'
        }
        if ($run.event -ne 'workflow_dispatch' -or $run.path -cne $workflowPath -or
            $run.head_repository.full_name -cne $env:GITHUB_REPOSITORY -or $run.head_branch -cne $branch) { continue }
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
