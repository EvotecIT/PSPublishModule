param([string] $OutputPath)
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
$publisherPath = Join-Path $scriptsRoot 'archive.ps1'
$publisherText = [IO.File]::ReadAllText($publisherPath)
$publisherAst = [Management.Automation.Language.Parser]::ParseInput($publisherText, [ref]$null, [ref]$null)
$transport = $publisherAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-NpmRegistryRequest' }, $true)
if (-not $transport) { throw 'The literal registry HTTP transport is missing.' }
$transportPath = Join-Path $scriptsRoot 'registry-http.ps1'
[IO.File]::WriteAllText($transportPath, $transport.Extent.Text)
$httpProofPath = if ($OutputPath) { "$OutputPath.http.json" } else { Join-Path $routeRoot 'registry-http-results.json' }
& (Join-Path $PSScriptRoot 'test-npm-registry-http.ps1') -TransportPath $transportPath -OutputPath $httpProofPath
# Replace only HTTP transport for the policy cases; the real extracted transport runs above.
[IO.File]::WriteAllText($publisherPath, $publisherText.Remove($transport.Extent.StartOffset, $transport.Extent.EndOffset - $transport.Extent.StartOffset))
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
$env:RELEASE_TAG_PREFIX = ''
$env:NORMAL_RELEASE_TAG_PREFIX = ''
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
    if ($args[1] -like 'repos/example/widget/releases/tags/*') { return $global:npmQualification_productRelease | ConvertTo-Json -Compress }
    if ($args[1] -like 'repos/example/widget/git/ref/tags/*') { return @{object=$global:npmQualification_productTag} | ConvertTo-Json -Compress }
    if ($args[1] -like 'repos/example/widget/git/tags/*') { return @{object=@{type='commit';sha=$env:GITHUB_SHA}} | ConvertTo-Json -Compress }
    if ($args[1] -eq 'repos/example/widget/git/matching-refs/tags/npm-v') {
        if ('--paginate' -notin $args -or '--slurp' -notin $args) { throw 'Reservation recheck must read all pages.' }
        return ConvertTo-Json -InputObject @($global:npmQualification_reservations) -Depth 10 -Compress
    }
    if ($args[1] -like 'repos/example/widget/compare/*') { return @{status=$global:npmQualification_comparison} | ConvertTo-Json -Compress }
    throw 'Unexpected GitHub metadata request.'
}
function global:Get-Date { $global:npmQualification_now }
function global:Start-Sleep {
    param([int]$Seconds)
    $global:npmQualification_sleeps.Add($Seconds)
    $global:npmQualification_now = $global:npmQualification_now.AddSeconds($Seconds)
}
function global:Get-RegistryReadbackFixture([double] $TimeoutSeconds) {
    $global:npmQualification_viewCalls++
    $global:npmQualification_now = $global:npmQualification_now.AddSeconds([Math]::Min($global:npmQualification_readbackSeconds, $TimeoutSeconds))
    if ($global:npmQualification_viewCalls -le $global:npmQualification_pendingReads) {
        if ($global:npmQualification_readbackStatus -eq 0) { throw [Net.Http.HttpRequestException]::new('Synthetic transport failure') }
        return [pscustomobject]@{StatusCode=$global:npmQualification_readbackStatus}
    }
    $dist = @{integrity=$global:npmQualification_registryIntegrity}
    if ($global:npmQualification_registryProvenance -and $global:npmQualification_viewCalls -gt $global:npmQualification_pendingProvenanceReads) { $dist.attestations=@{provenance=@{predicateType='https://slsa.dev/provenance/v1'}} }
    return [pscustomobject]@{StatusCode=200;Content=(@{name=$global:npmQualification_publishedName;version=$env:NPM_VERSION;dist=$dist} | ConvertTo-Json -Depth 5)}
}
function global:Get-NpmProvenanceFixture {
    $global:npmQualification_attestationReads++
    if ($global:npmQualification_attestationReads -le $global:npmQualification_pendingAttestationReads) {
        return [pscustomobject]@{StatusCode=404}
    }
    $payload=@{
        predicateType='https://slsa.dev/provenance/v1'
        subject=@(@{digest=@{sha512=$global:npmQualification_attestedArchive}})
        predicate=@{buildDefinition=@{resolvedDependencies=@(@{uri="git+https://github.com/$env:GITHUB_REPOSITORY@$env:GITHUB_REF";digest=@{gitCommit=$global:npmQualification_attestedSource}})}}
    } | ConvertTo-Json -Depth 12 -Compress
    $envelope=@{payload=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))}
    $content=@{attestations=@(@{predicateType='https://slsa.dev/provenance/v1';bundle=@{dsseEnvelope=$envelope}})} | ConvertTo-Json -Depth 12 -Compress
    return [pscustomobject]@{StatusCode=200;Content=$content}
}
function global:Invoke-NpmRegistryRequest([string] $Url, [double] $TimeoutSeconds) {
    if ($TimeoutSeconds -le 0 -or $TimeoutSeconds -gt 15) { throw 'Registry requests must have a finite total deadline.' }
    $global:npmQualification_requestTimeouts.Add($TimeoutSeconds)
    if ($Url -like 'https://registry.npmjs.org/-/npm/v1/attestations/*') { return Get-NpmProvenanceFixture }
    if ($Url -ne 'https://registry.npmjs.org/%40example%2Fwidget/0.1.0') { throw 'Unexpected registry readback URL.' }
    return Get-RegistryReadbackFixture $TimeoutSeconds
}
function global:npm {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'publish') { $global:npmQualification_npmSource=$env:GITHUB_SHA; $global:npmQualification_publishCalls++; $global:npmQualification_publishArguments = @($args); return 'MOCK: no registry mutation' }
    throw 'Unexpected npm operation in qualification fixture.'
}
function Invoke-Case([string]$Name,[scriptblock]$Operation,[bool]$Reject) {
    $global:npmQualification_publishCalls=0; $global:npmQualification_viewCalls=0; $global:npmQualification_publishArguments=@()
    $global:npmQualification_attestationReads=0
    $global:npmQualification_now = [DateTime]::SpecifyKind([DateTime]'2026-01-01', [DateTimeKind]::Utc)
    $global:npmQualification_sleeps = [Collections.Generic.List[int]]::new()
    $global:npmQualification_requestTimeouts = [Collections.Generic.List[double]]::new()
    $failed=$false; $failure=''
    try { & $Operation | Out-Null } catch { $failed=$true; $failure=$_.Exception.Message }
    if ($failed -ne $Reject) { throw "Unexpected outcome for $Name`: $failure" }
    $global:npmQualification_results.Add([pscustomobject]@{case=$Name;rejected=$failed;publishCalls=$global:npmQualification_publishCalls;viewCalls=$global:npmQualification_viewCalls;waitSeconds=($global:npmQualification_sleeps | Measure-Object -Sum).Sum;message=$failure})
}
$global:npmQualification_pendingReads=0
$global:npmQualification_readbackSeconds=0
$global:npmQualification_pendingProvenanceReads=0
$global:npmQualification_pendingAttestationReads=0
$global:npmQualification_readbackStatus=404
$global:npmQualification_publishedName=$env:NPM_PACKAGE
$gate = Join-Path $scriptsRoot 'quality.ps1'
$publisher = Join-Path $scriptsRoot 'archive.ps1'
function global:Invoke-WebRequest {
    param([Uri]$Uri, [hashtable]$Headers, [switch]$SkipHttpErrorCheck)
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
$global:npmQualification_pendingReads=27
Invoke-Case 'Accepted upload becomes visible after twenty-four minutes without another upload' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 28 -or
    ($global:npmQualification_sleeps | Measure-Object -Sum).Sum -lt 1440 -or
    ($global:npmQualification_sleeps | Measure-Object -Maximum).Maximum -gt 60) { throw 'Delayed visibility must back off, keep checking and upload only once.' }
$global:npmQualification_pendingReads=2
foreach ($status in @(404,408,429,503,0)) {
    $global:npmQualification_readbackStatus=$status
    Invoke-Case "Temporary registry read failure $status settles after one upload" { & $publisher } $false
    if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 3) { throw 'Transient read recovery repeated or skipped publication.' }
}
$global:npmQualification_readbackStatus=403
Invoke-Case 'Registry authorization failure stops verification immediately' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 1 -or $global:npmQualification_sleeps.Count -ne 0) { throw 'Authorization failure must not wait or upload again.' }
$global:npmQualification_readbackStatus=404
$global:npmQualification_pendingReads=[int]::MaxValue
Invoke-Case 'Permanent publication invisibility fails at the bounded deadline' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1 -or ($global:npmQualification_sleeps | Measure-Object -Sum).Sum -ne 1800 -or
    $global:npmQualification_results[-1].message -notlike '*within 30 minutes*') { throw 'Visibility exhaustion must preserve the accepted upload and report its deadline.' }
$global:npmQualification_readbackSeconds=15
Invoke-Case 'Slow registry reads consume the verification deadline' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1 -or ($global:npmQualification_sleeps | Measure-Object -Sum).Sum -ge 1800 -or
    $global:npmQualification_now -gt ([DateTime]'2026-01-01').AddSeconds(1800)) { throw 'The deadline must include time spent reading the registry.' }
$global:npmQualification_pendingReads=26
$global:npmQualification_readbackSeconds=14.5
Invoke-Case 'Late metadata visibility caps the attestation request by the remaining window' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_attestationReads -ne 1 -or
    $global:npmQualification_requestTimeouts[-1] -ge 15 -or $global:npmQualification_requestTimeouts[-1] -le 0) { throw 'The attestation read must share the remaining verification deadline.' }
$global:npmQualification_readbackSeconds=0
$global:npmQualification_pendingReads=0
$global:npmQualification_pendingProvenanceReads=2
Invoke-Case 'Public metadata waits for required provenance after one upload' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -ne 3) { throw 'Delayed provenance metadata repeated publication.' }
$global:npmQualification_pendingProvenanceReads=0
$global:npmQualification_pendingAttestationReads=2
Invoke-Case 'Public attestation endpoint visibility settles after one upload' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_attestationReads -ne 3) { throw 'Delayed attestation visibility repeated publication.' }
$global:npmQualification_pendingAttestationReads=0
$global:npmQualification_publishedName='@example/other'
Invoke-Case 'Wrong public package identity stops verification immediately' { & $publisher } $true
if ($global:npmQualification_viewCalls -ne 1 -or $global:npmQualification_sleeps.Count -ne 0) { throw 'Identity mismatch must not be retried.' }
$global:npmQualification_publishedName=$env:NPM_PACKAGE
$global:npmQualification_registryProvenance=$false
Invoke-Case 'Public source still requires registry provenance' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1 -or $global:npmQualification_viewCalls -lt 10 -or
    ($global:npmQualification_sleeps | Measure-Object -Sum).Sum -ne 1800) { throw 'Provenance read verification must wait boundedly and never republish.' }
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
    if ($Uri.AbsoluteUri -ne 'https://registry.npmjs.org/%40example%2Fwidget/0.1.0') { throw 'Unexpected registry check.' }
    $dist=@{integrity=$global:npmQualification_existingIntegrity}
    if ($global:npmQualification_registryProvenance) { $dist.attestations=@{provenance=@{predicateType='https://slsa.dev/provenance/v1'}} }
    return [pscustomobject]@{StatusCode=$global:npmQualification_existingStatus;Content=(@{name=$env:NPM_PACKAGE;version=$env:NPM_VERSION;dist=$dist} | ConvertTo-Json -Depth 5)}
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
$global:npmQualification_existingStatus=200
$global:npmQualification_existingIntegrity=$global:npmQualification_integrity
Invoke-Case 'Verified existing public version can be reconciled after GitHub tip advances' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 0 -or $env:GITHUB_SHA -cne ('b' * 40)) { throw 'Existing public reconciliation uploaded or changed identity.' }
$env:NPM_EXECUTE='false'
Invoke-Case 'Verified existing public dry run needs no new signing identity' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 0) { throw 'Existing public dry run reached upload.' }
$global:npmQualification_existingStatus=404; $env:NPM_EXECUTE='true'
Invoke-Case 'Public source rejects mismatched OIDC identity before an upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0 -or $env:GITHUB_SHA -cne ('b' * 40)) { throw 'OIDC mismatch reached publication or changed GitHub source identity.' }
$env:NPM_EXECUTE='false'
Invoke-Case 'Public dry run rejects a provenance identity it cannot publish' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Provenance mismatch reached npm.' }
$env:NPM_EXECUTE='true'; $env:SOURCE_PRIVATE='true'
Invoke-Case 'Private source keeps actual OIDC workflow identity for a qualified older archive' {
    & $publisher
    if ($global:npmQualification_npmSource -cne ('b' * 40) -or $global:npmQualification_publishArguments -notcontains '--provenance=false') { throw 'Private source changed GitHub identity or enabled provenance.' }
} $false
$env:SOURCE_PRIVATE='false'; $env:GITHUB_SHA=$env:QUALIFIED_SOURCE_SHA
Invoke-Case 'Matching public source publishes with unchanged GitHub identity' {
    & $publisher
    if ($global:npmQualification_npmSource -cne $env:QUALIFIED_SOURCE_SHA) { throw 'Public source identity changed.' }
} $false
$global:npmQualification_attestedSource='b' * 40
Invoke-Case 'Wrong attested source rejected after one upload without repeating' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1) { throw 'Provenance source verification repeated publication.' }
$global:npmQualification_attestedSource=$env:QUALIFIED_SOURCE_SHA
$global:npmQualification_attestedArchive='0' * 128
Invoke-Case 'Wrong attested archive rejected after one upload without repeating' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 1) { throw 'Provenance artifact verification repeated publication.' }
$global:npmQualification_attestedArchive=[Convert]::ToHexString([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($Archive))).ToLowerInvariant()
$env:RELEASE_TAG_PREFIX='npm-v'
$currentReservation=@{ref='refs/tags/npm-v0.1.0';object=@{type='commit';sha=$env:QUALIFIED_SOURCE_SHA}}
$newerReservation=@{ref='refs/tags/npm-v0.1.1';object=@{type='commit';sha=('b' * 40)}}
$global:npmQualification_reservations=@(@($currentReservation,$newerReservation))
$global:npmQualification_comparison='ahead'
Invoke-Case 'Newer reservation appearing during the build blocks the old archive before upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'A newer source reservation reached an older upload.' }
$global:npmQualification_comparison='behind'
Invoke-Case 'A newer source can recover above an ancestor reservation' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 1) { throw 'Newer source did not publish once.' }
$global:npmQualification_comparison='diverged'
Invoke-Case 'Diverged reserved source blocks publication' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Diverged source reached publication.' }
$global:npmQualification_reservations=@(@($newerReservation))
Invoke-Case 'Missing current reservation blocks an automatic upload' { & $publisher } $true
$global:npmQualification_reservations=@(@(@{ref='refs/tags/npm-v0.1.0';object=@{type='commit';sha=('b' * 40)}}))
Invoke-Case 'Version reserved by another source blocks an automatic upload' { & $publisher } $true
$global:npmQualification_reservations=@(@($currentReservation))
Invoke-Case 'Exact reservation accepts one verified upload' { & $publisher } $false
if ($global:npmQualification_publishCalls -ne 1) { throw 'Reserved upload was repeated or skipped.' }
$env:NPM_EXECUTE='false'; $global:npmQualification_reservations=@(@())
Invoke-Case 'Nonpublishing dry run does not require a new reservation' { & $publisher } $false
if ($global:npmQualification_publishArguments -notcontains '--dry-run') { throw 'Unreserved dry run attempted publication.' }
$env:NPM_EXECUTE='true'; $env:RELEASE_TAG_PREFIX=''
$env:QUALIFIED_SOURCE_SHA=''
Invoke-Case 'Missing validated provenance source blocks before upload' { & $publisher } $true
if ($global:npmQualification_publishCalls -ne 0) { throw 'Unbound source reached publication.' }

$env:GITHUB_SHA=$eventProof.workflow_run.head_sha
$env:QUALIFIED_SOURCE_SHA=$env:GITHUB_SHA
$global:npmQualification_run.head_sha=$env:GITHUB_SHA
$env:NORMAL_RELEASE_TAG_PREFIX='Widget-v'
$env:GITHUB_REF='refs/tags/Widget-v0.1.0'
$env:GITHUB_EVENT_NAME='release'
$global:npmQualification_productRelease=@{id=123;tag_name='Widget-v0.1.0';draft=$false;prerelease=$false;published_at='2026-01-01T00:00:00Z'}
$global:npmQualification_productTag=@{type='commit';sha=$env:GITHUB_SHA}
$global:npmQualification_policies=@{total_count=1;branch_policies=@(@{name='Widget-v*';type='tag'})}
$global:npmQualification_comparison='ahead'
@{action='published';release=@{id=123;tag_name='Widget-v0.1.0'}} | ConvertTo-Json -Depth 5 | Set-Content $env:GITHUB_EVENT_PATH
Invoke-Case 'Published stable product release uses the qualified tag commit' { & $gate } $false
$global:npmQualification_productTag=@{type='tag';sha=('c' * 40)}
Invoke-Case 'Annotated product tag resolves to the qualified commit' { & $gate } $false
$global:npmQualification_productTag=@{type='commit';sha=$env:GITHUB_SHA}
$env:GITHUB_EVENT_NAME='workflow_dispatch'
Invoke-Case 'Dispatch on a published product tag reconciles the same release' { & $gate } $false
$env:GITHUB_EVENT_NAME='release'; $env:NORMAL_RELEASE_TAG_PREFIX=''
Invoke-Case 'Product release requires explicit tag-mode opt in' { & $gate } $true
$env:NORMAL_RELEASE_TAG_PREFIX='Widget-v'; $env:GITHUB_REF='refs/heads/main'
Invoke-Case 'Product tag mode does not authorize a branch dispatch' { & $gate } $true
$env:GITHUB_REF='refs/tags/Other-v0.1.0'
Invoke-Case 'Unrelated product namespace rejected' { & $gate } $true
$env:GITHUB_REF='refs/tags/Widget-v0.1.0.1'
Invoke-Case 'Internal four-part product tag rejected' { & $gate } $true
$env:GITHUB_REF='refs/tags/Widget-v0.1.1'
Invoke-Case 'Product tag and archive version mismatch rejected' { & $gate } $true
$env:GITHUB_REF='refs/tags/Widget-v0.1.0'; $global:npmQualification_productRelease.draft=$true
Invoke-Case 'Draft product release rejected' { & $gate } $true
$global:npmQualification_productRelease.draft=$false; $global:npmQualification_productRelease.prerelease=$true
Invoke-Case 'Prerelease product cannot publish a stable npm version' { & $gate } $true
$global:npmQualification_productRelease.prerelease=$false; $global:npmQualification_productRelease.id=999
Invoke-Case 'Event release must match the live published release' { & $gate } $true
$global:npmQualification_productRelease.id=123; $global:npmQualification_productTag.sha='b' * 40
Invoke-Case 'Moved product tag rejected before archive publication' { & $gate } $true
$global:npmQualification_productTag.sha=$env:GITHUB_SHA; $global:npmQualification_comparison='diverged'
Invoke-Case 'Product source outside default-branch history rejected' { & $gate } $true
$global:npmQualification_comparison='ahead'; $global:npmQualification_policies.branch_policies[0].name='*'
Invoke-Case 'Broad tag environment rejected' { & $gate } $true
$global:npmQualification_policies.branch_policies[0].name='Widget-v*'; $global:npmQualification_policies.total_count=2
Invoke-Case 'Additional deployment policies rejected for product releases' { & $gate } $true
$global:npmQualification_policies.total_count=1; $global:npmQualification_run.head_sha='b' * 40
Invoke-Case 'Product release still requires exact successful push quality source' { & $gate } $true
$global:npmQualification_run.head_sha=$env:GITHUB_SHA
$env:GITHUB_REF='refs/tags/Widget-v20260101000000'
$global:npmQualification_productRelease.tag_name='Widget-v20260101000000'
@{action='published';release=@{id=123;tag_name='Widget-v20260101000000'}} | ConvertTo-Json -Depth 5 | Set-Content $env:GITHUB_EVENT_PATH
Invoke-Case 'Legacy timestamp product release accepts the archive version' { & $gate } $false

$resultPath = if ($OutputPath) { $OutputPath } else { Join-Path $routeRoot 'workflow-contract-results.json' }
$global:npmQualification_results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath
Write-Output "$($global:npmQualification_results.Count) release contracts passed; npm network mutations were replaced only at the process boundary."

} finally {
    # A unique task-created fixture contains the generated archive and extracted literal workflow steps.
    Remove-Item -LiteralPath $routeRoot -Recurse -ErrorAction Stop
}
