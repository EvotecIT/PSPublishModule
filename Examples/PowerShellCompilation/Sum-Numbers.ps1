param([int]$Count = 5)
[int]$sum = 0
for ([int]$number = 1; $number -le $Count; $number++) {
    $sum += $number
}
return $sum
