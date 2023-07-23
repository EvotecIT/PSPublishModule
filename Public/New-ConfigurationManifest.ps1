function New-ConfigurationManifest {
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
        [alias('PrereleaseTag')][string] $Prerelease
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
    }
    Remove-EmptyValue -Hashtable $Manifest

    $Option = @{
        Type          = 'Manifest'
        Configuration = $Manifest
    }
    $Option
}