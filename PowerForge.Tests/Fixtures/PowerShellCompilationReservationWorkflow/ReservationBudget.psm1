param([ValidateRange(1, 1000000)][int]$Capacity = 100)

[int]$script:Limit = $Capacity
[int]$script:Reserved = 0

function Get-AvailableCapacity {
    [OutputType([int])]
    param()
    return $script:Limit - $script:Reserved
}

function Get-ReservedCapacity {
    [OutputType([int])]
    param()
    return $script:Reserved
}

function Request-Capacity {
    [OutputType([bool])]
    param([ValidateRange(1, 1000000)][int]$Units)
    [int]$available = Get-AvailableCapacity
    if ($Units -gt $available) { return $false }
    $script:Reserved += $Units
    return $true
}

function Release-Capacity {
    [OutputType([bool])]
    param([ValidateRange(1, 1000000)][int]$Units)
    if ($Units -gt $script:Reserved) { return $false }
    $script:Reserved -= $Units
    return $true
}

Export-ModuleMember -Function Get-AvailableCapacity, Get-ReservedCapacity, Request-Capacity, Release-Capacity
