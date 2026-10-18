function Get-EnrolledPrivateCapturePolicy {
    $policyPath = [IO.Path]::GetFullPath($CapturePolicyPath)
    Assert-UnlinkedPath -Path $policyPath -Name 'Private capture policy'
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    foreach ($root in @($consumer, $toolRoot)) {
        if ($policyPath.StartsWith($root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $comparison)) {
            throw 'Private capture policy must be trusted local configuration outside the source and tool checkouts.'
        }
    }
    $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
    if ($policy.SchemaVersion -ne 1 -or
        [string]$policy.ControllerRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
        $policy.ControllerBranch -notin @('main', 'master') -or
        [string]$policy.ControllerCommit -cnotmatch '^[0-9a-f]{40}$' -or
        [string]$policy.WorkflowPath -cnotmatch '^\.github/workflows/[A-Za-z0-9_-]+\.ya?ml$' -or
        $policy.SourceRepository -ine $ExpectedConsumerRepository -or $policy.SourceBranch -cne $requiredBranch -or
        $policy.OwnerCommit -cne $ExpectedCommit -or
        $policy.ControllerRepository -ieq $ExpectedConsumerRepository -or
        [string]$policy.RunnerGroup -cnotmatch '^[A-Za-z0-9][A-Za-z0-9 _-]{0,99}$' -or
        $policy.RunnerGroup -ieq 'Default') {
        throw 'Private capture policy does not bind the enrolled controller, owner and consumer.'
    }
    return $policy
}

function Assert-PrivateCaptureController {
    param(
        [Parameter(Mandatory)][string] $GhPath,
        [Parameter(Mandatory)] $Provenance,
        [Parameter(Mandatory)] $Run,
        [Parameter(Mandatory)] $Policy
    )
    $policy = $Policy
    $workflowRef = "$($policy.ControllerRepository)/$($policy.WorkflowPath)@refs/heads/$($policy.ControllerBranch)"
    if ($Provenance.repository -ine $policy.ControllerRepository -or $Provenance.workflowRef -cne $workflowRef -or
        $Provenance.controllerCommit -cne $policy.ControllerCommit -or
        $Provenance.sourceRepository -ine $ExpectedConsumerRepository -or
        [string]$Provenance.captureRunAttempt -notmatch '^[1-9][0-9]{0,8}$' -or
        $Provenance.captureRunAttempt -ne $Run.run_attempt -or
        $Run.head_sha -cne $policy.ControllerCommit -or $Run.head_branch -cne $policy.ControllerBranch -or
        $Run.path -cne $policy.WorkflowPath -or $Run.head_repository.full_name -ine $policy.ControllerRepository -or
        $Run.event -cne 'workflow_dispatch' -or $Run.status -cne 'completed' -or $Run.conclusion -cne 'success' -or
        -not @($Run.referenced_workflows | Where-Object {
            $_.path -like 'EvotecIT/PSPublishModule/.github/workflows/powerforge-apple-screenshot-capture.yml@*' -and
            $_.sha -ceq $ExpectedCommit
        }).Count) {
        throw 'Private capture provenance does not match its enrolled controller commit and shared workflow.'
    }
    $controller = & $GhPath api "repos/$($policy.ControllerRepository)" 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $controller.private -ne $true -or
        $controller.full_name -ine $policy.ControllerRepository -or $controller.default_branch -cne $policy.ControllerBranch) {
        throw 'Capture controller must remain private on its enrolled default branch.'
    }
    $jobs = @(Get-AppleCaptureWorkflowJobs -Repository $policy.ControllerRepository -RunId $Provenance.captureRunId `
        -Attempt $Provenance.captureRunAttempt -GhPath $GhPath)
    $nativeJobs = @($jobs | Where-Object {
        $_.runner_name -ceq $Provenance.captureRunner.name -and $_.runner_group_name -ceq $policy.RunnerGroup -and
        $_.head_sha -ceq $policy.ControllerCommit -and $_.status -ceq 'completed' -and $_.conclusion -ceq 'success'
    })
    if ([string]::IsNullOrWhiteSpace([string]$Provenance.captureRunner.name) -or $nativeJobs.Count -ne 1) {
        throw 'Private capture did not execute in the enrolled private runner group.'
    }
    $qualification = Get-AppleCaptureQualification -Repository $ExpectedConsumerRepository -Branch $requiredBranch `
        -SourceCommit $Provenance.sourceCommit -WorkflowPath $policy.QualificationWorkflowPath `
        -RequiredChecks $policy.RequiredChecks -GhPath $GhPath
    foreach ($name in @('repository', 'sourceBranch', 'sourceCommit', 'workflowPath', 'runId', 'runAttempt')) {
        if ([string]$Provenance.qualification.$name -cne [string]$qualification.$name) {
            throw 'Retained private capture qualification differs from authoritative exact-source qualification.'
        }
    }
    if (($Provenance.qualification.requiredChecks | ConvertTo-Json -Compress) -cne
        ($qualification.requiredChecks | ConvertTo-Json -Compress)) {
        throw 'Private capture did not enforce the enrolled qualification jobs.'
    }
}
