function New-ConfigurationPlaceHolder {
    <#
    .SYNOPSIS
    Command helping define custom placeholders replacing content within a script or module during the build process.

    .DESCRIPTION
    Command helping define custom placeholders replacing content within a script or module during the build process.
    It modifies only the content of the script or module (PSM1) and does not modify the sources.

    .PARAMETER CustomReplacement
    Hashtable array with custom placeholders to replace. Each hashtable must contain two keys: Find and Replace.

    .PARAMETER Find
    The string to find in the script or module content.

    .PARAMETER Replace
    The string to replace the Find string in the script or module content.

    .EXAMPLE
    New-ConfigurationPlaceHolder -Find '{CustomName}' -Replace 'SpecialCase'

    .EXAMPLE
    New-ConfigurationPlaceHolder -CustomReplacement @(
        @{ Find = '{CustomName}'; Replace = 'SpecialCase' }
        @{ Find = '{CustomVersion}'; Replace = '1.0.0' }
    )

    .NOTES
    General notes
    #>
    [CmdletBinding(DefaultParameterSetName = 'FindAndReplace')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'CustomReplacement')][System.Collections.IDictionary[]] $CustomReplacement,
        [Parameter(Mandatory, ParameterSetName = 'FindAndReplace')][string] $Find,
        [Parameter(Mandatory, ParameterSetName = 'FindAndReplace')][string] $Replace
    )

    foreach ($Replacement in $CustomReplacement) {
        [ordered] @{
            Type          = 'PlaceHolder'
            Configuration = $Replacement
        }
    }
    if ($PSBoundParameters.ContainsKey("Find") -and $PSBoundParameters.ContainsKey("Replace")) {
        [ordered] @{
            Type          = 'PlaceHolder'
            Configuration = @{
                Find    = $Find
                Replace = $Replace
            }
        }
    }
}