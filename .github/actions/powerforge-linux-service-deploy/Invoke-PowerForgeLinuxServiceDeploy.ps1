[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string] $Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

if ($env:RUNNER_OS -ne 'Linux') {
    throw 'PowerForge Linux service deployment requires a Linux runner.'
}
if ($env:POWERFORGE_DEPLOYMENT_SERVICE -notmatch '^[a-z0-9][a-z0-9.-]{0,62}$') {
    throw 'deployment-service must be a lowercase DNS-style service identifier.'
}
if ($env:POWERFORGE_DEPLOYMENT_HOST -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$') {
    throw 'deployment-host must be a DNS name or IPv4 address.'
}
if ($env:POWERFORGE_DEPLOYMENT_USER -notmatch '^[a-z_][a-z0-9_-]{0,31}$') {
    throw 'deployment-user is not a valid Linux account name.'
}
$deploymentPort = 0
if (-not [int]::TryParse($env:POWERFORGE_DEPLOYMENT_PORT, [ref] $deploymentPort) -or
    $deploymentPort -lt 1 -or $deploymentPort -gt 65535) {
    throw 'deployment-port must be an integer from 1 through 65535.'
}
foreach ($requiredValue in @(
    $env:POWERFORGE_DEPLOYMENT_SSH_PRIVATE_KEY,
    $env:POWERFORGE_DEPLOYMENT_SSH_KNOWN_HOSTS
)) {
    if ([string]::IsNullOrWhiteSpace($requiredValue)) {
        throw 'The deployment SSH private key and pinned known-host entries are required.'
    }
}
if ($env:POWERFORGE_SOURCE_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'source-repository must be an owner/repository name.'
}
if ($env:POWERFORGE_SOURCE_SHA -notmatch '^[a-fA-F0-9]{40,64}$') {
    throw 'source-sha must be an exact commit.'
}
if ($env:GITHUB_RUN_ID -notmatch '^\d+$' -or $env:GITHUB_RUN_ATTEMPT -notmatch '^\d+$') {
    throw 'GitHub workflow run identity is missing or invalid.'
}

$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$stageRoot = Join-Path $runnerTemp 'powerforge-service-deployment'
$sshRoot = Join-Path $runnerTemp 'powerforge-service-deployment-ssh'
$artifactPath = Join-Path $stageRoot 'artifact.tar'
$packageMetadataPath = Join-Path $stageRoot 'package.json'
$metadataPath = Join-Path $stageRoot 'deployment.json'
$keyPath = Join-Path $sshRoot 'id_ed25519'
$knownHostsPath = Join-Path $sshRoot 'known_hosts'
$remoteBase = "/tmp/powerforge-service-$($env:POWERFORGE_DEPLOYMENT_SERVICE)-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)"
$handoffBase = "/tmp/powerforge-service-$($env:POWERFORGE_DEPLOYMENT_SERVICE)"
$remoteLock = "/tmp/powerforge-service-$($env:POWERFORGE_DEPLOYMENT_SERVICE).lock"
$target = "$($env:POWERFORGE_DEPLOYMENT_USER)@$($env:POWERFORGE_DEPLOYMENT_HOST)"
$sshOptions = @('-i', $keyPath, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', "UserKnownHostsFile=$knownHostsPath")
$remoteCreated = $false

if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
    throw "Downloaded service artifact not found: $artifactPath"
}
if (-not (Test-Path -LiteralPath $packageMetadataPath -PathType Leaf)) {
    throw "Downloaded service package metadata not found: $packageMetadataPath"
}
$resolvedArtifactPath = realpath --canonicalize-existing -- $artifactPath
Assert-LastExitCode 'Resolving the downloaded service artifact'
$resolvedArtifactPath = [IO.Path]::GetFullPath(([string]$resolvedArtifactPath).Trim())
if (-not [string]::Equals($resolvedArtifactPath, [IO.Path]::GetFullPath($artifactPath), [StringComparison]::Ordinal) -or
    (Get-Item -LiteralPath $resolvedArtifactPath).Length -eq 0) {
    throw 'The downloaded service artifact must be a non-empty regular file in the action staging directory.'
}
$resolvedPackageMetadataPath = realpath --canonicalize-existing -- $packageMetadataPath
Assert-LastExitCode 'Resolving the downloaded service package metadata'
$resolvedPackageMetadataPath = [IO.Path]::GetFullPath(([string]$resolvedPackageMetadataPath).Trim())
if (-not [string]::Equals($resolvedPackageMetadataPath, [IO.Path]::GetFullPath($packageMetadataPath), [StringComparison]::Ordinal) -or
    (Get-Item -LiteralPath $resolvedPackageMetadataPath).Length -eq 0) {
    throw 'The downloaded service package metadata must be a non-empty regular file in the action staging directory.'
}
$packageMetadata = Get-Content -LiteralPath $resolvedPackageMetadataPath -Raw | ConvertFrom-Json
$actualArtifactSha256 = (Get-FileHash -LiteralPath $resolvedArtifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not [string]::Equals([string]$packageMetadata.sourceRepository, $env:POWERFORGE_SOURCE_REPOSITORY, [StringComparison]::Ordinal) -or
    -not [string]::Equals([string]$packageMetadata.sourceSha, $env:POWERFORGE_SOURCE_SHA.ToLowerInvariant(), [StringComparison]::Ordinal) -or
    -not [string]::Equals([string]$packageMetadata.workflowRunId, $env:GITHUB_RUN_ID, [StringComparison]::Ordinal) -or
    [string]$packageMetadata.workflowRunAttempt -notmatch '^\d+$' -or
    -not [string]::Equals([string]$packageMetadata.artifactSha256, $actualArtifactSha256, [StringComparison]::Ordinal)) {
    throw 'The service artifact does not match its expected source, workflow run, or SHA-256 provenance.'
}

try {
    New-Item -ItemType Directory -Force -Path $sshRoot | Out-Null

    [ordered]@{
        schemaVersion      = 1
        sourceRepository   = $env:POWERFORGE_SOURCE_REPOSITORY
        sourceSha          = $env:POWERFORGE_SOURCE_SHA.ToLowerInvariant()
        workflowRunId      = $env:GITHUB_RUN_ID
        workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
        packageRunAttempt  = [string]$packageMetadata.workflowRunAttempt
        artifactSha256     = $actualArtifactSha256
        deployedAtUtc      = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8NoBOM

    Set-Content -LiteralPath $keyPath -Value $env:POWERFORGE_DEPLOYMENT_SSH_PRIVATE_KEY -Encoding utf8NoBOM
    Set-Content -LiteralPath $knownHostsPath -Value $env:POWERFORGE_DEPLOYMENT_SSH_KNOWN_HOSTS -Encoding utf8NoBOM
    chmod 700 $sshRoot
    Assert-LastExitCode 'Protecting the SSH directory'
    chmod 600 $keyPath $knownHostsPath
    Assert-LastExitCode 'Protecting the SSH credentials'

    ssh @sshOptions -p $deploymentPort $target "rm -rf -- '$remoteBase' && install -d -m 0700 '$remoteBase'"
    Assert-LastExitCode 'Creating the remote service staging directory'
    $remoteCreated = $true

    $scpArguments = @('-P', $deploymentPort) + $sshOptions + @($artifactPath, $metadataPath, "${target}:${remoteBase}/")
    scp @scpArguments
    Assert-LastExitCode 'Uploading the service deployment payload'

    $handoffCommand = 'flock -w 900 ''{0}'' sh -c "rm -rf -- ''{1}'' && mv -- ''{2}'' ''{1}'' && sudo /usr/local/sbin/powerforge-service-deploy --service ''{3}''; status=\$?; rm -rf -- ''{1}''; exit \$status"' -f @(
        $remoteLock,
        $handoffBase,
        $remoteBase,
        $env:POWERFORGE_DEPLOYMENT_SERVICE
    )
    ssh @sshOptions -p $deploymentPort $target $handoffCommand
    Assert-LastExitCode 'Promoting the service release'
}
finally {
    if ($remoteCreated -and (Test-Path -LiteralPath $keyPath) -and (Test-Path -LiteralPath $knownHostsPath)) {
        ssh @sshOptions -p $deploymentPort $target "rm -rf -- '$remoteBase'" 2>$null
        $global:LASTEXITCODE = 0
    }
    foreach ($path in @($stageRoot, $sshRoot)) {
        $resolvedPath = [IO.Path]::GetFullPath($path)
        if ($resolvedPath.StartsWith($runnerTemp + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $resolvedPath)) {
            Remove-Item -LiteralPath $resolvedPath -Recurse -Force
        }
    }
}
