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
    $result = [ordered]@{
        Success   = $false
        TagName   = $null
        ZipPath   = $null
        ReleaseUrl = $null
        ErrorMessage = $null
    }

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        $result.ErrorMessage = "Project path '$ProjectPath' not found."
        return [PSCustomObject]$result
    }
    $csproj = Get-ChildItem -Path $ProjectPath -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $csproj) {
        $result.ErrorMessage = "No csproj found in $ProjectPath"
        return [PSCustomObject]$result
    }
    try {
        [xml]$xml = Get-Content -LiteralPath $csproj.FullName -Raw -ErrorAction Stop
    } catch {
        $result.ErrorMessage = "Failed to read '$($csproj.FullName)' as XML: $_"
        return [PSCustomObject]$result
    }
    $version = ($xml.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -First 1).VersionPrefix
    if (-not $version) {
        $result.ErrorMessage = "VersionPrefix not found in '$($csproj.FullName)'"
        return [PSCustomObject]$result
    }
    $zipPath = Join-Path -Path $csproj.Directory.FullName -ChildPath ("bin/Release/{0}.{1}.zip" -f $csproj.BaseName, $version)
    if (-not (Test-Path -LiteralPath $zipPath)) {
        $result.ErrorMessage = "Zip file '$zipPath' not found."
        return [PSCustomObject]$result
    }
    $tagName = "v$version"
    $result.TagName = $tagName
    $result.ZipPath = $zipPath
    try {
        $statusGithub = Send-GitHubRelease -GitHubUsername $GitHubUsername -GitHubRepositoryName $GitHubRepositoryName -GitHubAccessToken $GitHubAccessToken -TagName $tagName -AssetFilePaths $zipPath -IsPreRelease:$IsPreRelease.IsPresent
        $result.Success = $statusGithub.Succeeded
        $result.ReleaseUrl = $statusGithub.ReleaseUrl
        if (-not $statusGithub.Succeeded) {
            $result.ErrorMessage = $statusGithub.ErrorMessage
        }
    } catch {
        $result.ErrorMessage = $_.Exception.Message
    }
    return [PSCustomObject]$result
}
