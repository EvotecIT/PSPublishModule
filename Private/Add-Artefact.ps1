﻿function Add-Artefact {
    [CmdletBinding()]
    param(
        [string] $ModuleName,
        [string] $ModuleVersion,
        [string] $ArtefactName,
        [alias('IncludeTagName')][nullable[bool]] $IncludeTag,
        [nullable[bool]] $LegacyName,
        [nullable[bool]]  $CopyMainModule,
        [nullable[bool]]  $CopyRequiredModules,
        [string] $ProjectPath,
        [string] $Destination,
        [string] $DestinationMainModule,
        [string] $DestinationRequiredModules,
        [nullable[bool]] $DestinationFilesRelative,
        [nullable[bool]] $DestinationFoldersRelative,
        [System.Collections.IDictionary] $Files,
        [System.Collections.IDictionary] $Folders,
        [array] $RequiredModules,
        [string] $TagName,
        [string] $FileName,
        [nullable[bool]] $ZipIt,
        [string] $DestinationZip,
        [System.Collections.IDictionary] $Configuration
    )

    $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)

    Write-TextWithTime -Text "Copying merged release to $ResolvedDestination" -PreAppend Plus {
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
    if ($ZipIt -and $DestinationZip) {
        $ResolvedDestinationZip = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationZip)
        Write-TextWithTime -Text "Zipping merged release to $ResolvedDestinationZip" -PreAppend Plus {
            $zipSplat = @{
                #Source        = $CompressPath
                #Destination   = $FolderPathReleases
                Source        = $ResolvedDestination
                Destination   = $ResolvedDestinationZip

                Configuration = $Configuration
                LegacyName    = if ($Configuration.Steps.BuildModule.Releases -is [bool]) { $true } else { $false }
                ModuleName    = $ModuleName
                ModuleVersion = $ModuleVersion
                IncludeTag    = $IncludeTag
                ArtefactName  = $ArtefactName
            }
            Compress-Artefact @zipSplat
        }
    }
}