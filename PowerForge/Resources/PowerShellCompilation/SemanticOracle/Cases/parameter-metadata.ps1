function Get-ValidatedValue {
    [CmdletBinding()]
    param([ValidateRange(1, 100)][int] $Value)
    $Value
}

$Rejected = $false
try {
    $null = Get-ValidatedValue -Value 101 -ErrorAction Stop
} catch {
    $Rejected = $_.FullyQualifiedErrorId -like 'ParameterArgumentValidationError*'
}
if (-not $Rejected) {
    throw 'ValidateRange did not reject the out-of-range value.'
}
(Get-ValidatedValue -Value 40) + 2
