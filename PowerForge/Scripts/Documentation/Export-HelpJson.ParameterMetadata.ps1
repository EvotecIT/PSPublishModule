function MergeParameterPossibleValues(
  [object[]]$metadataValues,
  [object[]]$enumValues
) {
  $result = [System.Collections.Generic.List[string]]::new()
  $seenFolded = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($value in @($metadataValues)) {
    if ($null -eq $value) { continue }
    $normalized = ([string]$value).Trim()
    if ($normalized -and $seenFolded.Add($normalized)) {
      $result.Add($normalized)
    }
  }

  $seenOrdinal = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($value in $result) { [void]$seenOrdinal.Add($value) }
  foreach ($value in @($enumValues)) {
    if ($null -eq $value) { continue }
    $normalized = ([string]$value).Trim()
    if ($normalized -and $seenOrdinal.Add($normalized)) {
      $result.Add($normalized)
    }
  }
  return @($result.ToArray())
}
