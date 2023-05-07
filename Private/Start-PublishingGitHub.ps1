function Start-PublishingGitHub {
    [cmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [string] $TagName,
        [string] $ProjectName
    )
    $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
    #$FileName = -join ("$TagName", '.zip')

    # if ($Configuration.Steps.BuildModule.Releases -eq $true -or $Configuration.Steps.BuildModule.Releases.Enabled) {
    #     if ($Configuration.Steps.BuildModule.Releases -is [System.Collections.IDictionary]) {
    #         if ($Configuration.Steps.BuildModule.Releases.Path) {
    #             if ($Configuration.Steps.BuildModule.Releases.Relative -eq $false) {
    #                 $FolderPathReleases = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Configuration.Steps.BuildModule.Releases.Path)
    #             } else {
    #                 $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Configuration.Steps.BuildModule.Releases.Path)
    #             }
    #         } else {
    #             $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
    #         }
    #     } else {
    #         # default values
    #         $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
    #     }
    #     $ZipPath = [System.IO.Path]::Combine($FolderPathReleases, $FileName)
    # } else {
    #     $ZipPath = [System.IO.Path]::Combine($FullProjectPath, 'Releases', $FileName)
    # }
    if (-not $Configuration.CurrentSettings.ArtefactZipPath -or -not (Test-Path -LiteralPath $Configuration.CurrentSettings.ArtefactZipPath)) {
        Write-Text -Text "[-] Publishing to GitHub failed. File $($Configuration.CurrentSettings.ArtefactZipPath) doesn't exists" -Color Red
        return $false
    }
    $ZipPath = $Configuration.CurrentSettings.ArtefactZipPath

    if ($Configuration.Options.GitHub.FromFile) {
        $GitHubAccessToken = Get-Content -LiteralPath $Configuration.Options.GitHub.ApiKey -Encoding UTF8
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
            if ($Configuration.Steps.PublishModule.Prerelease) {
                $IsPreRelease = $true
            } else {
                $IsPreRelease = $false
            }

            $sendGitHubReleaseSplat = @{
                GitHubUsername       = $Configuration.Options.GitHub.UserName
                GitHubRepositoryName = $GitHubRepositoryName
                GitHubAccessToken    = $GitHubAccessToken
                TagName              = $TagName
                AssetFilePaths       = $ZipPath
                IsPreRelease         = $IsPreRelease
                # those don't work, requires testing
                #GenerateReleaseNotes = $true
                #MakeLatest           = $true
                #GenerateReleaseNotes = if ($Configuration.Options.GitHub.GenerateReleaseNotes) { $true } else { $false }
                #MakeLatest           = if ($Configuration.Options.GitHub.MakeLatest) { $true } else { $false }
                Verbose              = $Configuration.Steps.PublishModule.GitHubVerbose
            }

            $StatusGithub = Send-GitHubRelease @sendGitHubReleaseSplat

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
                return $false
            }
        } else {
            Write-Text "[-] GitHub Release Creation Status: Failed" -Color Red
            Write-Text "[-] GitHub Release Creation Reason: $ZipPath doesn't exists. Most likely Releases option is disabled." -Color Red
            return $false
        }
    }
}