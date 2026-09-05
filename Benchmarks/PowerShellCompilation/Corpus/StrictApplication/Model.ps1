function ConvertTo-NormalizedLabel {
    [OutputType([string])]
    param([string] $Value)

    [string] $trimmed = $Value.Trim()
    return $trimmed.ToUpperInvariant()
}

function Get-LabelLength {
    [OutputType([int])]
    param([string] $Value)

    [string] $normalized = ConvertTo-NormalizedLabel -Value $Value
    return $normalized.Length
}

function Resolve-RuleMultiplier {
    [OutputType([int])]
    param([int] $Rule)

    switch ($Rule) {
        1 { return 2 }
        2 { return 3 }
        3 { return 4 }
        default { return 1 }
    }
}

function Test-ScoreThreshold {
    [OutputType([bool])]
    param(
        [int] $Score,
        [int] $Threshold
    )

    return $Score -ge $Threshold
}
