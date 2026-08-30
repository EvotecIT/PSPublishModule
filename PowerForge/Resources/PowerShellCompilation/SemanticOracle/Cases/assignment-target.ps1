$Values = [int[]] (0, 0)
$Values[1] = 40
$Holder = [pscustomobject] @{ Result = 0 }
$Holder.Result = 2
$Values[1] + $Holder.Result
