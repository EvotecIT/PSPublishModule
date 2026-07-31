function GetCanonicalTypeNameFromType([type]$type) {
  if ($null -eq $type) { return '' }
  if ($type.IsArray) {
    $elementName = GetCanonicalTypeNameFromType ($type.GetElementType())
    $rank = $type.GetArrayRank()
    if ($rank -le 1) {
      if ($type -eq $type.GetElementType().MakeArrayType()) { return ($elementName + '[]') }
      return ($elementName + '[*]')
    }
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

function GetPowerShellSafeEnumName([type]$enumType, [object]$value) {
  $enumName = [System.Enum]::GetName($enumType, $value)
  if ([string]::IsNullOrWhiteSpace($enumName)) { return '' }
  $caseInsensitiveMatches = 0
  foreach ($candidate in [System.Enum]::GetNames($enumType)) {
    if ($candidate -ieq $enumName) { $caseInsensitiveMatches++ }
  }
  if ($caseInsensitiveMatches -eq 1) { return $enumName }
  return ''
}

function GetConstructibleDictionaryTypeName([System.Collections.IDictionary]$value) {
  $dictionaryType = $value.GetType()
  if ($dictionaryType.IsAbstract -or $dictionaryType.IsInterface) { return '' }
  $constructor = $null
  try { $constructor = $dictionaryType.GetConstructor([System.Type]::EmptyTypes) } catch { $constructor = $null }
  if ($null -eq $constructor) { return '' }
  return GetCanonicalTypeNameFromType $dictionaryType
}

function GetDictionaryComparer([System.Collections.IDictionary]$value, [ref]$comparerType) {
  $comparerType.Value = $null
  $dictionaryType = $value.GetType()
  $flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
  foreach ($propertyName in @('Comparer', 'EqualityComparer', 'comparer')) {
    $property = $dictionaryType.GetProperty($propertyName, $flags)
    if ($null -eq $property -or $property.GetIndexParameters().Count -ne 0) { continue }
    try {
      $comparerType.Value = $property.PropertyType
      return $property.GetValue($value, $null)
    } catch {
      # Try the runtime's backing field when a protected comparer getter is unavailable.
    }
  }
  foreach ($fieldName in @('_keycomparer', '_comparer', 'comparer')) {
    $field = $dictionaryType.GetField($fieldName, $flags)
    if ($null -eq $field) { continue }
    try {
      $comparerType.Value = $field.FieldType
      return $field.GetValue($value)
    } catch {
      # Keep probing known runtime layouts.
    }
  }
  return $null
}

function GetKnownDictionaryComparerExpression([object]$comparer, [type]$comparerType) {
  if ($null -eq $comparer) { return '' }
  if ($comparerType -and $comparerType.IsGenericType) {
    $definition = $comparerType.GetGenericTypeDefinition()
    $argument = $comparerType.GetGenericArguments()[0]
    $defaultComparerType = $null
    if ($definition -eq [System.Collections.Generic.IEqualityComparer``1]) {
      $defaultComparerType = [System.Collections.Generic.EqualityComparer``1].MakeGenericType($argument)
    } elseif ($definition -eq [System.Collections.Generic.IComparer``1]) {
      $defaultComparerType = [System.Collections.Generic.Comparer``1].MakeGenericType($argument)
    }
    if ($defaultComparerType) {
      $defaultComparer = $defaultComparerType.GetProperty('Default').GetValue($null, $null)
      if ([object]::ReferenceEquals($comparer, $defaultComparer)) { return '' }
    }
  }
  foreach ($name in @('Ordinal', 'OrdinalIgnoreCase', 'InvariantCulture', 'InvariantCultureIgnoreCase')) {
    $knownComparer = [System.StringComparer].GetProperty($name).GetValue($null, $null)
    if ([object]::ReferenceEquals($comparer, $knownComparer)) {
      return ('[System.StringComparer]::' + $name)
    }
  }
  throw ('Unsupported dictionary comparer: ' + $comparer.GetType().FullName)
}

function GetDictionaryConstructorExpression([System.Collections.IDictionary]$value) {
  $dictionaryTypeName = GetConstructibleDictionaryTypeName $value
  if ([string]::IsNullOrWhiteSpace($dictionaryTypeName)) {
    return '[System.Collections.Specialized.OrderedDictionary]::new()'
  }
  $comparerType = $null
  $comparer = GetDictionaryComparer $value ([ref]$comparerType)
  $comparerExpression = GetKnownDictionaryComparerExpression $comparer $comparerType
  if ([string]::IsNullOrWhiteSpace($comparerExpression)) {
    return ('[' + $dictionaryTypeName + ']::new()')
  }
  $constructor = $null
  foreach ($candidate in $value.GetType().GetConstructors()) {
    $parameters = $candidate.GetParameters()
    if ($parameters.Count -eq 1 -and $parameters[0].ParameterType.IsInstanceOfType($comparer)) {
      $constructor = $candidate
      break
    }
  }
  if ($null -eq $constructor) {
    throw ('Dictionary type cannot reconstruct comparer: ' + $value.GetType().FullName)
  }
  return ('[' + $dictionaryTypeName + ']::new(' + $comparerExpression + ')')
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

function ResolveUniqueNestedType([string]$candidate, [ref]$isAmbiguous) {
  $isAmbiguous.Value = $false
  if ([string]::IsNullOrWhiteSpace($candidate) -or -not $candidate.Contains('.')) { return $null }

  $nestedCandidates = [System.Collections.Generic.List[string]]::new()
  $seenCandidates = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  for ($index = $candidate.Length - 1; $index -ge 0; $index--) {
    if ($candidate[$index] -ne '.') { continue }
    $nestedCandidate = $candidate.Substring(0, $index) + '+' + $candidate.Substring($index + 1).Replace('.', '+')
    if ($seenCandidates.Add($nestedCandidate)) {
      $nestedCandidates.Add($nestedCandidate)
    }
  }

  $matches = [System.Collections.Generic.Dictionary[string,System.Type]]::new([System.StringComparer]::Ordinal)
  foreach ($nestedCandidate in $nestedCandidates) {
    $resolvedType = ResolveExactType $nestedCandidate
    if ($resolvedType) {
      $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
    }
  }
  if ($matches.Count -eq 1) {
    foreach ($match in $matches.Values) { return $match }
  }
  if ($matches.Count -gt 1) {
    $isAmbiguous.Value = $true
    return $null
  }

  $ambiguous = $false
  foreach ($nestedCandidate in $nestedCandidates) {
    $candidateAmbiguous = $false
    $resolvedType = ResolveUniqueTypeCaseInsensitive $nestedCandidate ([ref]$candidateAmbiguous)
    if ($candidateAmbiguous) { $ambiguous = $true }
    if ($resolvedType) {
      $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
    }
  }
  $isAmbiguous.Value = $ambiguous -or $matches.Count -gt 1
  if ($isAmbiguous.Value -or $matches.Count -ne 1) { return $null }
  foreach ($match in $matches.Values) { return $match }
  return $null
}

function ResolveCanonicalTypeName([string]$candidate) {
  if ([string]::IsNullOrWhiteSpace($candidate)) { return '' }
  $trimmed = $candidate.Trim()
  $resolvedType = ResolveExactType $trimmed
  $ambiguous = $false
  if (-not $resolvedType) { $resolvedType = ResolveUniqueNestedType $trimmed ([ref]$ambiguous) }
  if (-not $resolvedType -and -not $ambiguous) { $resolvedType = ResolveUniqueTypeCaseInsensitive $trimmed ([ref]$ambiguous) }
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
  $uniqueKeys = [System.Collections.Generic.List[string]]::new()
  $seenKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($key in $keys) {
    if ($seenKeys.Add([string]$key)) {
      $uniqueKeys.Add([string]$key)
    }
  }
  return @($uniqueKeys.ToArray())
}

function GetTypeIdentity([string]$name, [string]$clrName) {
  foreach ($candidate in @($clrName, $name)) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    $trimmed = $candidate.Trim()
    $resolvedType = ResolveExactType $trimmed
    $ambiguous = $false
    if (-not $resolvedType) { $resolvedType = ResolveUniqueNestedType $trimmed ([ref]$ambiguous) }
    if (-not $resolvedType -and -not $ambiguous) { $resolvedType = ResolveUniqueTypeCaseInsensitive $trimmed ([ref]$ambiguous) }
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
