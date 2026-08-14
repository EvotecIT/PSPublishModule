function Resolve-PowerForgeEffectiveConfigurationReferences {
    <#
    .SYNOPSIS
    Preserves paths whose meaning depends on the caller-owned release configuration directory.
    .PARAMETER ReleaseConfig
    Parsed release configuration that will be persisted as authorized evidence.
    .PARAMETER SourceConfigurationPath
    Absolute caller-owned release configuration path.
    .PARAMETER EvidenceDirectory
    Directory that will contain the portable authorized configuration evidence bundle.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig,

        [Parameter(Mandatory)]
        [string] $SourceConfigurationPath,

        [Parameter(Mandatory)]
        [string] $EvidenceDirectory
    )

    $toolsProperty = $ReleaseConfig.PSObject.Properties['Tools']
    if ($null -eq $toolsProperty -or $null -eq $toolsProperty.Value) {
        return $ReleaseConfig
    }

    $publishConfigProperty = $toolsProperty.Value.PSObject.Properties['DotNetPublishConfigPath']
    if ($null -eq $publishConfigProperty -or
        [string]::IsNullOrWhiteSpace([string] $publishConfigProperty.Value)) {
        return $ReleaseConfig
    }

    $publishConfigPath = [string] $publishConfigProperty.Value
    if (-not [IO.Path]::IsPathRooted($publishConfigPath)) {
        $publishConfigPath = Join-Path (Split-Path -Parent $SourceConfigurationPath) $publishConfigPath
    }
    $publishConfigPath = [IO.Path]::GetFullPath($publishConfigPath)
    if (-not (Test-Path -LiteralPath $publishConfigPath -PathType Leaf)) {
        throw "DotNet publish configuration '$publishConfigPath' was not found."
    }

    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $hashAlgorithm.ComputeHash([IO.File]::ReadAllBytes($publishConfigPath))
        $publishConfigSha256 = ([BitConverter]::ToString($hashBytes)).Replace('-', '').ToLowerInvariant()
    } finally {
        $hashAlgorithm.Dispose()
    }
    $portableFileName = ".release.dotnetpublish.${publishConfigSha256}.json"
    $portablePath = Join-Path $EvidenceDirectory $portableFileName
    Copy-Item -LiteralPath $publishConfigPath -Destination $portablePath -Force
    $publishConfigProperty.Value = $portableFileName
    return $ReleaseConfig
}
