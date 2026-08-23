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
