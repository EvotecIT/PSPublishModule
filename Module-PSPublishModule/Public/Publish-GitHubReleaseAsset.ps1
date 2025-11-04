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

    .EXAMPLE
    # Multiple packages in one repo can share the same version.
    # Use a custom tag to avoid conflicts (e.g., include project name).
    Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\OfficeIMO\OfficeIMO.Markdown' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'OfficeIMO' -GitHubAccessToken $Token -IncludeProjectNameInTag

    .EXAMPLE
    # Override version and tag explicitly
    Publish-GitHubReleaseAsset -ProjectPath 'C:\Git\OfficeIMO\OfficeIMO.Excel' -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'OfficeIMO' -GitHubAccessToken $Token -Version '1.2.3' -TagName 'OfficeIMO.Excel-v1.2.3'
    #>
    [CmdletBinding(SupportsShouldProcess)]
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
        [switch]$IsPreRelease,
        [Parameter()]
        [ValidateNotNullOrEmpty()]
        [string]$Version,
        [Parameter()]
        [ValidateNotNullOrEmpty()]
        [string]$TagName,
        [Parameter()]
        [ValidateNotNullOrEmpty()]
        [string]$TagTemplate,
        [Parameter()]
        [ValidateNotNullOrEmpty()]
        [string]$ReleaseName,
        [switch]$IncludeProjectNameInTag,
        [Parameter()]
        [ValidateNotNullOrEmpty()]
        [string]$ZipPath
    )
    $result = [ordered]@{
        Success   = $false
        TagName   = $null
        ReleaseName = $null
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
    if (-not $PSBoundParameters.ContainsKey('Version')) {
        try {
            [xml]$xml = Get-Content -LiteralPath $csproj.FullName -Raw -ErrorAction Stop
        } catch {
            $result.ErrorMessage = "Failed to read '$($csproj.FullName)' as XML: $_"
            return [PSCustomObject]$result
        }
        $Version = ($xml.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -First 1).VersionPrefix
        if (-not $Version) {
            $result.ErrorMessage = "VersionPrefix not found in '$($csproj.FullName)'"
            return [PSCustomObject]$result
        }
    }
    if (-not $PSBoundParameters.ContainsKey('ZipPath')) {
        $zipPath = Join-Path -Path $csproj.Directory.FullName -ChildPath ("bin/Release/{0}.{1}.zip" -f $csproj.BaseName, $Version)
    } else {
        $zipPath = $ZipPath
    }
    if (-not (Test-Path -LiteralPath $zipPath)) {
        $result.ErrorMessage = "Zip file '$zipPath' not found."
        return [PSCustomObject]$result
    }
    if (-not $PSBoundParameters.ContainsKey('TagName')) {
        if ($PSBoundParameters.ContainsKey('TagTemplate') -and -not [string]::IsNullOrEmpty($TagTemplate)) {
            $TagName = $TagTemplate.Replace('{Project}', $csproj.BaseName).Replace('{Version}', $Version)
        } elseif ($IncludeProjectNameInTag.IsPresent) {
            $TagName = "$($csproj.BaseName)-v$Version"
        } else {
            $TagName = "v$Version"
        }
    }
    $tagName = $TagName
    $result.TagName = $tagName
    if (-not $PSBoundParameters.ContainsKey('ReleaseName') -or [string]::IsNullOrEmpty($ReleaseName)) {
        $ReleaseName = $tagName
    }
    $result.ReleaseName = $ReleaseName
    $result.ZipPath = $zipPath

    if ($PSCmdlet.ShouldProcess("$GitHubUsername/$GitHubRepositoryName", "Publish release $tagName to GitHub")) {
        try {
            $statusGithub = Send-GitHubRelease -GitHubUsername $GitHubUsername -GitHubRepositoryName $GitHubRepositoryName -GitHubAccessToken $GitHubAccessToken -TagName $tagName -ReleaseName $ReleaseName -AssetFilePaths $zipPath -IsPreRelease:$IsPreRelease.IsPresent
            $result.Success = $statusGithub.Succeeded
            $result.ReleaseUrl = $statusGithub.ReleaseUrl
            if (-not $statusGithub.Succeeded) {
                $result.ErrorMessage = $statusGithub.ErrorMessage
            }
        } catch {
            $result.ErrorMessage = $_.Exception.Message
        }
    } else {
        # WhatIf mode
        $result.Success = $true
        $result.ReleaseUrl = "https://github.com/$GitHubUsername/$GitHubRepositoryName/releases/tag/$tagName"
    }
    return [PSCustomObject]$result
}
