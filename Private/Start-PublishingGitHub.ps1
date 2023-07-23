function Start-PublishingGitHub {
    [cmdletBinding()]
    param(
        [System.Collections.IDictionary] $ChosenNuget,
        [System.Collections.IDictionary] $Configuration,
        [string] $ProjectName
    )
    if ($ChosenNuget) {
        if ($ChosenNuget.Id) {
            if ($Configuration.CurrentSettings['Artefact'][$ChosenNuget.Id]) {
                #$ZipName = $Configuration.CurrentSettings['Artefact'][$ChosenNuget.Id].ZipName
                $ZipPath = $Configuration.CurrentSettings['Artefact'][$ChosenNuget.Id].ZipPath
            } else {
                $ZipPath = $null
            }
        } else {
            if ($Configuration.CurrentSettings['ArtefactDefault']) {
                #$ZipName = $Configuration.CurrentSettings['ArtefactDefault'].ZipName
                $ZipPath = $Configuration.CurrentSettings['ArtefactDefault'].ZipPath
            } else {
                $ZipPath = $null
            }
        }
        if ($ZipPath -and (Test-Path -LiteralPath $ZipPath)) {
            if ($ChosenNuget.OverwriteTagName) {
                $TagName = $ChosenNuget.OverwriteTagName
            } else {
                $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
            }
            $sendGitHubReleaseSplat = [ordered] @{
                GitHubUsername       = $ChosenNuget.UserName
                GitHubRepositoryName = if ($ChosenNuget.RepositoryName) { $ChosenNuget.RepositoryName } else { $ProjectName }
                GitHubAccessToken    = $ChosenNuget.ApiKey
                TagName              = $TagName
                AssetFilePaths       = $ZipPath
                # settable via PSD1 manifest
                IsPreRelease         = if ($Configuration.CurrentSettings.Prerelease) { $true } else { $false }
                # those don't work, requires testing
                #GenerateReleaseNotes = $true
                #MakeLatest           = $true
                #GenerateReleaseNotes = if ($Configuration.Options.GitHub.GenerateReleaseNotes) { $true } else { $false }
                #MakeLatest           = if ($Configuration.Options.GitHub.MakeLatest) { $true } else { $false }
                Verbose              = if ($ChosenNuget.Verbose) { $ChosenNuget.Verbose } else { $false }
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
    } else {
        # old configuration
        if (-not $Configuration.CurrentSettings.ArtefactZipPath -or -not (Test-Path -LiteralPath $Configuration.CurrentSettings.ArtefactZipPath)) {
            Write-Text -Text "[-] Publishing to GitHub failed. File $($Configuration.CurrentSettings.ArtefactZipPath) doesn't exists" -Color Red
            return $false
        }
        $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"

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

                $sendGitHubReleaseSplat = [ordered] @{
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
                    Verbose              = if ($Configuration.Steps.PublishModule.GitHubVerbose) { $Configuration.Steps.PublishModule.GitHubVerbose } else { $false }
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
}