function Get-PipelineTotal {
    param([int[]] $Values)

    [int] $Total = 0
    [int] $Count = 0
    $Values | ForEach-Object {
        $Total += $_
        $Count += 1
    }
    [int] $Result = $Count
    $Result *= 1000
    $Result += $Total
    return $Result
}

Get-PipelineTotal -Values $null
