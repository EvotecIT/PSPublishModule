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
        $ID = if ($ChosenArtefact.ID) { $ChosenArtefact.ID } else { $null }
    } elseif ($Type) {
        if ($Configuration.Steps.BuildModule.$Type) {
            $Artefact = $Configuration.Steps.BuildModule.$Type
            $ChosenType = $Type
        } else {
            $Artefact = $null
        }
        $ID = $null
    } else {
        $ID = $null
        $Artefact = $null
    }

    if ($ID) {
        $TextToDisplay = "Preparing Artefact of type '$ChosenType' (ID: $ID)"
    } else {
        $TextToDisplay = "Preparing Artefact of type '$ChosenType'"
    }

    # If artefact is not enabled, we don't want to do anything
    if ($null -eq $Artefact -or $Artefact.Count -eq 0) {
        return
    }

    Write-TextWithTime -Text $TextToDisplay -PreAppend Information -SpacesBefore '   ' {
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
                        $CurrentModulePath = [System.IO.Path]::Combine($FullProjectPath, $ChosenType)
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
                } else {
                    # default values
                    $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, $ChosenType, $TagName)
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
                    ScriptName                     = $Artefact.ScriptName
                    ZipIt                          = if ($ChosenType -in 'Packed', 'Releases', 'ScriptPacked') { $true } else { $false }
                    ConvertToScript                = if ($ChosenType -in 'ScriptPacked', 'Script') { $true } else { $false }
                    DestinationZip                 = $ArtefactsPath
                    PreScriptMerge                 = $Artefact.PreScriptMerge
                    PostScriptMerge                = $Artefact.PostScriptMerge
                    ID                             = if ($ChosenArtefact.ID) { $ChosenArtefact.ID } else { $null }
                }
                Remove-EmptyValue -Hashtable $SplatArtefact
                Add-Artefact @SplatArtefact
            }
        }
    } -ColorBefore Yellow -ColorTime Yellow -Color Yellow
}