[CmdletBinding()]
param([switch] $ValidateOnly)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Assert-ExitCode {
    param([Parameter(Mandatory)][string] $Operation)
    if ($LASTEXITCODE -ne 0) { throw "$Operation failed with exit code $LASTEXITCODE." }
}

function Assert-ExactPath {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Description)
    if ($Path -notmatch '^/[A-Za-z0-9._/-]+$' -or $Path.Contains('//') -or
        @($Path.Split('/') | Where-Object { $_ -in @('.', '..') }).Count -ne 0) {
        throw "$Description must be an exact absolute path without shell metacharacters."
    }
}

function Assert-RootControlledFile {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Description)
    Assert-ExactPath $Path $Description
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "$Description must be a regular file."
    }
    $ownerAndMode = @(& stat -c '%u %a' -- $Path)
    Assert-ExitCode "Inspecting $Description"
    $parts = $ownerAndMode[0].Split(' ')
    if ($parts[0] -ne '0' -or (([Convert]::ToInt32($parts[1], 8) -band 0x12) -ne 0)) {
        throw "$Description must be root-owned and not group/world writable."
    }
    Assert-RootControlledPath $Path $Description
}

function Assert-RootControlledPath {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Description)
    $canonical = @(& realpath -e -- $Path)
    Assert-ExitCode "Resolving $Description"
    if ($canonical.Count -ne 1 -or $canonical[0] -ne $Path) {
        throw "$Description must use a canonical path without symbolic links."
    }
    $part = $Path
    while ($part -ne '/') {
        $ownerAndMode = @(& stat -c '%u %a' -- $part)
        Assert-ExitCode "Inspecting $Description path ownership"
        $fields = $ownerAndMode[0].Split(' ')
        if ($fields[0] -ne '0' -or (([Convert]::ToInt32($fields[1], 8) -band 0x12) -ne 0)) {
            throw "$Description path must be root-owned and not group/world writable: $part"
        }
        $part = [IO.Path]::GetDirectoryName($part.TrimEnd('/'))
        if ([string]::IsNullOrEmpty($part)) { $part = '/' }
    }
}

function Assert-SafeBackupTarget {
    param(
        [Parameter(Mandatory)][string] $Checkout,
        [Parameter(Mandatory)][string] $RelativePath
    )
    $checkoutPath = [IO.Path]::GetFullPath($Checkout)
    $current = $checkoutPath
    foreach ($part in $RelativePath.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -in @('.', '..')) {
            throw 'Backup target contains an invalid path component.'
        }
        $current = Join-Path $current $part
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not $item.PSIsContainer) {
                throw "Backup target contains a link or non-directory: $current"
            }
        }
    }
    if (-not $current.StartsWith($checkoutPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'Backup target escaped repository checkout.'
    }
    foreach ($name in @('LATEST.txt', 'index.json')) {
        $catalogPath = Join-Path $current $name
        $catalogItem = Get-Item -LiteralPath $catalogPath -Force -ErrorAction SilentlyContinue
        if ($null -ne $catalogItem) {
            if (($catalogItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                $catalogItem.PSIsContainer) {
                throw "Backup catalog contains a link or directory: $catalogPath"
            }
        }
    }
    return $current
}

function Assert-PublishedBackupCatalog {
    param(
        [Parameter(Mandatory)][string] $TargetRoot,
        [Parameter(Mandatory)][string] $CaptureName
    )
    $retained = @(Get-BackupCaptureDirectory -TargetRoot $TargetRoot)
    if ($retained.Count -eq 0) { throw 'Published backup catalog has no retained captures.' }
    $latest = (Get-Content -LiteralPath (Join-Path $TargetRoot 'LATEST.txt') -Raw).Trim()
    $index = Get-Content -LiteralPath (Join-Path $TargetRoot 'index.json') -Raw | ConvertFrom-Json
    if ($latest -ne $retained[0].Name -or $index.latest -ne $retained[0].Name -or
        @($index.captures | Where-Object { $_.stamp -eq $CaptureName }).Count -ne 1) {
        throw 'Published backup catalog does not match the retained captures.'
    }
}

function Assert-CaptureWithinRetention {
    param(
        [Parameter(Mandatory)][object[]] $Captures,
        [Parameter(Mandatory)][string] $CaptureName,
        [Parameter(Mandatory)][int] $KeepLatest
    )
    if (@($Captures | Select-Object -First $KeepLatest |
        Where-Object { $_.Name -ceq $CaptureName }).Count -ne 1) {
        throw 'This capture was superseded by newer captures before publication; no backup commit was pushed.'
    }
}

function Assert-ReadableKnownHostFile {
    param([Parameter(Mandatory)][string] $Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        if ($stream.Length -eq 0) { throw 'GitHub known-hosts file is empty.' }
    }
    finally { $stream.Dispose() }
}

function Assert-PinnedBackupDestination {
    param(
        [Parameter(Mandatory)][string] $Repository,
        [Parameter(Mandatory)][string] $Branch,
        [Parameter(Mandatory)][string] $ExpectedRepository,
        [Parameter(Mandatory)][string] $ExpectedBranch
    )
    if ($Repository -cne $ExpectedRepository -or $Branch -cne $ExpectedBranch) {
        throw 'Host backup destination does not match the root-controlled repository and branch.'
    }
}

function Assert-WorkRootLocation {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ExpectedUser
    )
    if ($Path.EndsWith('/', [StringComparison]::Ordinal) -or
        -not $Path.StartsWith("/var/lib/$ExpectedUser/", [StringComparison]::Ordinal)) {
        throw 'Backup work directory must be beneath the writable publisher directory without a trailing separator.'
    }
}

function Assert-PrivatePublisherDirectory {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $AccountUid
    )
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Publisher directory must be a real directory: $Path"
    }
    $ownerAndMode = @(& stat -c '%u %a' -- $Path)
    Assert-ExitCode 'Inspecting publisher directory'
    if ($ownerAndMode.Count -ne 1 -or $ownerAndMode[0] -ne "$AccountUid 700") {
        throw "Publisher directory must be owned by the publisher and mode 0700: $Path"
    }
}

function Assert-HostCaptureLockBudget {
    param([Parameter(Mandatory)][object] $Manifest)
    $count = if ($null -eq $Manifest.operationLocks) { 0 } else { @($Manifest.operationLocks).Count }
    if ($count -gt 8) {
        throw 'Host publisher supports at most eight operation locks within its six-hour service timeout.'
    }
}

function Remove-AbandonedBackupStage {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string] $WorkRoot)
    foreach ($stale in @(Get-ChildItem -LiteralPath $WorkRoot -Force -ErrorAction Stop |
        Where-Object { $_.Name -match '^stage-\d{8}T\d{6}Z-\d+-1$' })) {
        if (-not $stale.PSIsContainer -or
            ($stale.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not $stale.FullName.StartsWith($WorkRoot + '/', [StringComparison]::Ordinal)) {
            throw "Abandoned backup stage is unsafe to remove: $($stale.FullName)"
        }
        $links = @(Get-ChildItem -LiteralPath $stale.FullName -Recurse -Force -ErrorAction Stop |
            Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
        if ($links.Count -ne 0) { throw "Abandoned backup stage contains a link: $($stale.FullName)" }
        if ($PSCmdlet.ShouldProcess($stale.FullName, 'Remove abandoned backup stage')) {
            Remove-Item -LiteralPath $stale.FullName -Recurse
        }
    }
}

if (-not $IsLinux) { throw 'Host-initiated server backup requires Linux.' }
$lane = $env:POWERFORGE_BACKUP_LANE
$manifestPath = $env:POWERFORGE_BACKUP_MANIFEST
$engineRoot = $env:POWERFORGE_BACKUP_ENGINE_ROOT
$engineSha = $env:POWERFORGE_BACKUP_ENGINE_SHA
$runtimeSha256 = $env:POWERFORGE_BACKUP_RUNTIME_SHA256
$workRoot = $env:POWERFORGE_BACKUP_WORK_ROOT
$identityFile = $env:POWERFORGE_BACKUP_IDENTITY_FILE
$knownHostsFile = $env:POWERFORGE_BACKUP_KNOWN_HOSTS_FILE
$sourceRepository = $env:POWERFORGE_BACKUP_SOURCE_REPOSITORY
$expectedRepository = $env:POWERFORGE_BACKUP_REPOSITORY
$expectedBranch = $env:POWERFORGE_BACKUP_BRANCH

if ($lane -notmatch '^[a-z][a-z0-9-]{0,31}$' -or
    $engineSha -notmatch '^[a-f0-9]{40}$' -or
    $runtimeSha256 -notmatch '^[a-f0-9]{64}$' -or
    $sourceRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $expectedRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $expectedBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $expectedBranch.Contains('..') -or
    $expectedBranch.StartsWith('/')) {
    throw 'Host backup lane, engine revision, or source repository is invalid.'
}
$configPath = "/etc/powerforge/server-backup/$lane.env"
Assert-RootControlledFile $configPath 'Host backup environment file'
foreach ($path in @($manifestPath, $engineRoot, $workRoot, $identityFile, $knownHostsFile)) {
    Assert-ExactPath $path 'Host backup path'
}
Assert-RootControlledFile $manifestPath 'Recovery manifest'
Assert-RootControlledFile $knownHostsFile 'GitHub known-hosts file'
Assert-ReadableKnownHostFile $knownHostsFile
$expectedUser = "powerforge-$lane-publisher"
if ((& id -un) -ne $expectedUser) { throw "Host backup must run as $expectedUser." }
$account = @(& getent passwd $expectedUser)
Assert-ExitCode 'Inspecting publisher account'
$accountFields = $account[0].Split(':')
if ($account.Count -ne 1 -or $accountFields.Count -ne 7 -or
    $accountFields[5] -ne "/var/lib/$expectedUser" -or
    $accountFields[6] -notin @('/usr/sbin/nologin', '/sbin/nologin')) {
    throw 'Host publisher account must have a dedicated home and nologin shell.'
}
foreach ($path in @("$($accountFields[5])/.ssh/authorized_keys", "$($accountFields[5])/.ssh/authorized_keys2")) {
    if ($null -ne (Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue)) {
        throw 'Host publisher account must not accept SSH authorized keys.'
    }
}
Assert-WorkRootLocation -Path $workRoot -ExpectedUser $expectedUser
$accountUid = (& id -u).Trim()
foreach ($path in @($accountFields[5], "$($accountFields[5])/.ssh", $workRoot)) {
    Assert-PrivatePublisherDirectory -Path $path -AccountUid $accountUid
}
$keyStat = @(& stat -c '%u %a' -- $identityFile)
Assert-ExitCode 'Inspecting backup repository key'
if (-not (Test-Path -LiteralPath $identityFile -PathType Leaf) -or
    ((Get-Item -LiteralPath $identityFile -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    $keyStat[0] -ne "$accountUid 600") {
    throw 'Backup repository key must be a real mode-0600 file owned by the capture account.'
}
$engineStat = @(& stat -c '%u %a' -- $engineRoot)
Assert-ExitCode 'Inspecting PowerForge checkout'
$engineParts = $engineStat[0].Split(' ')
if (-not (Test-Path -LiteralPath $engineRoot -PathType Container) -or
    ((Get-Item -LiteralPath $engineRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    $engineParts[0] -ne '0' -or
    (([Convert]::ToInt32($engineParts[1], 8) -band 0x12) -ne 0)) {
    throw 'PowerForge checkout must be root-controlled.'
}
Assert-RootControlledPath $engineRoot 'PowerForge checkout'
$publisherPath = Join-Path $engineRoot 'Deployment/Linux/Invoke-PowerForgeServerBackupHost.ps1'
if ([IO.Path]::GetFullPath($PSCommandPath) -ne $publisherPath) {
    throw 'Host publisher must run from the pinned PowerForge checkout.'
}
Assert-RootControlledFile $publisherPath 'Host publisher script'
$checkoutSha = @(& git -c "safe.directory=$engineRoot" -C $engineRoot rev-parse HEAD)
Assert-ExitCode 'Resolving PowerForge checkout'
if ($checkoutSha[0] -ne $engineSha) { throw 'PowerForge checkout does not match the reviewed engine revision.' }
$engineOrigin = @(& git -c "safe.directory=$engineRoot" -C $engineRoot config --local --get remote.origin.url)
Assert-ExitCode 'Resolving PowerForge origin'
if ($engineOrigin.Count -ne 1 -or $engineOrigin[0] -ne 'https://github.com/EvotecIT/PSPublishModule.git') {
    throw 'PowerForge checkout must use the canonical engine origin.'
}
$trackedChanges = @(& git -c "safe.directory=$engineRoot" -C $engineRoot status --porcelain --untracked-files=no)
Assert-ExitCode 'Inspecting PowerForge checkout changes'
if ($trackedChanges.Count -ne 0) { throw 'PowerForge checkout contains tracked changes.' }
$runtimeRoot = Join-Path $engineRoot 'PowerForge.Web.Cli/bin/Release/net10.0'
$cli = Join-Path $runtimeRoot 'PowerForge.Web.Cli.dll'
$support = Join-Path $engineRoot '.github/actions/powerforge-server-backup/PowerForgeBackupSupport.ps1'
$catalog = Join-Path $engineRoot '.github/actions/powerforge-server-backup/PowerForgeBackupCatalog.ps1'
$sourceEncryptionHelper = Join-Path $engineRoot 'Deployment/Linux/powerforge-server-encrypted-capture.sh'
$installedEncryptionHelper = '/usr/local/sbin/powerforge-server-encrypted-capture'
Assert-RootControlledPath $runtimeRoot 'PowerForge runtime directory'
foreach ($path in @($cli, $support, $catalog, $sourceEncryptionHelper, $installedEncryptionHelper)) {
    Assert-RootControlledFile $path 'PowerForge runtime file'
}
if ((Get-FileHash -LiteralPath $sourceEncryptionHelper -Algorithm SHA256).Hash -cne
    (Get-FileHash -LiteralPath $installedEncryptionHelper -Algorithm SHA256).Hash) {
    throw 'Installed encrypted capture helper does not match the pinned engine revision.'
}
$runtimeItems = @(Get-ChildItem -LiteralPath $runtimeRoot -Recurse -Force -ErrorAction Stop)
if ($runtimeItems.Count -eq 0) { throw 'PowerForge runtime directory is empty.' }
foreach ($item in $runtimeItems) {
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "PowerForge runtime contains a link: $($item.FullName)"
    }
    $stat = @(& stat -c '%u %a' -- $item.FullName)
    Assert-ExitCode 'Inspecting PowerForge runtime ownership'
    $fields = $stat[0].Split(' ')
    if ($fields[0] -ne '0' -or (([Convert]::ToInt32($fields[1], 8) -band 0x12) -ne 0)) {
        throw "PowerForge runtime item is not root-controlled: $($item.FullName)"
    }
}
$runtimeEntries = [string[]]@($runtimeItems | Where-Object { -not $_.PSIsContainer } |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($runtimeRoot, $_.FullName).Replace('\', '/')
        "$relative $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
    })
[Array]::Sort($runtimeEntries, [StringComparer]::Ordinal)
$runtimeBytes = [Text.Encoding]::UTF8.GetBytes(($runtimeEntries -join "`n") + "`n")
$actualRuntimeSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($runtimeBytes)).ToLowerInvariant()
if ($actualRuntimeSha256 -ne $runtimeSha256) {
    throw 'Installed PowerForge runtime does not match the reviewed tree hash.'
}
. $support
. $catalog

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert-HostCaptureLockBudget $manifest
$backupRepository = [string]$manifest.backupTarget.repository
$backupBranch = [string]$manifest.backupTarget.branch
$backupPath = ([string]$manifest.backupTarget.path).Replace('\', '/')
$keepValue = if ($null -ne $manifest.backupTarget.retention.keepLatestInTree) {
    $manifest.backupTarget.retention.keepLatestInTree
} else { $manifest.backupTarget.retention.keepLatest }
$keepLatest = 0
if ($backupRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $backupBranch -notmatch '^[A-Za-z0-9._/-]+$' -or $backupBranch.Contains('..') -or $backupBranch.StartsWith('/') -or
    $backupPath -notmatch '^[A-Za-z0-9._/-]+$' -or $backupPath.Contains('..') -or $backupPath.StartsWith('/') -or
    -not [int]::TryParse([string]$keepValue, [ref]$keepLatest) -or $keepLatest -lt 1 -or $keepLatest -gt 365 -or
    $manifest.backupTarget.encryption -ne 'age' -or
    $manifest.backupTarget.recipient -notmatch '^age1[a-z0-9]+$') {
    throw 'Recovery manifest does not declare a valid encrypted backup destination and retention.'
}
Assert-PinnedBackupDestination -Repository $backupRepository -Branch $backupBranch `
    -ExpectedRepository $expectedRepository -ExpectedBranch $expectedBranch
if ($null -ne $manifest.backupTarget.retention.keepDays -or
    ($null -ne $manifest.backupTarget.retention.keepLatestInTree -and
     $null -ne $manifest.backupTarget.retention.keepLatest)) {
    throw 'Host backup retention must use one supported keepLatestInTree value.'
}
if ($ValidateOnly) {
    & dotnet $cli server validate --manifest $manifestPath
    Assert-ExitCode 'Validating host recovery manifest'
    Write-Output "Host backup lane $lane is configured for local capture and encrypted publication."
    return
}

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$captureName = '{0}-{1}-1' -f $stamp, [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$stage = Join-Path $workRoot "stage-$captureName"
$captureRoot = Join-Path $stage 'capture'
$checkout = Join-Path $stage 'repository'
Remove-AbandonedBackupStage -WorkRoot $workRoot
if (Test-Path -LiteralPath $stage) { throw 'Backup stage already exists.' }
New-Item -ItemType Directory -Path $stage | Out-Null
& chmod 700 -- $stage
Assert-ExitCode 'Protecting backup stage'

$oldGitSshCommand = $env:GIT_SSH_COMMAND
$oldGitConfigGlobal = $env:GIT_CONFIG_GLOBAL
try {
    & dotnet $cli server capture --manifest $manifestPath --out $captureRoot --local --fail-on-failure
    Assert-ExitCode 'Capturing local recovery state'
    foreach ($relative in @('capture-summary.json', 'manifest.json', 'plain-files.tar.gz', 'encrypted-secrets.tar.gz.age', 'restore-checklist.md')) {
        $path = Join-Path $captureRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Required capture artifact is missing or empty: $relative"
        }
    }
    $summary = Get-Content -LiteralPath (Join-Path $captureRoot 'capture-summary.json') -Raw | ConvertFrom-Json
    if (@($summary.warnings).Count -ne 0) { throw 'Local recovery capture reported warnings.' }
    [ordered]@{
        schemaVersion = 1
        sourceRepository = $sourceRepository
        engineRepository = 'EvotecIT/PSPublishModule'
        engineSha = $engineSha
        captureTrigger = 'host-systemd-timer'
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        retainedCapturesInTree = $keepLatest
        gitHistoryRetention = 'preserve'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $captureRoot 'capture-metadata.json') -Encoding utf8NoBOM
    $hashLines = Get-ChildItem -LiteralPath $captureRoot -File -Recurse -Force |
        Where-Object Name -ne 'SHA256SUMS.txt' |
        Sort-Object FullName |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($captureRoot, $_.FullName).Replace('\', '/')
            "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
        }
    $hashLines | Set-Content -LiteralPath (Join-Path $captureRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM

    $env:GIT_CONFIG_GLOBAL = '/dev/null'
    $env:GIT_SSH_COMMAND = "/usr/bin/ssh -F /dev/null -i $identityFile -o UserKnownHostsFile=$knownHostsFile -o GlobalKnownHostsFile=/dev/null -o StrictHostKeyChecking=yes -o IdentitiesOnly=yes -o IdentityAgent=none -o BatchMode=yes -o ConnectTimeout=15 -o ServerAliveInterval=30 -o ServerAliveCountMax=3"
    Invoke-GitWithRetry -Operation 'Cloning private backup repository' -ResetPath $checkout -Arguments @(
        'clone', '--depth', '1', '--no-tags', '--single-branch', '--branch', $backupBranch,
        "git@github.com:$backupRepository.git", $checkout)
    $targetRoot = Assert-SafeBackupTarget -Checkout $checkout -RelativePath $backupPath
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    $targetRoot = Assert-SafeBackupTarget -Checkout $checkout -RelativePath $backupPath
    $destination = Join-Path $targetRoot $captureName
    if ($null -ne (Get-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue)) {
        throw 'Recovery capture stamp already exists in the backup repository.'
    }
    New-Item -ItemType Directory -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $captureRoot -Force | Copy-Item -Destination $destination -Recurse
    & git -C $checkout config user.name 'PowerForge Server Backup'
    Assert-ExitCode 'Configuring backup Git author'
    & git -C $checkout config user.email 'powerforge-backup@users.noreply.github.com'
    Assert-ExitCode 'Configuring backup Git email'
    & git -C $checkout add -- $backupPath
    Assert-ExitCode 'Staging host recovery capture'
    & git -C $checkout commit -m "Backup $sourceRepository from host at $stamp"
    Assert-ExitCode 'Committing host recovery capture'

    $published = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Invoke-GitWithRetry -Operation 'Fetching private backup branch' -Arguments @(
            '-C', $checkout, 'fetch', '--no-tags', 'origin', "+refs/heads/${backupBranch}:refs/remotes/origin/${backupBranch}")
        $catalogPaths = @("$backupPath/LATEST.txt", "$backupPath/index.json")
        Invoke-BackupPublicationRebase -Checkout $checkout -Upstream "origin/$backupBranch" -GeneratedCatalogPaths $catalogPaths
        $targetRoot = Assert-SafeBackupTarget -Checkout $checkout -RelativePath $backupPath
        $captures = @(Get-BackupCaptureDirectory -TargetRoot $targetRoot)
        Assert-CaptureWithinRetention -Captures $captures -CaptureName $captureName -KeepLatest $keepLatest
        foreach ($stale in @($captures | Select-Object -Skip $keepLatest)) {
            Remove-Item -LiteralPath $stale.FullName -Recurse
        }
        Update-BackupCatalog -TargetRoot $targetRoot -TargetRelative $backupPath -KeepLatestInTree $keepLatest
        & git -C $checkout add -- $backupPath
        Assert-ExitCode 'Staging backup catalog'
        & git -C $checkout diff --cached --quiet
        if ($LASTEXITCODE -eq 1) {
            & git -C $checkout commit --amend --no-edit
            Assert-ExitCode 'Committing backup catalog'
        } elseif ($LASTEXITCODE -ne 0) { throw 'Inspecting staged backup catalog failed.' }
        & git -C $checkout push origin "HEAD:$backupBranch"
        if ($LASTEXITCODE -eq 0) { $published = $true; break }
        Start-Sleep -Seconds ($attempt * 5)
    }
    if (-not $published) { throw 'Publishing host recovery capture failed after three attempts.' }
    $publishedSha = @(& git -C $checkout rev-parse HEAD)
    Assert-ExitCode 'Resolving published backup commit'
    $entry = @(& git -C $checkout ls-tree --name-only $publishedSha[0] -- "$backupPath/$captureName")
    Assert-ExitCode 'Verifying published recovery capture'
    if ($entry.Count -ne 1 -or $entry[0] -ne "$backupPath/$captureName") {
        throw 'Published commit does not contain this recovery capture.'
    }
    Assert-PublishedBackupCatalog -TargetRoot $targetRoot -CaptureName $captureName
    Write-Output "Published encrypted recovery capture $captureName at $($publishedSha[0])."
}
finally {
    $env:GIT_SSH_COMMAND = $oldGitSshCommand
    $env:GIT_CONFIG_GLOBAL = $oldGitConfigGlobal
    if ($stage.StartsWith($workRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
        (Test-Path -LiteralPath $stage -PathType Container) -and
        -not ((Get-Item -LiteralPath $stage -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $stage -Recurse
    }
}
