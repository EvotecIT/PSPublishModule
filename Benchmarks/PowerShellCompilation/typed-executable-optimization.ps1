param([int] $Count, [int[]] $Values)

[long] $total = 0
for ([int] $value = 1; $value -le $Count; $value++) {
    $total += $value
}
foreach ($item in $Values) {
    $total += $item
}
return $total
