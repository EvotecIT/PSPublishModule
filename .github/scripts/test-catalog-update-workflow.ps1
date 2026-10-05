param([Parameter(Mandatory)][string] $ScratchRoot)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ScratchRoot -PathType Container)) { throw 'Scratch root is unavailable.' }
$fixtureRoot = Join-Path $ScratchRoot ('catalog-workflow-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$savedEnvironment = @{}
foreach ($name in @('GITHUB_REPOSITORY', 'GITHUB_OUTPUT', 'GITHUB_REF', 'GITHUB_WORKFLOW_REF', 'GITHUB_EVENT_NAME',
    'CATALOG_EXECUTE', 'CATALOG_VERIFY_AUTHENTICATION', 'CATALOG_DEFAULT_BRANCH', 'CATALOG_CONFIRM_NO_PRIOR_SUBMISSION', 'CATALOG_RESERVATION_KEY')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:GITHUB_REPOSITORY = 'fixture/app'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_WORKFLOW_REF = 'fixture/app/.github/workflows/catalog.yml@refs/heads/main'
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    $env:CATALOG_DEFAULT_BRANCH = 'main'
    $env:GITHUB_OUTPUT = Join-Path $fixtureRoot 'outputs.txt'
    $env:CATALOG_EXECUTE = 'false'; $env:CATALOG_VERIFY_AUTHENTICATION = 'false'; $env:CATALOG_CONFIRM_NO_PRIOR_SUBMISSION = 'false'
    $profilePath = Join-Path $fixtureRoot 'catalog.json'
    Set-Content -LiteralPath $profilePath -Value '{"ReleaseConfigPath":"release.json"}'
    $identity = "$env:GITHUB_REPOSITORY`n$profilePath`nApp-v1.2.3"
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity))).ToLowerInvariant()
    $global:CatalogWorkflowTestState = @{Expired = $false; Empty = $false; Confirmed = $true; Unavailable = $false;
        RunListingUnavailable = $false; ArtifactListingUnavailable = $false; WrongProducer = $false; OlderRunLatest = $false;
        RunListingMalformed = $null;
        Prefix = "catalog-update-$($digest.Substring(0, 24))-";
        Calls = [Collections.Generic.List[object]]::new(); Downloads = [Collections.Generic.List[object]]::new()}
    function gh {
        $global:LASTEXITCODE = 0
        if ($args[0] -eq 'api' -and $args[-1] -like '*actions/artifacts*') {
            throw 'Repository-wide artifact listing failed with HTTP 500.'
        } elseif ($args[0] -eq 'api' -and $args[-1] -like '*actions/workflows/*/runs*') {
            if ($global:CatalogWorkflowTestState.RunListingUnavailable) { $global:LASTEXITCODE = 1; return }
            if ($null -ne $global:CatalogWorkflowTestState.RunListingMalformed) { $global:CatalogWorkflowTestState.RunListingMalformed; return }
            @(
                @{ workflow_runs = @(@{id = 100}) },
                @{ workflow_runs = @(@{id = 200}, @{id = 300}) }
            ) | ConvertTo-Json -Depth 6 -Compress
        } elseif ($args[0] -eq 'api' -and $args[-1] -like '*actions/runs/*/artifacts*') {
            if ($global:CatalogWorkflowTestState.ArtifactListingUnavailable) { $global:LASTEXITCODE = 1; return }
            if ($global:CatalogWorkflowTestState.Empty) { '[{"artifacts":[]}]'; return }
            $runId = [int]($args[-1].Split('/')[-2])
            if ($runId -eq 300) { throw 'Untrusted run artifacts were inspected.' }
            $artifactId = $(if ($runId -eq 100) { if ($global:CatalogWorkflowTestState.OlderRunLatest) {25} else {10} } else {20})
            @(
                @{artifacts = @()},
                @{ artifacts = @(@{id = $artifactId; name = $global:CatalogWorkflowTestState.Prefix + 'receipt';
                    expired = ($runId -eq 200 -and $global:CatalogWorkflowTestState.Expired);
                    workflow_run = @{id = $(if ($global:CatalogWorkflowTestState.WrongProducer) {300} else {$runId})} }) }
            ) | ConvertTo-Json -Depth 6 -Compress
        } elseif ($args[0] -eq 'api') {
            if ($global:CatalogWorkflowTestState.Unavailable) { $global:LASTEXITCODE = 1; return }
            $runId = [int]($args[-1].Split('/')[-1])
            @{ id = $runId; event = $(if ($runId -eq 300) {'pull_request'} else {'workflow_dispatch'});
                path = '.github/workflows/catalog.yml'; head_branch = 'main';
                head_repository = @{ full_name = $(if ($runId -eq 300) {'attacker/fork'} else {'fixture/app'}) } } | ConvertTo-Json -Depth 3
        } elseif ($args[0] -eq 'run') {
            $global:CatalogWorkflowTestState.Downloads.Add(@($args))
            $destination = $args[[Array]::IndexOf($args, '--dir') + 1]
            New-Item -ItemType Directory -Path $destination | Out-Null
            Set-Content -LiteralPath (Join-Path $destination 'catalog-update.json') -Value '{"DeliveryReleaseId":"release-123"}'
            @{SchemaVersion = 1; Confirmed = $global:CatalogWorkflowTestState.Confirmed} | ConvertTo-Json |
                Set-Content -LiteralPath (Join-Path $destination 'workflow-history.json')
        } elseif ($args[0] -ne 'release') { throw 'Unexpected artifact-service operation.' }
    }
    function dotnet {
        $global:CatalogWorkflowTestState.Calls.Add(@($args)); $global:LASTEXITCODE = 0
        if ($args[3] -eq 'prepare') {
            New-Item -ItemType Directory -Path $args[[Array]::IndexOf($args, '--out') + 1] | Out-Null
        }
    }
    $parameters = @{ToolPath = 'owner.dll'; ProfilePath = $profilePath; ReleaseTag = 'App-v1.2.3';
        DeliveryReleaseId = 'release-123'; OutputPath = (Join-Path $fixtureRoot 'prepared'); Channel = 'winget'}
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Prepare @parameters
    if ($global:CatalogWorkflowTestState.Downloads.Count -ne 1 -or $global:CatalogWorkflowTestState.Downloads[0][2] -ne 200) { throw 'Untrusted newer receipt displaced the trusted dispatch receipt.' }
    if ($global:CatalogWorkflowTestState.Calls[0] -notcontains '--resume-from' -or $global:CatalogWorkflowTestState.Calls[1] -contains '--execute') { throw 'Restored input was not requalified before preflight.' }
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Reserve @parameters
    $key = (Get-Content -LiteralPath $env:GITHUB_OUTPUT | Where-Object { $_ -like 'reservation-key=*' }).Substring(16)
    if ($key -notmatch '^[a-f0-9]{32}$') { throw 'Did not create a fresh submission reservation.' }
    $env:CATALOG_RESERVATION_KEY = $key
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Submit @parameters
    if ($global:CatalogWorkflowTestState.Calls[3] -notcontains '--execute' -or $global:CatalogWorkflowTestState.Calls[3] -notcontains $key) { throw 'Execution did not consume the archived reservation.' }
    function Assert-PrepareRejected([string] $Message) {
        $rejected = $false
        try { & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Prepare @parameters } catch { $rejected = $_.Exception.Message -like $Message }
        if (!$rejected) { throw "Unsafe recovery was accepted; expected: $Message" }
    }
    $global:CatalogWorkflowTestState.Expired = $true
    Assert-PrepareRejected '*receipt expired*'
    if ($global:CatalogWorkflowTestState.Calls.Count -ne 4) { throw 'Expired history permitted another attempt.' }
    $global:CatalogWorkflowTestState.Expired = $false; $global:CatalogWorkflowTestState.Unavailable = $true
    Assert-PrepareRejected '*verify*producer*'
    $global:CatalogWorkflowTestState.Unavailable = $false; $global:CatalogWorkflowTestState.RunListingUnavailable = $true
    Assert-PrepareRejected '*inspect*workflow*runs*'
    $global:CatalogWorkflowTestState.RunListingUnavailable = $false; $global:CatalogWorkflowTestState.ArtifactListingUnavailable = $true
    Assert-PrepareRejected '*inspect*receipts*'
    $global:CatalogWorkflowTestState.ArtifactListingUnavailable = $false
    foreach ($malformed in @('', '{}', '[{}]')) {
        $global:CatalogWorkflowTestState.RunListingMalformed = $malformed
        Assert-PrepareRejected '*inspect*workflow*runs*'
    }
    $global:CatalogWorkflowTestState.RunListingMalformed = $null
    $global:CatalogWorkflowTestState.WrongProducer = $true
    Assert-PrepareRejected '*artifact producer*'
    $global:CatalogWorkflowTestState.WrongProducer = $false; $global:CatalogWorkflowTestState.OlderRunLatest = $true
    $parameters.OutputPath = Join-Path $fixtureRoot 'older-run-latest'
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Prepare @parameters
    if ($global:CatalogWorkflowTestState.Downloads[-1][2] -ne 100) { throw 'Did not restore the most recently created receipt across producer runs.' }
    $global:CatalogWorkflowTestState.OlderRunLatest = $false; $global:CatalogWorkflowTestState.Empty = $true
    $parameters.OutputPath = Join-Path $fixtureRoot 'lost-history'; $env:CATALOG_EXECUTE = 'true'
    Assert-PrepareRejected '*No confirmed submission history*'
    # A subsequent dry run cannot manufacture confirmed history after receipt deletion.
    $global:CatalogWorkflowTestState.Empty = $false; $global:CatalogWorkflowTestState.Confirmed = $false
    $parameters.OutputPath = Join-Path $fixtureRoot 'unconfirmed-preflight'
    Assert-PrepareRejected '*No confirmed submission history*'
    $env:CATALOG_CONFIRM_NO_PRIOR_SUBMISSION = 'true'
    $parameters.OutputPath = Join-Path $fixtureRoot 'confirmed-first-attempt'
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Prepare @parameters
    if (!(Get-Content -LiteralPath (Join-Path $parameters.OutputPath 'workflow-history.json') -Raw | ConvertFrom-Json).Confirmed) { throw 'Explicit confirmation was not retained.' }
    $env:CATALOG_EXECUTE = 'false'; $env:CATALOG_VERIFY_AUTHENTICATION = 'true'; $env:CATALOG_CONFIRM_NO_PRIOR_SUBMISSION = 'false'
    $parameters.OutputPath = Join-Path $fixtureRoot 'authenticated-preflight'
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Prepare @parameters
    $authenticatedPreflight = $global:CatalogWorkflowTestState.Calls[-1]
    if ($authenticatedPreflight -notcontains '--require-authentication' -or $authenticatedPreflight -contains '--execute') { throw 'Authentication verification must remain a non-mutating preflight.' }
    if ((Get-Content -LiteralPath (Join-Path $parameters.OutputPath 'workflow-history.json') -Raw | ConvertFrom-Json).Confirmed) { throw 'Authentication verification manufactured confirmed submission history.' }
    Write-Host 'Catalog producer trust, release requalification, reservation and lost-history reconciliation contracts passed.'
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Remove-Variable CatalogWorkflowTestState -Scope Global
    Remove-Item -LiteralPath $fixtureRoot -Recurse
}
