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
function Get-ExplicitPositionValue {
    [CmdletBinding(PositionalBinding = $false)]
    param(
        [Parameter(Position = 5)][int] $Five,
        [Parameter(Position = 2)][int] $Two,
        [int] $NamedOnly
    )
    "$Two|$Five|$NamedOnly"
}
function Test-ParameterBinding {
    $ExactLeft = Get-LeftValue -Right 2 -Left 40
    $ExactRight = Get-RightValue -Right 2 -Left 40
    $AliasLeft = Get-LeftValue -Rig 4 -L 38
    $AbbreviatedRight = Get-RightValue -Rig 4 -L 38
    $ExplicitPosition = Get-ExplicitPositionValue 2 -NamedOnly 3 17
    $ExplicitPositionAfterNamed = Get-ExplicitPositionValue -Five 17 -NamedOnly 3 2
    if ($ExactLeft -eq 40 -and $ExactRight -eq 2 -and $AliasLeft -eq 38 -and $AbbreviatedRight -eq 4 -and $ExplicitPosition -eq '2|17|3' -and $ExplicitPositionAfterNamed -eq '2|17|3') {
        return 42
    }
    return 0
}
Test-ParameterBinding
