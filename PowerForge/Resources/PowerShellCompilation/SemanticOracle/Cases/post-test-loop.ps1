[int] $Value = 40
do { $Value += 1 } while ($Value -lt 41)
do { $Value += 1 } until ($Value -ge 42)
$Value
