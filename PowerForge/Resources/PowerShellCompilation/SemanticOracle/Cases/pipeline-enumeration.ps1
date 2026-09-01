function Get-PipelineTotal {
    param([int[]] $Values)

    [int] $Total = 0
    $Values | ForEach-Object { $Total += $_ }
    return $Total
}

return (Get-PipelineTotal -Values (40, 2)) -eq 42 -and (Get-PipelineTotal -Values $null) -eq 0
