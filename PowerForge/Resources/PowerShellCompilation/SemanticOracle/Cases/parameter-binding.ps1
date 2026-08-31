function Get-LeftValue {
    param(
        [Alias('L')][int] $Left,
        [int] $Right
    )
    $Left
}
function Get-RightValue {
    param(
        [Alias('L')][int] $Left,
        [int] $Right
    )
    $Right
}
function Test-ParameterBinding {
    $ExactLeft = Get-LeftValue -Right 2 -Left 40
    $ExactRight = Get-RightValue -Right 2 -Left 40
    $AliasLeft = Get-LeftValue -Rig 4 -L 38
    $AbbreviatedRight = Get-RightValue -Rig 4 -L 38
    if ($ExactLeft -eq 40 -and $ExactRight -eq 2 -and $AliasLeft -eq 38 -and $AbbreviatedRight -eq 4) {
        return 42
    }
    return 0
}
Test-ParameterBinding
