function Resolve-PowerForgeEffectiveConfigurationReferences {
    <#
    .SYNOPSIS
    Preserves paths whose meaning depends on the caller-owned release configuration directory.
    .PARAMETER ReleaseConfig
    Parsed release configuration that will be persisted as authorized evidence.
    .PARAMETER SourceConfigurationPath
    Absolute caller-owned release configuration path.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig,

        [Parameter(Mandatory)]
        [string] $SourceConfigurationPath
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
    $publishConfigProperty.Value = [IO.Path]::GetFullPath($publishConfigPath)
    return $ReleaseConfig
}
