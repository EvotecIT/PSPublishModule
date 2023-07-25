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

    $Failures = $false
    if ($Manifest.Contains('RequiredModules')) {
        foreach ($SubModule in $Manifest.RequiredModules) {
            if ($SubModule -is [string]) {
                #[Array] $AvailableModule = Get-Module -ListAvailable $SubModule -Verbose:$false
            } else {
                [Array] $AvailableModule = Get-Module -ListAvailable $SubModule.ModuleName -Verbose:$false
                if ($SubModule.ModuleVersion -in 'Latest', 'Auto') {
                    if ($AvailableModule) {
                        $SubModule.ModuleVersion = $AvailableModule[0].Version.ToString()
                    } else {
                        Write-Text -Text "[-] Module $($SubModule.ModuleName) is not available (Version), but defined as required with last version. Terminating." -Color Red
                        $Failures = $true
                    }
                }
                if ($SubModule.Guid -in 'Latest', 'Auto') {
                    if ($AvailableModule) {
                        $SubModule.Guid = $AvailableModule[0].Guid.ToString()
                    } else {
                        Write-Text -Text "[-] Module $($SubModule.ModuleName) is not available (GUID), but defined as required with last version. Terminating." -Color Red
                        $Failures = $true
                    }
                }
            }
        }
    }
    if ($Failures -eq $true) {
        $false
    }
}