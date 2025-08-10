function New-ConfigurationManifest {
    <#
    .SYNOPSIS
    Creates a new configuration manifest for a PowerShell module.

    .DESCRIPTION
    This function generates a new configuration manifest for a PowerShell module. The manifest includes metadata about the module such as version, author, company, and other relevant information. It also allows specifying the functions, cmdlets, and aliases to export.

    .PARAMETER ModuleVersion
    Specifies the version of the module. When multiple versions of a module exist on a system, the latest version is loaded by default when you run Import-Module.

    .PARAMETER CompatiblePSEditions
    Specifies the module's compatible PowerShell editions. Valid values are 'Desktop' and 'Core'.

    .PARAMETER GUID
    Specifies a unique identifier for the module. The GUID is used to distinguish between modules with the same name.

    .PARAMETER Author
    Identifies the module author.

    .PARAMETER CompanyName
    Identifies the company or vendor who created the module.

    .PARAMETER Copyright
    Specifies a copyright statement for the module.

    .PARAMETER Description
    Describes the module at a high level.

    .PARAMETER PowerShellVersion
    Specifies the minimum version of PowerShell this module requires. Default is '5.1'.

    .PARAMETER Tags
    Specifies tags for the module.

    .PARAMETER IconUri
    Specifies the URI for the module's icon.

    .PARAMETER ProjectUri
    Specifies the URI for the module's project page.

    .PARAMETER DotNetFrameworkVersion
    Specifies the minimum version of the Microsoft .NET Framework that the module requires.

    .PARAMETER LicenseUri
    Specifies the URI for the module's license.

    .PARAMETER Prerelease
    Specifies the prerelease tag for the module.

    .PARAMETER FunctionsToExport
    Defines functions to export in the module manifest. By default, functions are auto-detected, but this allows you to override that.

    .PARAMETER AliasesToExport
    Defines aliases to export in the module manifest. By default, aliases are auto-detected, but this allows you to override that.

    .PARAMETER CmdletsToExport
    Defines cmdlets to export in the module manifest. By default, cmdlets are auto-detected, but this allows you to override that.

    .PARAMETER FormatsToProcess
    Specifies formatting files (.ps1xml) that run when the module is imported.

    .EXAMPLE
    New-ConfigurationManifest -ModuleVersion '1.0.0' -GUID '12345678-1234-1234-1234-1234567890ab' -Author 'John Doe' -CompanyName 'Example Corp' -Description 'This is an example module.'

    .NOTES
    This function helps in creating a standardized module manifest for PowerShell modules.
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
        [string[]] $AliasesToExport,
        [string[]] $FormatsToProcess
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
        FormatsToProcess       = $FormatsToProcess
    }
    Remove-EmptyValue -Hashtable $Manifest

    $Option = @{
        Type          = 'Manifest'
        Configuration = $Manifest
    }
    $Option
}