function Get-ItemCount {
    $items = [System.Collections.Generic.List[string]]::new()
    $items.AddRange([string[]] ('alpha', 'beta'))
    $copy = $items.ToArray()
    return $items.Count -eq 2 -and $copy.Length -eq 2
}

Get-ItemCount
