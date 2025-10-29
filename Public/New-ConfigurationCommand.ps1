function New-ConfigurationCommand {
    <#
    .SYNOPSIS
    Defines a command import configuration for the build pipeline.

    .DESCRIPTION
    Creates a configuration object that specifies a module and one or more command names
    to reference during the build process (for discovery, linking, or documentation).

    .PARAMETER ModuleName
    Name of the module that contains the commands.

    .PARAMETER CommandName
    One or more command names to reference from the module.

    .EXAMPLE
    New-ConfigurationCommand -ModuleName 'PSSharedGoods' -CommandName 'Write-Text','Remove-EmptyValue'
    #>
    [CmdletBinding()]
    param(
        [string] $ModuleName,
        [string[]] $CommandName
    )

    $Configuration = [ordered] @{
        Type          = 'Command'
        Configuration = [ordered] @{
            ModuleName  = $ModuleName
            CommandName = $CommandName
        }
    }
    $Configuration
}
