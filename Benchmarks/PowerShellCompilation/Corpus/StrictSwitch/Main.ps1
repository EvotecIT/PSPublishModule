[CmdletBinding()]
param(
    [ValidateRange(0, 9)]
    [int] $Code = 2
)

. "$PSScriptRoot/Rules.ps1"

[int] $value = Resolve-RuleValue -Code $Code
return $value
