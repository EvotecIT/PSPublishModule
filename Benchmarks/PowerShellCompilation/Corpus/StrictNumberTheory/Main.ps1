[CmdletBinding()]
param(
    [ValidateRange(1, 1000000)]
    [int] $Left = 1071,
    [ValidateRange(1, 1000000)]
    [int] $Right = 462,
    [ValidateRange(2, 10000)]
    [int] $Maximum = 100
)

function Get-GreatestCommonDivisor {
    param([int] $A, [int] $B)
    while ($B -ne 0) {
        [int] $remainder = $A % $B
        $A = $B
        $B = $remainder
    }
    return $A
}

function Get-PrimeCount {
    param([int] $Limit)
    [int] $count = 0
    for ([int] $candidate = 2; $candidate -le $Limit; $candidate++) {
        [bool] $prime = $true
        for ([int] $divisor = 2; $divisor -lt $candidate; $divisor++) {
            if (($candidate % $divisor) -eq 0) {
                $prime = $false
                break
            }
        }
        if ($prime) { $count++ }
    }
    return $count
}

[int] $gcd = Get-GreatestCommonDivisor -A $Left -B $Right
[int] $primes = Get-PrimeCount -Limit $Maximum
return "$gcd|$primes"
