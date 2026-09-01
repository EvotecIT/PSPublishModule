function Test-NullListCount {
    param([System.Collections.Generic.List[string]] $Items)

    return $Items.Count -eq 0
}

if ([Type]::GetType('PowerForge.Semantic.Missing.Type').MetadataToken -ne $null) {
    return $false
}

return Test-NullListCount
