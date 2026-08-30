function Measure-Total {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)][int] $Value)
    begin { $Total = 0 }
    process { $Total += $Value }
    end { $Total }
}
40, 2 | Measure-Total
