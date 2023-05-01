function Add-Artefact {
    [CmdletBinding()]
    param(
        [string] $ModuleName,
        [bool] $CopyMainModule,
        [bool] $CopyRequiredModules,
        [bool] $IncludeTagName,
        [string] $ProjectPath,
        [string] $Destination,
        [string] $DestinationMainModule,
        [string] $DestinationRequiredModules,
        [bool] $DestinationFilesRelative,
        [bool] $DestinationFoldersRelative,
        [System.Collections.IDictionary] $Files,
        [System.Collections.IDictionary] $Folders,
        [array] $RequiredModules
    )

    $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)

    Write-TextWithTime -Text "Copying merged release to $ResolvedDestination" -PreAppend Plus {
        $copyMainModuleSplat = @{
            Enabled        = $true
            IncludeTagName = $IncludeTagName
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