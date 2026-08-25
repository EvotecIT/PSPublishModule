function Get-PortableScore {
    [OutputType([int])]
    param(
        [int] $Number,
        [string] $Label
    )

    [int] $countdown = Get-PortableCountdown -Number $Number
    [int] $length = $Label.Length
    [int] $score = ($length -shl 1)
    $score += $countdown
    return $score
}

function Get-PortableCountdown {
    [OutputType([int])]
    param(
        [int] $Number
    )

    if ($Number -le 0) {
        return $Number
    }

    $Number -= 1
    return Get-PortableCountdown -Number $Number
}
