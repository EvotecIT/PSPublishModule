function New-ConfigurationImportModule {
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