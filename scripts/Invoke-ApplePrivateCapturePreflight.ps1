param(
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][ValidateSet('main', 'master')][string] $Branch,
    [Parameter(Mandatory)][string] $SourceCommit,
    [Parameter(Mandatory)][string] $WorkflowPath,
    [Parameter(Mandatory)][string[]] $RequiredChecks,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $ToolCommit
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-AppleCaptureQualification.ps1')
$controller = & gh api "repos/$env:GITHUB_REPOSITORY" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $controller.private -ne $true -or
    $controller.full_name -ine $env:GITHUB_REPOSITORY -or
    $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
    $env:GITHUB_REF -cne "refs/heads/$($controller.default_branch)") {
    throw 'Cross-repository capture requires a private controller dispatched from its default branch.'
}
$run = & gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $run.event -cne 'workflow_dispatch' -or
    $run.head_repository.full_name -ine $env:GITHUB_REPOSITORY -or
    $run.head_branch -cne $controller.default_branch -or $run.head_sha -cne $env:GITHUB_SHA -or
    -not @($run.referenced_workflows | Where-Object {
        $_.path -like 'EvotecIT/PSPublishModule/.github/workflows/powerforge-apple-private-screenshot-capture.yml@*' -and
        $_.sha -ceq $ToolCommit
    }).Count) {
    throw 'Private capture controller did not invoke the enrolled shared workflow pin.'
}
foreach ($actor in @($run.actor.login, $run.triggering_actor.login) | Select-Object -Unique) {
    if ($actor -notmatch '^[A-Za-z0-9-]+(?:\[bot\])?$') { throw 'Invalid capture maintainer identity.' }
    $permission = & gh api "repos/$env:GITHUB_REPOSITORY/collaborators/$actor/permission" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $permission.permission -notin @('admin', 'maintain', 'write')) {
        throw 'Only a controller maintainer may request or rerun a private capture.'
    }
}
$qualification = Get-AppleCaptureQualification -Repository $Repository -Branch $Branch `
    -SourceCommit $SourceCommit -WorkflowPath $WorkflowPath -RequiredChecks $RequiredChecks
$evidence = $qualification | ConvertTo-Json -Depth 4 -Compress
"qualification=$evidence" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
