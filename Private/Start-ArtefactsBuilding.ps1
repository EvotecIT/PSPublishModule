function Start-ArtefactsBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [System.Collections.IDictionary] $DestinationPaths
    )
    if ($Configuration.Steps.BuildModule.Releases -or $Configuration.Steps.BuildModule.ReleasesUnpacked) {
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
            if ($DestinationPaths.Desktop) {
                $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Desktop, '*')
            } elseif ($DestinationPaths.Core) {
                $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Core, '*')
            }

            $compressArtefactSplat = @{
                Configuration = $Configuration
                LegacyName    = if ($Configuration.Steps.BuildModule.Releases -is [bool]) { $true } else { $false }
                Source        = $CompressPath
                Destination   = $FolderPathReleases
                ModuleName    = $Configuration.Information.ModuleName
                ModuleVersion = $Configuration.Information.Manifest.ModuleVersion
                IncludeTag    = $Configuration.Steps.BuildModule.Releases.IncludeTagName
                ArtefactName  = $Configuration.Steps.BuildModule.Releases.ArtefactName
            }

            $OutputArchive = Compress-Artefact @compressArtefactSplat
            if ($OutputArchive -eq $false) {
                return $false
            }
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
                # $FolderPathReleasesUnpacked = $ArtefactsPath
            } else {
                # default values
                $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName)
                #$FolderPathReleasesUnpacked = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName )
                $RequiredModulesPath = $ArtefactsPath
                $CurrentModulePath = $ArtefactsPath
            }

            $SplatArtefact = @{
                ModuleName                     = $Configuration.Information.ModuleName
                ModuleVersion                  = $Configuration.Information.Manifest.ModuleVersion
                LegacyName                     = if ($Configuration.Steps.BuildModule.ReleasesUnpacked -is [bool]) { $true } else { $false }
                CopyMainModule                 = $true
                CopyRequiredModules            = $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules -eq $true -or $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Enabled
                ProjectPath                    = $FullProjectPath
                Destination                    = $ArtefactsPath
                DestinationMainModule          = $CurrentModulePath
                DestinationRequiredModules     = $RequiredModulesPath
                RequiredModules                = $Configuration.Information.Manifest.RequiredModules
                Files                          = $Configuration.Steps.BuildModule.ReleasesUnpacked.FilesOutput
                Folders                        = $Configuration.Steps.BuildModule.ReleasesUnpacked.DirectoryOutput
                DestinationFilesRelative       = $Configuration.Steps.BuildModule.ReleasesUnpacked.DestinationFilesRelative
                DestinationDirectoriesRelative = $Configuration.Steps.BuildModule.ReleasesUnpacked.DestinationDirectoriesRelative
                Configuration                  = $Configuration
                IncludeTag                     = $Configuration.Steps.BuildModule.ReleasesUnpacked.IncludeTagName
                ArtefactName                   = $Configuration.Steps.BuildModule.ReleasesUnpacked.ArtefactName
                ZipIt                          = $false
                DestinationZip                 = $null
            }
            Remove-EmptyValue -Hashtable $SplatArtefact
            Add-Artefact @SplatArtefact
        }
    }
}