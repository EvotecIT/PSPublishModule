﻿function New-ConfigurationModule {
    <#
    .SYNOPSIS
    Provides a way to configure Required Modules or External Modules that will be used in the project.

    .DESCRIPTION
    Provides a way to configure Required Modules or External Modules that will be used in the project.

    .PARAMETER Type
    Choose between RequiredModule, ExternalModule and ApprovedModule, where RequiredModule is the default.

    .PARAMETER Name
    Name of PowerShell module that you want your module to depend on.

    .PARAMETER Version
    Version of PowerShell module that you want your module to depend on.
    If you don't specify a version, any version of the module is acceptable.
    You can also use word 'Latest' to specify that you want to use the latest version of the module, and the module will be pickup up latest version available on the system.

    .PARAMETER RequiredVersion
    RequiredVersion of PowerShell module that you want your module to depend on.
    This forces the module to require this specific version.
    When using Version, the module will be picked up if it's equal or higher than the version specified.
    When using RequiredVersion, the module will be picked up only if it's equal to the version specified.

    .PARAMETER Guid
    Guid of PowerShell module that you want your module to depend on. If you don't specify a Guid, any Guid of the module is acceptable, but it is recommended to specify it.
    Alternatively you can use word 'Auto' to specify that you want to use the Guid of the module, and the module GUID

    .EXAMPLE
    # Add standard module dependencies (directly, but can be used with loop as well)
    New-ConfigurationModule -Type RequiredModule -Name 'platyPS' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModule -Type RequiredModule -Name 'powershellget' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModule -Type RequiredModule -Name 'PSScriptAnalyzer' -Guid 'Auto' -Version 'Latest'

    .EXAMPLE
    # Add external module dependencies, using loop for simplicity
    foreach ($Module in @('Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')) {
        New-ConfigurationModule -Type ExternalModule -Name $Module
    }

    .EXAMPLE
    # Add approved modules, that can be used as a dependency, but only when specific function from those modules is used
    # And on that time only that function and dependant functions will be copied over
    # Keep in mind it has it's limits when "copying" functions such as it should not depend on DLLs or other external files
    New-ConfigurationModule -Type ApprovedModule -Name 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [validateset('RequiredModule', 'ExternalModule', 'ApprovedModule')] $Type = 'RequiredModule',
        [Parameter(Mandatory)][string[]] $Name,
        [string] $Version,
        [string] $RequiredVersion,
        [string] $Guid
    )
    foreach ($N in $Name) {
        if ($Type -eq 'ApprovedModule') {
            # Approved modules are simplified, as they don't have any other options
            $Configuration = $N
        } else {
            $ModuleInformation = [ordered] @{
                ModuleName      = $N
                ModuleVersion   = $Version
                RequiredVersion = $RequiredVersion
                Guid            = $Guid
            }
            if ($Version -and $RequiredVersion) {
                throw 'You cannot use both Version and RequiredVersion at the same time for the same module. Please choose one or the other (New-ConfigurationModule) '
            }
            Remove-EmptyValue -Hashtable $ModuleInformation
            if ($ModuleInformation.Count -eq 0) {
                return
            } elseif ($ModuleInformation.Count -eq 1 -and $ModuleInformation.Contains('ModuleName')) {
                $Configuration = $N
            } else {
                $Configuration = $ModuleInformation
            }
        }
        $Option = @{
            Type          = $Type
            Configuration = $Configuration
        }
        $Option
    }
}

Register-ArgumentCompleter -CommandName New-ConfigurationModule -ParameterName Version -ScriptBlock {
    param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
    'Auto', 'Latest' | Where-Object { $_ -like "*$wordToComplete*" }
}

Register-ArgumentCompleter -CommandName New-ConfigurationModule -ParameterName Guid -ScriptBlock {
    param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
    'Auto', 'Latest' | Where-Object { $_ -like "*$wordToComplete*" }
}