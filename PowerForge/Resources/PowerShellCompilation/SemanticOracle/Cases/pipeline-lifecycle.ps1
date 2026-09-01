function Measure-Total {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)][int] $Value)
    begin { [int] $Total = 0 }
    process { $Total += $Value }
    end { $Total }
}
function Invoke-Measure {
    param([int[]] $Values)
    $Values | Measure-Total
}
Invoke-Measure -Values @(40, 2)
