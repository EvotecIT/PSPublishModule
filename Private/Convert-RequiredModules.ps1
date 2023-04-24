function Convert-RequiredModules {
    <#
    .SYNOPSIS
    Converts the RequiredModules section of the manifest to the correct format

    .DESCRIPTION
    Converts the RequiredModules section of the manifest to the correct format
    Fixes the ModuleVersion and Guid if set to 'Latest' or 'Auto'

    .PARAMETER Configuration
    The configuration object of the module

    .EXAMPLE
    Convert-RequiredModules -Configuration $Configuration

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration
    )
    $Manifest = $Configuration.Information.Manifest

    if ($Manifest.Contains('RequiredModules')) {
        foreach ($SubModule in $Manifest.RequiredModules) {
            [Array] $AvailableModule = Get-Module -ListAvailable $SubModule.ModuleName -Verbose:$false

            if ($SubModule.ModuleVersion -eq 'Latest') {
                if ($AvailableModule) {
                    $SubModule.ModuleVersion = $AvailableModule[0].Version.ToString()
                } else {
                    Write-Text -Text "[-] Module $($SubModule.ModuleName) is not available, but defined as required with last version. Terminating." -Color Red
                    return $false
                }
            }
            if ($SubModule.Guid -eq 'Auto') {
                if ($AvailableModule) {
                    $SubModule.Guid = $AvailableModule[0].Guid.ToString()
                } else {
                    Write-Text -Text "[-] Module $($SubModule.ModuleName) is not available, but defined as required with last version. Terminating." -Color Red
                    return $false
                }
            }
        }
    }

}