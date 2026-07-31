function MergeParameterPossibleValues(
  [object[]]$metadataValues,
  [object[]]$enumValues,
  [bool]$metadataCaseSensitive = $false,
  [bool]$preserveMetadataText = $false
) {
  $result = [System.Collections.Generic.List[string]]::new()
  $metadataComparer = if ($metadataCaseSensitive) {
    [System.StringComparer]::Ordinal
  } else {
    [System.StringComparer]::OrdinalIgnoreCase
  }
  $seenMetadata = [System.Collections.Generic.HashSet[string]]::new($metadataComparer)
  $seenMetadataDisplay = [System.Collections.Generic.HashSet[string]]::new($metadataComparer)
  foreach ($value in @($metadataValues)) {
    if ($null -eq $value) { continue }
    $normalized = if ($preserveMetadataText) { [string]$value } else { ([string]$value).Trim() }
    $display = ConvertToXmlSafeDefaultHelpText $normalized
    if (($preserveMetadataText -or $normalized) -and
        $seenMetadata.Add($normalized) -and
        $seenMetadataDisplay.Add($display)) {
      $result.Add($display)
    }
  }

  $seenOrdinal = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($value in $result) { [void]$seenOrdinal.Add($value) }
  foreach ($value in @($enumValues)) {
    if ($null -eq $value) { continue }
    $normalized = ([string]$value).Trim()
    $display = ConvertToXmlSafeDefaultHelpText $normalized
    if ($normalized -and $seenOrdinal.Add($display)) {
      $result.Add($display)
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
