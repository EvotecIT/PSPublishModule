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

function Write-ActionOutput {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Value
    )

    "$Name=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

if ($env:RUNNER_OS -ne 'Linux') {
    throw 'PowerForge server backup requires a Linux runner.'
}
if ($env:POWERFORGE_CAPTURE_USER -notmatch '^[a-z_][a-z0-9_-]{0,31}$') {
    throw 'capture-user is not a valid Linux account name.'
}
if ($env:POWERFORGE_ENGINE_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'The action repository must resolve to an owner/repository name.'
}
if ($env:POWERFORGE_ENGINE_REF -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'The PowerForge backup action must be pinned to an exact 40-character commit.'
}
if ($env:POWERFORGE_SOURCE_SHA -notmatch '^[a-fA-F0-9]{40,64}$') {
    throw 'Unable to resolve exact caller source provenance.'
}
foreach ($requiredSecret in @(
    $env:POWERFORGE_SERVER_SSH_PRIVATE_KEY,
    $env:POWERFORGE_SERVER_SSH_KNOWN_HOSTS,
    $env:POWERFORGE_BACKUP_REPOSITORY_SSH_PRIVATE_KEY,
    $env:POWERFORGE_BACKUP_REPOSITORY_SSH_KNOWN_HOSTS
)) {
    if ([string]::IsNullOrWhiteSpace($requiredSecret)) {
        throw 'Server and backup repository SSH keys and pinned known-host entries are required.'
    }
}

$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspacePrefix = $workspace + [IO.Path]::DirectorySeparatorChar
$manifestPath = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_MANIFEST_PATH))
if (-not $manifestPath.StartsWith($workspacePrefix, [StringComparison]::Ordinal) -or
    -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'manifest-path must identify a file inside the caller repository.'
}
$engineRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_ACTION_PATH '../../..'))
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$captureAlias = [string]$manifest.target.sshAlias
$captureHost = [string]$manifest.target.host
if ($captureAlias -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
    throw 'The backup action requires target.sshAlias with a safe SSH alias.'
}
if ($captureHost -notmatch '^[A-Za-z0-9][A-Za-z0-9.:-]{0,252}$') {
    throw 'target.host is not a valid hostname or IP address.'
}
$capturePort = 0
if (-not [int]::TryParse([string]$manifest.target.sshPort, [ref]$capturePort) -or
    $capturePort -lt 1 -or $capturePort -gt 65535) {
    throw 'target.sshPort must be an integer from 1 through 65535.'
}

$backupRepository = [string]$manifest.backupTarget.repository
$backupBranch = [string]$manifest.backupTarget.branch
$backupPath = ([string]$manifest.backupTarget.path).Replace('\', '/')
$keepLatest = 0
if ($backupRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'backupTarget.repository must be an owner/repository name.'
}
if ($backupBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $backupBranch.Contains('..') -or $backupBranch.StartsWith('/')) {
    throw 'backupTarget.branch contains unsupported characters.'
}
if ($backupPath -notmatch '^[A-Za-z0-9._/-]+$' -or $backupPath.Contains('..') -or $backupPath.StartsWith('/')) {
    throw 'backupTarget.path must be a safe repository-relative path.'
}
if (-not [int]::TryParse([string]$manifest.backupTarget.retention.keepLatest, [ref]$keepLatest) -or
    $keepLatest -lt 1 -or $keepLatest -gt 365) {
    throw 'backupTarget.retention.keepLatest must be from 1 through 365.'
}
if (-not [string]::Equals([string]$manifest.backupTarget.encryption, 'age', [StringComparison]::OrdinalIgnoreCase) -or
    [string]::IsNullOrWhiteSpace([string]$manifest.backupTarget.recipient)) {
    throw 'Automated encrypted capture requires backupTarget.encryption=age and a public recipient.'
}

$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$sshRoot = Join-Path $runnerTemp 'powerforge-server-backup-ssh'
$captureRoot = Join-Path $runnerTemp 'powerforge-server-backup-capture'
$backupCheckout = Join-Path $runnerTemp 'powerforge-server-backup-repository'
$serverKey = Join-Path $sshRoot 'server_ed25519'
$serverKnownHosts = Join-Path $sshRoot 'server_known_hosts'
$backupKey = Join-Path $sshRoot 'backup_repository_ed25519'
$backupKnownHosts = Join-Path $sshRoot 'backup_repository_known_hosts'
$sshConfig = Join-Path $sshRoot 'config'
$serverSshCommand = Join-Path $sshRoot 'server-ssh'

foreach ($stagingPath in @($sshRoot, $captureRoot, $backupCheckout)) {
    $resolvedPath = [IO.Path]::GetFullPath($stagingPath)
    if (-not $resolvedPath.StartsWith($runnerTemp + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'Backup staging path escaped the runner temporary directory.'
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

try {
    New-Item -ItemType Directory -Force -Path $sshRoot | Out-Null
    Set-Content -LiteralPath $serverKey -Value $env:POWERFORGE_SERVER_SSH_PRIVATE_KEY -Encoding utf8NoBOM
    Set-Content -LiteralPath $serverKnownHosts -Value $env:POWERFORGE_SERVER_SSH_KNOWN_HOSTS -Encoding utf8NoBOM
    Set-Content -LiteralPath $backupKey -Value $env:POWERFORGE_BACKUP_REPOSITORY_SSH_PRIVATE_KEY -Encoding utf8NoBOM
    Set-Content -LiteralPath $backupKnownHosts -Value $env:POWERFORGE_BACKUP_REPOSITORY_SSH_KNOWN_HOSTS -Encoding utf8NoBOM
    @"
Host $captureAlias
  HostName $captureHost
  User $($env:POWERFORGE_CAPTURE_USER)
  Port $capturePort
  IdentityFile $serverKey
  UserKnownHostsFile $serverKnownHosts
  StrictHostKeyChecking yes
  IdentitiesOnly yes
  BatchMode yes

Host github.com-powerforge-backup
  HostName github.com
  User git
  Port 22
  IdentityFile $backupKey
  UserKnownHostsFile $backupKnownHosts
  StrictHostKeyChecking yes
  IdentitiesOnly yes
  BatchMode yes
"@ | Set-Content -LiteralPath $sshConfig -Encoding utf8NoBOM
    @'
#!/usr/bin/env bash
set -Eeuo pipefail
exec /usr/bin/ssh -F "${POWERFORGE_SERVER_SSH_CONFIG:?}" "$@"
'@ | Set-Content -LiteralPath $serverSshCommand -Encoding utf8NoBOM
    chmod 700 $sshRoot $serverSshCommand
    Assert-LastExitCode 'Protecting the backup SSH directory'
    chmod 600 $serverKey $serverKnownHosts $backupKey $backupKnownHosts $sshConfig
    Assert-LastExitCode 'Protecting the backup SSH credentials'

    $env:POWERFORGE_SERVER_SSH_CONFIG = $sshConfig
    $env:GIT_SSH_COMMAND = "ssh -F $sshConfig"
    & $serverSshCommand $captureAlias 'printf capture-host-ok'
    Assert-LastExitCode 'Verifying the capture host identity'

    $project = Join-Path $engineRoot 'PowerForge.Web.Cli/PowerForge.Web.Cli.csproj'
    dotnet build $project -c Release -f net10.0 --nologo
    Assert-LastExitCode 'Building the pinned PowerForge capture CLI'
    $cli = Join-Path $engineRoot 'PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll'
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
        throw "PowerForge capture CLI was not produced: $cli"
    }

    dotnet $cli server capture --manifest $manifestPath --out $captureRoot --ssh $serverSshCommand --encrypt-remote --fail-on-failure
    Assert-LastExitCode 'Capturing and encrypting server recovery state'

    foreach ($relativePath in @(
        'capture-summary.json',
        'manifest.json',
        'plain-files.tar.gz',
        'encrypted-secrets.tar.gz.age',
        'restore-checklist.md'
    )) {
        $path = Join-Path $captureRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Required capture artifact is missing or empty: $relativePath"
        }
    }
    $summary = Get-Content -LiteralPath (Join-Path $captureRoot 'capture-summary.json') -Raw | ConvertFrom-Json
    if (@($summary.Warnings).Count -ne 0) {
        throw "Capture summary contains warnings: $($summary.Warnings -join '; ')"
    }
    [ordered]@{
        schemaVersion      = 1
        sourceRepository   = $env:GITHUB_REPOSITORY
        sourceSha          = $env:POWERFORGE_SOURCE_SHA.ToLowerInvariant()
        engineRepository   = $env:POWERFORGE_ENGINE_REPOSITORY
        engineSha          = $env:POWERFORGE_ENGINE_REF.ToLowerInvariant()
        workflowRunId      = $env:GITHUB_RUN_ID
        workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
        capturedAtUtc      = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $captureRoot 'capture-metadata.json') -Encoding utf8NoBOM

    $hashLines = Get-ChildItem -LiteralPath $captureRoot -File -Recurse |
        Where-Object Name -ne 'SHA256SUMS.txt' |
        Sort-Object FullName |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($captureRoot, $_.FullName).Replace('\', '/')
            "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
        }
    $hashLines | Set-Content -LiteralPath (Join-Path $captureRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM

    git clone --single-branch --branch $backupBranch "git@github.com-powerforge-backup:${backupRepository}.git" $backupCheckout
    Assert-LastExitCode 'Cloning the private backup repository'

    $checkout = [IO.Path]::GetFullPath($backupCheckout).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $targetRoot = [IO.Path]::GetFullPath((Join-Path $checkout $backupPath))
    if (-not $targetRoot.StartsWith($checkout + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'Resolved backup target escaped the backup repository checkout.'
    }
    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
    $captureName = '{0}-{1}-{2}' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'), $env:GITHUB_RUN_ID, $env:GITHUB_RUN_ATTEMPT
    $destination = Join-Path $targetRoot $captureName
    New-Item -ItemType Directory -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $captureRoot -Force | Copy-Item -Destination $destination -Recurse -Force

    $captures = @(Get-ChildItem -LiteralPath $targetRoot -Directory |
        Where-Object Name -Match '^\d{8}T\d{6}Z-\d+-\d+$' |
        Sort-Object Name -Descending)
    foreach ($stale in @($captures | Select-Object -Skip $keepLatest)) {
        Remove-Item -LiteralPath $stale.FullName -Recurse -Force
    }

    git -C $checkout config user.name 'PowerForge Server Backup'
    Assert-LastExitCode 'Configuring the backup Git author'
    git -C $checkout config user.email 'powerforge-backup@users.noreply.github.com'
    Assert-LastExitCode 'Configuring the backup Git email'
    git -C $checkout add -- $backupPath
    Assert-LastExitCode 'Staging the encrypted server backup'
    git -C $checkout commit -m "Backup $($env:GITHUB_REPOSITORY) at $($env:POWERFORGE_SOURCE_SHA)"
    Assert-LastExitCode 'Committing the encrypted server backup'

    $published = $false
    $publishedCommit = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        git -C $checkout fetch origin $backupBranch
        Assert-LastExitCode 'Fetching the backup branch'
        git -C $checkout rebase "origin/$backupBranch"
        if ($LASTEXITCODE -ne 0) {
            git -C $checkout rebase --abort 2>$null
            throw 'Rebasing the backup publication failed.'
        }

        $captures = @(Get-ChildItem -LiteralPath $targetRoot -Directory |
            Where-Object Name -Match '^\d{8}T\d{6}Z-\d+-\d+$' |
            Sort-Object Name -Descending)
        foreach ($stale in @($captures | Select-Object -Skip $keepLatest)) {
            Remove-Item -LiteralPath $stale.FullName -Recurse -Force
        }
        git -C $checkout add -- $backupPath
        Assert-LastExitCode 'Restaging retained backup captures'

        git -C $checkout diff --cached --quiet "origin/$backupBranch"
        $diffFromOrigin = $LASTEXITCODE
        if ($diffFromOrigin -eq 0) {
            $publishedCommit = (git -C $checkout rev-parse "origin/$backupBranch").Trim()
            Assert-LastExitCode 'Resolving the superseding backup commit'
            $published = $true
            break
        }
        if ($diffFromOrigin -ne 1) {
            throw 'Comparing the staged backup with the remote branch failed.'
        }

        git -C $checkout diff --cached --quiet
        $stagedChanges = $LASTEXITCODE
        if ($stagedChanges -eq 1) {
            git -C $checkout commit --amend --no-edit
            Assert-LastExitCode 'Amending retained backup captures'
        } elseif ($stagedChanges -ne 0) {
            throw 'Inspecting staged backup retention changes failed.'
        }

        git -C $checkout push origin "HEAD:$backupBranch"
        if ($LASTEXITCODE -eq 0) {
            $publishedCommit = (git -C $checkout rev-parse HEAD).Trim()
            Assert-LastExitCode 'Resolving the published backup commit'
            $published = $true
            break
        }
        Start-Sleep -Seconds ($attempt * 5)
    }
    if (-not $published) {
        throw 'Backup publication failed after three push attempts.'
    }

    if ($publishedCommit -notmatch '^[a-f0-9]{40}$') {
        throw 'Unable to resolve the published backup commit.'
    }
    Write-ActionOutput -Name 'backup-repository' -Value $backupRepository
    Write-ActionOutput -Name 'backup-path' -Value $backupPath
    Write-ActionOutput -Name 'capture-name' -Value $captureName
    Write-ActionOutput -Name 'published-commit' -Value $publishedCommit
}
finally {
    foreach ($stagingPath in @($sshRoot, $captureRoot, $backupCheckout)) {
        $resolvedPath = [IO.Path]::GetFullPath($stagingPath)
        if ($resolvedPath.StartsWith($runnerTemp + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $resolvedPath)) {
            Remove-Item -LiteralPath $resolvedPath -Recurse -Force
        }
    }
}
