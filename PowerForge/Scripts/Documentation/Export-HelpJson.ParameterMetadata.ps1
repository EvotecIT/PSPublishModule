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

function ConvertParametersToXmlSafeDocumentationText([object[]]$parameters) {
  $reservedNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  foreach ($parameter in @($parameters)) {
    $name = [string]$parameter.name
    if (TestXmlSafeIdentityText $name) { [void]$reservedNames.Add($name) }
  }

  $usedNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  foreach ($parameter in @($parameters)) {
    $rawName = [string]$parameter.name
    $name = $rawName
    if (-not (TestXmlSafeIdentityText $rawName)) {
      $baseName = ConvertToXmlSafeIdentityText $rawName
      $name = $baseName
      $suffix = 1
      while ($reservedNames.Contains($name) -or -not $usedNames.Add($name)) {
        $name = $baseName + ' [encoded ' +
          $suffix.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ']'
        $suffix++
      }
    } else {
      [void]$usedNames.Add($name)
    }
    $parameter.name = $name

    $rawAliases = [System.Collections.Generic.List[string]]::new()
    $seenRawAliases = [System.Collections.Generic.HashSet[string]]::new(
      [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($aliasValue in @($parameter.aliases)) {
      $alias = [string]$aliasValue
      if ($alias -and $seenRawAliases.Add($alias)) { $rawAliases.Add($alias) }
    }

    $reservedAliases = [System.Collections.Generic.HashSet[string]]::new(
      [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($alias in $rawAliases) {
      if (TestXmlSafeIdentityText $alias) { [void]$reservedAliases.Add($alias) }
    }
    $usedAliases = [System.Collections.Generic.HashSet[string]]::new(
      [System.StringComparer]::OrdinalIgnoreCase)
    $aliases = [System.Collections.Generic.List[string]]::new()
    foreach ($alias in $rawAliases) {
      $display = $alias
      if (-not (TestXmlSafeIdentityText $alias)) {
        $display = ConvertToXmlSafeDefaultHelpText $alias
        if ($reservedAliases.Contains($display) -or $usedAliases.Contains($display)) {
          $baseDisplay = ConvertToPowerShellDefaultValue $alias
          $display = $baseDisplay
          $suffix = 1
          while ($reservedAliases.Contains($display) -or $usedAliases.Contains($display)) {
            $display = $baseDisplay + ' [encoded ' +
              $suffix.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ']'
            $suffix++
          }
        }
      }
      if ($usedAliases.Add($display)) { $aliases.Add($display) }
    }
    $parameter.aliases = @($aliases.ToArray())
  }

  return @($parameters)
}

function ConvertParameterSetIdentitiesToXmlSafeDocumentationText(
  [string]$defaultSet,
  [object[]]$syntax,
  [object[]]$parameters
) {
  $rawNames = [System.Collections.Generic.List[string]]::new()
  if ($defaultSet) { $rawNames.Add($defaultSet) }
  foreach ($entry in @($syntax)) { $rawNames.Add([string]$entry.name) }
  foreach ($parameter in @($parameters)) {
    foreach ($name in @($parameter.parameterSets)) { $rawNames.Add([string]$name) }
    foreach ($name in @($parameter.parameterSetRequired.Keys)) { $rawNames.Add([string]$name) }
  }

  $reserved = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  foreach ($name in $rawNames) {
    if (TestXmlSafeIdentityText $name) { [void]$reserved.Add($name) }
  }
  $used = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  $map = [System.Collections.Generic.Dictionary[string,string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  foreach ($rawName in $rawNames) {
    if ($map.ContainsKey($rawName)) { continue }
    $name = $rawName
    if (-not (TestXmlSafeIdentityText $rawName)) {
      $baseName = ConvertToXmlSafeIdentityText $rawName
      $name = $baseName
      $suffix = 1
      while ($reserved.Contains($name) -or -not $used.Add($name)) {
        $name = $baseName + ' [encoded ' +
          $suffix.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ']'
        $suffix++
      }
    } else {
      [void]$used.Add($name)
    }
    $map.Add($rawName, $name)
  }

  if ($defaultSet -and $map.ContainsKey($defaultSet)) { $defaultSet = $map[$defaultSet] }
  foreach ($entry in @($syntax)) {
    $rawName = [string]$entry.name
    if ($map.ContainsKey($rawName)) { $entry.name = $map[$rawName] }
  }
  foreach ($parameter in @($parameters)) {
    $parameter.parameterSets = @($parameter.parameterSets | Microsoft.PowerShell.Core\ForEach-Object {
      $rawName = [string]$_
      if ($map.ContainsKey($rawName)) { $map[$rawName] } else { $rawName }
    })
    $required = [System.Collections.Generic.Dictionary[string,bool]]::new(
      [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $parameter.parameterSetRequired.GetEnumerator()) {
      $rawName = [string]$entry.Key
      $name = if ($map.ContainsKey($rawName)) { $map[$rawName] } else { $rawName }
      $required[$name] = [bool]$entry.Value
    }
    $parameter.parameterSetRequired = $required
  }
  return [pscustomobject]@{
    DefaultSet = $defaultSet
    Syntax = @($syntax)
    Parameters = @($parameters)
  }
}
