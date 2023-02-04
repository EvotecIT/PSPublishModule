function New-ConfigurationModules {
    <#
    .SYNOPSIS
    Provides a way to configure Required Modules or External Modules that will be used in the project.

    .DESCRIPTION
    Provides a way to configure Required Modules or External Modules that will be used in the project.

    .PARAMETER Type
    Choose between RequiredModule and ExternalModule, where RequiredModule is the default.

    .PARAMETER Name
    Name of PowerShell module that you want your module to depend on.

    .PARAMETER Version
    Version of PowerShell module that you want your module to depend on. If you don't specify a version, any version of the module is acceptable.
    You can also use word 'Latest' to specify that you want to use the latest version of the module, and the module will be pickup up latest version available on the system.

    .PARAMETER Guid
    Guid of PowerShell module that you want your module to depend on. If you don't specify a Guid, any Guid of the module is acceptable, but it is recommended to specify it.
    Alternatively you can use word 'Auto' to specify that you want to use the Guid of the module, and the module GUID

    .EXAMPLE
    An example

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [validateset('RequiredModule', 'ExternalModule')] $Type = 'RequiredModule',
        [Parameter(Mandatory)][string] $Name,
        [string] $Version,
        [string] $Guid
    )
    $ModuleInformation = [ordered] @{
        ModuleName    = $Name
        ModuleVersion = $Version
        Guid          = $Guid
    }
    Remove-EmptyValue -Hashtable $ModuleInformation
    if ($ModuleInformation.Count -eq 0) {
        return
    } elseif ($ModuleInformation.Count -eq 1 -and $ModuleInformation.Contains('ModuleName')) {
        $Configuration = $Name
    } else {
        $Configuration = $ModuleInformation
    }
    $Option = @{
        Type          = $Type
        Configuration = $Configuration
    }
    $Option
}