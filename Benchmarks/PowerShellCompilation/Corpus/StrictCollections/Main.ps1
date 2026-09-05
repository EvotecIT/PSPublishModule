[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Token = 'alpha'
)

. "$PSScriptRoot/Operations.ps1"

[int] $score = Get-CollectionScore -Token $Token
return $score
