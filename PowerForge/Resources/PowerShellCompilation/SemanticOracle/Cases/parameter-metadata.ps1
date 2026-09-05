function Get-ValidatedValue {
    [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute('PowerForge.Semantic', 'ParameterMetadata', Justification = 'Compile-time-only oracle metadata.')]
    [CmdletBinding()]
    param([ValidateRange(1, 100)][int] $Value)
    $Value
}

$Rejected = $false
try {
    $null = Get-ValidatedValue -Value 101
} catch {
    $Rejected = $true
}
if (-not $Rejected) {
    return -1
}
[int] $Result = Get-ValidatedValue -Value 40
$Result += 2
return $Result
