function New-ConfigurationInformation {
    <#
    .SYNOPSIS
    Describes what to include/exclude in the module build and how libraries are organized.

    .DESCRIPTION
    Emits a configuration block with folder-level include/exclude rules and optional library
    locations that the builder uses to stage content prior to merge/packaging.

    .PARAMETER FunctionsToExportFolder
    Folder name containing public functions to export (e.g., 'Public').

    .PARAMETER AliasesToExportFolder
    Folder name containing public aliases to export (e.g., 'Public').

    .PARAMETER ExcludeFromPackage
    Paths or patterns to exclude from artefacts (e.g., 'Ignore','Docs','Examples').

    .PARAMETER IncludeRoot
    File patterns from the root to include (e.g., '*.psm1','*.psd1','License*').

    .PARAMETER IncludePS1
    Folder names where PS1 files should be included (e.g., 'Private','Public','Enums','Classes').

    .PARAMETER IncludeAll
    Folder names to include entirely (e.g., 'Images','Resources','Templates','Bin','Lib','Data').

    .PARAMETER IncludeCustomCode
    Scriptblock executed during staging to add custom files/folders.

    .PARAMETER IncludeToArray
    Advanced form to pass IncludeRoot/IncludePS1/IncludeAll as a single hashtable.

    .PARAMETER LibrariesCore
    Relative path to libraries compiled for Core (default 'Lib/Core').

    .PARAMETER LibrariesDefault
    Relative path to libraries for classic .NET (default 'Lib/Default').

    .PARAMETER LibrariesStandard
    Relative path to libraries for .NET Standard (default 'Lib/Standard').

    .EXAMPLE
    New-ConfigurationInformation -IncludeAll 'Internals\' -IncludePS1 'Private','Public' -ExcludeFromPackage 'Ignore','Docs'
    #>
    [cmdletbinding()]
    param(
        [string] $FunctionsToExportFolder,
        [string] $AliasesToExportFolder,
        [string[]] $ExcludeFromPackage,
        [string[]] $IncludeRoot,
        [string[]] $IncludePS1,
        [string[]] $IncludeAll,
        [scriptblock] $IncludeCustomCode,
        [System.Collections.IDictionary] $IncludeToArray,
        [string] $LibrariesCore,
        [string] $LibrariesDefault,
        [string] $LibrariesStandard
    )

    $Configuration = [ordered] @{
        FunctionsToExportFolder = $FunctionsToExportFolder
        AliasesToExportFolder   = $AliasesToExportFolder
        ExcludeFromPackage      = $ExcludeFromPackage
        IncludeRoot             = $IncludeRoot
        IncludePS1              = $IncludePS1
        IncludeAll              = $IncludeAll
        IncludeCustomCode       = $IncludeCustomCode
        IncludeToArray          = $IncludeToArray
        LibrariesCore           = $LibrariesCore
        LibrariesDefault        = $LibrariesDefault
        LibrariesStandard       = $LibrariesStandard
    }
    Remove-EmptyValue -Hashtable $Configuration

    $Option = @{
        Type          = 'Information'
        Configuration = $Configuration
    }
    $Option
}
