$ErrorActionPreference = 'Stop'
if (-not $IsLinux) {
    Write-Output 'Host publisher path-safety fixture requires Linux.'
    return
}

$scriptPath = Join-Path $PSScriptRoot '../../Deployment/Linux/Invoke-PowerForgeServerBackupHost.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $scriptPath).Path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Host publisher did not parse.' }
$exitCheck = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-ExitCode'
}, $true)
if ($null -eq $exitCheck) { throw 'Host publisher exit-code helper is missing.' }
. ([scriptblock]::Create($exitCheck.Extent.Text))
$relativePathSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-BackupRelativePath'
}, $true)
if ($null -eq $relativePathSafety) { throw 'Host publisher relative-path function is missing.' }
. ([scriptblock]::Create($relativePathSafety.Extent.Text))
$safety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-SafeBackupTarget'
}, $true)
if ($null -eq $safety) { throw 'Host publisher path-safety function is missing.' }
. ([scriptblock]::Create($safety.Extent.Text))
$catalogSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-PublishedBackupCatalog'
}, $true)
if ($null -eq $catalogSafety) { throw 'Host publisher catalog-safety function is missing.' }
. ([scriptblock]::Create($catalogSafety.Extent.Text))
$stageCleanup = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Remove-AbandonedBackupStage'
}, $true)
if ($null -eq $stageCleanup) { throw 'Host publisher stage cleanup function is missing.' }
. ([scriptblock]::Create($stageCleanup.Extent.Text))
$retentionSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-CaptureWithinRetention'
}, $true)
if ($null -eq $retentionSafety) { throw 'Host publisher retention function is missing.' }
. ([scriptblock]::Create($retentionSafety.Extent.Text))
$destinationPin = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-PinnedBackupDestination'
}, $true)
if ($null -eq $destinationPin) { throw 'Host publisher destination pin function is missing.' }
. ([scriptblock]::Create($destinationPin.Extent.Text))
$knownHostsSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-ReadableKnownHostFile'
}, $true)
if ($null -eq $knownHostsSafety) { throw 'Host publisher known-hosts function is missing.' }
. ([scriptblock]::Create($knownHostsSafety.Extent.Text))
$passwordSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-LockedPasswordStatus'
}, $true)
if ($null -eq $passwordSafety) { throw 'Host publisher password-status function is missing.' }
. ([scriptblock]::Create($passwordSafety.Extent.Text))
$helperModeSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-ExecutableRootHelper'
}, $true)
if ($null -eq $helperModeSafety) { throw 'Host publisher helper-mode function is missing.' }
. ([scriptblock]::Create($helperModeSafety.Extent.Text))
$recipientSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-LiteralAgeRecipient'
}, $true)
if ($null -eq $recipientSafety) { throw 'Host publisher recipient function is missing.' }
. ([scriptblock]::Create($recipientSafety.Extent.Text))
$branchSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-GitBackupBranch'
}, $true)
if ($null -eq $branchSafety) { throw 'Host publisher Git branch function is missing.' }
. ([scriptblock]::Create($branchSafety.Extent.Text))
$identityLocationSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-PublisherIdentityLocation'
}, $true)
if ($null -eq $identityLocationSafety) { throw 'Host publisher identity-location function is missing.' }
. ([scriptblock]::Create($identityLocationSafety.Extent.Text))
$captureNameBuilder = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Get-HostCaptureName'
}, $true)
if ($null -eq $captureNameBuilder) { throw 'Host publisher capture-name function is missing.' }
. ([scriptblock]::Create($captureNameBuilder.Extent.Text))
$workRootSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-WorkRootLocation'
}, $true)
if ($null -eq $workRootSafety) { throw 'Host publisher work-root function is missing.' }
. ([scriptblock]::Create($workRootSafety.Extent.Text))
$privateDirectorySafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-PrivatePublisherDirectory'
}, $true)
if ($null -eq $privateDirectorySafety) { throw 'Host publisher private-directory function is missing.' }
. ([scriptblock]::Create($privateDirectorySafety.Extent.Text))
$lockBudgetSafety = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-HostCaptureLockBudget'
}, $true)
if ($null -eq $lockBudgetSafety) { throw 'Host publisher lock-budget function is missing.' }
. ([scriptblock]::Create($lockBudgetSafety.Extent.Text))
. (Join-Path $PSScriptRoot '../../.github/actions/powerforge-server-backup/PowerForgeBackupCatalog.ps1')
. (Join-Path $PSScriptRoot '../../.github/actions/powerforge-server-backup/PowerForgeBackupSupport.ps1')

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('powerforge-backup-path-' + [Guid]::NewGuid().ToString('N'))
$checkout = Join-Path $fixture 'checkout'
$outside = Join-Path $fixture 'outside'
try {
    New-Item -ItemType Directory -Path $checkout, $outside | Out-Null
    $safe = Assert-SafeBackupTarget -Checkout $checkout -RelativePath 'ovh/example'
    if ($safe -ne (Join-Path $checkout 'ovh/example')) { throw 'Missing safe path was rejected.' }
    foreach ($invalid in @('backups/', 'backups//daily', '/backups', 'backups/../daily', '.git/backups', 'backups/.git/daily')) {
        try {
            Assert-BackupRelativePath $invalid
            throw 'Invalid backup path passed preflight.'
        } catch {
            if ($_.Exception.Message -eq 'Invalid backup path passed preflight.') { throw }
        }
    }
    Assert-LiteralAgeRecipient 'age18mnmcf7j440ethr6459dvpjy540ll7q2e0088n6gjm4wlmft4cpqhxrmnd'
    foreach ($invalid in @('age1abc123', 'age18mnmcf7j440ethr6459dvpjy540ll7q2e0088n6gjm4wlmft4cpqhxrmnq',
            'AGE18mnmcf7j440ethr6459dvpjy540ll7q2e0088n6gjm4wlmft4cpqhxrmnd')) {
        try {
            Assert-LiteralAgeRecipient $invalid
            throw 'Invalid age recipient passed preflight.'
        } catch {
            if ($_.Exception.Message -eq 'Invalid age recipient passed preflight.') { throw }
        }
    }
    Assert-GitBackupBranch 'main'
    foreach ($invalid in @('main/', 'main//daily', 'main.lock', 'main.', '-main', 'HEAD')) {
        try {
            Assert-GitBackupBranch $invalid
            throw 'Git-invalid backup branch passed preflight.'
        } catch {
            if ($_.Exception.Message -eq 'Git-invalid backup branch passed preflight.') { throw }
        }
    }
    $privateSsh = '/var/lib/powerforge-example-publisher/.ssh'
    Assert-PublisherIdentityLocation -IdentityFile "$privateSsh/backup_ed25519" -SshDirectory $privateSsh
    foreach ($invalid in @('/tmp/backup_ed25519', '/var/lib/powerforge-example-publisher/work/backup_ed25519')) {
        try {
            Assert-PublisherIdentityLocation -IdentityFile $invalid -SshDirectory $privateSsh
            throw 'Publisher key outside private SSH directory passed preflight.'
        } catch {
            if ($_.Exception.Message -eq 'Publisher key outside private SSH directory passed preflight.') { throw }
        }
    }
    $fixedTime = [DateTimeOffset]::Parse('2026-09-25T04:00:00Z')
    $captureNames = @(1..25 | ForEach-Object { Get-HostCaptureName -Now $fixedTime })
    if (@($captureNames | Sort-Object -Unique).Count -ne $captureNames.Count -or
        @($captureNames | Where-Object { $_ -cnotmatch '^20260925T040000Z-\d+-1$' }).Count -ne 0) {
        throw 'Concurrent host captures did not receive distinct catalog-compatible names.'
    }

    New-Item -ItemType SymbolicLink -Path (Join-Path $checkout 'ovh') -Target $outside | Out-Null
    try {
        Assert-SafeBackupTarget -Checkout $checkout -RelativePath 'ovh/example' | Out-Null
        throw 'Existing parent symlink was accepted.'
    } catch {
        if ($_.Exception.Message -eq 'Existing parent symlink was accepted.') { throw }
    }
    Remove-Item -LiteralPath (Join-Path $checkout 'ovh')

    New-Item -ItemType SymbolicLink -Path (Join-Path $checkout 'ovh') -Target (Join-Path $fixture 'missing') | Out-Null
    try {
        Assert-SafeBackupTarget -Checkout $checkout -RelativePath 'ovh/example' | Out-Null
        throw 'Dangling parent symlink was accepted.'
    } catch {
        if ($_.Exception.Message -eq 'Dangling parent symlink was accepted.') { throw }
    }
    Remove-Item -LiteralPath (Join-Path $checkout 'ovh')

    $target = Join-Path $checkout 'ovh/example'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    New-Item -ItemType SymbolicLink -Path (Join-Path $target 'index.json') -Target (Join-Path $outside 'index.json') | Out-Null
    try {
        Assert-SafeBackupTarget -Checkout $checkout -RelativePath 'ovh/example' | Out-Null
        throw 'Catalog symlink was accepted.'
    } catch {
        if ($_.Exception.Message -eq 'Catalog symlink was accepted.') { throw }
    }
    Remove-Item -LiteralPath (Join-Path $target 'index.json')
    Assert-SafeBackupTarget -Checkout $checkout -RelativePath 'ovh/example' | Out-Null

    $older = '20260925T020000Z-1-1'
    $newer = '20260925T020001Z-2-1'
    foreach ($name in @($older, $newer)) {
        $capture = Join-Path $target $name
        New-Item -ItemType Directory -Path $capture | Out-Null
        Set-Content -LiteralPath (Join-Path $capture 'capture-summary.json') -Value '{"commandResults":[],"warnings":[]}'
    }
    Update-BackupCatalog -TargetRoot $target -TargetRelative 'ovh/example' -KeepLatestInTree 24
    Assert-PublishedBackupCatalog -TargetRoot $target -CaptureName $older
    $captures = @(Get-BackupCaptureDirectory -TargetRoot $target)
    Assert-CaptureWithinRetention -Captures $captures -CaptureName $older -KeepLatest 2
    try {
        Assert-CaptureWithinRetention -Captures $captures -CaptureName $older -KeepLatest 1
        throw 'Superseded capture passed retention preflight.'
    } catch {
        if ($_.Exception.Message -eq 'Superseded capture passed retention preflight.') { throw }
    }
    Assert-CaptureWithinRetention -Captures $captures -CaptureName $newer -KeepLatest 1
    Assert-PinnedBackupDestination -Repository 'Owner/Backups' -Branch 'main' `
        -ExpectedRepository 'Owner/Backups' -ExpectedBranch 'main'
    Assert-WorkRootLocation -Path '/var/lib/powerforge-example-publisher/work' `
        -ExpectedUser 'powerforge-example-publisher'
    foreach ($invalid in @('/var/lib/powerforge-example-publisher/work/', '/tmp/powerforge-example-publisher/work')) {
        try {
            Assert-WorkRootLocation -Path $invalid -ExpectedUser 'powerforge-example-publisher'
            throw 'Unsafe publisher work root passed preflight.'
        } catch {
            if ($_.Exception.Message -eq 'Unsafe publisher work root passed preflight.') { throw }
        }
    }
    $privateDirectory = Join-Path $fixture 'private'
    New-Item -ItemType Directory -Path $privateDirectory | Out-Null
    & chmod 700 -- $privateDirectory
    Assert-LockedPasswordStatus -Account 'publisher' -Status @('publisher L 2026-09-25 -1 -1 -1 -1')
    foreach ($status in @('publisher P 2026-09-25 0 99999 7 -1', 'publisher NP 2026-09-25 -1 -1 -1 -1')) {
        try {
            Assert-LockedPasswordStatus -Account 'publisher' -Status @($status)
            throw 'Unlocked publisher password passed preflight.'
        } catch {
            if ($_.Exception.Message -eq 'Unlocked publisher password passed preflight.') { throw }
        }
    }
    $helper = Join-Path $fixture 'encrypted-capture-helper'
    Set-Content -LiteralPath $helper -Value '#!/bin/sh'
    & chmod 755 -- $helper
    Assert-ExecutableRootHelper $helper
    & chmod 644 -- $helper
    try {
        Assert-ExecutableRootHelper $helper
        throw 'Non-executable helper passed preflight.'
    } catch {
        if ($_.Exception.Message -eq 'Non-executable helper passed preflight.') { throw }
    }
    Assert-PrivatePublisherDirectory -Path $privateDirectory -AccountUid (& id -u).Trim()
    & chmod 755 -- $privateDirectory
    try {
        Assert-PrivatePublisherDirectory -Path $privateDirectory -AccountUid (& id -u).Trim()
        throw 'World-readable publisher directory passed preflight.'
    } catch {
        if ($_.Exception.Message -eq 'World-readable publisher directory passed preflight.') { throw }
    }
    & chmod 700 -- $privateDirectory
    Assert-HostCaptureLockBudget ([pscustomobject]@{ operationLocks = 1..8 })
    try {
        Assert-HostCaptureLockBudget ([pscustomobject]@{ operationLocks = 1..9 })
        throw 'Over-budget manifest locks passed preflight.'
    } catch {
        if ($_.Exception.Message -eq 'Over-budget manifest locks passed preflight.') { throw }
    }
    try {
        Assert-PinnedBackupDestination -Repository 'Owner/Backups' -Branch 'Main' `
            -ExpectedRepository 'Owner/Backups' -ExpectedBranch 'main'
        throw 'Branch case mismatch passed destination pin.'
    } catch {
        if ($_.Exception.Message -eq 'Branch case mismatch passed destination pin.') { throw }
    }
    $knownHosts = Join-Path $fixture 'known_hosts'
    Set-Content -LiteralPath $knownHosts -Value 'github.com example'
    Assert-ReadableKnownHostFile $knownHosts
    if ((& id -u).Trim() -ne '0') {
        & chmod 000 -- $knownHosts
        try {
            Assert-ReadableKnownHostFile $knownHosts
            throw 'Unreadable known-hosts file passed preflight.'
        } catch {
            if ($_.Exception.Message -eq 'Unreadable known-hosts file passed preflight.') { throw }
        }
        finally { & chmod 600 -- $knownHosts }
    }
    Set-Content -LiteralPath $knownHosts -Value '' -NoNewline
    try {
        Assert-ReadableKnownHostFile $knownHosts
        throw 'Empty known-hosts file passed preflight.'
    } catch {
        if ($_.Exception.Message -eq 'Empty known-hosts file passed preflight.') { throw }
    }
    try {
        Assert-PublishedBackupCatalog -TargetRoot $target -CaptureName 'missing'
        throw 'Unretained own capture was accepted.'
    } catch {
        if ($_.Exception.Message -eq 'Unretained own capture was accepted.') { throw }
    }
    Set-Content -LiteralPath (Join-Path $target 'LATEST.txt') -Value $older
    try {
        Assert-PublishedBackupCatalog -TargetRoot $target -CaptureName $older
        throw 'Stale catalog head was accepted.'
    } catch {
        if ($_.Exception.Message -eq 'Stale catalog head was accepted.') { throw }
    }
    $work = Join-Path $fixture 'work'
    $stale = Join-Path $work 'stage-20260925T020000Z-1-1'
    New-Item -ItemType Directory -Path $stale -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $stale 'plain.txt') -Value 'stale'
    Remove-AbandonedBackupStage -WorkRoot $work
    if (Test-Path -LiteralPath $stale) { throw 'Abandoned stage was retained.' }
    New-Item -ItemType Directory -Path $stale | Out-Null
    New-Item -ItemType SymbolicLink -Path (Join-Path $stale 'outside') -Target $outside | Out-Null
    Remove-AbandonedBackupStage -WorkRoot $work
    if ((Test-Path -LiteralPath $stale) -or -not (Test-Path -LiteralPath $outside)) {
        throw 'Abandoned stage cleanup followed a link or retained the stage.'
    }
    Write-Output 'Host publisher path-safety fixture passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse }
}
