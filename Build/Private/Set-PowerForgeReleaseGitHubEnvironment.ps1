function Set-PowerForgeReleaseGitHubEnvironment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig,

        [AllowEmptyString()]
        [ValidatePattern('^(?:[A-Za-z_][A-Za-z0-9_]*)?$')]
        [string] $EnvironmentVariable
    )

    if ([string]::IsNullOrWhiteSpace($EnvironmentVariable)) {
        return $ReleaseConfig
    }
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($EnvironmentVariable))) {
        throw "GitHub credential environment variable '$EnvironmentVariable' is missing or empty."
    }
    $gitHubProperty = $ReleaseConfig.PSObject.Properties['GitHub']
    if ($null -eq $gitHubProperty -or $null -eq $gitHubProperty.Value) {
        throw 'The release configuration does not declare a GitHub section.'
    }

    # Keep the effective configuration secret-free. File credentials have higher
    # precedence in the shared resolver, so clear them for this explicit override.
    $gitHubProperty.Value | Add-Member -NotePropertyName Token -NotePropertyValue $null -Force
    $gitHubProperty.Value | Add-Member -NotePropertyName TokenFilePath -NotePropertyValue $null -Force
    $gitHubProperty.Value | Add-Member -NotePropertyName TokenEnvName -NotePropertyValue $EnvironmentVariable -Force
    $packages = $ReleaseConfig.PSObject.Properties['Packages']
    if ($null -ne $packages -and $null -ne $packages.Value) {
        $packages.Value | Add-Member -NotePropertyName GitHubAccessToken -NotePropertyValue $null -Force
        $packages.Value | Add-Member -NotePropertyName GitHubAccessTokenFilePath -NotePropertyValue $null -Force
        $packages.Value | Add-Member -NotePropertyName GitHubAccessTokenEnvName -NotePropertyValue $EnvironmentVariable -Force
    }
    $tools = $ReleaseConfig.PSObject.Properties['Tools']
    if ($null -ne $tools -and $null -ne $tools.Value) {
        $toolGitHub = $tools.Value.PSObject.Properties['GitHub']
        if ($null -ne $toolGitHub -and $null -ne $toolGitHub.Value) {
            $toolGitHub.Value | Add-Member -NotePropertyName Token -NotePropertyValue $null -Force
            $toolGitHub.Value | Add-Member -NotePropertyName TokenFilePath -NotePropertyValue $null -Force
            $toolGitHub.Value | Add-Member -NotePropertyName TokenEnvName -NotePropertyValue $EnvironmentVariable -Force
        }
    }
    $ReleaseConfig
}
