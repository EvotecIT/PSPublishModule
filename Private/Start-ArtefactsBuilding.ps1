function Start-ArtefactsBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [System.Collections.IDictionary] $DestinationPaths,
        [parameter(Mandatory)][ValidateSet('ReleasesUnpacked', 'Releases')][string] $Type
    )
    if ($Configuration.Steps.BuildModule.$Type -eq $true -or $Configuration.Steps.BuildModule.$Type.Enabled) {
        if ($Configuration.Steps.BuildModule.$Type -is [System.Collections.IDictionary]) {
            if ($Configuration.Steps.BuildModule.$Type.Path) {
                if ($Configuration.Steps.BuildModule.$Type.Relative -eq $false) {
                    $FolderPathReleases = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Configuration.Steps.BuildModule.$Type.Path)
                } else {
                    $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Configuration.Steps.BuildModule.$Type.Path)
                }
            } else {
                $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Type)
            }
        } else {
            # default values
            $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Type)
        }
        if ($DestinationPaths.Desktop) {
            $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Desktop, '*')
        } elseif ($DestinationPaths.Core) {
            $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Core, '*')
        }

        $compressArtefactSplat = @{
            Configuration = $Configuration
            LegacyName    = if ($Configuration.Steps.BuildModule.$Type -is [bool]) { $true } else { $false }
            Source        = $CompressPath
            Destination   = $FolderPathReleases
            ModuleName    = $Configuration.Information.ModuleName
            ModuleVersion = $Configuration.Information.Manifest.ModuleVersion
            IncludeTag    = $Configuration.Steps.BuildModule.$Type.IncludeTagName
            ArtefactName  = $Configuration.Steps.BuildModule.$Type.ArtefactName
        }

        # $OutputArchive = Compress-Artefact @compressArtefactSplat
        # if ($OutputArchive -eq $false) {
        #     return $false
        # }
    }
    if ($Configuration.Steps.BuildModule.$Type) {
        if ($Configuration.Steps.BuildModule.$Type.RequiredModules.ModulesPath) {
            $DirectPathForPrimaryModule = $Configuration.Steps.BuildModule.$Type.RequiredModules.ModulesPath
        } elseif ($Configuration.Steps.BuildModule.$Type.RequiredModules.Path) {
            $DirectPathForPrimaryModule = $Configuration.Steps.BuildModule.$Type.RequiredModules.Path
        } elseif ($Configuration.Steps.BuildModule.$Type.Path) {
            $DirectPathForPrimaryModule = $Configuration.Steps.BuildModule.$Type.Path
        } else {
            $DirectPathForPrimaryModule = $FolderPathReleases
        }
        if ($Configuration.Steps.BuildModule.$Type.RequiredModules.Path) {
            $DirectPathForRequiredModules = $Configuration.Steps.BuildModule.$Type.RequiredModules.Path
        } elseif ($Configuration.Steps.BuildModule.$Type.RequiredModules.ModulesPath) {
            $DirectPathForRequiredModules = $Configuration.Steps.BuildModule.$Type.RequiredModules.ModulesPath
        } elseif ($Configuration.Steps.BuildModule.$Type.Path) {
            $DirectPathForRequiredModules = $Configuration.Steps.BuildModule.$Type.Path
        } else {
            $DirectPathForRequiredModules = $FolderPathReleases
        }
        if ($Configuration.Steps.BuildModule.$Type -eq $true -or $Configuration.Steps.BuildModule.$Type.Enabled) {
            if ($Configuration.Steps.BuildModule.$Type -is [System.Collections.IDictionary]) {
                if ($DirectPathForPrimaryModule) {
                    if ($Configuration.Steps.BuildModule.$Type.Relative -eq $false) {
                        $CurrentModulePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DirectPathForPrimaryModule)
                    } else {
                        $CurrentModulePath = [System.IO.Path]::Combine($FullProjectPath, $DirectPathForPrimaryModule)
                    }
                } else {
                    $CurrentModulePath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked')
                }
                if ($DirectPathForRequiredModules) {
                    if ($Configuration.Steps.BuildModule.$Type.Relative -eq $false) {
                        $RequiredModulesPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DirectPathForRequiredModules)
                    } else {
                        $RequiredModulesPath = [System.IO.Path]::Combine($FullProjectPath, $DirectPathForRequiredModules)
                    }
                } else {
                    $RequiredModulesPath = $ArtefactsPath
                }
                $ArtefactsPath = $Configuration.Steps.BuildModule.$Type.Path
                # $FolderPathReleasesUnpacked = $ArtefactsPath
            } else {
                # default values
                $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName)
                #$FolderPathReleasesUnpacked = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName )
                $RequiredModulesPath = $ArtefactsPath
                $CurrentModulePath = $ArtefactsPath
            }

            # we try to set some defaults just in case some settings are not available (mainly because user is using non-DSL model)
            # this is to make sure that if user is using relative paths we can still use them for copying files/folders
            if ($null -eq $Configuration.Steps.BuildModule.$Type.DestinationFilesRelative) {
                if ($null -ne $Configuration.Steps.BuildModule.$Type.Relative) {
                    $Configuration.Steps.BuildModule.$Type.DestinationFilesRelative = $Configuration.Steps.BuildModule.$Type.Relative
                }
            }
            if ($null -eq $Configuration.Steps.BuildModule.$Type.DestinationDirectoriesRelative) {
                if ($null -ne $Configuration.Steps.BuildModule.$Type.Relative) {
                    $Configuration.Steps.BuildModule.$Type.DestinationDirectoriesRelative = $Configuration.Steps.BuildModule.$Type.Relative
                }
            }

            $SplatArtefact = @{
                ModuleName                     = $Configuration.Information.ModuleName
                ModuleVersion                  = $Configuration.Information.Manifest.ModuleVersion
                LegacyName                     = if ($Configuration.Steps.BuildModule.$Type -is [bool]) { $true } else { $false }
                CopyMainModule                 = $true
                CopyRequiredModules            = $Configuration.Steps.BuildModule.$Type.RequiredModules -eq $true -or $Configuration.Steps.BuildModule.$Type.RequiredModules.Enabled
                ProjectPath                    = $FullProjectPath
                Destination                    = $ArtefactsPath
                DestinationMainModule          = $CurrentModulePath
                DestinationRequiredModules     = $RequiredModulesPath
                RequiredModules                = $Configuration.Information.Manifest.RequiredModules
                Files                          = $Configuration.Steps.BuildModule.$Type.FilesOutput
                Folders                        = $Configuration.Steps.BuildModule.$Type.DirectoryOutput
                DestinationFilesRelative       = $Configuration.Steps.BuildModule.$Type.DestinationFilesRelative
                DestinationDirectoriesRelative = $Configuration.Steps.BuildModule.$Type.DestinationDirectoriesRelative
                Configuration                  = $Configuration
                IncludeTag                     = $Configuration.Steps.BuildModule.$Type.IncludeTagName
                ArtefactName                   = $Configuration.Steps.BuildModule.$Type.ArtefactName
                ZipIt                          = if ($Type -eq 'Releases') { $true } else { $false }
                DestinationZip                 = $CurrentModulePath
            }
            Remove-EmptyValue -Hashtable $SplatArtefact
            Add-Artefact @SplatArtefact
        }
    }
}