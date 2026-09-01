function Measure-Total {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)][int] $Value)
    begin {
        [int] $Total = 0
        [int] $Count = 0
    }
    process {
        $Total += $Value
        $Count += 1
    }
    end {
        [int] $Result = $Count
        $Result *= 1000
        $Result += $Total
        $Result
    }
}
function Invoke-Measure {
    param([int[]] $Values)
    $Values | Measure-Total
}
Invoke-Measure -Values $null
