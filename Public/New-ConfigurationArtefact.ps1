function New-ConfigurationArtefact {
    <#
    .SYNOPSIS
    Tells the module to create artefact of specified type

    .DESCRIPTION
    Tells the module to create artefact of specified type
    There can be multiple artefacts created (even of same type)
    At least one packed artefact is required for publishing to GitHub

    .PARAMETER Type
    There are 4 types of artefacts:
    - Unpacked - unpacked module (useful for testing)
    - Packed - packed module (as zip) - usually used for publishing to GitHub or copying somewhere
    - Script - script that is module in form of PS1 without PSD1 - only applicable to very simple modules
    - PackedScript - packed module (as zip) that is script that is module in form of PS1 without PSD1 - only applicable to very simple modules

    .PARAMETER ID
    Optional ID of the artefact. To be used by New-ConfigurationPublish cmdlet
    If not specified, the first packed artefact will be used for publishing to GitHub

    .PARAMETER Enable
    Enable artefact creation. By default artefact creation is disabled.

    .PARAMETER IncludeTagName
    Include tag name in artefact name. By default tag name is not included.
    Alternatively you can provide ArtefactName parameter to specify your own artefact name (with or without TagName)

    .PARAMETER Path
    Path where artefact will be created.
    Please choose a separate directory for each artefact type, as logic may be interfering one another.

    .PARAMETER AddRequiredModules
    Add required modules to artefact by copying them over. By default required modules are not added.

    .PARAMETER ModulesPath
    Path where main module or required module (if not specified otherwise in RequiredModulesPath) will be copied to.
    By default it will be put in the Path folder if not specified

    .PARAMETER RequiredModulesPath
    Path where required modules will be copied to. By default it will be put in the Path folder if not specified.
    If ModulesPath is specified, but RequiredModulesPath is not specified it will be put into ModulesPath folder.

    .PARAMETER CopyDirectories
    Provide Hashtable of directories to copy to artefact. Key is source directory, value is destination directory.

    .PARAMETER CopyFiles
    Provide Hashtable of files to copy to artefact. Key is source file, value is destination file.

    .PARAMETER CopyDirectoriesRelative
    Define if destination directories should be relative to artefact root. By default they are not.

    .PARAMETER CopyFilesRelative
    Define if destination files should be relative to artefact root. By default they are not.

    .PARAMETER Clear
    Clear artefact directory before creating artefact. By default artefact directory is not cleared.

    .PARAMETER ArtefactName
    The name of the artefact. If not specified, the default name will be used.
    You can use following variables that will be replaced with actual values:
    - <ModuleName> / {ModuleName} - the name of the module i.e PSPublishModule
    - <ModuleVersion> / {ModuleVersion} - the version of the module i.e 1.0.0
    - <ModuleVersionWithPreRelease> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
    - <TagModuleVersionWithPreRelease> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
    - <TagName> / {TagName} - the name of the tag - i.e. v1.0.0

    .EXAMPLE
    New-ConfigurationArtefact -Type Unpacked -Enable -Path "$PSScriptRoot\..\Artefacts\Unpacked" -RequiredModulesPath "$PSScriptRoot\..\Artefacts\Unpacked\Modules"

    .EXAMPLE
    # standard artefact, packed with tag name without any additional modules or required modules
    New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed" -IncludeTagName

    .EXAMPLE
    # Create artefact in form of a script. This is useful for very simple modules that should be just single PS1 file
    New-ConfigurationArtefact -Type Script -Enable -Path "$PSScriptRoot\..\Artefacts\Script" -IncludeTagName

    .EXAMPLE
    # Create artefact in form of a script. This is useful for very simple modules that should be just single PS1 file
    # But additionally pack it into zip fileĄŚż$%#
    New-ConfigurationArtefact -Type ScriptPacked -Enable -Path "$PSScriptRoot\..\Artefacts\ScriptPacked" -ArtefactName "Script-<ModuleName>-$((Get-Date).ToString('yyyy-MM-dd')).zip"

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)][ScriptBlock] $ScriptMerge,
        [Parameter(Mandatory)][ValidateSet('Unpacked', 'Packed', 'Script', 'ScriptPacked')][string] $Type,
        [switch] $Enable,
        [switch] $IncludeTagName,
        [string] $Path,
        [alias('RequiredModules')][switch] $AddRequiredModules,
        [string] $ModulesPath,
        [string] $RequiredModulesPath,
        [System.Collections.IDictionary] $CopyDirectories,
        [System.Collections.IDictionary] $CopyFiles,
        [switch] $CopyDirectoriesRelative,
        [switch] $CopyFilesRelative,
        [switch] $Clear,
        [string] $ArtefactName,
        [string] $ID
    )

    # if ($Type -eq 'Packed') {
    #     $ArtefactType = 'Releases'
    # } else {
    #     $ArtefactType = 'ReleasesUnpacked'
    # }

    $Artefact = [ordered ] @{
        Type          = $Type #$ArtefactType
        Configuration = [ordered] @{
            Type            = $Type
            RequiredModules = [ordered] @{

            }
        }
    }

    if ($PSBoundParameters.ContainsKey('Enable')) {
        $Artefact['Configuration']['Enabled'] = $Enable
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         Enabled = $Enable
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('IncludeTagName')) {
        $Artefact['Configuration']['IncludeTagName'] = $IncludeTagName
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         IncludeTagName = $IncludeTagName
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('Path')) {
        $Artefact['Configuration']['Path'] = $Path
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         Path = $Path
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('RequiredModulesPath')) {
        $Artefact['Configuration']['RequiredModules']['Path'] = $RequiredModulesPath
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         RequiredModules = @{
        #             Path = $RequiredModulesPath
        #         }
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('AddRequiredModules')) {
        $Artefact['Configuration']['RequiredModules']['Enabled'] = $true
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         RequiredModules = @{
        #             Enabled = $true
        #         }
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('ModulesPath')) {
        $Artefact['Configuration']['RequiredModules']['ModulesPath'] = $ModulesPath
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         RequiredModules = @{
        #             ModulesPath = $ModulesPath
        #         }
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('CopyDirectories')) {
        $Artefact['Configuration']['DirectoryOutput'] = $CopyDirectories
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         DirectoryOutput = $CopyDirectories
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('CopyDirectoriesRelative')) {
        $Artefact['Configuration']['DestinationDirectoriesRelative'] = $CopyDirectoriesRelative.IsPresent
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         DestinationDirectoriesRelative = $CopyDirectoriesRelative.IsPresent
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('CopyFiles')) {
        $Artefact['Configuration']['FilesOutput'] = $CopyFiles
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         FilesOutput = $CopyFiles
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('CopyFilesRelative')) {
        $Artefact['Configuration']['DestinationFilesRelative'] = $CopyFilesRelative.IsPresent
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         DestinationFilesRelative = $CopyFilesRelative.IsPresent
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('Clear')) {
        $Artefact['Configuration']['Clear'] = $Clear
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         Clear = $Clear
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('ArtefactName')) {
        $Artefact['Configuration']['ArtefactName'] = $ArtefactName
        # [ordered] @{
        #     Type          = $ArtefactType
        #     $ArtefactType = [ordered] @{
        #         ArtefactName = $ArtefactName
        #     }
        # }
    }
    if ($PSBoundParameters.ContainsKey('ScriptMerge')) {
        try {
            $Artefact['Configuration']['ScriptMerge'] = Invoke-Formatter -ScriptDefinition $ScriptMerge.ToString()
        } catch {
            Write-Text -Text "[i] Unable to format merge script provided by user. Error: $($_.Exception.Message). Using original script." -Color Red
            $Artefact['Configuration']['ScriptMerge'] = $ScriptMerge.ToString()
        }
    }
    if ($PSBoundParameters.ContainsKey('ID')) {
        $Artefact['Configuration']['ID'] = $ID
    }
    $Artefact
}