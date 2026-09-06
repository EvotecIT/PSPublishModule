[CmdletBinding()]
param(
    [ValidateRange(1, 9999)]
    [int] $FirstYear = 1900,
    [ValidateRange(1, 9999)]
    [int] $LastYear = 2100
)

function Test-LeapYear {
    param([int] $Year)
    return ($Year % 4 -eq 0) -and (($Year % 100 -ne 0) -or ($Year % 400 -eq 0))
}

function Get-LeapYearCount {
    param([int] $First, [int] $Last)
    [int] $count = 0
    for ([int] $year = $First; $year -le $Last; $year++) {
        if (Test-LeapYear -Year $year) { $count++ }
    }
    return $count
}

if ($FirstYear -gt $LastYear) {
    throw [System.ArgumentException]::new('The first year must not follow the last year.')
}
[int] $count = Get-LeapYearCount -First $FirstYear -Last $LastYear
[bool] $century = Test-LeapYear -Year 1900
[bool] $fourHundred = Test-LeapYear -Year 2000
return "$FirstYear-$LastYear|$count|$century|$fourHundred"
