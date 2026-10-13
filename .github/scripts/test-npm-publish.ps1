$ErrorActionPreference = 'Stop'
$routeRoot = Join-Path ([IO.Path]::GetTempPath()) ('powerforge-npm-publisher-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $routeRoot | Out-Null
try {
$scriptsRoot = Join-Path $routeRoot 'steps'
New-Item -ItemType Directory -Path $scriptsRoot | Out-Null
$workflow = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../workflows/powerforge-npm-publish.yml')
function Export-WorkflowStep([string] $Name, [string] $Destination) {
    $start = [Array]::IndexOf($workflow, "      - name: $Name")
    if ($start -lt 0) { throw 'The publisher contract step is missing.' }
    $position = $start + 1
    while ($position -lt $workflow.Count -and $workflow[$position] -ne '        run: |') {
        if ($workflow[$position] -match '^      - ') { throw 'The publisher step has no literal script.' }
        $position++
    }
    $body = [Collections.Generic.List[string]]::new()
    for ($position++; $position -lt $workflow.Count; $position++) {
        $line = $workflow[$position]
        if ($line -match '^          ') { $body.Add($line.Substring(10)) }
        elseif ([string]::IsNullOrWhiteSpace($line)) { $body.Add('') }
        else { break }
    }
    if ($body.Count -eq 0) { throw 'The publisher contract step is empty.' }
    $body | Set-Content -LiteralPath $Destination
}
Export-WorkflowStep 'Require successful quality checks for this default-branch commit' (Join-Path $scriptsRoot 'quality.ps1')
Export-WorkflowStep 'Verify archive identity and publish the same bytes' (Join-Path $scriptsRoot 'archive.ps1')
$package = Join-Path $routeRoot 'package'
New-Item -ItemType Directory -Path $package | Out-Null
@{name='@example/widget';version='0.1.0';repository=@{url='git+https://github.com/example/widget.git'}} | ConvertTo-Json | Set-Content (Join-Path $package 'package.json')
$Archive = Join-Path $routeRoot 'widget.tgz'
& tar -czf $Archive -C $routeRoot package
if ($LASTEXITCODE -ne 0) { throw 'Could not create the package fixture.' }
$env:GITHUB_REPOSITORY = 'example/widget'
$env:GITHUB_SHA = 'a805eca6a9a9e76b3d09bd72f5c436e96c3ba6dc'
$env:GITHUB_REF = 'refs/heads/main'
$env:GITHUB_EVENT_NAME = 'workflow_dispatch'
$env:DEFAULT_BRANCH = 'main'
$env:QUALITY_RUN = '38050281825'
$env:QUALITY_WORKFLOW = 'quality.yml'
$env:NPM_PACKAGE = '@example/widget'
$env:NPM_VERSION = '0.1.0'
$env:NPM_EXECUTE = 'false'
$env:NPM_BOOTSTRAP = 'false'
$env:SOURCE_PRIVATE = 'false'
$env:RELEASE_ENVIRONMENT = 'npm-release'
$env:RUNNER_TEMP = Join-Path $routeRoot 'fixture'
$env:GITHUB_STEP_SUMMARY = Join-Path $routeRoot 'fixture-summary.md'
$env:GITHUB_ENV=Join-Path $routeRoot 'job-env'
$env:QUALIFIED_SOURCE_SHA=$env:GITHUB_SHA
$fixtureArchives = Join-Path $env:RUNNER_TEMP 'npm-release'
New-Item -ItemType Directory -Path $fixtureArchives -Force | Out-Null
Copy-Item -LiteralPath $Archive -Destination $fixtureArchives
$env:NPM_SHA256 = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
$global:npmQualification_originalHash = $env:NPM_SHA256
$global:npmQualification_integrity = 'sha512-' + [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($Archive)))
$global:npmQualification_run = [ordered]@{head_sha=$env:GITHUB_SHA;head_branch='main';event='push';status='completed';conclusion='success';path='.github/workflows/quality.yml'}
$global:npmQualification_environment = @{deployment_branch_policy=@{protected_branches=$false;custom_branch_policies=$true}}
$global:npmQualification_policies = @{total_count=1;branch_policies=@(@{name='main';type='branch'})}
$global:npmQualification_attestedSource=$env:QUALIFIED_SOURCE_SHA
$global:npmQualification_attestedArchive=[Convert]::ToHexString([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($Archive))).ToLowerInvariant()
$global:npmQualification_registryProvenance = $true
$global:npmQualification_results = [Collections.Generic.List[object]]::new()
foreach ($scriptFile in Get-ChildItem -LiteralPath $scriptsRoot -Filter '*.ps1') {
    $tokens=$null; $parseErrors=$null
    [Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName,[ref]$tokens,[ref]$parseErrors) | Out-Null
    if ($parseErrors) { throw "PowerShell parse errors: $($parseErrors.Message -join '; ')" }
}
function global:gh {
    $global:LASTEXITCODE = 0
    if ($args[1] -eq 'repos/example/widget/environments/npm-release') { return $global:npmQualification_environment | ConvertTo-Json -Depth 5 -Compress }
    if ($args[1] -eq 'repos/example/widget/environments/npm-release/deployment-branch-policies') { return $global:npmQualification_policies | ConvertTo-Json -Depth 5 -Compress }
    if ($args[1] -eq "repos/example/widget/actions/runs/$env:QUALITY_RUN") { return $global:npmQualification_run | ConvertTo-Json -Compress }
    throw 'Unexpected GitHub metadata request.'
}
function global:Start-Sleep { param([int]$Seconds) }
function global:Get-NpmProvenanceFixture {
    $payload=@{
        predicateType='https://slsa.dev/provenance/v1'
        subject=@(@{digest=@{sha512=$global:npmQualification_attestedArchive}})
        predicate=@{buildDefinition=@{resolvedDependencies=@(@{uri="git+https://github.com/$env:GITHUB_REPOSITORY@$env:GITHUB_REF";digest=@{gitCommit=$global:npmQualification_attestedSource}})}}
    } | ConvertTo-Json -Depth 12 -Compress
    $envelope=@{payload=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))}
    $content=@{attestations=@(@{predicateType='https://slsa.dev/provenance/v1';bundle=@{dsseEnvelope=$envelope}})} | ConvertTo-Json -Depth 12 -Compress
    return [pscustomobject]@{StatusCode=200;Content=$content}
}
function global:npm {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'publish') { $global:npmQualification_npmSource=$env:GITHUB_SHA; $global:npmQualification_publishCalls++; $global:npmQualification_publishArguments = @($args); return 'MOCK: no registry mutation' }
    if ($args[0] -eq 'view') {
        $global:npmQualification_viewCalls++
        $dist = @{integrity=$global:npmQualification_registryIntegrity}
        if ($global:npmQualification_registryProvenance) { $dist.attestations=@{provenance=@{predicateType='https://slsa.dev/provenance/v1'}} }
        $dist | ConvertTo-Json -Depth 5; return
    }
    throw 'Unexpected npm operation in qualification fixture.'
}
function Invoke-Case([string]$Name,[scriptblock]$Operation,[bool]$Reject) {
    $global:npmQualification_publishCalls=0; $global:npmQualification_viewCalls=0; $global:npmQualification_publishArguments=@()
    $failed=$false; $failure=''
    try { & $Operation | Out-Null } catch { $failed=$true; $failure=$_.Exception.Message }
    if ($failed -ne $Reject) { throw "Unexpected outcome for $Name`: $failure" }
    $global:npmQualification_results.Add([pscustomobject]@{case=$Name;rejected=$failed;publishCalls=$global:npmQualification_publishCalls;viewCalls=$global:npmQualification_viewCalls;message=$failure})
}
$gate = Join-Path $scriptsRoot 'quality.ps1'
$publisher = Join-Path $scriptsRoot 'archive.ps1'
function global:Invoke-WebRequest {
    param([Uri]$Uri, [hashtable]$Headers, [switch]$SkipHttpErrorCheck)
    if ($Uri.AbsoluteUri -like 'https://registry.npmjs.org/-/npm/v1/attestations/*') { return Get-NpmProvenanceFixture }
    if ($Uri.AbsoluteUri -ne 'https://registry.npmjs.org/%40example%2Fwidget/0.1.0') { throw 'Unexpected registry check.' }
    return [pscustomobject]@{StatusCode=404}
}

Invoke-Case 'Successful exact default-branch quality run' { & $gate } $false
$global:npmQualification_run.path='.github/workflows/quality.yml@main'
Invoke-Case 'Documented ref-suffixed workflow path accepted' { & $gate } $false
$global:npmQualification_run.path='.github/workflows/quality.yml'
$global:npmQualification_environment.deployment_branch_policy.custom_branch_policies=$false
Invoke-Case 'Unrestricted release environment rejected' { & $gate } $true
$global:npmQualification_environment.deployment_branch_policy.custom_branch_policies=$true
$global:npmQualification_policies.branch_policies[0].name='feature/*'
Invoke-Case 'Non-default branch environment policy rejected' { & $gate } $true
$global:npmQualification_policies.branch_policies[0].name='main'; $global:npmQualification_policies.branch_policies[0].type='tag'
Invoke-Case 'Main-named tag cannot satisfy release branch policy' { & $gate } $true
$global:npmQualification_policies.branch_policies[0].type='branch'
$global:npmQualification_run.head_sha='0000000000000000000000000000000000000000'
Invoke-Case 'Another commit cannot qualify release' { & $gate } $true
$global:npmQualification_run.head_sha=$env:GITHUB_SHA
$global:npmQualification_run.conclusion='failure'
Invoke-Case 'Failed quality cannot qualify release' { & $gate } $true
$global:npmQualification_run.conclusion='success'; $global:npmQualification_run.path='.github/workflows/another.yml'
Invoke-Case 'Another workflow cannot qualify release' { & $gate } $true
$global:npmQualification_run.path='.github/workflows/quality.yml'; $global:npmQualification_run.event='pull_request'
Invoke-Case 'PR run cannot qualify default-branch release' { & $gate } $true
$global:npmQualification_run.event='push'; $env:GITHUB_REF='refs/heads/feature/other'
Invoke-Case 'Non-default dispatch rejected' { & $gate } $true
$env:GITHUB_REF='refs/heads/main'; $env:GITHUB_EVENT_NAME='push'
Invoke-Case 'Push cannot execute release dispatcher' { & $gate } $true
$env:GITHUB_EVENT_NAME='workflow_dispatch'
Invoke-Case 'Qualified archive dry run' { & $publisher; if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_publishArguments -notcontains '--dry-run' -or $global:npmQualification_publishArguments -notcontains '--ignore-scripts') { throw 'Dry run contract failed' } } $false
$env:NPM_SHA256='0' * 64
Invoke-Case 'Wrong archive bytes rejected before publisher' { & $publisher } $true
$env:NPM_SHA256=$global:npmQualification_originalHash; $env:NPM_VERSION='0.1.1'
Invoke-Case 'Wrong manifest version rejected before publisher' { & $publisher } $true
$env:NPM_VERSION='0.1.0.1'
Invoke-Case 'Internal four-part public version rejected' { & $publisher } $true
$env:NPM_VERSION='0.1.0'; $env:GITHUB_REPOSITORY='example/other'
Invoke-Case 'Wrong provenance repository rejected before publisher' { & $publisher } $true
$env:GITHUB_REPOSITORY='example/widget'; $env:NPM_EXECUTE='true'; $global:npmQualification_registryIntegrity=$global:npmQualification_integrity
Invoke-Case 'Public registry identity and provenance accepted' { & $publisher; if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 1 -or $global:npmQualification_publishArguments -contains '--dry-run') { throw 'Publication verification contract failed' } } $false
$global:npmQualification_registryProvenance=$false
Invoke-Case 'Public source still requires registry provenance' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 10) { throw 'Provenance read verification must never republish.' }
$env:SOURCE_PRIVATE='true'
Invoke-Case 'Private source verifies integrity without unsupported provenance' { & $publisher; if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 1 -or $global:npmQualification_publishArguments -notcontains '--provenance=false') { throw 'Private source publication contract failed.' } } $false
$global:npmQualification_registryIntegrity='sha512-wrong'
Invoke-Case 'Private source still rejects wrong public archive integrity' { & $publisher } $true
$env:SOURCE_PRIVATE=''
Invoke-Case 'Missing source visibility rejected before upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Missing source visibility reached upload.' }
$env:SOURCE_PRIVATE='false'; $global:npmQualification_registryProvenance=$true
$global:npmQualification_registryIntegrity=$global:npmQualification_integrity.Replace('sha512-','sha512-').ToUpperInvariant()
Invoke-Case 'Case-changed base64 integrity rejected after one simulated publish' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 1) { throw 'Verification must not repeat publication' }
$env:NPM_EXECUTE='false'
$env:NPM_BOOTSTRAP='true'
Invoke-Case 'Bootstrap cannot run as a dry run' { & $publisher } $true
$env:NPM_EXECUTE='true'; $env:NODE_AUTH_TOKEN=''
Invoke-Case 'Bootstrap missing credential rejected' { & $publisher } $true
function global:Invoke-WebRequest {
    param([Uri]$Uri, [hashtable]$Headers, [switch]$SkipHttpErrorCheck)
    if ($Uri.AbsoluteUri -like 'https://registry.npmjs.org/-/npm/v1/attestations/*') { return Get-NpmProvenanceFixture }
    $global:npmQualification_metadataAuthenticated = $Headers.Authorization -ceq "Bearer $env:NODE_AUTH_TOKEN"
    if ($Uri.AbsoluteUri -ne 'https://registry.npmjs.org/%40example%2Fwidget') { throw 'Unexpected metadata destination.' }
    if ($global:npmQualification_privatePackage) {
        return [pscustomobject]@{StatusCode=$(if ($global:npmQualification_metadataAuthenticated) { 200 } else { 404 })}
    }
    [pscustomobject]@{StatusCode=$global:npmQualification_registryStatus}
}
$env:NODE_AUTH_TOKEN='SYNTHETIC-QUALIFICATION-FIXTURE-NOT-A-CREDENTIAL'
$global:npmQualification_registryStatus=200
Invoke-Case 'Bootstrap cannot publish an existing package' { & $publisher } $true
$global:npmQualification_registryStatus=503
Invoke-Case 'Registry failure cannot stand in for absent package' { & $publisher } $true
$global:npmQualification_registryIntegrity=$global:npmQualification_integrity
$global:npmQualification_privatePackage=$true
Invoke-Case 'Existing private package visible to publisher credential rejected before upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Existing private package must be rejected before upload.' }
$global:npmQualification_privatePackage=$false
$global:npmQualification_registryStatus=404; $global:npmQualification_registryIntegrity=$global:npmQualification_integrity
Invoke-Case 'Absent package accepts explicit first-publication mode' { & $publisher; if ($global:npmQualification_publishCalls -ne 1 -or -not $global:npmQualification_metadataAuthenticated) { throw 'Expected one bootstrap upload after authenticated absence check' } } $false
$env:NPM_EXECUTE='false'; $env:NPM_BOOTSTRAP='false'; $env:NODE_AUTH_TOKEN=''

$env:GITHUB_EVENT_PATH = Join-Path $routeRoot 'workflow-event.json'
$eventProof=@{action='completed';workflow_run=@{id=$env:QUALITY_RUN;head_sha=$env:GITHUB_SHA;head_repository=@{full_name=$env:GITHUB_REPOSITORY}}}
$env:GITHUB_EVENT_NAME='workflow_run'
$eventProof | ConvertTo-Json -Depth 5 | Set-Content $env:GITHUB_EVENT_PATH
Invoke-Case 'Completed matching workflow_run accepts automatic release' { & $gate } $false
foreach ($invalid in @('id','head_sha','repository','action')) {
    $changed = $eventProof | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    switch ($invalid) {
        'id' { $changed.workflow_run.id='999' }
        'head_sha' { $changed.workflow_run.head_sha='b' * 40 }
        'repository' { $changed.workflow_run.head_repository.full_name='example/fork' }
        'action' { $changed.action='requested' }
    }
    $changed | ConvertTo-Json -Depth 5 | Set-Content $env:GITHUB_EVENT_PATH
    Invoke-Case "Automatic event mismatch $invalid rejected" { & $gate } $true
}
$env:GITHUB_EVENT_NAME='workflow_dispatch'
$env:GITHUB_EVENT_NAME='workflow_run'
$eventProof | ConvertTo-Json -Depth 5 | Set-Content $env:GITHUB_EVENT_PATH
$env:GITHUB_SHA='b' * 40
Invoke-Case 'Automatic quality source survives later default-branch merge' { & $gate } $false
$env:GITHUB_SHA=$eventProof.workflow_run.head_sha
$env:GITHUB_EVENT_NAME='workflow_dispatch'
$env:NPM_EXECUTE='true';$env:SOURCE_PRIVATE='true'
$global:npmQualification_registryIntegrity=$global:npmQualification_integrity
$global:npmQualification_existingStatus=200
$global:npmQualification_existingIntegrity=$global:npmQualification_integrity
function global:Invoke-WebRequest {
    param([Uri]$Uri, [hashtable]$Headers, [switch]$SkipHttpErrorCheck)
    if ($Uri.AbsoluteUri -like 'https://registry.npmjs.org/-/npm/v1/attestations/*') { return Get-NpmProvenanceFixture }
    if ($Uri.AbsoluteUri -ne 'https://registry.npmjs.org/%40example%2Fwidget/0.1.0') { throw 'Unexpected registry check.' }
    return [pscustomobject]@{StatusCode=$global:npmQualification_existingStatus;Content=(@{name=$env:NPM_PACKAGE;version=$env:NPM_VERSION;dist=@{integrity=$global:npmQualification_existingIntegrity}} | ConvertTo-Json -Depth 5)}
}
Invoke-Case 'Already published exact bytes succeed without another upload' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 0) { throw 'A retry uploaded again.' }
$env:NPM_EXECUTE='false'
Invoke-Case 'Published exact-byte dry run succeeds without occupied-version npm call' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 0) { throw 'An occupied-version dry run reached npm publish.' }
$env:NPM_EXECUTE='true'
$global:npmQualification_existingIntegrity='sha512-wrong'
Invoke-Case 'Existing different bytes block without an upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Conflicting version reached upload.' }
$global:npmQualification_existingStatus=503
Invoke-Case 'Unknown registry state blocks without an upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Unknown registry state reached upload.' }


$global:npmQualification_existingStatus=404
$global:npmQualification_registryIntegrity=$global:npmQualification_integrity
$global:npmQualification_registryProvenance=$true
$env:SOURCE_PRIVATE='false'; $env:NPM_EXECUTE='true'
$env:GITHUB_SHA='b' * 40
Invoke-Case 'Public-source npm process receives qualified checkout SHA, not newer workflow tip' { & $publisher; if ($global:npmQualification_npmSource -cne $env:QUALIFIED_SOURCE_SHA) { throw 'npm inherited the wrong provenance source.' } } $false
$global:npmQualification_attestedSource='b' * 40
Invoke-Case 'Wrong attested source rejected after one upload without repeating' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1) { throw 'Provenance source verification repeated publication.' }
$global:npmQualification_attestedSource=$env:QUALIFIED_SOURCE_SHA
$global:npmQualification_attestedArchive='0' * 128
Invoke-Case 'Wrong attested archive rejected after one upload without repeating' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1) { throw 'Provenance artifact verification repeated publication.' }
$env:QUALIFIED_SOURCE_SHA=''
Invoke-Case 'Missing validated provenance source blocks before upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Unbound source reached publication.' }

$global:npmQualification_results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $routeRoot 'workflow-contract-results.json')
Write-Output "$($global:npmQualification_results.Count) release contracts passed; npm network mutations were replaced only at the process boundary."

} finally {
    # A unique task-created fixture contains the generated archive and extracted literal workflow steps.
    Remove-Item -LiteralPath $routeRoot -Recurse -ErrorAction Stop
}
