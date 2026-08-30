function Resolve-ReportStatus {
    [OutputType([string])]
    param([int] $Total)

    if ($Total -ge 40) {
        return 'high'
    }
    if ($Total -ge 20) {
        return 'medium'
    }
    return 'low'
}

function Get-ApplicationReport {
    [OutputType([string])]
    param(
        [string] $Label,
        [string[]] $Values,
        [int] $Rule,
        [int] $Threshold
    )

    [string] $normalized = ConvertTo-NormalizedLabel -Value $Label
    [int] $multiplier = Resolve-RuleMultiplier -Rule $Rule
    [int] $total = Get-TotalScore -Values $Values -Multiplier $multiplier
    [int] $maximum = Get-MaximumScore -Values $Values -Multiplier $multiplier
    [int] $qualified = Get-QualifiedScoreCount -Values $Values -Multiplier $multiplier -Threshold $Threshold
    [string] $status = Resolve-ReportStatus -Total $total
    [string] $report = $normalized
    $report += '|'
    $report += $total.ToString()
    $report += '|'
    $report += $maximum.ToString()
    $report += '|'
    $report += $qualified.ToString()
    $report += '|'
    $report += $status
    return $report
}
