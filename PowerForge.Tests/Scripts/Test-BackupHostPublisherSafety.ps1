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
    Write-Output 'Host publisher path-safety fixture passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse }
}
