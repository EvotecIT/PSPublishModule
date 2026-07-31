function GetCanonicalTypeNameFromType([type]$type) {
  if ($null -eq $type) { return '' }
  if ($type.IsArray) {
    $elementName = GetCanonicalTypeNameFromType ($type.GetElementType())
    $rank = $type.GetArrayRank()
    if ($rank -le 1) { return ($elementName + '[]') }
    return ($elementName + '[' + (',' * ($rank - 1)) + ']')
  }
  if ($type.IsGenericTypeDefinition) {
    if ($type.FullName) { return [string]$type.FullName }
    return [string]$type.Name
  }
  if ($type.IsGenericType) {
    $definition = $type.GetGenericTypeDefinition()
    $definitionName = [string]$definition.FullName
    if (-not $definitionName) { $definitionName = [string]$definition.Name }
    if ($definitionName.IndexOf('+') -lt 0) {
      $definitionName = $definitionName -replace '`\d+$', ''
    }
    $arguments = @()
    foreach ($argument in $type.GetGenericArguments()) {
      $arguments += GetCanonicalTypeNameFromType $argument
    }
    return ($definitionName + '[' + ($arguments -join ',') + ']')
  }
  if ($type.FullName) { return [string]$type.FullName }
  return [string]$type.Name
}

function ResolveExactType([string]$candidate) {
  if ([string]::IsNullOrWhiteSpace($candidate)) { return $null }
  $resolvedType = $null
  try { $resolvedType = [System.Type]::GetType($candidate, $false, $false) } catch { $resolvedType = $null }
  if ($resolvedType) { return $resolvedType }
  foreach ($assembly in [System.AppDomain]::CurrentDomain.GetAssemblies()) {
    try { $resolvedType = $assembly.GetType($candidate, $false, $false) } catch { $resolvedType = $null }
    if ($resolvedType) { return $resolvedType }
  }
  return $null
}

function ResolveUniqueTypeCaseInsensitive([string]$candidate, [ref]$isAmbiguous) {
  $isAmbiguous.Value = $false
  if ([string]::IsNullOrWhiteSpace($candidate)) { return $null }
  $matches = [System.Collections.Generic.Dictionary[string,System.Type]]::new([System.StringComparer]::Ordinal)
  $ambiguous = $false
  $resolvedType = $null
  try {
    $resolvedType = [System.Type]::GetType($candidate, $false, $true)
  } catch [System.Reflection.AmbiguousMatchException] {
    $ambiguous = $true
  } catch {
    $resolvedType = $null
  }
  if ($resolvedType) {
    $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
  }
  foreach ($assembly in [System.AppDomain]::CurrentDomain.GetAssemblies()) {
    $resolvedType = $null
    try {
      $resolvedType = $assembly.GetType($candidate, $false, $true)
    } catch [System.Reflection.AmbiguousMatchException] {
      $ambiguous = $true
    } catch {
      $resolvedType = $null
    }
    if ($resolvedType) {
      $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
    }
  }
  $isAmbiguous.Value = $ambiguous
  if ($ambiguous -or $matches.Count -ne 1) { return $null }
  foreach ($match in $matches.Values) { return $match }
  return $null
}

function ResolveCanonicalTypeName([string]$candidate) {
  if ([string]::IsNullOrWhiteSpace($candidate)) { return '' }
  $trimmed = $candidate.Trim()
  $resolvedType = ResolveExactType $trimmed
  $ambiguous = $false
  if (-not $resolvedType) { $resolvedType = ResolveUniqueTypeCaseInsensitive $trimmed ([ref]$ambiguous) }
  if (-not $resolvedType -and -not $ambiguous) {
    try { $resolvedType = $trimmed -as [type] } catch { $resolvedType = $null }
  }
  if ($resolvedType) { return GetCanonicalTypeNameFromType $resolvedType }
  return ($trimmed -replace '\s+', '')
}

function GetCanonicalTypeName([string]$candidate) {
  return ResolveCanonicalTypeName $candidate
}

function GetTypeKeys([string]$name, [string]$clrName) {
  $keys = @()
  foreach ($candidate in @($name, $clrName)) {
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
      $trimmed = $candidate.Trim()
      $keys += $trimmed
      $canonical = GetCanonicalTypeName $trimmed
      if ($canonical) {
        $keys += $canonical
        $genericIndex = $canonical.IndexOf('[')
        $baseName = if ($genericIndex -ge 0) { $canonical.Substring(0, $genericIndex) } else { $canonical }
        $genericSuffix = if ($genericIndex -ge 0) { $canonical.Substring($genericIndex) } else { '' }
        $separatorIndex = [System.Math]::Max($baseName.LastIndexOf('.'), $baseName.LastIndexOf('+'))
        if ($separatorIndex -ge 0 -and $separatorIndex -lt ($baseName.Length - 1)) {
          $keys += ($baseName.Substring($separatorIndex + 1) + $genericSuffix)
        }
      }
    }
  }
  return @($keys | Sort-Object -Unique)
}

function GetTypeIdentity([string]$name, [string]$clrName) {
  foreach ($candidate in @($clrName, $name)) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    $trimmed = $candidate.Trim()
    $resolvedType = ResolveExactType $trimmed
    $ambiguous = $false
    if (-not $resolvedType) { $resolvedType = ResolveUniqueTypeCaseInsensitive $trimmed ([ref]$ambiguous) }
    if ($resolvedType) { return GetCanonicalTypeNameFromType $resolvedType }
    $identity = GetCanonicalTypeName $trimmed
    if ($identity) { return $identity }
  }
  return ''
}

function TestQualifiedTypeIdentity([string]$identity) {
  if ([string]::IsNullOrWhiteSpace($identity)) { return $false }
  $baseName = $identity
  $genericIndex = $baseName.IndexOf('[')
  if ($genericIndex -ge 0) { $baseName = $baseName.Substring(0, $genericIndex) }
  return $baseName.Contains('.') -or $baseName.Contains('+')
}

function TestConflictingQualifiedTypeIdentity([string]$left, [string]$right) {
  return $left -cne $right -and
    (TestQualifiedTypeIdentity $left) -and
    (TestQualifiedTypeIdentity $right)
}
