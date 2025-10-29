function New-ConfigurationInformation {
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
