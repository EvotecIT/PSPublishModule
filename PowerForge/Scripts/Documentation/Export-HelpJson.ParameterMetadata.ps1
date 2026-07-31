function MergeParameterPossibleValues(
  [object[]]$metadataValues,
  [object[]]$enumValues,
  [bool]$metadataCaseSensitive = $false
) {
  $result = [System.Collections.Generic.List[string]]::new()
  $metadataComparer = if ($metadataCaseSensitive) {
    [System.StringComparer]::Ordinal
  } else {
    [System.StringComparer]::OrdinalIgnoreCase
  }
  $seenMetadata = [System.Collections.Generic.HashSet[string]]::new($metadataComparer)
  foreach ($value in @($metadataValues)) {
    if ($null -eq $value) { continue }
    $normalized = ([string]$value).Trim()
    if ($normalized -and $seenMetadata.Add($normalized)) {
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

function TestValidateSetCaseSensitive(
  [System.Management.Automation.ValidateSetAttribute]$attribute
) {
  try {
    return $attribute.PSObject.Properties['IgnoreCase'] -and -not [bool]$attribute.IgnoreCase
  } catch {
    return $false
  }
}
