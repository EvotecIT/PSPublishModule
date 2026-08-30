[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Label = 'ready',

    [ValidateRange(0, 9)]
    [int] $Rule = 2,

    [ValidateRange(0, 1000)]
    [int] $Threshold = 12,

    [string[]] $Values = @('alpha', 'beta', 'gamma')
)

. "$PSScriptRoot/Model.ps1"
. "$PSScriptRoot/Aggregation.ps1"
. "$PSScriptRoot/Report.ps1"

return Get-ApplicationReport -Label $Label -Values $Values -Rule $Rule -Threshold $Threshold
