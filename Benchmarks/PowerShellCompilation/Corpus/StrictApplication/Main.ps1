[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Label = 'ready',

    [ValidateRange(0, 9)]
    [int] $Rule = 2,

    [ValidateRange(0, 1000)]
    [int] $Threshold = 12,

    [string[]] $Values = @('alpha', 'beta', 'gamma'),

    [string] $ResourcePath = '',

    [switch] $Fail
)

. "$PSScriptRoot/Model.ps1"
. "$PSScriptRoot/Aggregation.ps1"
. "$PSScriptRoot/Report.ps1"

if ($Fail) {
    throw [System.InvalidOperationException]::new('strict-application-requested-failure')
}

[string] $report = Get-ApplicationReport -Label $Label -Values $Values -Rule $Rule -Threshold $Threshold
if (-not [string]::IsNullOrWhiteSpace($ResourcePath)) {
    [string] $resource = [System.IO.File]::ReadAllText($ResourcePath)
    return "$report|$resource"
}
return $report
