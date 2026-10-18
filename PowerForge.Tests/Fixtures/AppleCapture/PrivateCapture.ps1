param([Parameter(Mandatory)][string] $Root, [Parameter(Mandatory)][string] $Fixture)
$ErrorActionPreference = 'Stop'
. (Join-Path $Root 'scripts/Get-AppleCaptureQualification.ps1')
. (Join-Path $Root 'scripts/Invoke-PinnedPowerForge.Capture.ps1')
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Root 'scripts/Invoke-PinnedPowerForge.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Operator parse failure.' }
foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Parent -is [Management.Automation.Language.NamedBlockAst] }, $true)) {
    Invoke-Expression $function.Extent.Text
}
$source = 'b' * 40; $owner = 'a' * 40
$ExpectedConsumerRepository = 'Synthetic/PublicApp'; $requiredBranch = 'master'; $ExpectedCommit = $owner
$consumer = Join-Path $Fixture 'source'; $toolRoot = Join-Path $Fixture 'operator'
New-Item -ItemType Directory -Path $consumer, $toolRoot -Force | Out-Null
$base = @{
    'repos/Synthetic/PublicApp' = @{ full_name = 'Synthetic/PublicApp'; default_branch = 'master'; private = $false }
    'repos/Synthetic/Controller' = @{ full_name = 'Synthetic/Controller'; default_branch = 'main'; private = $true }
    "repos/Synthetic/PublicApp/compare/$source...master" = @{ base_commit = @{ sha = $source }; merge_base_commit = @{ sha = $source }; status = 'ahead' }
    "repos/Synthetic/PublicApp/actions/workflows/native.yml/runs?head_sha=$source&branch=master&event=push&per_page=100" = @{
        workflow_runs = @(@{ id = 11; run_attempt = 2; path = '.github/workflows/native.yml'; event = 'push'; head_branch = 'master';
            head_repository = @{ full_name = 'Synthetic/PublicApp' }; head_sha = $source; status = 'completed'; conclusion = 'success' })
    }
    'repos/Synthetic/PublicApp/actions/runs/11/attempts/2/jobs?per_page=100&page=1' = @{
        total_count = 1; jobs = @(@{ id = 1; name = 'Native'; head_sha = $source; status = 'completed'; conclusion = 'success' })
    }
    'repos/Synthetic/Controller/actions/runs/22' = @{
        run_attempt = 1; event = 'workflow_dispatch'; head_repository = @{ full_name = 'Synthetic/Controller' }; head_branch = 'main'; head_sha = $owner;
        path = '.github/workflows/capture.yml'; actor = @{ login = 'maintainer' }; triggering_actor = @{ login = 'maintainer' };
        status = 'completed'; conclusion = 'success'; referenced_workflows = @(@{
            path = "EvotecIT/PSPublishModule/.github/workflows/powerforge-apple-private-screenshot-capture.yml@$owner"; sha = $owner })
    }
    'repos/Synthetic/Controller/actions/runs/22/attempts/1/jobs?per_page=100&page=1' = @{
        total_count = 1; jobs = @(@{ id = 22; name = 'capture'; head_sha = $owner; status = 'completed'; conclusion = 'success';
            runner_name = 'capture-vm'; runner_group_name = 'private-capture' })
    }
    'repos/Synthetic/Controller/collaborators/maintainer/permission' = @{ permission = 'write' }
}
function Reset-Fixture {
    $global:captureFixtureResponses = $base | ConvertTo-Json -Depth 12 | ConvertFrom-Json -AsHashtable
    $global:captureFixtureApiFailure = $false
}
function gh {
    param([string] $Operation, [string] $Endpoint)
    $global:LASTEXITCODE = 0
    if ($global:captureFixtureApiFailure) { $global:LASTEXITCODE = 1; return '{}' }
    if ($Operation -ne 'api' -or -not $global:captureFixtureResponses.ContainsKey($Endpoint)) { throw "Unexpected GitHub operation: $Operation $Endpoint" }
    $global:captureFixtureResponses[$Endpoint] | ConvertTo-Json -Depth 12 -Compress
}
function Reject {
    param([scriptblock] $Action)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe capture was accepted.' }
}
function Qualify {
    Get-AppleCaptureQualification -Repository 'Synthetic/PublicApp' -Branch master -SourceCommit $source `
        -WorkflowPath '.github/workflows/native.yml' -RequiredChecks @('Native')
}
Reset-Fixture
$qualified = Qualify
if ($qualified.runId -ne '11' -or $qualified.runAttempt -ne 2) { throw 'Wrong qualification identity.' }
$runsKey = "repos/Synthetic/PublicApp/actions/workflows/native.yml/runs?head_sha=$source&branch=master&event=push&per_page=100"
$jobsKey = 'repos/Synthetic/PublicApp/actions/runs/11/attempts/2/jobs?per_page=100&page=1'
foreach ($mutate in @(
    { $global:captureFixtureResponses["repos/Synthetic/PublicApp/compare/$source...master"].merge_base_commit.sha = 'c' * 40 },
    { $global:captureFixtureResponses['repos/Synthetic/PublicApp'].default_branch = 'main' },
    { $global:captureFixtureResponses[$runsKey].workflow_runs[0].event = 'pull_request' },
    { $global:captureFixtureResponses[$runsKey].workflow_runs[0].head_repository.full_name = 'Other/Fork' },
    { $global:captureFixtureResponses[$runsKey].workflow_runs[0].head_sha = 'c' * 40 },
    { $global:captureFixtureResponses[$runsKey].workflow_runs[0].status = 'in_progress' },
    { $global:captureFixtureResponses[$jobsKey].jobs[0].conclusion = 'skipped' },
    { $global:captureFixtureResponses[$jobsKey].jobs[0].head_sha = 'c' * 40 },
    { $global:captureFixtureResponses[$jobsKey].jobs[0].name = 'Unrelated job' },
    { $global:captureFixtureApiFailure = $true }
)) {
    Reset-Fixture; & $mutate; Reject { Qualify }
}
Reset-Fixture
$global:captureFixtureResponses[$jobsKey] = @{ total_count = 101; jobs = @(1..100 | ForEach-Object { @{ id = $_; name = "Other $_" } }) }
$global:captureFixtureResponses['repos/Synthetic/PublicApp/actions/runs/11/attempts/2/jobs?per_page=100&page=2'] = @{ total_count = 101; jobs = @(@{ id = 101; name = 'Native'; head_sha = $source; status = 'completed'; conclusion = 'success' }) }
$null = Qualify
$global:captureFixtureResponses.Remove('repos/Synthetic/PublicApp/actions/runs/11/attempts/2/jobs?per_page=100&page=2')
Reject { Qualify }
Reset-Fixture
Reject { Get-AppleCaptureQualification -Repository 'Synthetic/PublicApp' -Branch master -SourceCommit $source -WorkflowPath '.github/workflows/native.yml' -RequiredChecks @('Native', 'Native') }

$env:GITHUB_REPOSITORY = 'Synthetic/Controller'; $env:GITHUB_RUN_ID = '22'
$env:GITHUB_SHA = $owner; $env:GITHUB_REF = 'refs/heads/main'; $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
$env:GITHUB_OUTPUT = Join-Path $Fixture 'output.txt'
$preflight = Join-Path $Root 'scripts/Invoke-ApplePrivateCapturePreflight.ps1'
& $preflight -Repository 'Synthetic/PublicApp' -Branch master -SourceCommit $source -WorkflowPath '.github/workflows/native.yml' -RequiredChecks @('Native') -ToolCommit $owner
if ((Get-Content $env:GITHUB_OUTPUT -Raw) -notmatch 'qualification=') { throw 'Preflight did not produce source qualification.' }
foreach ($mutate in @(
    { $global:captureFixtureResponses['repos/Synthetic/Controller'].private = $false },
    { $global:captureFixtureResponses['repos/Synthetic/Controller/collaborators/maintainer/permission'].permission = 'read' },
    { $global:captureFixtureResponses['repos/Synthetic/Controller/actions/runs/22'].referenced_workflows[0].sha = 'd' * 40 },
    { $global:captureFixtureResponses['repos/Synthetic/Controller/actions/runs/22'].head_sha = 'd' * 40 }
)) {
    Reset-Fixture; & $mutate
    Reject { & $preflight -Repository 'Synthetic/PublicApp' -Branch master -SourceCommit $source -WorkflowPath '.github/workflows/native.yml' -RequiredChecks @('Native') -ToolCommit $owner }
}
Reset-Fixture
$policy = @{ RunnerGroup = 'private-capture'; SchemaVersion = 1; ControllerRepository = 'Synthetic/Controller'; ControllerBranch = 'main'; ControllerCommit = $owner;
    WorkflowPath = '.github/workflows/capture.yml'; OwnerCommit = $owner; SourceRepository = 'Synthetic/PublicApp'; SourceBranch = 'master';
    QualificationWorkflowPath = '.github/workflows/native.yml'; RequiredChecks = @('Native') }
$CapturePolicyPath = Join-Path $Fixture 'capture-policy.json'
$policy | ConvertTo-Json -Depth 5 | Set-Content $CapturePolicyPath
$enrolled = Get-EnrolledPrivateCapturePolicy
$provenance = [pscustomobject]@{ captureRunId = '22'; captureRunAttempt = 1; captureRunner = @{ name = 'capture-vm' }; repository = 'Synthetic/Controller'; workflowRef = 'Synthetic/Controller/.github/workflows/capture.yml@refs/heads/main';
    controllerCommit = $owner; sourceRepository = 'Synthetic/PublicApp'; sourceCommit = $source; qualification = $qualified }
$run = $global:captureFixtureResponses['repos/Synthetic/Controller/actions/runs/22']
Assert-PrivateCaptureController -GhPath gh -Provenance $provenance -Run $run -Policy $enrolled
foreach ($property in @('controllerCommit', 'sourceRepository')) {
    $saved = $provenance.$property; $provenance.$property = 'wrong'
    Reject { Assert-PrivateCaptureController -GhPath gh -Provenance $provenance -Run $run -Policy $enrolled }
    $provenance.$property = $saved
}
$runJobs = $global:captureFixtureResponses['repos/Synthetic/Controller/actions/runs/22/attempts/1/jobs?per_page=100&page=1']
$runJobs.jobs[0].runner_group_name = 'Default'
Reject { Assert-PrivateCaptureController -GhPath gh -Provenance $provenance -Run $run -Policy $enrolled }
$runJobs.jobs[0].runner_group_name = 'private-capture'
$provenance.qualification.runId = '12'
Reject { Assert-PrivateCaptureController -GhPath gh -Provenance $provenance -Run $run -Policy $enrolled }
$provenance.qualification.runId = '11'
$CapturePolicyPath = Join-Path $consumer 'untrusted-policy.json'
$policy | ConvertTo-Json -Depth 5 | Set-Content $CapturePolicyPath
Reject { Get-EnrolledPrivateCapturePolicy }
'PRIVATE_CAPTURE_PASS'
