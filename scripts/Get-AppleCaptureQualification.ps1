function Get-AppleCaptureQualification {
    <#
    .SYNOPSIS
    Verifies an exact merged source and its latest successful native qualification.
    .DESCRIPTION
    Reads authoritative GitHub metadata before capture code is checked out or run.
    A commit must be an ancestor of the enrolled default branch. Required jobs must
    pass in the latest attempt of the exact-source push workflow, including jobs
    returned on later pages. GitHub observation failures reject the capture.
    #>
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string] $Repository,
        [Parameter(Mandatory)][ValidateSet('main', 'master')][string] $Branch,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $SourceCommit,
        [Parameter(Mandatory)][ValidatePattern('^\.github/workflows/[A-Za-z0-9_-]+\.ya?ml$')][string] $WorkflowPath,
        [Parameter(Mandatory)][string[]] $RequiredChecks,
        [string] $GhPath = 'gh'
    )
    $checks = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $RequiredChecks) {
        if ([string]::IsNullOrWhiteSpace($name) -or $name -cne $name.Trim() -or
            $name.Length -gt 200 -or $name -match '[\r\n]' -or -not $checks.Add($name)) {
            throw 'Capture qualification requires unique, nonempty exact job names.'
        }
    }
    if ($checks.Count -eq 0) { throw 'Capture qualification requires at least one job.' }
    $api = {
        param([string] $Endpoint)
        $json = @(& $GhPath api $Endpoint 2>$null)
        if ($LASTEXITCODE -ne 0) { throw "Cannot verify capture source through GitHub: $Endpoint" }
        return ($json -join "`n") | ConvertFrom-Json -ErrorAction Stop
    }
    $metadata = & $api "repos/$Repository"
    if ($metadata.full_name -ine $Repository -or $metadata.default_branch -cne $Branch) {
        throw 'Capture source repository or default branch differs from the enrolled policy.'
    }
    $comparison = & $api "repos/$Repository/compare/$SourceCommit...$Branch"
    if ($comparison.base_commit.sha -cne $SourceCommit -or
        $comparison.merge_base_commit.sha -cne $SourceCommit -or
        $comparison.status -notin @('ahead', 'identical')) {
        throw 'Capture source is not merged into the enrolled default branch.'
    }
    $workflow = [Uri]::EscapeDataString((Split-Path $WorkflowPath -Leaf))
    $runs = & $api "repos/$Repository/actions/workflows/$workflow/runs?head_sha=$SourceCommit&branch=$Branch&event=push&per_page=100"
    $latest = @($runs.workflow_runs) | Select-Object -First 1
    if ($null -eq $latest -or $latest.path -cne $WorkflowPath -or
        $latest.event -cne 'push' -or $latest.head_branch -cne $Branch -or
        $latest.head_repository.full_name -ine $Repository -or $latest.head_sha -cne $SourceCommit -or
        $latest.status -cne 'completed' -or $latest.conclusion -cne 'success' -or
        [string]$latest.id -notmatch '^[1-9][0-9]{0,18}$' -or
        [string]$latest.run_attempt -notmatch '^[1-9][0-9]{0,8}$') {
        throw 'Capture requires the latest successful exact-source default-branch push qualification.'
    }
    $jobs = @(Get-AppleCaptureWorkflowJobs -Repository $Repository -RunId $latest.id -Attempt $latest.run_attempt -GhPath $GhPath)
    foreach ($name in $checks) {
        $matches = @($jobs | Where-Object { $_.name -ceq $name })
        if ($matches.Count -ne 1 -or $matches[0].head_sha -cne $SourceCommit -or
            $matches[0].status -cne 'completed' -or $matches[0].conclusion -cne 'success') {
            throw "Capture qualification job '$name' did not pass at the exact source and latest attempt."
        }
    }
    return [pscustomobject][ordered]@{
        repository = $Repository
        sourceBranch = $Branch
        sourceCommit = $SourceCommit
        workflowPath = $WorkflowPath
        runId = [string]$latest.id
        runAttempt = [int]$latest.run_attempt
        requiredChecks = @($RequiredChecks)
    }
}

function Get-AppleCaptureWorkflowJobs {
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string] $Repository,
        [Parameter(Mandatory)][ValidatePattern('^[1-9][0-9]{0,18}$')][string] $RunId,
        [Parameter(Mandatory)][ValidatePattern('^[1-9][0-9]{0,8}$')][string] $Attempt,
        [string] $GhPath = 'gh'
    )
    $jobs = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $total = $null
    for ($page = 1; $page -le 100; $page++) {
        $json = @(& $GhPath api "repos/$Repository/actions/runs/$RunId/attempts/$Attempt/jobs?per_page=100&page=$page" 2>$null)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot verify capture workflow job inventory.' }
        $batch = ($json -join "`n") | ConvertFrom-Json -ErrorAction Stop
        if ([string]$batch.total_count -notmatch '^[1-9][0-9]{0,4}$' -or $batch.total_count -gt 10000 -or
            @($batch.jobs).Count -eq 0 -or ($null -ne $total -and $batch.total_count -ne $total)) {
            throw 'Capture workflow job inventory is empty, truncated or inconsistent.'
        }
        $total = [int]$batch.total_count
        foreach ($job in $batch.jobs) {
            if ([string]$job.id -notmatch '^[1-9][0-9]{0,18}$' -or -not $ids.Add([string]$job.id)) {
                throw 'Capture workflow job inventory contains invalid or repeated jobs.'
            }
            $jobs.Add($job)
        }
        if ($jobs.Count -gt $total) { throw 'Capture workflow job inventory exceeds its declared count.' }
        if ($jobs.Count -eq $total) { return $jobs.ToArray() }
    }
    throw 'Capture workflow job inventory is incomplete.'
}
