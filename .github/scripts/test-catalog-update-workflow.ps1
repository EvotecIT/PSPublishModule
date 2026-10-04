param([Parameter(Mandatory)][string] $ScratchRoot)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ScratchRoot -PathType Container)) { throw 'Scratch root is unavailable.' }
$fixtureRoot = Join-Path $ScratchRoot ('catalog-workflow-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$savedEnvironment = @{}
foreach ($name in @('GITHUB_REPOSITORY', 'GITHUB_OUTPUT', 'GITHUB_REF', 'GITHUB_WORKFLOW_REF', 'GITHUB_EVENT_NAME',
    'CATALOG_EXECUTE', 'CATALOG_DEFAULT_BRANCH', 'CATALOG_CONFIRM_NO_PRIOR_SUBMISSION', 'CATALOG_RESERVATION_KEY')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:GITHUB_REPOSITORY = 'fixture/app'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_WORKFLOW_REF = 'fixture/app/.github/workflows/catalog.yml@refs/heads/main'
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    $env:CATALOG_DEFAULT_BRANCH = 'main'
    $env:GITHUB_OUTPUT = Join-Path $fixtureRoot 'outputs.txt'
    $env:CATALOG_EXECUTE = 'false'; $env:CATALOG_CONFIRM_NO_PRIOR_SUBMISSION = 'false'
    $profilePath = Join-Path $fixtureRoot 'catalog.json'
    Set-Content -LiteralPath $profilePath -Value '{"ReleaseConfigPath":"release.json"}'
    $identity = "$env:GITHUB_REPOSITORY`n$profilePath`nApp-v1.2.3"
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity))).ToLowerInvariant()
    $global:CatalogWorkflowTestState = @{Expired = $false; Empty = $false; Confirmed = $true; Unavailable = $false;
        Prefix = "catalog-update-$($digest.Substring(0, 24))-";
        Calls = [Collections.Generic.List[object]]::new(); Downloads = [Collections.Generic.List[object]]::new()}
    function gh {
        $global:LASTEXITCODE = 0
        if ($args[0] -eq 'api' -and $args[-1] -like '*actions/artifacts*') {
            if ($global:CatalogWorkflowTestState.Empty) { '[{"artifacts":[]}]'; return }
            @(
                @{ artifacts = @(@{ id = 10; name = $global:CatalogWorkflowTestState.Prefix + 'older-result'; expired = $false; workflow_run = @{id = 100} }) },
                @{ artifacts = @(@{ id = 20; name = $global:CatalogWorkflowTestState.Prefix + 'newer-intent'; expired = $global:CatalogWorkflowTestState.Expired; workflow_run = @{id = 200} },
                    @{ id = 30; name = $global:CatalogWorkflowTestState.Prefix + 'forged-result'; expired = $false; workflow_run = @{id = 300} }) }
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
    $global:CatalogWorkflowTestState.Unavailable = $false; $global:CatalogWorkflowTestState.Empty = $true
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
    Write-Host 'Catalog producer trust, release requalification, reservation and lost-history reconciliation contracts passed.'
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Remove-Variable CatalogWorkflowTestState -Scope Global
    Remove-Item -LiteralPath $fixtureRoot -Recurse
}
