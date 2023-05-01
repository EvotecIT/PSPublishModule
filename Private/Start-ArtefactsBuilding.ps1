function Start-ArtefactsBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [System.Collections.IDictionary] $DestinationPaths
    )
    if ($Configuration.Steps.BuildModule.Releases -or $Configuration.Steps.BuildModule.ReleasesUnpacked) {
        $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
        $FileName = -join ("$TagName", '.zip')

        if ($Configuration.Steps.BuildModule.Releases -eq $true -or $Configuration.Steps.BuildModule.Releases.Enabled) {
            if ($Configuration.Steps.BuildModule.Releases -is [System.Collections.IDictionary]) {
                if ($Configuration.Steps.BuildModule.Releases.Path) {
                    if ($Configuration.Steps.BuildModule.Releases.Relative -eq $false) {
                        $FolderPathReleases = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Configuration.Steps.BuildModule.Releases.Path)
                    } else {
                        $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Configuration.Steps.BuildModule.Releases.Path)
                    }
                } else {
                    $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
                }
            } else {
                # default values
                $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
            }

            Compress-Artefact -Destination $FolderPathReleases -FileName $FileName
        }

        if ($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.ModulesPath) {
            $DirectPathForPrimaryModule = $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.ModulesPath
        } elseif ($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Path) {
            $DirectPathForPrimaryModule = $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Path
        } elseif ($Configuration.Steps.BuildModule.ReleasesUnpacked.Path) {
            $DirectPathForPrimaryModule = $Configuration.Steps.BuildModule.ReleasesUnpacked.Path
        } else {
            $DirectPathForPrimaryModule = $FolderPathReleases
        }
        if ($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Path) {
            $DirectPathForRequiredModules = $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Path
        } elseif ($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.ModulesPath) {
            $DirectPathForRequiredModules = $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.ModulesPath
        } elseif ($Configuration.Steps.BuildModule.ReleasesUnpacked.Path) {
            $DirectPathForPrimaryModule = $Configuration.Steps.BuildModule.ReleasesUnpacked.Path
        } else {
            $DirectPathForRequiredModules = $FolderPathReleases
        }
        if ($Configuration.Steps.BuildModule.ReleasesUnpacked -eq $true -or $Configuration.Steps.BuildModule.ReleasesUnpacked.Enabled) {
            if ($Configuration.Steps.BuildModule.ReleasesUnpacked -is [System.Collections.IDictionary]) {
                if ($DirectPathForPrimaryModule) {
                    if ($Configuration.Steps.BuildModule.ReleasesUnpacked.Relative -eq $false) {
                        $ArtefactsPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DirectPathForPrimaryModule)
                    } else {
                        $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, $DirectPathForPrimaryModule)
                    }
                } else {
                    $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked')
                }
                if ($DirectPathForRequiredModules) {
                    if ($Configuration.Steps.BuildModule.ReleasesUnpacked.Relative -eq $false) {
                        $RequiredModulesPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DirectPathForRequiredModules)
                    } else {
                        $RequiredModulesPath = [System.IO.Path]::Combine($FullProjectPath, $DirectPathForRequiredModules)
                    }
                } else {
                    $RequiredModulesPath = $ArtefactsPath
                }
                $CurrentModulePath = $ArtefactsPath
                $FolderPathReleasesUnpacked = $ArtefactsPath
            } else {
                # default values
                $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName)
                $FolderPathReleasesUnpacked = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName )
                $RequiredModulesPath = $ArtefactsPath
                $CurrentModulePath = $ArtefactsPath
            }
            Write-TextWithTime -Text "Copying final merged release to $ArtefactsPath" -PreAppend Plus {
                $copyMainModuleSplat = @{
                    Enabled           = $true
                    IncludeTagName    = $Configuration.Steps.BuildModule.ReleasesUnpacked.IncludeTagName
                    ModuleName        = $Configuration.Information.ModuleName
                    Destination       = $FolderPathReleasesUnpacked
                    CurrentModulePath = $CurrentModulePath
                }
                Copy-ArtefactMainModule @copyMainModuleSplat

                $copyRequiredModuleSplat = @{
                    Enabled         = $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules -eq $true -or $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Enabled
                    RequiredModules = $Configuration.Information.Manifest.RequiredModules
                    FolderPath      = $RequiredModulesPath
                }
                Copy-ArtefactRequiredModule @copyRequiredModuleSplat

                $copyArtefactRequiredFoldersSplat = @{
                    FoldersInput = $Configuration.Steps.BuildModule.ReleasesUnpacked.DirectoryOutput
                    ProjectPath  = $FullProjectPath
                }
                Copy-ArtefactRequiredFolders @copyArtefactRequiredFoldersSplat

                $copyArtefactRequiredFilesSplat = @{
                    FilesInput  = $Configuration.Steps.BuildModule.ReleasesUnpacked.FilesOutput
                    ProjectPath = $FullProjectPath
                    Destination = $FolderPathReleasesUnpacked
                }
                Copy-ArtefactRequiredFiles @copyArtefactRequiredFilesSplat
            }
        }
    }
}