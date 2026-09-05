function Get-CollectionScore {
    [OutputType([int])]
    param(
        [string] $Token
    )

    $items = [System.Collections.ArrayList]::new()
    $null = $items.Add($Token)
    $null = $items.Add($Token.ToUpperInvariant())
    [int] $score = $items.Count
    return $score
}
