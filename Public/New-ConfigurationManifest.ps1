function New-ConfigurationManifest {
    <#
    .SYNOPSIS
    Short description

    .DESCRIPTION
    Long description

    .PARAMETER ModuleVersion
    This setting specifies the version of the module. When multiple versions of a module exist on a system, the latest version is loaded by default when you run Import-Module

    .PARAMETER CompatiblePSEditions
    This setting specifies the module's compatible PSEditions.

    .PARAMETER GUID
    This setting specifies a unique identifier for the module. The GUID is used to distinguish between modules with the same name.

    .PARAMETER Author
    This setting identifies the module author.

    .PARAMETER CompanyName
    This setting identifies the company or vendor who created the module.

    .PARAMETER Copyright
    This setting specifies a copyright statement for the module.

    .PARAMETER Description
    This setting describes the module at a high level.

    .PARAMETER PowerShellVersion
    This setting specifies the minimum version of PowerShell this module requires.

    .PARAMETER Tags
    Parameter description

    .PARAMETER IconUri
    Parameter description

    .PARAMETER ProjectUri
    Parameter description

    .PARAMETER DotNetFrameworkVersion
    This setting specifies the minimum version of the Microsoft .NET Framework that the module requires.

    .PARAMETER LicenseUri
    Parameter description

    .PARAMETER Prerelease
    Parameter description

    .PARAMETER FunctionsToExport
    Allows ability to define functions to export in the module manifest.
    By default functions are auto-detected, but this allows you to override that.

    .PARAMETER AliasesToExport
    Allows ability to define aliases to export in the module manifest.
    By default aliases are auto-detected, but this allows you to override that.

    .PARAMETER CmdletsToExport
    Allows ability to define commands to export in the module manifest.
    Currently module is not able to auto-detect commands, so you can use it to define, or module will use wildcard if it detects binary module.

    .EXAMPLE
    An example

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ModuleVersion,
        [ValidateSet('Desktop', 'Core')][string[]] $CompatiblePSEditions = @('Desktop', 'Core'),
        [Parameter(Mandatory)][string] $GUID,
        [Parameter(Mandatory)][string] $Author,
        [string] $CompanyName,
        [string] $Copyright,
        [string] $Description,
        [string] $PowerShellVersion = '5.1',
        [string[]] $Tags,
        [string] $IconUri,
        [string] $ProjectUri,
        [string] $DotNetFrameworkVersion,
        [string] $LicenseUri,
        [alias('PrereleaseTag')][string] $Prerelease,
        [string[]] $FunctionsToExport,
        [string[]] $CmdletsToExport,
        [string[]] $AliasesToExport
    )

    $Manifest = [ordered] @{
        ModuleVersion          = $ModuleVersion
        CompatiblePSEditions   = @($CompatiblePSEditions)
        GUID                   = $GUID
        Author                 = $Author
        CompanyName            = $CompanyName
        Copyright              = $Copyright
        Description            = $Description
        PowerShellVersion      = $PowerShellVersion
        Tags                   = $Tags
        IconUri                = $IconUri
        ProjectUri             = $ProjectUri
        DotNetFrameworkVersion = $DotNetFrameworkVersion
        LicenseUri             = $LicenseUri
        Prerelease             = $Prerelease
        FunctionsToExport      = $FunctionsToExport
        CmdletsToExport        = $CmdletsToExport
        AliasesToExport        = $AliasesToExport
    }
    Remove-EmptyValue -Hashtable $Manifest

    $Option = @{
        Type          = 'Manifest'
        Configuration = $Manifest
    }
    $Option
}