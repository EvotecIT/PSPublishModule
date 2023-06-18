function New-ConfigurationImportModule {
    [CmdletBinding()]
    param(
        [switch] $ImportSelf,
        [switch] $ImportRequiredModules
    )

    if ($PSBoundParameters.Keys.Contains('ImportSelf')) {
        $Output = [ordered] @{
            Type          = 'ImportModules'
            ImportModules = [ordered] @{
                Self = $ImportSelf
            }
        }
        $Output
    }
    if ($PSBoundParameters.Keys.Contains('ImportRequiredModules')) {
        $Output = [ordered] @{
            Type          = 'ImportModules'
            ImportModules = [ordered] @{
                RequiredModules = $ImportRequiredModules
            }
        }
        $Output
    }
    if ($VerbosePreference) {
        $Output = [ordered] @{
            Type          = 'ImportModules'
            ImportModules = [ordered] @{
                Verbose = $true
            }
        }
        $Output
    }
}