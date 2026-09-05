function Get-WeightedScore {
    [OutputType([int])]
    param(
        [string] $Value,
        [int] $Multiplier
    )

    [int] $length = Get-LabelLength -Value $Value
    [int] $score = $length
    $score *= $Multiplier
    return $score
}

function Get-TotalScore {
    [OutputType([int])]
    param(
        [string[]] $Values,
        [int] $Multiplier
    )

    [int] $total = 0
    [string] $value = ''
    foreach ($value in $Values) {
        $total += Get-WeightedScore -Value $value -Multiplier $Multiplier
    }
    return $total
}

function Get-MaximumScore {
    [OutputType([int])]
    param(
        [string[]] $Values,
        [int] $Multiplier
    )

    [int] $maximum = 0
    [string] $value = ''
    foreach ($value in $Values) {
        [int] $score = Get-WeightedScore -Value $value -Multiplier $Multiplier
        if ($score -gt $maximum) {
            $maximum = $score
        }
    }
    return $maximum
}

function Get-QualifiedScoreCount {
    [OutputType([int])]
    param(
        [string[]] $Values,
        [int] $Multiplier,
        [int] $Threshold
    )

    [int] $count = 0
    [string] $value = ''
    foreach ($value in $Values) {
        [int] $score = Get-WeightedScore -Value $value -Multiplier $Multiplier
        if (Test-ScoreThreshold -Score $score -Threshold $Threshold) {
            $count += 1
        }
    }
    return $count
}
