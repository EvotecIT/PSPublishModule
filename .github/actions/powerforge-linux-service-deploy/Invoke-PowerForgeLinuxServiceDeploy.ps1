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

$workspace = realpath --canonicalize-existing -- $env:GITHUB_WORKSPACE
Assert-LastExitCode 'Resolving the caller repository'
$workspace = [IO.Path]::GetFullPath(([string]$workspace).Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspacePrefix = $workspace + [IO.Path]::DirectorySeparatorChar
$serviceRoot = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_SERVICE_ROOT))
if (-not [string]::Equals($serviceRoot, $workspace, [StringComparison]::Ordinal) -and
    -not $serviceRoot.StartsWith($workspacePrefix, [StringComparison]::Ordinal)) {
    throw 'service-root must remain inside the caller repository.'
}

if (-not (Test-Path -LiteralPath $serviceRoot -PathType Container)) {
    throw "Service root not found after validation: $serviceRoot"
}
$resolvedServiceRoot = realpath --canonicalize-existing -- $serviceRoot
Assert-LastExitCode 'Resolving the service root'
$resolvedServiceRoot = [IO.Path]::GetFullPath(([string]$resolvedServiceRoot).Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not [string]::Equals($resolvedServiceRoot, $workspace, [StringComparison]::Ordinal) -and
    -not $resolvedServiceRoot.StartsWith($workspacePrefix, [StringComparison]::Ordinal)) {
    throw 'service-root resolved outside the caller repository.'
}

$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$stageRoot = Join-Path $runnerTemp 'powerforge-service-deployment'
$sshRoot = Join-Path $runnerTemp 'powerforge-service-deployment-ssh'
$artifactPath = Join-Path $stageRoot 'artifact.tar'
$metadataPath = Join-Path $stageRoot 'deployment.json'
$keyPath = Join-Path $sshRoot 'id_ed25519'
$knownHostsPath = Join-Path $sshRoot 'known_hosts'
$remoteBase = "/tmp/powerforge-service-$($env:POWERFORGE_DEPLOYMENT_SERVICE)"
$target = "$($env:POWERFORGE_DEPLOYMENT_USER)@$($env:POWERFORGE_DEPLOYMENT_HOST)"
$sshOptions = @('-i', $keyPath, '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', "UserKnownHostsFile=$knownHostsPath")
$remoteCreated = $false

try {
    New-Item -ItemType Directory -Force -Path $stageRoot, $sshRoot | Out-Null
    tar --directory $resolvedServiceRoot -cf $artifactPath --exclude=.git --exclude=.github --exclude=_powerforge .
    Assert-LastExitCode 'Archiving the service artifact'

    [ordered]@{
        schemaVersion      = 1
        sourceRepository   = $env:POWERFORGE_SOURCE_REPOSITORY
        sourceSha          = $env:POWERFORGE_SOURCE_SHA.ToLowerInvariant()
        workflowRunId      = $env:GITHUB_RUN_ID
        workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
        artifactSha256     = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
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

    ssh @sshOptions -p $deploymentPort $target "sudo /usr/local/sbin/powerforge-service-deploy --service '$($env:POWERFORGE_DEPLOYMENT_SERVICE)'"
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
