function Add-Values {
    param(
        [Alias('L')][int] $Left,
        [int] $Right
    )
    "$Left`:$Right"
}
$Exact = Add-Values -Right 2 -Left 40
$AliasAndAbbreviation = Add-Values -Rig 4 -L 38
if ($Exact -ne '40:2' -or $AliasAndAbbreviation -ne '38:4') {
    throw 'Exact, alias, or abbreviated parameter binding did not preserve parameter identity.'
}
42
