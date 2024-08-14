function New-ConfigurationPlaceHolder {
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