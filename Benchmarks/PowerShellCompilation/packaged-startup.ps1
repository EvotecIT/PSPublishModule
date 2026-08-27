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
