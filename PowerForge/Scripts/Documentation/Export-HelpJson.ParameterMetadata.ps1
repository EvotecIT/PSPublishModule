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
  $metadataEntries = [System.Collections.Generic.List[object]]::new()
  foreach ($value in @($metadataValues)) {
    if ($null -eq $value) { continue }
    $normalized = if ($preserveMetadataText) { [string]$value } else { ([string]$value).Trim() }
    $display = ConvertToXmlSafeDefaultHelpText $normalized
    if (($preserveMetadataText -or $normalized) -and
        $seenMetadata.Add($normalized)) {
      $metadataEntries.Add([pscustomobject]@{ Original = $normalized; Display = $display })
    }
  }

  $displayCounts = [System.Collections.Generic.Dictionary[string,int]]::new($metadataComparer)
  foreach ($entry in $metadataEntries) {
    if ($displayCounts.ContainsKey($entry.Display)) {
      $displayCounts[$entry.Display]++
    } else {
      $displayCounts.Add($entry.Display, 1)
    }
  }
  $displayCandidates = [System.Collections.Generic.List[object]]::new()
  foreach ($entry in $metadataEntries) {
    $needsFallback = $displayCounts[$entry.Display] -gt 1
    $displayCandidates.Add([pscustomobject]@{
      Display = if ($needsFallback) {
        ConvertToPowerShellDefaultValue ([string]$entry.Original)
      } else {
        [string]$entry.Display
      }
      NeedsFallback = $needsFallback
    })
  }

  $reservedDisplays = [System.Collections.Generic.HashSet[string]]::new($metadataComparer)
  foreach ($candidate in $displayCandidates) {
    if (-not $candidate.NeedsFallback) { [void]$reservedDisplays.Add([string]$candidate.Display) }
  }
  $usedDisplays = [System.Collections.Generic.HashSet[string]]::new($metadataComparer)
  foreach ($candidate in $displayCandidates) {
    $display = [string]$candidate.Display
    if ($candidate.NeedsFallback) {
      $baseDisplay = $display
      $suffix = 1
      while ($reservedDisplays.Contains($display) -or -not $usedDisplays.Add($display)) {
        $display = $baseDisplay + ' [encoded ' +
          $suffix.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ']'
        $suffix++
      }
    } else {
      [void]$usedDisplays.Add($display)
    }
    $result.Add($display)
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
