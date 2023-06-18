function New-ConfigurationModuleSkip {
    <#
    .SYNOPSIS
    Provides a way to ignore certain commands or modules during build process and continue module building on errors.

    .DESCRIPTION
    Provides a way to ignore certain commands or modules during build process and continue module building on errors.
    During build if a build module can't find require module or command it will fail the build process to prevent incomplete module from being created.
    This option allows to skip certain modules or commands and continue building the module.
    This is useful for commands we know are not available on all systems, or we get them different way.

    .PARAMETER IgnoreModuleName
    Ignore module name or names. If the module is not available on the system it will be ignored and build process will continue.

    .PARAMETER IgnoreFunctionName
    Ignore function name or names. If the function is not available in the module it will be ignored and build process will continue.

    .PARAMETER Force
    This switch will force build process to continue even if the module or command is not available (aka you know what you are doing)

    .EXAMPLE
    New-ConfigurationModuleSkip -IgnoreFunctionName 'Invoke-Formatter', 'Find-Module' -IgnoreModuleName 'platyPS'

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [string[]] $IgnoreModuleName,
        [string[]] $IgnoreFunctionName,
        [switch] $Force
    )

    $Configuration = [ordered] @{
        Type          = 'ModuleSkip'
        Configuration = [ordered] @{
            IgnoreModuleName   = $IgnoreModuleName
            IgnoreFunctionName = $IgnoreFunctionName
            Force              = $Force
        }
    }
    Remove-EmptyValue -Hashtable $Configuration.Configuration
    $Configuration

}