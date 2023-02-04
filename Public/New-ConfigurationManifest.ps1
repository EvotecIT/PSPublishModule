function New-ConfigurationManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ModuleVersion,
        [ValidateSet('Desktop', 'Core')][string[]] $CompatiblePSEditions = @('Desktop', 'Core'),
        [Parameter(Mandatory)][string] $GUID,
        [string] $Author,
        [string] $CompanyName,
        [string] $Copyright,
        [string] $Description,
        [string] $PowerShellVersion = '5.1',
        [string[]] $Tags,
        [string] $IconUri,
        [string] $ProjectUri,
        [string] $DotNetFrameworkVersion
    )

    $Manifest = [ordered] @{
        ModuleVersion        = $ModuleVersion
        CompatiblePSEditions = @($CompatiblePSEditions)
        GUID                 = $GUID
        Author               = $Author
        CompanyName          = $CompanyName
        Copyright            = $Copyright
        Description          = $Description
        PowerShellVersion    = $PowerShellVersion
        Tags                 = $Tags
        IconUri              = $IconUri
        ProjectUri           = $ProjectUri
    }

    $Option = @{
        Type          = 'Manifest'
        Configuration = $Manifest
    }
    $Option
}