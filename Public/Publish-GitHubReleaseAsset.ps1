function Publish-GitHubReleaseAsset {
    <#
    .SYNOPSIS
    Publishes a release asset to GitHub.

    .DESCRIPTION
    Uses `Send-GitHubRelease` to create or update a GitHub release based on the
    project version and upload the generated zip archive.

    .PARAMETER ProjectPath
    Path to the project folder containing the *.csproj file.

    .PARAMETER GitHubUsername
    GitHub account name owning the repository.

    .PARAMETER GitHubRepositoryName
    Name of the GitHub repository.

    .PARAMETER GitHubAccessToken
    Personal access token used for authentication.

    .PARAMETER IsPreRelease
    Publish the release as a pre-release.

    .EXAMPLE
    Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\MyProject' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyRepo' -GitHubAccessToken $Token
    Uploads the current project zip to the specified GitHub repository.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$ProjectPath,
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$GitHubUsername,
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$GitHubRepositoryName,
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$GitHubAccessToken,
        [switch]$IsPreRelease
    )
    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        Write-Error "Publish-GitHubReleaseAsset - Project path '$ProjectPath' not found."
        return
    }
    $csproj = Get-ChildItem -Path $ProjectPath -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $csproj) {
        Write-Error "Publish-GitHubReleaseAsset - No csproj found in $ProjectPath"
        return
    }
    [xml]$xml = Get-Content -LiteralPath $csproj.FullName -Raw
    $version = $xml.Project.PropertyGroup.VersionPrefix
    $zipPath = Join-Path -Path $csproj.Directory.FullName -ChildPath ("bin/Release/{0}.{1}.zip" -f $csproj.BaseName, $version)
    if (-not (Test-Path -LiteralPath $zipPath)) {
        Write-Error "Publish-GitHubReleaseAsset - Zip file '$zipPath' not found."
        return
    }
    $tagName = "v$version"
    try {
        Send-GitHubRelease -GitHubUsername $GitHubUsername -GitHubRepositoryName $GitHubRepositoryName -GitHubAccessToken $GitHubAccessToken -TagName $tagName -AssetFilePaths $zipPath -IsPreRelease:$IsPreRelease.IsPresent
    } catch {
        Write-Error "Publish-GitHubReleaseAsset - Failed to publish release: $_"
    }
}
