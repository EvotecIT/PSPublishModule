function Start-ArtefactsBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $ChosenArtefact,
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [System.Collections.IDictionary] $DestinationPaths,
        [ValidateSet('ReleasesUnpacked', 'Releases')][string] $Type
    )

    if ($Artefact) {
        $Artefact = $ChosenArtefact
        $ChosenType = $Artefact.Type
    } elseif ($Type) {
        if ($Configuration.Steps.BuildModule.$Type) {
            $Artefact = $Configuration.Steps.BuildModule.$Type
            $ChosenType = $Type
        } else {
            $Artefact = $null
        }
    } else {
        $Artefact = $null
    }
    if ($Artefact -or $Artefact.Enabled) {
        if ($Artefact -is [System.Collections.IDictionary]) {
            if ($Artefact.Path) {
                if ($Artefact.Relative -eq $false) {
                    $FolderPathReleases = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Artefact.Path)
                } else {
                    $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Artefact.Path)
                }
            } else {
                $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Type)
            }
        } else {
            # default values
            $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Type)
        }
        # if ($DestinationPaths.Desktop) {
        #     $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Desktop, '*')
        # } elseif ($DestinationPaths.Core) {
        #     $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Core, '*')
        # }

        # $compressArtefactSplat = @{
        #     Configuration = $Configuration
        #     LegacyName    = if ($Artefact -is [bool]) { $true } else { $false }
        #     Source        = $CompressPath
        #     Destination   = $FolderPathReleases
        #     ModuleName    = $Configuration.Information.ModuleName
        #     ModuleVersion = $Configuration.Information.Manifest.ModuleVersion
        #     IncludeTag    = $Artefact.IncludeTagName
        #     ArtefactName  = $Artefact.ArtefactName
        # }

        # $OutputArchive = Compress-Artefact @compressArtefactSplat
        # if ($OutputArchive -eq $false) {
        #     return $false
        # }

        #if ($Artefact) {
        if ($Artefact.RequiredModules.ModulesPath) {
            $DirectPathForPrimaryModule = $Artefact.RequiredModules.ModulesPath
        } elseif ($Artefact.RequiredModules.Path) {
            $DirectPathForPrimaryModule = $Artefact.RequiredModules.Path
        } elseif ($Artefact.Path) {
            $DirectPathForPrimaryModule = $Artefact.Path
        } else {
            $DirectPathForPrimaryModule = $FolderPathReleases
        }
        if ($Artefact.RequiredModules.Path) {
            $DirectPathForRequiredModules = $Artefact.RequiredModules.Path
        } elseif ($Artefact.RequiredModules.ModulesPath) {
            $DirectPathForRequiredModules = $Artefact.RequiredModules.ModulesPath
        } elseif ($Artefact.Path) {
            $DirectPathForRequiredModules = $Artefact.Path
        } else {
            $DirectPathForRequiredModules = $FolderPathReleases
        }
        if ($Artefact -eq $true -or $Artefact.Enabled) {
            if ($Artefact -is [System.Collections.IDictionary]) {
                if ($DirectPathForPrimaryModule) {
                    if ($Artefact.Relative -eq $false) {
                        $CurrentModulePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DirectPathForPrimaryModule)
                    } else {
                        $CurrentModulePath = [System.IO.Path]::Combine($FullProjectPath, $DirectPathForPrimaryModule)
                    }
                } else {
                    $CurrentModulePath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked')
                }
                if ($DirectPathForRequiredModules) {
                    if ($Artefact.Relative -eq $false) {
                        $RequiredModulesPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DirectPathForRequiredModules)
                    } else {
                        $RequiredModulesPath = [System.IO.Path]::Combine($FullProjectPath, $DirectPathForRequiredModules)
                    }
                } else {
                    $RequiredModulesPath = $ArtefactsPath
                }
                $ArtefactsPath = $Artefact.Path
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
            if ($null -eq $Artefact.DestinationFilesRelative) {
                if ($null -ne $Artefact.Relative) {
                    $Artefact.DestinationFilesRelative = $Artefact.Relative
                }
            }
            if ($null -eq $Artefact.DestinationDirectoriesRelative) {
                if ($null -ne $Artefact.Relative) {
                    $Artefact.DestinationDirectoriesRelative = $Artefact.Relative
                }
            }

            $SplatArtefact = @{
                ModuleName                     = $Configuration.Information.ModuleName
                ModuleVersion                  = $Configuration.Information.Manifest.ModuleVersion
                LegacyName                     = if ($Artefact -is [bool]) { $true } else { $false }
                CopyMainModule                 = $true
                CopyRequiredModules            = $Artefact.RequiredModules -eq $true -or $Artefact.RequiredModules.Enabled
                ProjectPath                    = $FullProjectPath
                Destination                    = $ArtefactsPath
                DestinationMainModule          = $CurrentModulePath
                DestinationRequiredModules     = $RequiredModulesPath
                RequiredModules                = $Configuration.Information.Manifest.RequiredModules
                Files                          = $Artefact.FilesOutput
                Folders                        = $Artefact.DirectoryOutput
                DestinationFilesRelative       = $Artefact.DestinationFilesRelative
                DestinationDirectoriesRelative = $Artefact.DestinationDirectoriesRelative
                Configuration                  = $Configuration
                IncludeTag                     = $Artefact.IncludeTagName
                ArtefactName                   = $Artefact.ArtefactName
                ZipIt                          = if ($ChosenType -in 'Packed', 'Releases', 'ScriptPacked') { $true } else { $false }
                ConvertToScript                = if ($ChosenType -in 'ScriptPacked', 'Script') { $true } else { $false }
                DestinationZip                 = $CurrentModulePath
                ScriptMerge                    = $Artefact.ScriptMerge
            }
            Remove-EmptyValue -Hashtable $SplatArtefact
            Add-Artefact @SplatArtefact
        }
    }
}