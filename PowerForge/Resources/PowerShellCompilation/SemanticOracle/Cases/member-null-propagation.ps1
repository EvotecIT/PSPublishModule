function Test-NullCollectionCount {
    param(
        [System.Collections.Generic.List[string]] $Items,
        [array] $ArrayItems
    )

    return ($Items.Count -eq 0) -and ($ArrayItems.Count -eq 0)
}

if ([Type]::GetType('PowerForge.Semantic.Missing.Type').MetadataToken -ne $null) {
    return $false
}

return Test-NullCollectionCount
