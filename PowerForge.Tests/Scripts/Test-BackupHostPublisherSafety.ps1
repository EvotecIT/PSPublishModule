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
. (Join-Path $PSScriptRoot '../../.github/actions/powerforge-server-backup/PowerForgeBackupCatalog.ps1')

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('powerforge-backup-path-' + [Guid]::NewGuid().ToString('N'))
$checkout = Join-Path $fixture 'checkout'
$outside = Join-Path $fixture 'outside'
try {
    New-Item -ItemType Directory -Path $checkout, $outside | Out-Null
    $safe = Assert-SafeBackupTarget -Checkout $checkout -RelativePath 'ovh/example'
    if ($safe -ne (Join-Path $checkout 'ovh/example')) { throw 'Missing safe path was rejected.' }

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
    & chmod 000 -- $knownHosts
    try {
        Assert-ReadableKnownHostFile $knownHosts
        throw 'Unreadable known-hosts file passed preflight.'
    } catch {
        if ($_.Exception.Message -eq 'Unreadable known-hosts file passed preflight.') { throw }
    }
    finally { & chmod 600 -- $knownHosts }
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
    try {
        Remove-AbandonedBackupStage -WorkRoot $work
        throw 'Stage containing a symlink was deleted.'
    } catch {
        if ($_.Exception.Message -eq 'Stage containing a symlink was deleted.') { throw }
    }
    if (-not (Test-Path -LiteralPath $stale) -or -not (Test-Path -LiteralPath $outside)) {
        throw 'Unsafe stage check modified the stage or outside directory.'
    }
    Remove-Item -LiteralPath (Join-Path $stale 'outside')
    Write-Output 'Host publisher path-safety fixture passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse }
}
