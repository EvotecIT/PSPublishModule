function New-ConfigurationImportModule {
    <#
    .SYNOPSIS
    Creates a configuration for importing PowerShell modules.

    .DESCRIPTION
    This function generates a configuration object for importing PowerShell modules. It allows specifying whether to import the current module itself and/or any required modules.

    .PARAMETER ImportSelf
    Indicates whether to import the current module itself.

    .PARAMETER ImportRequiredModules
    Indicates whether to import any required modules specified in the module manifest.

    .EXAMPLE
    New-ConfigurationImportModule -ImportSelf -ImportRequiredModules

    .NOTES
    This function helps in creating a standardized import configuration for PowerShell modules.
    #>
    [CmdletBinding()]
    param(
        [switch] $ImportSelf,
        [switch] $ImportRequiredModules
    )

    $Output = [ordered] @{
        Type          = 'ImportModules'
        ImportModules = [ordered] @{}
    }
    if ($PSBoundParameters.Keys.Contains('ImportSelf')) {
        $Output['ImportModules']['Self'] = $ImportSelf
    }
    if ($PSBoundParameters.Keys.Contains('ImportRequiredModules')) {
        $Output['ImportModules']['RequiredModules'] = $ImportRequiredModules
    }
    if ($VerbosePreference) {
        $Output['ImportModules']['Verbose'] = $true
    }
    $Output
}