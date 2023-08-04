function Add-Artefact {
    [CmdletBinding()]
    param(
        [string] $ModuleName,
        [string] $ModuleVersion,
        [string] $ArtefactName,
        [alias('IncludeTagName')][nullable[bool]] $IncludeTag,
        [nullable[bool]] $LegacyName,
        [nullable[bool]] $CopyMainModule,
        [nullable[bool]] $CopyRequiredModules,
        [string] $ProjectPath,
        [string] $Destination,
        [string] $DestinationMainModule,
        [string] $DestinationRequiredModules,
        [nullable[bool]] $DestinationFilesRelative,
        [alias('DestinationDirectoriesRelative')][nullable[bool]] $DestinationFoldersRelative,
        [alias('FilesOutput')][System.Collections.IDictionary] $Files,
        [alias('DirectoryOutput')][System.Collections.IDictionary] $Folders,
        [array] $RequiredModules,
        # [string] $TagName,
        #[string] $FileName,
        [nullable[bool]] $ZipIt,
        [string] $DestinationZip,
        [bool] $ConvertToScript,
        [string] $ScriptName,
        [string] $PreScriptMerge,
        [string] $PostScriptMerge,
        [System.Collections.IDictionary] $Configuration,
        [string] $ID
    )

    $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)

    if ($ConvertToScript) {
        Write-TextWithTime -Text "Converting merged release to script" -PreAppend Plus -SpacesBefore '      ' {
            $convertToScriptSplat = @{
                Enabled         = $true
                IncludeTagName  = $IncludeTag
                ModuleName      = $ModuleName
                Destination     = $DestinationMainModule
                PreScriptMerge  = $PreScriptMerge
                PostScriptMerge = $PostScriptMerge
                ScriptName      = $ScriptName
                Configuration   = $Configuration
                ModuleVersion   = $ModuleVersion
            }
            Remove-EmptyValue -Hashtable $convertToScriptSplat
            Copy-ArtefactToScript @convertToScriptSplat

            $copyRequiredModuleSplat = @{
                Enabled         = $CopyRequiredModules
                RequiredModules = $RequiredModules
                Destination     = $DestinationRequiredModules
            }
            Copy-ArtefactRequiredModule @copyRequiredModuleSplat

            $copyArtefactRequiredFoldersSplat = @{
                FoldersInput        = $Folders
                ProjectPath         = $ProjectPath
                Destination         = $Destination
                DestinationRelative = $DestinationFoldersRelative
            }
            Copy-ArtefactRequiredFolders @copyArtefactRequiredFoldersSplat

            $copyArtefactRequiredFilesSplat = @{
                FilesInput          = $Files
                ProjectPath         = $ProjectPath
                Destination         = $Destination
                DestinationRelative = $DestinationFilesRelative
            }
            Copy-ArtefactRequiredFiles @copyArtefactRequiredFilesSplat
        }
    } else {
        Write-TextWithTime -Text "Copying merged release to $ResolvedDestination" -PreAppend Addition -SpacesBefore '      ' {
            $copyMainModuleSplat = @{
                Enabled        = $true
                IncludeTagName = $IncludeTag
                ModuleName     = $ModuleName
                Destination    = $DestinationMainModule
            }
            Copy-ArtefactMainModule @copyMainModuleSplat

            $copyRequiredModuleSplat = @{
                Enabled         = $CopyRequiredModules
                RequiredModules = $RequiredModules
                Destination     = $DestinationRequiredModules
            }
            Copy-ArtefactRequiredModule @copyRequiredModuleSplat

            $copyArtefactRequiredFoldersSplat = @{
                FoldersInput        = $Folders
                ProjectPath         = $ProjectPath
                Destination         = $Destination
                DestinationRelative = $DestinationFoldersRelative
            }
            Copy-ArtefactRequiredFolders @copyArtefactRequiredFoldersSplat

            $copyArtefactRequiredFilesSplat = @{
                FilesInput          = $Files
                ProjectPath         = $ProjectPath
                Destination         = $Destination
                DestinationRelative = $DestinationFilesRelative
            }
            Copy-ArtefactRequiredFiles @copyArtefactRequiredFilesSplat
        }
    }
    if ($ZipIt -and $DestinationZip) {
        $ResolvedDestinationZip = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationZip)
        Write-TextWithTime -Text "Zipping merged release to $ResolvedDestinationZip" -PreAppend Information -SpacesBefore '      ' {
            $zipSplat = @{
                Source        = $ResolvedDestination
                Destination   = $ResolvedDestinationZip

                Configuration = $Configuration
                LegacyName    = if ($Configuration.Steps.BuildModule.Releases -is [bool]) { $true } else { $false }
                ModuleName    = $ModuleName
                ModuleVersion = $ModuleVersion
                IncludeTag    = $IncludeTag
                ArtefactName  = $ArtefactName
                ID            = $ID
            }
            Compress-Artefact @zipSplat
        }
        Write-TextWithTime -Text "Removing temporary files from $ResolvedDestination" -SpacesBefore '      ' -PreAppend Minus {
            Remove-ItemAlternative -Path $ResolvedDestination -SkipFolder -Exclude '*.zip' -ErrorAction Stop
        } -ColorBefore Yellow -ColorTime Green -ColorError Red -Color Yellow
    }
}