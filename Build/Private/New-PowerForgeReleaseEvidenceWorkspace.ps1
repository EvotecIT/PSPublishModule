function New-PowerForgeReleaseEvidenceWorkspace {
    <#
    .SYNOPSIS
    Creates a unique path for one release-evidence invocation.

    .DESCRIPTION
    Derives a stable repository namespace and adds a cryptographically random invocation identifier so
    concurrent Plan, Prepare, and Publish operations never share mutable effective-configuration files.

    .PARAMETER RepositoryRoot
    Root directory of the release checkout.

    .EXAMPLE
    New-PowerForgeReleaseEvidenceWorkspace -RepositoryRoot C:\Source\PSPublishModule
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot).ToLowerInvariant()
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($root)
        $repositoryHash = ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '').Substring(0, 16).ToLowerInvariant()
    } finally {
        $algorithm.Dispose()
    }

    $invocationId = [Guid]::NewGuid().ToString('N')
    return Join-Path ([IO.Path]::GetTempPath()) "PowerForge\ReleaseEvidence\$repositoryHash\$invocationId"
}
