function Get-AllowedAverageMs {
    param(
        [double] $BaselineMs,
        [double] $RelativeTolerance,
        [double] $AbsoluteToleranceMs
    )

    $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
    $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
    if ($relativeCap -gt $absoluteCap) {
        return $relativeCap
    }

    return $absoluteCap
}

function Get-TriangularNumber {
    param([int] $Count)

    [long] $total = 0
    for ([int] $i = 1; $i -le $Count; $i++) {
        $total += $i
    }

    return $total
}

function Get-RepeatedTriangularNumber {
    param([int] $Calls, [int] $Count)

    [long] $result = 0
    [long] $total = 0
    for ([int] $call = 0; $call -lt $Calls; $call++) {
        $total = 0
        for ([int] $value = 1; $value -le $Count; $value++) {
            $total += $value
        }
        $result = $total
    }

    return $result
}
