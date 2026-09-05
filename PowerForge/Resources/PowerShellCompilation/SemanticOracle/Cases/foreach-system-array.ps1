function Measure-SystemArray {
    param(
        [AllowNull()]
        [AllowEmptyCollection()]
        [array] $Values
    )

    [int] $count = 0
    foreach ($value in $Values) {
        $count += 1
    }
    return $count
}

$matrix = New-Object 'int[,]' 2,2
@(
    Measure-SystemArray -Values $null
    Measure-SystemArray -Values @()
    Measure-SystemArray -Values 42
    Measure-SystemArray -Values @('a', 2, $null)
    Measure-SystemArray -Values $matrix
) -join '|'
