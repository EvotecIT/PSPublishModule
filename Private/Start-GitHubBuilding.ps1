function Start-GitHubBuilding {
    [cmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [string] $TagName,
        [string] $ProjectName
    )
    $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
    $FileName = -join ("$TagName", '.zip')
    #$FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
    $ZipPath = [System.IO.Path]::Combine($FullProjectPath, 'Releases', $FileName)

    if ($Configuration.Options.GitHub.FromFile) {
        $GitHubAccessToken = Get-Content -LiteralPath $Configuration.Options.GitHub.ApiKey
    } else {
        $GitHubAccessToken = $Configuration.Options.GitHub.ApiKey
    }
    if ($GitHubAccessToken) {
        if ($Configuration.Options.GitHub.RepositoryName) {
            $GitHubRepositoryName = $Configuration.Options.GitHub.RepositoryName
        } else {
            $GitHubRepositoryName = $ProjectName
        }
        if (Test-Path -LiteralPath $ZipPath) {
            if ($Configuration.Steps.PublishModule.Prerelease -ne '') {
                $IsPreRelease = $true
            } else {
                $IsPreRelease = $false
            }

            $StatusGithub = Send-GitHubRelease -GitHubUsername $Configuration.Options.GitHub.UserName -GitHubRepositoryName $GitHubRepositoryName -GitHubAccessToken $GitHubAccessToken -TagName $TagName -AssetFilePaths $ZipPath -IsPreRelease $IsPreRelease
            if ($StatusGithub.ReleaseCreationSucceeded -and $statusGithub.Succeeded) {
                $GithubColor = 'Green'
                $GitHubText = '+'
            } else {
                $GithubColor = 'Red'
                $GitHubText = '-'
            }

            Write-Text "[$GitHubText] GitHub Release Creation Status: $($StatusGithub.ReleaseCreationSucceeded)" -Color $GithubColor
            Write-Text "[$GitHubText] GitHub Release Succeeded: $($statusGithub.Succeeded)" -Color $GithubColor
            Write-Text "[$GitHubText] GitHub Release Asset Upload Succeeded: $($statusGithub.AllAssetUploadsSucceeded)" -Color $GithubColor
            Write-Text "[$GitHubText] GitHub Release URL: $($statusGitHub.ReleaseUrl)" -Color $GithubColor
            if ($statusGithub.ErrorMessage) {
                Write-Text "[$GitHubText] GitHub Release ErrorMessage: $($statusGithub.ErrorMessage)" -Color $GithubColor
            }
        }
    }
}