function Set-PowerForgeAuthorizedReleaseVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig,

        [Parameter(Mandatory)]
        [ValidatePattern('^\d+\.\d+\.\d+$')]
        [string] $Version,

        [switch] $DisableVersionUpdates
    )

    if ($null -eq $ReleaseConfig.Module) {
        throw 'The release configuration does not declare a module section.'
    }
    if ($null -eq $ReleaseConfig.Packages) {
        throw 'The release configuration does not declare a packages section.'
    }

    $tracks = @($ReleaseConfig.Packages.VersionTracks.PSObject.Properties)
    if ($tracks.Count -eq 0) {
        throw 'The release configuration does not declare any package version tracks.'
    }

    $ReleaseConfig.Module.ModuleVersion = $Version
    foreach ($track in $tracks) {
        $track.Value.ExpectedVersion = $Version
    }
    if ($DisableVersionUpdates) {
        $ReleaseConfig.Packages.UpdateVersions = $false
    }

    $ReleaseConfig
}
