function Test-AndNullRefinement {
    param([System.Version] $Value)

    return ($null -ne $Value) -and ($Value.Major -gt 0)
}

$one = [System.Version]::new(1, 0)
Test-AndNullRefinement -Value $one
