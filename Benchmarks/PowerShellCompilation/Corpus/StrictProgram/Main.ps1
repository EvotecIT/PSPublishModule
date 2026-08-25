[CmdletBinding()]
param(
    [Alias('n')]
    [ValidateRange(0, 32)]
    [int] $Number = 4,

    [ValidateNotNullOrEmpty()]
    [string] $Label = 'item'
)

. "$PSScriptRoot/Operations.ps1"

[int] $score = Get-PortableScore -Number $Number -Label $Label
return $score
