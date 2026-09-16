function Set-PowerForgePublicReleaseAuthorizationReceipt {
    <#
    .SYNOPSIS
    Persists the release-source and tool authorization binding in a successful public-release receipt.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ReceiptPath,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{40}$')]
        [string] $ReleaseCommit,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{40}$')]
        [string] $ToolCommit,

        [Parameter(Mandatory)]
        [string] $ReleaseSourceRoot,

        [Parameter(Mandatory)]
        [string] $EffectiveConfigPath,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{64}$')]
        [string] $EffectiveConfigSha256
    )

    $resolvedReceiptPath = (Resolve-Path -LiteralPath $ReceiptPath).Path
    $receipt = Get-Content -Raw -LiteralPath $resolvedReceiptPath | ConvertFrom-Json -Depth 100
    if ($receipt.Success -ne $true) {
        throw 'Only a successful public-release receipt can receive an authorization binding.'
    }

    $authorization = [pscustomobject] [ordered]@{
        ReleaseCommit        = $ReleaseCommit.ToLowerInvariant()
        ToolCommit           = $ToolCommit.ToLowerInvariant()
        ReleaseSourceRoot    = [IO.Path]::GetFullPath($ReleaseSourceRoot)
        EffectiveConfigPath  = [IO.Path]::GetFullPath($EffectiveConfigPath)
        EffectiveConfigSha256 = $EffectiveConfigSha256.ToLowerInvariant()
    }
    $receipt | Add-Member `
        -NotePropertyName ReleaseAuthorization `
        -NotePropertyValue $authorization `
        -Force

    $temporaryPath = "$resolvedReceiptPath.$([Guid]::NewGuid().ToString('N')).tmp"
    $backupPath = "$resolvedReceiptPath.$([Guid]::NewGuid().ToString('N')).bak"
    try {
        $encoding = [Text.UTF8Encoding]::new($true)
        [IO.File]::WriteAllText(
            $temporaryPath,
            ($receipt | ConvertTo-Json -Depth 100),
            $encoding)
        [IO.File]::Replace($temporaryPath, $resolvedReceiptPath, $backupPath, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -ErrorAction SilentlyContinue
        }
    }
}
