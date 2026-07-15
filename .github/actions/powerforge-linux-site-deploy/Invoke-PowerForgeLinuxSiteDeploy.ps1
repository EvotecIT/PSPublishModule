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
    throw 'PowerForge Linux site deployment requires a Linux runner.'
}
if ($env:POWERFORGE_DEPLOYMENT_SITE -notmatch '^[a-z0-9][a-z0-9.-]{0,62}$') {
    throw 'deployment-site must be a lowercase DNS-style site identifier.'
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
if ($env:POWERFORGE_ENGINE_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'engine-repository must be an owner/repository name.'
}
if ($env:POWERFORGE_ENGINE_MODE -notin @('source', 'binary')) {
    throw 'engine-mode must be source or binary.'
}
if ($env:POWERFORGE_ENGINE_MODE -eq 'source' -and $env:POWERFORGE_ENGINE_SHA -notmatch '^[a-fA-F0-9]{40,64}$') {
    throw 'engine-sha must be an exact commit in source mode.'
}
if ($env:POWERFORGE_ENGINE_MODE -eq 'binary' -and
    ([string]::IsNullOrWhiteSpace($env:POWERFORGE_ENGINE_ASSET) -or
     $env:POWERFORGE_ENGINE_ASSET_SHA256 -notmatch '^[a-fA-F0-9]{64}$')) {
    throw 'engine-asset and its SHA-256 are required in binary mode.'
}
if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_ZONE)) {
    if ($env:POWERFORGE_CLOUDFLARE_ZONE -notmatch '^[A-Za-z0-9.-]+$') {
        throw 'deployment-cloudflare-zone must be a DNS zone name.'
    }
    if ([string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_API_TOKEN)) {
        throw 'deployment-cloudflare-api-token is required when a Cloudflare zone is configured.'
    }
}

$artifactPath = [IO.Path]::GetFullPath($env:POWERFORGE_ARTIFACT_PATH)
if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
    throw "Downloaded site artifact not found: $artifactPath"
}

$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$sshRoot = Join-Path $runnerTemp 'powerforge-site-deployment-ssh'
$metadataPath = Join-Path $runnerTemp 'deployment.json'
$remoteBase = "/tmp/powerforge-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)-$($env:POWERFORGE_DEPLOYMENT_SITE)"
$target = "$($env:POWERFORGE_DEPLOYMENT_USER)@$($env:POWERFORGE_DEPLOYMENT_HOST)"
$keyPath = Join-Path $sshRoot 'id_ed25519'
$knownHostsPath = Join-Path $sshRoot 'known_hosts'
$sshOptions = @('-i', $keyPath, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', "UserKnownHostsFile=$knownHostsPath")
$remoteCreated = $false

try {
    $metadata = [ordered]@{
        schemaVersion      = 1
        sourceRepository   = $env:POWERFORGE_SOURCE_REPOSITORY
        sourceSha          = $env:POWERFORGE_SOURCE_SHA.ToLowerInvariant()
        engineMode         = $env:POWERFORGE_ENGINE_MODE
        engineRepository   = $env:POWERFORGE_ENGINE_REPOSITORY
        engineRef          = $env:POWERFORGE_ENGINE_REF
        workflowRunId      = $env:GITHUB_RUN_ID
        workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
        artifactSha256     = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        deployedAtUtc      = [DateTimeOffset]::UtcNow.ToString('O')
    }
    if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_ENGINE_SHA)) {
        $metadata['engineSha'] = $env:POWERFORGE_ENGINE_SHA.ToLowerInvariant()
    }
    if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_ENGINE_ASSET)) {
        $metadata['engineAsset'] = $env:POWERFORGE_ENGINE_ASSET
        $metadata['engineAssetSha256'] = $env:POWERFORGE_ENGINE_ASSET_SHA256.ToLowerInvariant()
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8NoBOM

    New-Item -ItemType Directory -Force -Path $sshRoot | Out-Null
    Set-Content -LiteralPath $keyPath -Value $env:POWERFORGE_DEPLOYMENT_SSH_PRIVATE_KEY -Encoding utf8NoBOM
    Set-Content -LiteralPath $knownHostsPath -Value $env:POWERFORGE_DEPLOYMENT_SSH_KNOWN_HOSTS -Encoding utf8NoBOM
    chmod 700 $sshRoot
    Assert-LastExitCode 'Protecting the SSH directory'
    chmod 600 $keyPath $knownHostsPath
    Assert-LastExitCode 'Protecting the SSH credentials'

    $deploymentSources = @($artifactPath, $metadataPath)
    if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_ZONE)) {
        $zoneId = [string]$env:POWERFORGE_CLOUDFLARE_ZONE_ID
        if ([string]::IsNullOrWhiteSpace($zoneId)) {
            $headers = @{ Authorization = "Bearer $($env:POWERFORGE_CLOUDFLARE_API_TOKEN)" }
            $zoneName = [Uri]::EscapeDataString($env:POWERFORGE_CLOUDFLARE_ZONE)
            $response = Invoke-RestMethod -Method Get -Uri "https://api.cloudflare.com/client/v4/zones?name=$zoneName&status=active&per_page=5" -Headers $headers
            if (-not $response.success -or @($response.result).Count -ne 1) {
                throw "Cloudflare did not return exactly one active zone for '$($env:POWERFORGE_CLOUDFLARE_ZONE)'."
            }
            $zoneId = [string]$response.result[0].id
        }
        if ($zoneId -notmatch '^[A-Fa-f0-9]{32}$') {
            throw 'cloudflare-zone-id is invalid and zone discovery did not return a valid id.'
        }
        $cloudflareTokenPath = Join-Path $sshRoot 'cloudflare-api.token'
        $cloudflareZonePath = Join-Path $sshRoot 'cloudflare-zone-id'
        Set-Content -LiteralPath $cloudflareTokenPath -Value $env:POWERFORGE_CLOUDFLARE_API_TOKEN -Encoding utf8NoBOM -NoNewline
        Set-Content -LiteralPath $cloudflareZonePath -Value $zoneId.ToLowerInvariant() -Encoding ascii -NoNewline
        chmod 600 $cloudflareTokenPath $cloudflareZonePath
        Assert-LastExitCode 'Protecting the Cloudflare deployment credentials'
        $deploymentSources += $cloudflareTokenPath, $cloudflareZonePath
    }

    ssh @sshOptions -p $deploymentPort $target "install -d -m 0700 '$remoteBase'"
    Assert-LastExitCode 'Creating the remote site staging directory'
    $remoteCreated = $true

    $scpArguments = @('-P', $deploymentPort) + $sshOptions + $deploymentSources + @("${target}:${remoteBase}/")
    scp @scpArguments
    Assert-LastExitCode 'Uploading the site deployment payload'

    ssh @sshOptions -p $deploymentPort $target "sudo /usr/local/sbin/powerforge-site-deploy --site '$($env:POWERFORGE_DEPLOYMENT_SITE)' --archive '$remoteBase/artifact.tar' --metadata '$remoteBase/deployment.json'"
    Assert-LastExitCode 'Promoting the site release'
}
finally {
    if ($remoteCreated -and (Test-Path -LiteralPath $keyPath) -and (Test-Path -LiteralPath $knownHostsPath)) {
        ssh @sshOptions -p $deploymentPort $target "rm -rf -- '$remoteBase'" 2>$null
        $global:LASTEXITCODE = 0
    }
    foreach ($path in @($sshRoot, $metadataPath)) {
        $resolvedPath = [IO.Path]::GetFullPath($path)
        if ($resolvedPath.StartsWith($runnerTemp + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $resolvedPath)) {
            Remove-Item -LiteralPath $resolvedPath -Recurse -Force
        }
    }
}
